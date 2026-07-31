using ConduitSharp.Integration.Tests.Fixtures;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// v2.0.0 replaced one RAM+disk total with two independent per-resource budgets. The removed keys
/// must fail the host at startup rather than being ignored: a config that still looks deliberate
/// while silently running on defaults is exactly the failure this change exists to prevent, and its
/// symptom is an OOM-kill rather than a 503.
/// </summary>
public sealed class RemovedRequestLimitKeysTests
{
    private static async Task<Exception> StartWithAsync(string key, string value)
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var factory  = await GatewayFactory.CreateAsync(upstream, settings: new Dictionary<string, string?>
        {
            [$"Gateway:RequestLimits:{key}"] = value,
        });

        return await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync("/x");
        });
    }

    [Fact]
    public async Task MaxTotalBufferedBodyBytes_IsRejected_AndNamesBothReplacements()
    {
        var ex = await StartWithAsync("MaxTotalBufferedBodyBytes", "134217728");

        Assert.Contains("MaxTotalBufferedBodyBytes was removed in v2.0.0", ex.ToString(), StringComparison.Ordinal);
        Assert.Contains("MaxDiskBufferedBodyBytes", ex.ToString(), StringComparison.Ordinal);
        Assert.Contains("MaxRamBufferedBodyBytes", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MaxMemoryBufferedBodyBytes_IsRejected_AndNamesItsRename()
    {
        var ex = await StartWithAsync("MaxMemoryBufferedBodyBytes", "67108864");

        Assert.Contains("MaxRamBufferedBodyBytes", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemoryBufferThresholdBytes_IsRejected_AndNamesItsRename()
    {
        var ex = await StartWithAsync("MemoryBufferThresholdBytes", "1048576");

        Assert.Contains("RamBufferThresholdBytes", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NegativeBudget_IsRejected_RatherThanSilentlyCoerced()
    {
        var ex = await StartWithAsync("MaxRamBufferedBodyBytes", "-1");

        Assert.Contains("cannot be negative", ex.ToString(), StringComparison.Ordinal);
    }
}
