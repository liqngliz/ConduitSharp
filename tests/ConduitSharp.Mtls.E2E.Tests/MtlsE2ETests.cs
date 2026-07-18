using Xunit.Abstractions;

namespace ConduitSharp.Mtls.E2E.Tests;

/// <summary>
/// End-to-end proof of the gateway's per-route client-certificate (mTLS) support: a real TLS
/// handshake against an nginx upstream that requires a client cert. The gateway configured with
/// the client cert must succeed (200, verify=SUCCESS); an identical gateway without it must be
/// rejected (400). Runs on any OS via <c>dotnet test</c> — certs are generated in-process and
/// Docker is driven through the CLI. Skips gracefully when Docker is unavailable.
/// </summary>
[Trait("Category", "E2E")]
public sealed class MtlsE2ETests(ITestOutputHelper output)
{
    [Fact]
    public async Task Gateway_with_client_cert_completes_mTLS_and_control_gateway_is_rejected()
    {
        var e2eDir = Path.Combine(FindRepoRoot(), "tests", "ConduitSharp.Mtls.E2E.Tests", "assets");
        var compose = Path.Combine(e2eDir, "docker-compose.mtls.yml");

        if (!await DockerAvailableAsync())
        {
            output.WriteLine("Docker not available — skipping mTLS E2E.");
            return;
        }

        CertificateMaterial.Generate(Path.Combine(e2eDir, "certs"));

        try
        {
            // Build the gateway image and start upstream + both gateways.
            await DockerAsync(e2eDir, ["compose", "-f", compose, "up", "-d", "--build"], timeoutSeconds: 600);

            await WaitForHealthAsync("http://127.0.0.1:8080/healthz");
            await WaitForHealthAsync("http://127.0.0.1:8081/healthz");

            using var http = new HttpClient();

            // With the client cert → the upstream verifies it and returns 200.
            var ok = await http.GetAsync("http://127.0.0.1:8080/");
            var okBody = await ok.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Contains("verify=SUCCESS", okBody);

            // Without the client cert → the upstream rejects the handshake (nginx 400).
            var denied = await http.GetAsync("http://127.0.0.1:8081/");
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }
        finally
        {
            await DockerAsync(e2eDir, ["compose", "-f", compose, "down", "-v", "--remove-orphans"],
                timeoutSeconds: 120, ignoreFailure: true);
        }
    }

    // -------------------------------------------------------------------------

    private static async Task WaitForHealthAsync(string url)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if ((await http.GetAsync(url)).IsSuccessStatusCode) return;
            }
            catch { /* not up yet */ }
            await Task.Delay(1000);
        }
        throw new TimeoutException($"Gateway health endpoint {url} did not become ready.");
    }

    private static async Task<bool> DockerAvailableAsync()
    {
        try
        {
            return await DockerAsync(Environment.CurrentDirectory, ["info"], timeoutSeconds: 20, ignoreFailure: true) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<int> DockerAsync(
        string workingDir, string[] arguments, int timeoutSeconds, bool ignoreFailure = false)
    {
        var psi = new ProcessStartInfo("docker")
        {
            WorkingDirectory       = workingDir,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the docker CLI (is Docker installed?).");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)).Token);

        if (!ignoreFailure && process.ExitCode != 0)
            throw new InvalidOperationException(
                $"`docker {string.Join(' ', arguments)}` exited {process.ExitCode}.\n" +
                $"stdout: {await stdout}\nstderr: {await stderr}");

        return process.ExitCode;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ConduitSharp.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root (ConduitSharp.sln).");
    }
}
