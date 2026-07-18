namespace ConduitSharp.Grafana.E2E.Tests;

/// <summary>
/// Shared fixture for the Grafana-stack observability E2E suite.
///
/// Lifecycle (IAsyncLifetime):
///   InitializeAsync — docker compose up (grafana stack + e2e override) → poll until
///                     gateway + Tempo + Prometheus + Loki are ready → seed traffic
///   DisposeAsync    — docker compose down -v
///
/// When no Docker daemon is available, <see cref="DockerAvailable"/> is false and
/// every test returns early (same pattern as the pwsh-dependent LegacyGateway tests).
/// </summary>
[CollectionDefinition("Grafana E2E", DisableParallelization = true)]
public sealed class GrafanaStackCollection : ICollectionFixture<GrafanaStackFixture>;

public sealed class GrafanaStackFixture : IAsyncLifetime
{
    public const string GatewayUrl    = "http://localhost:5850";
    public const string TempoUrl      = "http://localhost:3200";
    public const string PrometheusUrl = "http://localhost:9090";
    public const string LokiUrl       = "http://localhost:3100";
    public const string ApiKey        = "demo-api-key-conduitsharp-example";

    private static readonly string LegacyGatewayRoot = LocateLegacyGatewayRoot();

    private static readonly string[] ComposeArgs =
    [
        "compose",
        "-f", "docker-compose.grafana.yml",
        "-f", "docker-compose.e2e.yml",
    ];

    public bool       DockerAvailable { get; private set; }
    public HttpClient Client          { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        DockerAvailable = await IsDockerAvailableAsync();
        if (!DockerAvailable)
            return;

        Client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        await RunDockerAsync([.. ComposeArgs, "up", "--build", "-d"], timeoutSeconds: 900);

        await WaitForAsync("gateway",    $"{GatewayUrl}/health",       timeoutSeconds: 180);
        await WaitForAsync("Tempo",      $"{TempoUrl}/ready",          timeoutSeconds: 120);
        await WaitForAsync("Prometheus", $"{PrometheusUrl}/-/ready",   timeoutSeconds: 120);
        await WaitForAsync("Loki",       $"{LokiUrl}/ready",           timeoutSeconds: 180);
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (DockerAvailable)
            await RunDockerAsync([.. ComposeArgs, "down", "-v"], timeoutSeconds: 180, ignoreFailure: true);
    }

    // -------------------------------------------------------------------------
    // Docker helpers
    // -------------------------------------------------------------------------

    private static async Task<bool> IsDockerAvailableAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("docker", "info")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return false;
            await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task RunDockerAsync(
        string[] arguments, int timeoutSeconds, bool ignoreFailure = false)
    {
        var psi = new ProcessStartInfo("docker")
        {
            WorkingDirectory       = LegacyGatewayRoot,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start docker.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)).Token);

        if (!ignoreFailure && process.ExitCode != 0)
            throw new InvalidOperationException(
                $"`docker {string.Join(' ', arguments)}` exited with code {process.ExitCode}.\n" +
                $"stdout: {await stdoutTask}\nstderr: {await stderrTask}");
    }

    private async Task WaitForAsync(string name, string url, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await Client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch { /* not ready yet */ }

            await Task.Delay(1_000);
        }

        throw new TimeoutException($"{name} at {url} did not become ready within {timeoutSeconds}s.");
    }

    // -------------------------------------------------------------------------
    // Polling helper for the backend query APIs — telemetry lands asynchronously
    // (gateway export → collector batch (5s) → backend ingest).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Polls <paramref name="url"/> until <paramref name="hasData"/> returns true for
    /// the response JSON, or the deadline passes. Returns the last response body either way.
    /// </summary>
    public async Task<(bool Found, string LastBody)> PollUntilAsync(
        string url, Func<JsonElement, bool> hasData, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var lastBody = "(no response)";
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await Client.GetAsync(url);
                lastBody = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(lastBody);
                    if (hasData(doc.RootElement))
                        return (true, lastBody);
                }
            }
            catch { /* transient — keep polling */ }

            await Task.Delay(2_000);
        }
        return (false, lastBody);
    }

    // -------------------------------------------------------------------------
    // Solution-root discovery
    // -------------------------------------------------------------------------

    private static string LocateLegacyGatewayRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
            {
                var candidate = Path.Combine(dir.FullName, "examples", "LegacyGateway");
                if (Directory.Exists(candidate))
                    return candidate;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Cannot locate examples/LegacyGateway from the test output directory. " +
            "Run tests from within the ConduitSharp repository.");
    }
}
