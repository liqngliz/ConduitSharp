using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ConduitSharp.PluginChain.E2E.Tests;

/// <summary>
/// Boots the real ConduitSharp.Host as an OS process with the order-probe plugin dropped into a
/// plugins folder as a compiled DLL (loaded by the folder scanner, not registered in code), forwards
/// to a real upstream, and captures the host's stdout. Probe execution order is read from the actual
/// JSON log records the process emits.
/// </summary>
public sealed class GatewayHostProcessFixture : IAsyncLifetime
{
    private readonly string _root = FindRepoRoot();
    private string _work = "";
    private Process? _host;
    private WebApplication? _upstream;
    private int _upstreamHits;

    private readonly ConcurrentQueue<(string Id, string Message)> _probeEvents = new();
    private readonly List<string> _rawStdout = [];
    private readonly object _stdoutLock = new();

    public HttpClient Client { get; private set; } = null!;
    public int UpstreamHits => Volatile.Read(ref _upstreamHits);

    /// <summary>Probe events (id + message) captured since the last <see cref="ClearProbeEvents"/>.</summary>
    public string[] ProbeSequence() => _probeEvents.Select(e => $"{e.Id}:{e.Message}").ToArray();
    public void ClearProbeEvents() => _probeEvents.Clear();

    public async Task InitializeAsync()
    {
        _work = Directory.CreateTempSubdirectory("csharp-pluginchain-e2e-").FullName;
        var pluginsDir = Path.Combine(_work, "plugins", "order-probe");

        // 1. Publish the probe project into the plugins folder as a dropped-in DLL.
        Run("dotnet", $"publish \"{Path.Combine(_root, "examples/ConduitSharp.Plugin.OrderProbe/src/ConduitSharp.Plugin.OrderProbe/ConduitSharp.Plugin.OrderProbe.csproj")}\" -c Debug -o \"{pluginsDir}\" -v q");

        // 2. Real upstream: 200 with a small cacheable body; counts hits so a cache hit is provable.
        var ub = WebApplication.CreateBuilder();
        ub.WebHost.UseUrls("http://127.0.0.1:0");
        ub.Logging.ClearProviders();
        _upstream = ub.Build();
        _upstream.MapGet("/{**rest}", (HttpContext _) =>
        {
            Interlocked.Increment(ref _upstreamHits);
            return Results.Text("ok", "text/plain");
        });
        await _upstream.StartAsync();
        var upstreamUrl = _upstream.Urls.First();

        // 3. routes.json: probe-a(1), probe-b(2), probe-c(3), cache(4), probe-e(5), then the forward.
        var routesPath = Path.Combine(_work, "routes.json");
        await File.WriteAllTextAsync(routesPath, $$"""
        {
          "routes": [{
            "id": "chain",
            "route": { "match": { "path": "/{**rest}" } },
            "cluster": {
              "loadBalancingPolicy": "RoundRobin",
              "destinations": { "node-0": { "address": "{{upstreamUrl}}" } },
              "httpRequest": { "activityTimeout": "00:00:05" }
            },
            "plugins": [
              { "name": "custom", "variant": "probe-a", "order": 1 },
              { "name": "custom", "variant": "probe-b", "order": 2 },
              { "name": "custom", "variant": "probe-c", "order": 3 },
              { "name": "cache",  "order": 4, "config": { "ttlSeconds": 60, "varyByHeaders": [], "maxCacheableBytes": 1048576 } },
              { "name": "custom", "variant": "probe-e", "order": 5 }
            ]
          }]
        }
        """);

        // 4. Spawn the host, pointed at the temp routes + plugins, logging JSON to stdout.
        var port = FreePort();
        var psi = new ProcessStartInfo("dotnet",
            $"run --project \"{Path.Combine(_root, "src/ConduitSharp.Host/ConduitSharp.Host.csproj")}\" -c Debug --no-launch-profile")
        {
            WorkingDirectory        = Path.Combine(_root, "src/ConduitSharp.Host"),
            RedirectStandardOutput  = true,
            RedirectStandardError   = true,
            UseShellExecute         = false,
        };
        psi.Environment["Gateway__RoutesPath"]            = routesPath;
        psi.Environment["Gateway__PluginsPath"]           = Path.Combine(_work, "plugins");
        psi.Environment["ASPNETCORE_URLS"]                = $"http://127.0.0.1:{port}";
        psi.Environment["ASPNETCORE_ENVIRONMENT"]         = "Production";
        psi.Environment["Logging__Console__FormatterName"]= "Json";
        psi.Environment["Logging__LogLevel__Default"]     = "Information";
        psi.Environment["Logging__LogLevel__Probe"]       = "Information";

        _host = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _host.OutputDataReceived += (_, e) => OnStdout(e.Data);
        _host.ErrorDataReceived  += (_, e) => OnStdout(e.Data);
        _host.Start();
        _host.BeginOutputReadLine();
        _host.BeginErrorReadLine();

        Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(15) };
        await WaitForHealthAsync(TimeSpan.FromSeconds(150));
    }

    private void OnStdout(string? line)
    {
        if (string.IsNullOrEmpty(line)) return;
        lock (_stdoutLock) _rawStdout.Add(line);

        // The Json console formatter emits one JSON object per record. Pull probe records out.
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("Category", out var cat)
                && cat.GetString() is { } category
                && category.StartsWith("Probe.", StringComparison.Ordinal)
                && doc.RootElement.TryGetProperty("Message", out var msg))
            {
                _probeEvents.Enqueue((category["Probe.".Length..], msg.GetString() ?? ""));
            }
        }
        catch (JsonException) { /* build noise / non-JSON lines */ }
    }

    private async Task WaitForHealthAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_host!.HasExited)
                throw new InvalidOperationException($"host exited early (code {_host.ExitCode}).\n{StdoutTail()}");
            try
            {
                var r = await Client.GetAsync("/healthz");
                if (r.IsSuccessStatusCode) return;
            }
            catch { /* not up yet */ }
            await Task.Delay(500);
        }
        throw new TimeoutException($"host did not become healthy within {timeout.TotalSeconds}s.\n{StdoutTail()}");
    }

    public string StdoutTail(int lines = 40)
    {
        lock (_stdoutLock) return string.Join("\n", _rawStdout.TakeLast(lines));
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_host is { HasExited: false }) { _host.Kill(entireProcessTree: true); _host.WaitForExit(5000); }
        _host?.Dispose();
        if (_upstream is not null) await _upstream.DisposeAsync();
        try { Directory.Delete(_work, recursive: true); } catch { /* best effort */ }
    }

    private static int FreePort()
    {
        var l = new TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static void Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        })!;
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"`{exe} {args}` failed ({p.ExitCode}):\n{stderr}");
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "ConduitSharp.sln")))
            dir = Directory.GetParent(dir)?.FullName;
        return dir ?? throw new InvalidOperationException("ConduitSharp.sln not found above the test binary.");
    }
}

[CollectionDefinition("PluginChain host process")]
public sealed class GatewayHostProcessCollection : ICollectionFixture<GatewayHostProcessFixture>;
