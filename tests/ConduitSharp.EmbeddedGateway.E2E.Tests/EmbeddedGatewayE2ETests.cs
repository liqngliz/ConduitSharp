using ConduitSharp.E2E.Shared;

namespace ConduitSharp.EmbeddedGateway.E2E.Tests;

/// <summary>
/// End-to-end tests for the EmbeddedGateway example: the shared gateway contract
/// (<see cref="GatewayE2ETestsBase"/>) plus the plain streamOnly upload route,
/// which carries no body capture here.
///
/// The fixture handles: make clean → make run → gateway ready → dispose with make stop.
/// Each test uses a real HttpClient pointed at http://localhost:6050.
///
/// Run via:
///   cd examples/EmbeddedGateway && make test-e2e
///   dotnet test tests/ConduitSharp.EmbeddedGateway.E2E.Tests
/// </summary>
[Collection("EmbeddedGateway E2E")]
[Trait("Category", "E2E")]
public sealed class EmbeddedGatewayE2ETests(EmbeddedGatewayFixture fx) : GatewayE2ETestsBase(fx)
{
    // =========================================================================
    // Uploads — streamOnly, no body capture
    // =========================================================================

    [Fact]
    public async Task PostUpload_WithStreamOnly_Succeeds_AndNoBodyCapture()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/upload/file");
        request.Content = new StringContent(
            """{"upload":"video"}""",
            Encoding.UTF8, "application/json");

        var response = await Fx.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify that BodyCapture did NOT log this route (it's not configured on the upload route)
        var logPath = Path.Combine(Fx.ExampleRoot, "logs", "gateway.log");
        if (File.Exists(logPath))
        {
            var content = await ReadSharedAsync(logPath);
            Assert.DoesNotContain("""Captured request body for path /api/upload/file""", content);
        }
    }
}
