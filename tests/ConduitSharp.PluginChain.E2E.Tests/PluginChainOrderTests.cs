using Xunit.Abstractions;

namespace ConduitSharp.PluginChain.E2E.Tests;

/// <summary>
/// The plugin chain order and cache short-circuit, proven against a real spawned ConduitSharp.Host
/// with the probe plugins loaded as a dropped-in DLL. Order is read from the process's own JSON logs.
///
/// Route: probe-a(1), probe-b(2), probe-c(3), cache(4), probe-e(5), then the YARP forward. probe-e
/// sits in front of the forward, so its presence in the log means the request reached the forward.
/// </summary>
[Trait("Category", "E2E")]
[Collection("PluginChain host process")]
public sealed class PluginChainOrderTests
{
    private readonly GatewayHostProcessFixture _fx;
    private readonly ITestOutputHelper _out;

    public PluginChainOrderTests(GatewayHostProcessFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    [Fact]
    public async Task Request_down_response_up_and_cache_hit_stops_at_cache_from_real_process_logs()
    {
        // Request 1: cache miss. Down 1,2,3,(cache),5 then up 5,(cache),3,2,1. Forward reached.
        _fx.ClearProbeEvents();
        var r1 = await _fx.Client.GetAsync("/data");
        var seq1 = await SettleAsync();
        _out.WriteLine("req1 (miss): " + string.Join(" ", seq1));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(
            new[] { "a:enter", "b:enter", "c:enter", "e:enter", "e:exit", "c:exit", "b:exit", "a:exit" },
            seq1);
        Assert.Equal(1, _fx.UpstreamHits);

        // Request 2: same path, cache HIT. Down 1,2,3 then cache answers and unwinds 3,2,1.
        // probe-e never runs; the forward is never reached.
        _fx.ClearProbeEvents();
        var r2 = await _fx.Client.GetAsync("/data");
        var seq2 = await SettleAsync();
        _out.WriteLine("req2 (hit):  " + string.Join(" ", seq2));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal("HIT", r2.Headers.GetValues("X-Cache").Single());
        Assert.Equal(
            new[] { "a:enter", "b:enter", "c:enter", "c:exit", "b:exit", "a:exit" },
            seq2);
        Assert.DoesNotContain("e:enter", seq2);
        Assert.Equal(1, _fx.UpstreamHits); // forward NOT reached on the hit

        // Request 3: different path, cache miss again. Full chain, forward reached a second time.
        _fx.ClearProbeEvents();
        var r3 = await _fx.Client.GetAsync("/other");
        var seq3 = await SettleAsync();
        _out.WriteLine("req3 (miss): " + string.Join(" ", seq3));
        Assert.Equal(HttpStatusCode.OK, r3.StatusCode);
        Assert.Equal(
            new[] { "a:enter", "b:enter", "c:enter", "e:enter", "e:exit", "c:exit", "b:exit", "a:exit" },
            seq3);
        Assert.Equal(2, _fx.UpstreamHits);
    }

    // The probe log records arrive over a redirected stdout pipe, so they trail the HTTP response
    // slightly. Poll until the sequence stops growing.
    private async Task<string[]> SettleAsync()
    {
        var previous = Array.Empty<string>();
        for (var i = 0; i < 40; i++)
        {
            await Task.Delay(50);
            var current = _fx.ProbeSequence();
            if (current.Length > 0 && current.Length == previous.Length) return current;
            previous = current;
        }
        return _fx.ProbeSequence();
    }
}
