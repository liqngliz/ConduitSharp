using ConduitSharp.E2E.Shared;

namespace ConduitSharp.EmbeddedGatewayPrefixed.E2E.Tests;

/// <summary>
/// End-to-end tests for the EmbeddedGatewayPrefixed example: the shared gateway
/// contract (<see cref="GatewayE2ETestsBase"/>) under a "/api" path prefix, plus
/// what only this stack exercises — host-owned root path and standard ASP.NET Core
/// middleware wrapping the gateway.
///
/// The fixture handles: make clean → make run → gateway ready → dispose with make stop.
/// Each test uses a real HttpClient pointed at http://localhost:7050.
///
/// Run via:
///   cd examples/EmbeddedGatewayPrefixed && make test-e2e
///   dotnet test tests/ConduitSharp.EmbeddedGatewayPrefixed.E2E.Tests
/// </summary>
[Collection("EmbeddedGatewayPrefixed E2E")]
[Trait("Category", "E2E")]
public sealed class EmbeddedGatewayPrefixedE2ETests(EmbeddedGatewayPrefixedFixture fx) : GatewayE2ETestsBase(fx)
{

    [Fact]
    public async Task PostUpload_WithStreamOnly_Succeeds_AndNoBodyCapture()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/upload/file");
        request.Content = new StringContent(
            """{"upload":"video"}""",
            Encoding.UTF8, "application/json");

        var response = await Fx.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var logPath = Path.Combine(Fx.ExampleRoot, "logs", "gateway.log");
        if (File.Exists(logPath))
        {
            var content = await ReadSharedAsync(logPath);
            Assert.DoesNotContain("""Captured request body for path /api/upload/file""", content);
        }
    }

    [Fact]
    public async Task Gateway_ExecutesStandardAspNetCoreMiddleware()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        var response = await Fx.Client.SendAsync(request);

        response.EnsureSuccessStatusCode();

        Assert.True(response.Headers.Contains("X-Standard-Middleware"),
            "The standard ASP.NET middleware did not execute or set the header.");
        Assert.Equal("Executed", response.Headers.GetValues("X-Standard-Middleware").First());
    }

    [Fact]
    public async Task GetRoot_ReturnsHI()
    {
        var response = await Fx.Client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("HI", content);
    }
}
