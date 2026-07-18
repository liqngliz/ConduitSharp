using ConduitSharp.Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// Verifies the graceful-shutdown drain window (R5): the configured
/// Gateway:ShutdownTimeoutSeconds is applied to the host's shutdown timeout, so
/// in-flight requests are drained rather than cut off when the process stops
/// (including on admin route reload).
/// </summary>
public sealed class ShutdownDrainTests : IAsyncLifetime
{
    private FakeUpstream _upstream = null!;

    public async Task InitializeAsync() => _upstream = await FakeUpstream.StartAsync();
    public async Task DisposeAsync()    => await _upstream.DisposeAsync();

    [Fact]
    public async Task ConfiguredShutdownTimeout_IsAppliedToHost()
    {
        // Set via env var, not the in-memory override: UseShutdownTimeout reads the
        // options eagerly at host-build time (before WebApplicationFactory's in-memory
        // config is layered in), and env vars are already present at that point —
        // exactly as in production.
        Environment.SetEnvironmentVariable("Gateway__ShutdownTimeoutSeconds", "17");
        try
        {
            await using var factory = await GatewayFactory.CreateAsync(_upstream);
            using var client = factory.CreateClient(); // force the host to build

            var hostOptions = factory.Services.GetRequiredService<IOptions<HostOptions>>().Value;

            Assert.Equal(TimeSpan.FromSeconds(17), hostOptions.ShutdownTimeout);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Gateway__ShutdownTimeoutSeconds", null);
        }
    }

    [Fact]
    public async Task DefaultShutdownTimeout_Is30Seconds()
    {
        await using var factory = await GatewayFactory.CreateAsync(_upstream);
        using var client = factory.CreateClient();

        var hostOptions = factory.Services.GetRequiredService<IOptions<HostOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(30), hostOptions.ShutdownTimeout);
    }
}
