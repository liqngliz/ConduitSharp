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

    [Fact]
    public void DiscoverPluginTypes_NonExistentDirectory_ReturnsEmpty()
    {
        var result = _loader.DiscoverPluginTypes("/this/path/does/not/exist");

        Assert.Empty(result);
    }

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

    [Fact]
    public void DiscoverPluginTypes_DllWithNoPlugins_ReturnsEmpty()
    {
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

    [Fact]
    public void DiscoverPluginTypes_CorruptDll_SkipsFileAndReturnsEmpty()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "corrupt.dll"), [0x4D, 0x5A, 0x00, 0x00]);

            var result = _loader.DiscoverPluginTypes(dir);

            Assert.Empty(result);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

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
            File.Copy(
                Path.Combine(AppContext.BaseDirectory, "ConduitSharp.Core.dll"),
                Path.Combine(dir, "ConduitSharp.Core.dll"));

            var result = _loader.DiscoverServiceType<ConduitSharp.Traffic.Caching.ICacheService>(dir);

            Assert.Null(result);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void DiscoverPluginTypes_CorruptDllInSubdirectory_SkipsAndReturnsEmpty()
    {
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
        var root = CreateTempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(root, "corrupt.dll"), [0x4D, 0x5A, 0x00, 0x00]);

            var result = _loader.DiscoverServiceType<ConduitSharp.Traffic.Caching.ICacheService>(root);

            Assert.Null(result);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }
}
