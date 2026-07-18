using ConduitSharp.Gateway.Middleware;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// The two-tier buffering budget in isolation. The tiers exist so the gateway steps down under
/// pressure — RAM while it lasts, disk after that, 503 only when both are gone — so the cases that
/// matter are the transitions between them.
/// </summary>
[Trait("Category", "Security")]
public sealed class RequestBodyBudgetTests
{
    [Fact]
    public void MemoryTier_WhileItHasHeadroom_HandsOutRam()
    {
        var budget = new RequestBodyBudget(maxTotalBytes: 1000, maxMemoryBytes: 100);

        Assert.Equal(100, budget.MemoryHeadroom);
        Assert.True(budget.TryReserveMemory(60));
        Assert.Equal(40, budget.MemoryHeadroom);
    }

    [Fact]
    public void MemoryTier_WhenFull_RefusesRamButTotalStillAdmits()
    {
        // The step-down itself: no RAM left, so the body spills — but it is still served, because
        // the combined budget has room. A refusal here is routine, not an error.
        var budget = new RequestBodyBudget(maxTotalBytes: 1000, maxMemoryBytes: 100);

        Assert.True(budget.TryReserveMemory(100));
        Assert.False(budget.TryReserveMemory(1));
        Assert.Equal(0, budget.MemoryHeadroom);

        Assert.True(budget.TryReserve(500)); // spilled bytes still fit the total → no 503
    }

    [Fact]
    public void TotalTier_WhenExhausted_RefusesEvenThoughItIsTheLastResort()
    {
        var budget = new RequestBodyBudget(maxTotalBytes: 1000, maxMemoryBytes: 100);

        Assert.True(budget.TryReserve(1000));
        Assert.False(budget.TryReserve(1)); // this is the 503
    }

    [Fact]
    public void ReleaseMemory_OnSpill_ReturnsRamToTheTierWithoutTouchingTheTotal()
    {
        // What BufferRequestBody does when a body outgrows its threshold: the rented buffer went
        // back to the pool, so the RAM is genuinely free — but the bytes still occupy the total,
        // they just live on disk now.
        var budget = new RequestBodyBudget(maxTotalBytes: 1000, maxMemoryBytes: 100);

        Assert.True(budget.TryReserveMemory(100));
        Assert.True(budget.TryReserve(100));
        Assert.Equal(0, budget.MemoryHeadroom);

        budget.ReleaseMemory(100);

        Assert.Equal(100, budget.MemoryHeadroom); // RAM freed for the next request
        Assert.False(budget.TryReserve(901));     // total still holds the spilled bytes
    }

    [Fact]
    public void MemoryTier_DisabledByNonPositiveLimit_SpillsEverything()
    {
        // Non-positive limits disable the two tiers in opposite directions, which is the one
        // genuinely surprising thing about this type: no memory tier means no RAM at all…
        var budget = new RequestBodyBudget(maxTotalBytes: 1000, maxMemoryBytes: 0);

        Assert.Equal(0, budget.MemoryHeadroom);
        Assert.False(budget.TryReserveMemory(1));
        Assert.True(budget.TryReserve(1000)); // …while the total still bounds the spill
    }

    [Fact]
    public void TotalTier_DisabledByNonPositiveLimit_IsUnlimited()
    {
        // …whereas no total limit means unlimited, not zero.
        var budget = new RequestBodyBudget(maxTotalBytes: 0, maxMemoryBytes: 100);

        Assert.True(budget.TryReserve(long.MaxValue));
    }

    [Fact]
    public void Release_ReturnsBytesToTheTotal()
    {
        var budget = new RequestBodyBudget(maxTotalBytes: 1000, maxMemoryBytes: 100);

        Assert.True(budget.TryReserve(1000));
        Assert.False(budget.TryReserve(1));

        budget.Release(1000);

        Assert.True(budget.TryReserve(1000)); // the finally in BufferRequestBody
    }

    [Fact]
    public async Task ConcurrentReserves_NeverOversubscribeEitherTier()
    {
        // The tiers are the memory bound. If the CAS loop is wrong under contention the bound is
        // decorative, so hammer both from every core at once.
        const int max = 1000;
        var budget = new RequestBodyBudget(maxTotalBytes: max, maxMemoryBytes: max);

        var totalGranted  = 0;
        var memoryGranted = 0;

        await Task.WhenAll(Enumerable.Range(0, Environment.ProcessorCount * 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                if (budget.TryReserve(1)) Interlocked.Increment(ref totalGranted);
                if (budget.TryReserveMemory(1)) Interlocked.Increment(ref memoryGranted);
            }
        })));

        Assert.Equal(max, totalGranted);
        Assert.Equal(max, memoryGranted);
    }
}
