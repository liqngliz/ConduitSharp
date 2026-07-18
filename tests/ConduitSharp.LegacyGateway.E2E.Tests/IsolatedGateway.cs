using System.Net.Sockets;

namespace ConduitSharp.LegacyGateway.E2E.Tests;

/// <summary>
/// Boots a real ConduitSharp.Host process in isolation: its own random port, its own
/// temp BasePath/plugins directory, and a caller-supplied routes.json. Used by the
/// hostile-config E2E tests, which need malicious configuration the shared LegacyGateway
/// stack can never carry (the gateway refuses to start with some of it — by design).
///
/// The host DLL is built once per test session and then executed directly
/// (`dotnet ConduitSharp.Host.dll`), so the gateway is a leaf process — no
/// `dotnet run`/MSBuild grandchildren holding pipes.
/// </summary>
public sealed class IsolatedGateway : IAsyncDisposable
{
    internal static readonly string RepoRoot = LocateRepoRoot();
    private static readonly Lazy<Task> HostBuilt = new(() =>
        BuildProjectAsync(Path.Combine(RepoRoot, "src", "ConduitSharp.Host")));

    private readonly Process _process;
    private readonly StringBuilder _output = new();

    public string     BaseDir { get; }
    public int        Port    { get; }
    public HttpClient Client  { get; }

    /// <summary>Combined stdout+stderr captured so far (complete after exit).</summary>
    public string Output { get { lock (_output) return _output.ToString(); } }

    public bool HasExited => _process.HasExited;
    public int  ExitCode  => _process.ExitCode;

    private IsolatedGateway(Process process, string baseDir, int port)
    {
        _process = process;
        BaseDir  = baseDir;
        Port     = port;
        Client   = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{port}"),
            Timeout     = TimeSpan.FromSeconds(10),
        };
    }

    /// <summary>
    /// Starts a gateway with the given routes.json. When <paramref name="expectStartupFailure"/>
    /// is false, waits until the gateway answers HTTP (any status). When true, waits for the
    /// process to exit instead — assert on <see cref="ExitCode"/> and <see cref="Output"/>.
    /// Files in <paramref name="pluginFiles"/> are copied into the plugins root before start —
    /// the drop-in path for global service backends (IRouteMatcher, ICacheService, …).
    /// </summary>
    public static async Task<IsolatedGateway> StartAsync(
        string routesJson,
        bool expectStartupFailure = false,
        IReadOnlyList<string>? pluginFiles = null)
    {
        await HostBuilt.Value;

        var baseDir = Path.Combine(Path.GetTempPath(), $"conduit-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(baseDir, "plugins"));

        foreach (var file in pluginFiles ?? [])
            File.Copy(file, Path.Combine(baseDir, "plugins", Path.GetFileName(file)));

        var routesPath = Path.Combine(baseDir, "routes.json");
        await File.WriteAllTextAsync(routesPath, routesJson);

        var port    = GetFreePort();
        var hostDll = Path.Combine(
            RepoRoot, "src", "ConduitSharp.Host", "bin", "Release", "net10.0", "ConduitSharp.Host.dll");

        var psi = new ProcessStartInfo("dotnet", $"\"{hostDll}\"")
        {
            WorkingDirectory       = baseDir,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        psi.Environment["ASPNETCORE_URLS"]      = $"http://localhost:{port}";
        psi.Environment["Gateway__RoutesPath"]  = routesPath;
        psi.Environment["Gateway__BasePath"]    = baseDir;
        psi.Environment["Gateway__PluginsPath"] = Path.Combine(baseDir, "plugins");
        psi.Environment.Remove("GATEWAY_CONFIG_FILE");

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start gateway process.");

        var gateway = new IsolatedGateway(process, baseDir, port);
        process.OutputDataReceived += (_, e) => gateway.Append(e.Data);
        process.ErrorDataReceived  += (_, e) => gateway.Append(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var deadline = DateTime.UtcNow.AddSeconds(60);
        if (expectStartupFailure)
        {
            while (!process.HasExited && DateTime.UtcNow < deadline)
                await Task.Delay(250);
            if (!process.HasExited)
                throw new TimeoutException(
                    $"Gateway was expected to fail startup but is still running.\n{gateway.Output}");
            return gateway;
        }

        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
                throw new InvalidOperationException(
                    $"Gateway exited during startup (code {process.ExitCode}).\n{gateway.Output}");
            try
            {
                // Any HTTP response (404 included) means the pipeline is serving.
                using var response = await gateway.Client.GetAsync("/__ready-probe");
                return gateway;
            }
            catch { await Task.Delay(500); }
        }

        throw new TimeoutException($"Gateway did not start within 60s.\n{gateway.Output}");
    }

    private void Append(string? line)
    {
        if (line is null) return;
        lock (_output) _output.AppendLine(line);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync(new CancellationTokenSource(5_000).Token);
            }
        }
        catch { /* teardown best-effort */ }
        _process.Dispose();

        try { Directory.Delete(BaseDir, recursive: true); } catch { }
    }

    // -------------------------------------------------------------------------

    internal static async Task BuildProjectAsync(string projectDir)
    {
        var psi = new ProcessStartInfo(
            "dotnet", $"build \"{projectDir}\" -c Release -v q")
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        using var process = Process.Start(psi)!;

        // Exit-gated drain: MSBuild node-reuse workers inherit the pipes and outlive
        // the build, so never wait for stream EOF (same lesson as LegacyGatewayFixture).
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAny(Task.WhenAll(stdout, stderr), Task.Delay(2_000));

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"dotnet build {Path.GetFileName(projectDir)} failed ({process.ExitCode}).\n" +
                $"{(stdout.IsCompletedSuccessfully ? stdout.Result : "")}\n" +
                $"{(stderr.IsCompletedSuccessfully ? stderr.Result : "")}");
    }

    internal static int GetFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Cannot locate repository root from test output directory.");
    }
}
