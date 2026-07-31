using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace ConduitSharp.E2E.Shared;

/// <summary>
/// Boots one gateway-example stack as real OS processes and tears it down — the shared
/// lifecycle every example E2E suite needs. The three stacks (LegacyGateway,
/// EmbeddedGateway, EmbeddedGatewayPrefixed) differ only in which example directory to
/// launch, which ports they bind, and whether gateway paths carry an "/api" prefix; a
/// concrete fixture supplies those four facts and inherits everything else — clean →
/// build+start → readiness poll → JWT mint → stop.
///
/// Platform:
///   macOS / Linux  →  make clean / make run / make stop
///   Windows        →  pwsh start.ps1 (-Stop) + manual dir wipe
/// </summary>
public abstract class GatewayProcessFixture : IAsyncLifetime, IGatewayE2EFixture
{
    private const string SigningKeyBase64 = "ZGVtby1zaWduaW5nLWtleS1jb25kdWl0c2hhcnAtZXhhbXBsZS0zMmNo";

    /// <summary>Directory under examples/ to launch, e.g. "EmbeddedGateway".</summary>
    protected abstract string ExampleDirName { get; }
    protected abstract int GatewayPort { get; }
    protected abstract int GrpcPort { get; }
    public abstract string PathPrefix { get; }
    public abstract (string A, string B) InventoryUpstreamPorts { get; }

    private string GatewayUrl => $"http://localhost:{GatewayPort}";
    public string GrpcUrl => $"http://localhost:{GrpcPort}";

    private string? _exampleRoot;
    public string ExampleRoot => _exampleRoot ??= LocateExampleRoot(ExampleDirName);

    public HttpClient Client  { get; private set; } = null!;
    public string     DemoJwt { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await CleanAsync();
        await StartAsync();
        await WaitForGatewayAsync(timeoutSeconds: 120);
        AssertYarpForwarderIsServing();

        DemoJwt = MintDemoJwt();
        Client  = new HttpClient
        {
            BaseAddress = new Uri(GatewayUrl),
            Timeout     = TimeSpan.FromSeconds(15),
        };
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        await StopAsync();
    }

    private async Task CleanAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            await StopAsync();
            foreach (var dir in new[] { "bin", "logs", Path.Combine("gateway", "plugins") })
            {
                var path = Path.Combine(ExampleRoot, dir);
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
        }
        else
        {
            await RunAsync("make", "clean", ExampleRoot);
        }
    }

    private Task StartAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            var ps1 = Path.Combine(ExampleRoot, "start.ps1");
            return RunAsync("pwsh", $"-NonInteractive -NoProfile -File \"{ps1}\"", ExampleRoot);
        }
        return RunAsync("make", "run", ExampleRoot);
    }

    private Task StopAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            var ps1 = Path.Combine(ExampleRoot, "start.ps1");
            return RunAsync("pwsh", $"-NonInteractive -NoProfile -File \"{ps1}\" -Stop", ExampleRoot);
        }
        return RunAsync("make", "stop", ExampleRoot, ignoreFailure: true);
    }

    private static async Task RunAsync(
        string executable,
        string arguments,
        string workingDir,
        bool ignoreFailure = false)
    {
        var psi = new ProcessStartInfo(executable, arguments)
        {
            WorkingDirectory       = workingDir,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {executable}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAny(Task.WhenAll(stdoutTask, stderrTask), Task.Delay(2_000));

        static string Drain(Task<string> t) =>
            t.IsCompletedSuccessfully ? t.Result : "(stream still held open by grandchild processes)";

        if (!ignoreFailure && process.ExitCode != 0)
            throw new InvalidOperationException(
                $"`{executable} {arguments}` exited with code {process.ExitCode}.\n" +
                $"stdout: {Drain(stdoutTask)}\nstderr: {Drain(stderrTask)}");
    }

    private async Task WaitForGatewayAsync(int timeoutSeconds)
    {
        using var http     = new HttpClient();
        var       deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var       healthUrl = GatewayUrl + PathPrefix + "/health";

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await http.GetAsync(healthUrl).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch { }

            await Task.Delay(1_000).ConfigureAwait(false);
        }

        var logPath = Path.Combine(ExampleRoot, "logs", "gateway.log");
        var tail    = File.Exists(logPath)
            ? string.Join('\n', File.ReadLines(logPath).TakeLast(30))
            : "(log not found)";

        throw new TimeoutException(
            $"Gateway at {healthUrl} did not become ready within {timeoutSeconds}s.\n" +
            $"Last 30 lines of gateway.log:\n{tail}");
    }

    private void AssertYarpForwarderIsServing()
    {
        var logPath = Path.Combine(ExampleRoot, "logs", "gateway.log");
        var log = File.Exists(logPath) ? File.ReadAllText(logPath) : "";

        Assert.Contains("Yarp.ReverseProxy.Forwarder.HttpForwarder", log, StringComparison.Ordinal);
    }

    private static string MintDemoJwt()
    {
        var keyBytes = Convert.FromBase64String(SigningKeyBase64);
        var now      = DateTimeOffset.UtcNow;

        var header  = Base64UrlEncode("""{"alg":"HS256","typ":"JWT"}""");
        var payload = Base64UrlEncode(
            $"{{\"sub\":\"demo-user\",\"iss\":\"conduitsharp-demo\",\"aud\":\"conduitsharp-demo\"," +
            $"\"iat\":{now.ToUnixTimeSeconds()},\"exp\":{now.AddHours(1).ToUnixTimeSeconds()}," +
            $"\"name\":\"Demo User\",\"role\":\"analyst\"}}");

        var sigInput = Encoding.ASCII.GetBytes($"{header}.{payload}");
        using var hmac = new HMACSHA256(keyBytes);
        var sig = Base64UrlEncode(hmac.ComputeHash(sigInput));

        return $"{header}.{payload}.{sig}";
    }

    private static string Base64UrlEncode(string input) =>
        Base64UrlEncode(Encoding.UTF8.GetBytes(input));

    private static string Base64UrlEncode(byte[] input) =>
        Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string LocateExampleRoot(string exampleDirName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
            {
                var candidate = Path.Combine(dir.FullName, "examples", exampleDirName);
                if (Directory.Exists(candidate))
                    return candidate;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"Cannot locate examples/{exampleDirName} from the test output directory. " +
            "Run tests from within the ConduitSharp repository.");
    }
}
