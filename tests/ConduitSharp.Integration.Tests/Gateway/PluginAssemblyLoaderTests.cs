using ConduitSharp.Core.Routing;
using ConduitSharp.Gateway.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using ConduitSharp.Gateway.Routing;
using Yarp.ReverseProxy.Configuration;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// Unit tests for <see cref="PluginAssemblyLoader"/> and the internal
/// <see cref="PluginLoadContext"/>. These live in the integration test project
/// because that project already has a reference to ConduitSharp.Host.
/// </summary>
public class PluginAssemblyLoaderTests
{
    private readonly PluginAssemblyLoader _loader =
        new(NullLogger<PluginAssemblyLoader>.Instance);

    // -------------------------------------------------------------------------
    // Non-existent directory
    // -------------------------------------------------------------------------

    [Fact]
    public void DiscoverPluginTypes_NonExistentDirectory_ReturnsEmpty()
    {
        var result = _loader.DiscoverPluginTypes("/this/path/does/not/exist");

        Assert.Empty(result);
    }

    // -------------------------------------------------------------------------
    // Existing but empty directory
    // -------------------------------------------------------------------------

    [Fact]
    public void DiscoverPluginTypes_EmptyDirectory_ReturnsEmpty()
    {
        var dir = CreateTempDir();
        try
        {
            var result = _loader.DiscoverPluginTypes(dir);
            Assert.Empty(result);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // -------------------------------------------------------------------------
    // Real DLL with no IPipelinePlugin implementations → covers PluginLoadContext
    // and the GetExportedTypes / filter path
    // -------------------------------------------------------------------------

    [Fact]
    public void DiscoverPluginTypes_DllWithNoPlugins_ReturnsEmpty()
    {
        // ConduitSharp.Core.dll is always in the test output dir and has no plugins.
        var sourceDll = Path.Combine(AppContext.BaseDirectory, "ConduitSharp.Core.dll");
        var dir = CreateTempDir();
        try
        {
            File.Copy(sourceDll, Path.Combine(dir, "ConduitSharp.Core.dll"));

            var result = _loader.DiscoverPluginTypes(dir);

            Assert.Empty(result);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // -------------------------------------------------------------------------
    // Corrupt / unreadable DLL → covers the load-failure catch branch
    // -------------------------------------------------------------------------

    [Fact]
    public void DiscoverPluginTypes_CorruptDll_SkipsFileAndReturnsEmpty()
    {
        var dir = CreateTempDir();
        try
        {
            // Valid PE header magic (MZ) but truncated — LoadFromAssemblyPath will throw.
            File.WriteAllBytes(Path.Combine(dir, "corrupt.dll"), [0x4D, 0x5A, 0x00, 0x00]);

            var result = _loader.DiscoverPluginTypes(dir);

            Assert.Empty(result);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }



    // -------------------------------------------------------------------------
    // DiscoverServiceType — global service backends dropped in the plugins root
    // -------------------------------------------------------------------------

    [Fact]
    public void DiscoverServiceType_MissingDirectory_ReturnsNull()
    {
        var result = _loader.DiscoverServiceType<ConduitSharp.Traffic.Caching.ICacheService>(
            "/this/path/does/not/exist");

        Assert.Null(result);
    }

    [Fact]
    public void DiscoverServiceType_NoImplementationInRoot_ReturnsNull()
    {
        var dir = CreateTempDir();
        try
        {
            // A DLL with no ICacheService implementation → nothing discovered.
            File.Copy(
                Path.Combine(AppContext.BaseDirectory, "ConduitSharp.Core.dll"),
                Path.Combine(dir, "ConduitSharp.Core.dll"));

            var result = _loader.DiscoverServiceType<ConduitSharp.Traffic.Caching.ICacheService>(dir);

            Assert.Null(result);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // -------------------------------------------------------------------------
    // Corrupt DLL where it is actually scanned → covers the load-failure catch
    // on both scan paths (subdirectory scan and root scan). The earlier corrupt
    // test drops the DLL in the root, which DiscoverPluginTypes never scans, so
    // it did not reach the catch.
    // -------------------------------------------------------------------------

    [Fact]
    public void DiscoverPluginTypes_CorruptDllInSubdirectory_SkipsAndReturnsEmpty()
    {
        // DiscoverPluginTypes scans per-route SUBDIRECTORIES. A corrupt DLL dropped into one
        // must be logged and skipped, not crash the scan — the appliance drop-in model has to
        // survive a bad artifact.
        var root = CreateTempDir();
        try
        {
            var sub = Directory.CreateDirectory(Path.Combine(root, "route-a")).FullName;
            File.WriteAllBytes(Path.Combine(sub, "corrupt.dll"), [0x4D, 0x5A, 0x00, 0x00]);

            var result = _loader.DiscoverPluginTypes(root);

            Assert.Empty(result);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void DiscoverServiceType_CorruptDllInRoot_ReturnsNull()
    {
        // DiscoverServiceType scans the plugins ROOT directly. A corrupt DLL there must be
        // skipped, leaving the built-in default in place rather than failing the gateway.
        var root = CreateTempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(root, "corrupt.dll"), [0x4D, 0x5A, 0x00, 0x00]);

            var result = _loader.DiscoverServiceType<ConduitSharp.Traffic.Caching.ICacheService>(root);

            Assert.Null(result);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }
}
