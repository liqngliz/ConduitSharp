using System.Diagnostics;
using System.Net.Sockets;
using StackExchange.Redis;
using Xunit;

namespace ConduitSharp.Cache.RedisProtocol.E2E.Tests;

// Two collections so the same distributed tests run against both a Redis and a Valkey server.
[CollectionDefinition("Redis Cache E2E")]
public sealed class RedisCollection  : ICollectionFixture<RedisFixture>;

[CollectionDefinition("Valkey Cache E2E")]
public sealed class ValkeyCollection : ICollectionFixture<ValkeyFixture>;

/// <summary>Redis 7 (last BSD line) backend fixture.</summary>
public sealed class RedisFixture() : CacheServerFixture("redis:7-alpine");

/// <summary>Valkey backend fixture — the BSD-licensed, RESP-compatible Redis fork.</summary>
public sealed class ValkeyFixture() : CacheServerFixture("valkey/valkey");

/// <summary>
/// Runs a real RESP-compatible cache server (Redis or Valkey) in Docker for the suite.
/// When Docker or the image is unavailable, <see cref="Available"/> stays false and tests
/// self-skip — the suite is opt-in and never fails on environment.
/// </summary>
public abstract class CacheServerFixture(string image) : IAsyncLifetime
{
    private string _containerId = "";

    public bool   Available        { get; private set; }
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        if (!await IsDockerAvailableAsync())
            return;

        // Pull first (separate, generous timeout) so a cold image doesn't fail `docker run`.
        await RunAsync("docker", ["pull", image], 300, ignoreFailure: true);

        var port = FreePort();
        var (exit, id, _) = await RunAsync("docker",
            ["run", "-d", "-p", $"127.0.0.1:{port}:6379", image], 60, ignoreFailure: true);
        if (exit != 0)
            return; // image unavailable / run failed → skip

        _containerId     = id.Trim();
        ConnectionString = $"127.0.0.1:{port}";
        try
        {
            await WaitForReadyAsync();
            Available = true;
        }
        catch
        {
            await DisposeAsync(); // clean up the half-started container
        }
    }

    public async Task DisposeAsync()
    {
        if (_containerId != "")
            await RunAsync("docker", ["rm", "-f", _containerId], 30, ignoreFailure: true);
    }

    private async Task WaitForReadyAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await using var mux = await ConnectionMultiplexer.ConnectAsync(
                    ConnectionString + ",abortConnect=false");
                if ((await mux.GetDatabase().PingAsync()) >= TimeSpan.Zero) return;
            }
            catch { /* not ready */ }
            await Task.Delay(500);
        }
        throw new TimeoutException($"{image} did not become ready within 30s.");
    }

    private static async Task<bool> IsDockerAvailableAsync()
    {
        var (exit, _, _) = await RunAsync("docker", ["info"], 15);
        return exit == 0;
    }

    private static int FreePort()
    {
        using var l = new TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static async Task<(int, string, string)> RunAsync(
        string exe, string[] args, int timeoutSeconds, bool ignoreFailure = false)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        Process? started;
        try { started = Process.Start(psi); }
        catch (Exception ex) { return (-1, "", ex.Message); } // e.g. docker not installed → treated as unavailable
        if (started is null) return (-1, "", "process did not start");

        using var p = started;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        try { await p.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)).Token); }
        catch (OperationCanceledException) when (ignoreFailure) { try { p.Kill(true); } catch { } }
        return (p.ExitCode, await stdout, await stderr);
    }
}
