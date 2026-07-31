using ConduitSharp.Gateway.Middleware;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// The two buffering budgets in isolation. They meter different physical resources — RAM and spill
/// disk — and are independent, so the cases that matter are the handoff between them (a body that
/// outgrows RAM moves to disk) and the boundaries of each.
/// </summary>
[Trait("Category", "Security")]
public sealed class RequestBodyBudgetTests
{
    [Fact]
    public void RamBudget_WhileItHasHeadroom_HandsOutRam()
    {
        var budget = new RequestBodyBudget(maxRamBytes: 100, maxDiskBytes: 1000);

        Assert.Equal(100, budget.RamHeadroom);
        Assert.True(budget.TryReserveRam(60));
        Assert.Equal(40, budget.RamHeadroom);
    }

    [Fact]
    public void RamBudget_WhenFull_RefusesRamButDiskStillAdmits()
    {
        var budget = new RequestBodyBudget(maxRamBytes: 100, maxDiskBytes: 1000);

        Assert.True(budget.TryReserveRam(100));
        Assert.False(budget.TryReserveRam(1));
        Assert.Equal(0, budget.RamHeadroom);

        Assert.True(budget.TryReserveDisk(500));
    }

    [Fact]
    public void DiskBudget_WhenExhausted_RefusesAndThatIsThe503()
    {
        var budget = new RequestBodyBudget(maxRamBytes: 100, maxDiskBytes: 1000);

        Assert.True(budget.TryReserveDisk(1000));
        Assert.False(budget.TryReserveDisk(1));
    }

    [Fact]
    public void ReleaseRam_OnSpill_ReturnsRamWithoutTouchingTheDiskCharge()
    {
        var budget = new RequestBodyBudget(maxRamBytes: 100, maxDiskBytes: 1000);

        Assert.True(budget.TryReserveRam(100));
        Assert.True(budget.TryReserveDisk(100));
        Assert.Equal(0, budget.RamHeadroom);

        budget.ReleaseRam(100);

        Assert.Equal(100, budget.RamHeadroom);
        Assert.False(budget.TryReserveDisk(901));
    }

    [Fact]
    public void RamBudget_Zero_MeansNoRamAtAll_SoEverythingSpills()
    {
        var budget = new RequestBodyBudget(maxRamBytes: 0, maxDiskBytes: 1000);

        Assert.Equal(0, budget.RamHeadroom);
        Assert.False(budget.TryReserveRam(1));
        Assert.True(budget.TryReserveDisk(1000));
    }

    [Fact]
    public void DiskBudget_Zero_MeansNoSpilling_SoABodyMustFitRam()
    {
        var budget = new RequestBodyBudget(maxRamBytes: 100, maxDiskBytes: 0);

        Assert.True(budget.TryReserveRam(100));
        Assert.False(budget.TryReserveDisk(1));
    }

    [Fact]
    public void ReleaseDisk_ReturnsBytesToTheDiskBudget()
    {
        var budget = new RequestBodyBudget(maxRamBytes: 100, maxDiskBytes: 1000);

        Assert.True(budget.TryReserveDisk(1000));
        Assert.False(budget.TryReserveDisk(1));

        budget.ReleaseDisk(1000);

        Assert.True(budget.TryReserveDisk(1000));
    }

    [Fact]
    public async Task ConcurrentReserves_NeverOversubscribeEitherBudget()
    {
        const int max = 1000;
        var budget = new RequestBodyBudget(maxRamBytes: max, maxDiskBytes: max);

        var diskGranted = 0;
        var ramGranted  = 0;

        await Task.WhenAll(Enumerable.Range(0, Environment.ProcessorCount * 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                if (budget.TryReserveDisk(1)) Interlocked.Increment(ref diskGranted);
                if (budget.TryReserveRam(1))  Interlocked.Increment(ref ramGranted);
            }
        })));

        Assert.Equal(max, diskGranted);
        Assert.Equal(max, ramGranted);
    }
}
