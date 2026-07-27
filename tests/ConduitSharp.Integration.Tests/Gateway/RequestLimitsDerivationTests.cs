using ConduitSharp.Gateway.Configuration;
using Microsoft.Extensions.Configuration;

namespace ConduitSharp.Integration.Tests.Gateway;

/// <summary>
/// The two buffering budgets meter different physical resources and are independent — neither is
/// derived from, nor clamped by, the other:
///   MaxRamBufferedBodyBytes  — heap held by buffered bodies and capture prefixes
///   MaxDiskBufferedBodyBytes — bytes resident in spill files
///
/// Regression guard: any coupling (deriving one from the other, or a single combined "total") is
/// unsafe. An operator raising the disk budget to suit a large spill volume would silently get a
/// proportionally larger RAM allowance and be OOM-killed instead of shedding.
/// </summary>
public sealed class RequestLimitsDerivationTests
{
    private static RequestLimitsOptions Bind(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => $"RequestLimits:{s.Key}", s => (string?)s.Value))
            .Build()
            .GetSection("RequestLimits")
            .Get<RequestLimitsOptions>() ?? new RequestLimitsOptions();

    [Fact]
    public void RaisingTheDiskBudget_DoesNotRaiseTheRamBudget()
    {
        // The safety property: 10 GiB of spill capacity must leave RAM at its own default.
        var limits = Bind(("MaxDiskBufferedBodyBytes", (10L * 1024 * 1024 * 1024).ToString()));

        Assert.Equal(10L * 1024 * 1024 * 1024, limits.MaxDiskBufferedBodyBytes);
        Assert.Equal(64 * 1024 * 1024, limits.MaxRamBufferedBodyBytes); // untouched
    }

    [Fact]
    public void RaisingTheRamBudget_DoesNotRaiseTheDiskBudget()
    {
        var limits = Bind(("MaxRamBufferedBodyBytes", (4L * 1024 * 1024 * 1024).ToString()));

        Assert.Equal(4L * 1024 * 1024 * 1024, limits.MaxRamBufferedBodyBytes);
        Assert.Equal(64 * 1024 * 1024, limits.MaxDiskBufferedBodyBytes); // untouched
    }

    [Fact]
    public void EachBudget_AcceptsZero_MeaningNoneOfThatResource()
    {
        // 0 reads the same way on both: none of that resource is available. RAM 0 => everything
        // spills; disk 0 => a body must fit RAM or be shed.
        var noRam  = Bind(("MaxRamBufferedBodyBytes", "0"));
        var noDisk = Bind(("MaxDiskBufferedBodyBytes", "0"));

        Assert.Equal(0, noRam.MaxRamBufferedBodyBytes);
        Assert.Equal(0, noDisk.MaxDiskBufferedBodyBytes);
    }

    [Fact]
    public void AllDefaults_AreSizedForASmallContainer()
    {
        var limits = new RequestLimitsOptions();
        Assert.Equal(64 * 1024 * 1024, limits.MaxRamBufferedBodyBytes);
        Assert.Equal(64 * 1024 * 1024, limits.MaxDiskBufferedBodyBytes);
        Assert.Equal(1024 * 1024, limits.RamBufferThresholdBytes);
        Assert.Equal(8 * 1024 * 1024, limits.MaxRequestBodyBytes);
    }
}
