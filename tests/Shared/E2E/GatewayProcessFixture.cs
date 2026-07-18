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
    // HS256 signing key from every example's routes.json (base64) — same demo credentials.
    private const string SigningKeyBase64 = "ZGVtby1zaWduaW5nLWtleS1jb25kdWl0c2hhcnAtZXhhbXBsZS0zMmNo";

    // ---- What each concrete stack supplies -----------------------------------
    /// <summary>Directory under examples/ to launch, e.g. "EmbeddedGateway".</summary>
    protected abstract string ExampleDirName { get; }
    protected abstract int GatewayPort { get; }
    protected abstract int GrpcPort { get; }
    public abstract string PathPrefix { get; }
    public abstract (string A, string B) InventoryUpstreamPorts { get; }

    // ---- Derived / shared ----------------------------------------------------
    private string GatewayUrl => $"http://localhost:{GatewayPort}";
    public string GrpcUrl => $"http://localhost:{GrpcPort}";

    private string? _exampleRoot;
    public string ExampleRoot => _exampleRoot ??= LocateExampleRoot(ExampleDirName);

    public HttpClient Client  { get; private set; } = null!;
    public string     DemoJwt { get; private set; } = "";

    // ---- IAsyncLifetime ------------------------------------------------------

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

    // ---- Launcher ------------------------------------------------------------

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
        // Ignore errors — nothing may be running on the first clean.
        return RunAsync("make", "stop", ExampleRoot, ignoreFailure: true);
    }

    // Runs an external process and waits for it to exit.
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

        // Read both streams concurrently (sequential reads can deadlock when stderr
        // fills its pipe buffer), and gate on process EXIT rather than stream EOF:
        // long-lived grandchildren — the services themselves, or MSBuild node-reuse
        // workers spawned by dotnet publish — inherit the launcher's stdout pipe and
        // keep it open long after the launcher exits, so waiting for EOF hangs forever.
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

    // ---- Readiness -----------------------------------------------------------

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
            catch { /* not ready yet */ }

            await Task.Delay(1_000).ConfigureAwait(false);
        }

        // Dump gateway log to help diagnose startup failures.
        var logPath = Path.Combine(ExampleRoot, "logs", "gateway.log");
        var tail    = File.Exists(logPath)
            ? string.Join('\n', File.ReadLines(logPath).TakeLast(30))
            : "(log not found)";

        throw new TimeoutException(
            $"Gateway at {healthUrl} did not become ready within {timeoutSeconds}s.\n" +
            $"Last 30 lines of gateway.log:\n{tail}");
    }

    // The readiness probe above already forwarded /health upstream, so YARP's forwarder must have
    // logged it. Guards against the gateway silently falling back to some other engine — forwarding
    // is YARP's ForwarderMiddleware now, not a swappable "http-proxy" plugin.
    private void AssertYarpForwarderIsServing()
    {
        var logPath = Path.Combine(ExampleRoot, "logs", "gateway.log");
        var log = File.Exists(logPath) ? File.ReadAllText(logPath) : "";

        Assert.Contains("Yarp.ReverseProxy.Forwarder.HttpForwarder", log, StringComparison.Ordinal);
    }

    // ---- JWT minting — same algorithm as generate-token.sh / generate-token.ps1

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

    // ---- Solution-root discovery ---------------------------------------------

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
