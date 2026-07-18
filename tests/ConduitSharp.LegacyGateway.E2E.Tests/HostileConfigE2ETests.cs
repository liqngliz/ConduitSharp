namespace ConduitSharp.LegacyGateway.E2E.Tests;

/// <summary>
/// Security negatives that require hostile configuration, run against real isolated
/// gateway processes (one per test, own port, own temp BasePath). The shared LegacyGateway
/// stack can never carry these configs — S6's, by design, prevents startup entirely —
/// so each case spins up its own throwaway stack via <see cref="IsolatedGateway"/>.
///
/// Positive-path security behaviour (body limits, auth, error hygiene) lives in
/// <see cref="LegacyGatewayE2ETests"/> against the shipped routes.json.
/// </summary>
[Collection("Hostile config E2E")]
[Trait("Category", "E2E")]
public sealed class HostileConfigE2ETests
{
    [CollectionDefinition("Hostile config E2E", DisableParallelization = true)]
    public sealed class HostileConfigCollection;

    // =========================================================================
    // S2 — SSRF via swagger fetchFrom
    // =========================================================================

    [Fact]
    public async Task S2_FetchFromMetadataEndpoint_Returns403WithoutLeak()
    {
        await using var gw = await IsolatedGateway.StartAsync("""
            {
              "routes": [{
                "id": "ssrf-route",
                "route": { "match": { "path": "/api/x/{**rest}" } },
                "cluster": null,
                "swagger": { "fetchFrom": "http://169.254.169.254/latest/meta-data/" },
                "plugins": []
              }]
            }
            """);

        var response = await gw.Client.GetAsync("/swagger/ssrf-route.json");
        var body     = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("169.254", body);
    }

    // =========================================================================
    // S3 — specFile path traversal
    // =========================================================================

    [Fact]
    public async Task S3_SpecFileTraversal_Returns400WithoutFileContents()
    {
        await using var gw = await IsolatedGateway.StartAsync("""
            {
              "routes": [{
                "id": "traversal-route",
                "route": { "match": { "path": "/api/x/{**rest}" } },
                "cluster": null,
                "swagger": { "specFile": "../../../../../../../etc/hosts" },
                "plugins": []
              }]
            }
            """);

        var response = await gw.Client.GetAsync("/swagger/traversal-route.json");
        var body     = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("localhost", body); // /etc/hosts contents
        Assert.DoesNotContain("/etc/",     body); // resolved path echo
    }

    // =========================================================================
    // S5 — error bodies must not leak internal topology
    // =========================================================================

    [Fact]
    public async Task S5_FetchFromUnreachableUpstream_502BodyIsGeneric()
    {
        // Loopback is allowlisted by default, so the fetch is attempted and fails —
        // the 502 body must not reveal where the gateway tried to go.
        await using var gw = await IsolatedGateway.StartAsync("""
            {
              "routes": [{
                "id": "leak-route",
                "route": { "match": { "path": "/api/x/{**rest}" } },
                "cluster": null,
                "swagger": { "fetchFrom": "http://127.0.0.1:1/internal/openapi.json" },
                "plugins": []
              }]
            }
            """);

        var response = await gw.Client.GetAsync("/swagger/leak-route.json");
        var body     = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("127.0.0.1", body);
        Assert.DoesNotContain("internal/openapi", body);
    }

    // =========================================================================
    // S6 — hostile route ID must prevent startup, before any directory I/O
    // =========================================================================

    [Fact]
    public async Task S6_TraversalRouteId_GatewayRefusesToStart()
    {
        await using var gw = await IsolatedGateway.StartAsync("""
            {
              "routes": [{
                "id": "../../evil",
                "route": { "match": { "path": "/api/evil" } },
                "cluster": null,
                "plugins": []
              }]
            }
            """,
            expectStartupFailure: true);

        Assert.True(gw.HasExited);
        Assert.NotEqual(0, gw.ExitCode);
        Assert.Contains("Route IDs", gw.Output);

        // The traversal target must never have been created: validation runs before
        // SyncPluginDirectories touches the filesystem.
        var escaped = Path.GetFullPath(Path.Combine(gw.BaseDir, "plugins", "..", "..", "evil"));
        Assert.False(Directory.Exists(escaped),
            $"Traversal directory was created outside the plugins root: {escaped}");
    }
}
