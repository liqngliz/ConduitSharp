using System.Reflection;
using System.Runtime.Loader;
using ConduitSharp.Core.Pipeline;
using ConduitSharp.Core.Routing;
using Microsoft.Extensions.Logging;
using ConduitSharp.Gateway.Routing;

namespace ConduitSharp.Gateway.Plugins;

/// <summary>
/// Manages external plugins stored under a per-route subdirectory layout:
///
///   plugins/
///     {routeId}/          ← one folder per route
///       MyPlugin.dll
///       MyPlugin.deps.json
///       ...
///
/// All assemblies are loaded into <see cref="AssemblyLoadContext.Default"/> with full trust.
/// The per-route directory structure is cosmetic for organization only; discovered types are
/// registered globally and resolve by (PluginName, Variant) key regardless of which route folder
/// they came from. Which routes actually *run* a plugin is decided entirely by each route's
/// <c>plugins</c> list in routes.json — that is where per-route scoping lives (auth, rate-limit,
/// cache, forwarder, custom variants). P/Invoke and COM interop require this shared context.
///
/// Usage: drop a compiled plugin .dll into the matching route subdirectory,
/// then restart the gateway. No rebuild of the gateway required.
///
/// Security note: loaded assemblies run in-process with full trust.
/// Only load assemblies from sources you control.
/// </summary>
internal sealed class PluginAssemblyLoader(ILogger<PluginAssemblyLoader> logger)
{
    private readonly ILogger<PluginAssemblyLoader> _logger = logger;

    private readonly List<AssemblyDependencyResolver> _resolvers = [];
    private bool _resolvingHandlerAttached;

    private Assembly? ResolveFromAnyScannedPlugin(AssemblyLoadContext ctx, AssemblyName name)
    {
        var existing = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a =>
            string.Equals(a.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        foreach (var resolver in _resolvers)
        {
            var path = resolver.ResolveAssemblyToPath(name);
            if (path is not null) return ctx.LoadFromAssemblyPath(path);
        }

        return null;
    }

    /// <summary>
    /// Scans every route subdirectory under <paramref name="pluginsRoot"/> for
    /// <see cref="IPipelinePlugin"/> implementations and returns all discovered types.
    /// Subdirectories that contain no DLLs are silently skipped.
    /// Assemblies that fail to load are skipped with a warning.
    /// </summary>
    public IReadOnlyList<Type> DiscoverPluginTypes(string pluginsRoot)
    {
        if (!Directory.Exists(pluginsRoot))
        {
            _logger.LogInformation(
                "Plugins directory '{Dir}' does not exist — no external plugins loaded.",
                pluginsRoot);
            return [];
        }

        var discovered = new List<Type>();

        foreach (var subDir in Directory.GetDirectories(pluginsRoot))
        {
            var dlls = Directory.GetFiles(subDir, "*.dll");
            if (dlls.Length == 0) continue;

            _logger.LogInformation(
                "Scanning {Count} assemblies in '{Dir}' for IPipelinePlugin implementations " +
                "(discovered plugins are available to all routes; activation is per-route via routes.json).",
                dlls.Length, subDir);

            foreach (var dll in dlls)
                discovered.AddRange(LoadPluginTypes(dll));
        }

        _logger.LogInformation(
            "Discovered {Count} external plugin type(s): {Names}",
            discovered.Count,
            string.Join(", ", discovered.Select(t => t.FullName)));

        return discovered;
    }

    /// <summary>
    /// Scans DLLs directly in the plugins root — not the per-route subdirectories — for a
    /// single implementation of <typeparamref name="TService"/>. Used to drop in a global
    /// service backend such as a Redis <c>ICacheService</c>: place the DLL in the plugins
    /// root and the host registers it in place of the built-in default. Returns the last
    /// implementation found, or null.
    /// </summary>
    public Type? DiscoverServiceType<TService>(string pluginsRoot)
    {
        if (!Directory.Exists(pluginsRoot)) return null;

        Type? found = null;
        foreach (var dll in Directory.GetFiles(pluginsRoot, "*.dll"))
            foreach (var type in LoadTypes(dll, typeof(TService)))
                found = type;

        if (found is not null)
            _logger.LogInformation(
                "Discovered external {Service} implementation: {Type}.",
                typeof(TService).Name, found.FullName);

        return found;
    }

    private IEnumerable<Type> LoadPluginTypes(string dllPath) =>
        LoadTypes(dllPath, typeof(IPipelinePlugin));

    private IEnumerable<Type> LoadTypes(string dllPath, Type serviceType)
    {
        var resolver = new AssemblyDependencyResolver(dllPath);
        _resolvers.Add(resolver);

        if (!_resolvingHandlerAttached)
        {
            AssemblyLoadContext.Default.Resolving += ResolveFromAnyScannedPlugin;
            _resolvingHandlerAttached = true;
        }

        Assembly assembly;
        try
        {
            var simpleName = AssemblyName.GetAssemblyName(dllPath).Name;
            var loadedByName = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a =>
                string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));

            if (loadedByName is not null &&
                !string.Equals(loadedByName.Location, dllPath, StringComparison.OrdinalIgnoreCase))
            {
                return [];
            }

            assembly = loadedByName ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(dllPath);
        }
        catch (Exception ex)
        {
            _resolvers.Remove(resolver);
            _logger.LogWarning(ex, "Failed to load assembly '{Path}' — skipping.", dllPath);
            return [];
        }

        try
        {
            return assembly
                .GetExportedTypes()
                .Where(t => !t.IsAbstract
                         && !t.IsInterface
                         && serviceType.IsAssignableFrom(t));
        }
        catch (ReflectionTypeLoadException ex)
        {
            _logger.LogWarning(ex,
                "Could not inspect types in '{Path}' — skipping. " +
                "Ensure all dependencies are present alongside the plugin dll.",
                dllPath);
            return [];
        }
    }
}
