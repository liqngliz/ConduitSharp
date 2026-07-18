using Ocelot.Configuration;
using Ocelot.Provider.Polly.Interfaces;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Ocelot.Bench;

/// <summary>
/// Gives Ocelot the retry it does not ship, so the comparison can measure it rather than skip it.
///
/// Ocelot has no retry in config: <c>FileQoSOptions</c> is circuit-breaker + timeout, and its own
/// <c>Ocelot.Provider.Polly</c> pipeline calls <c>AddCircuitBreaker</c> and <c>AddTimeout</c> and
/// never <c>AddRetry</c>. But <c>AddPolly&lt;TProvider&gt;</c> takes a custom
/// <see cref="IPollyQoSResiliencePipelineProvider{T}"/>, which is the documented seam people use to
/// hand-write one. This is that, as favourably as it can be built: retry sits outermost so it wraps
/// the whole attempt, and the strategies below it mirror Ocelot's own defaults.
///
/// The point is not that it is fast. It is what a retry can retry. Ocelot maps the incoming request
/// body straight through to the downstream <c>HttpRequestMessage</c> — nothing rewinds it — so the
/// second attempt sends whatever is left of a stream the first attempt already drained. The
/// benchmark exists to show what that produces at the upstream, which is a correctness question a
/// throughput column cannot answer.
/// </summary>
public sealed class RetryQoSProvider : IPollyQoSResiliencePipelineProvider<HttpResponseMessage>
{
    private const int MaxRetryAttempts = 2; // matches the gateway's routes: retry.maxAttempts = 2

    public ResiliencePipeline<HttpResponseMessage> GetResiliencePipeline(DownstreamRoute route)
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();

        // Outermost: a retry that re-runs the whole downstream call, which is what a gateway retry
        // has to mean. Retries on a 5xx as well as an exception — an upstream that answers 503 is
        // the case a retry exists for.
        builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = MaxRetryAttempts,
            Delay = TimeSpan.Zero, // no backoff: measure the retry, not a sleep
            BackoffType = DelayBackoffType.Constant,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(r => (int)r.StatusCode >= 500),
        });

        // Below it, Ocelot's own defaults, applied only when the route asks for them — same
        // condition its stock provider uses, so a route without QoSOptions gets retry alone.
        var qos = route.QosOptions;
        if (qos.ExceptionsAllowedBeforeBreaking > 0)
        {
            builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = 0.8,
                MinimumThroughput = qos.ExceptionsAllowedBeforeBreaking,
                SamplingDuration = TimeSpan.FromSeconds(10),
                BreakDuration = TimeSpan.FromMilliseconds(qos.DurationOfBreak),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .Handle<BrokenCircuitException>(),
            });
        }
        if (qos.TimeoutValue > 0)
        {
            builder.AddTimeout(TimeSpan.FromMilliseconds(qos.TimeoutValue));
        }

        return builder.Build();
    }
}
