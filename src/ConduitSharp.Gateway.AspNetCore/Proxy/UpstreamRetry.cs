using ConduitSharp.Core.Pipeline;
using ConduitSharp.Gateway.Routing;
using Polly;
using Polly.Retry;
using Yarp.ReverseProxy.Model;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace ConduitSharp.Gateway.Proxy;

/// <summary>
/// Retry around YARP's forwarder, driven by a per-route Polly <see cref="ResiliencePipeline{T}"/>
/// (delay, backoff, jitter). YARP deliberately ships no retry (a proxy cannot safely replay a
/// half-streamed body), so the loop lives here where it can rewind the buffered request body and
/// re-run load balancing to land on a different node.
///
/// Only idempotent methods are retried: a POST/PATCH may already have been applied upstream.
/// Config: the route's <c>retry</c> block (maxAttempts / delayMs / backoff / jitter / retryOn).
/// </summary>
internal sealed class UpstreamRetry
{
    /// <summary>
    /// Set per attempt: true while another attempt is still available, which is what lets
    /// <see cref="SuppressRetriedResponseTransform"/> hold back a failing response.
    /// </summary>
    internal const string CanRetryKey = "ConduitSharp.CanRetry";

    /// <summary>The route's effective retryOn status set, read by the response transform.</summary>
    internal const string RetryOnKey = "ConduitSharp.RetryOn";

    // Polly outcome for "done — do not retry" regardless of the response status
    // (last attempt, response already streaming, or client gone).
    private const int DoNotRetry = 0;

    private static readonly HashSet<string> IdempotentMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "PUT", "DELETE", "TRACE" };

    /// <summary>
    /// True if the retry loop could ever replay this method. The body-buffering step uses
    /// this too: a non-idempotent request on a retry route can never rewind, so buffering
    /// it would be pure waste — it streams instead.
    /// </summary>
    internal static bool IsIdempotent(string method) => IdempotentMethods.Contains(method);

    private volatile Dictionary<string, (ResiliencePipeline<int> Pipeline, RetryConfig Config, HashSet<int> RetryOn)> _routes = new();

    public UpstreamRetry(GatewayRoutesConfiguration gatewayRoutes) => Load(gatewayRoutes);

    /// <summary>Rebuilds the per-route pipelines. Called at startup and on admin hot reload.</summary>
    internal void Load(GatewayRoutesConfiguration gatewayRoutes) =>
        _routes = gatewayRoutes.Routes
            .Where(r => r.Cluster is not null && r.Retry is { MaxAttempts: > 1 })
            .ToDictionary(
                r => r.Id,
                r =>
                {
                    var config  = r.Retry!;
                    var retryOn = config.RetryOn.ToHashSet();
                    return (BuildPipeline(config, retryOn), config, retryOn);
                },
                StringComparer.OrdinalIgnoreCase);

    private static ResiliencePipeline<int> BuildPipeline(RetryConfig config, HashSet<int> retryOn) =>
        new ResiliencePipelineBuilder<int>()
            .AddRetry(new RetryStrategyOptions<int>
            {
                MaxRetryAttempts = config.MaxAttempts - 1,
                Delay            = TimeSpan.FromMilliseconds(config.DelayMs),
                BackoffType      = config.Backoff switch
                {
                    RetryBackoff.Linear      => DelayBackoffType.Linear,
                    RetryBackoff.Exponential => DelayBackoffType.Exponential,
                    _                        => DelayBackoffType.Constant,
                },
                UseJitter    = config.Jitter,
                ShouldHandle = args => ValueTask.FromResult(retryOn.Contains(args.Outcome.Result)),
            })
            .Build();

    /// <summary>
    /// Runs the rest of the proxy pipeline (load balancing → passive health → forwarder) up to
    /// <c>maxAttempts</c> times. A retried attempt never reaches the client: the response
    /// transform suppresses its body and the loop resets the status and headers it left behind.
    /// </summary>
    internal async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var routeId = (string)context.Items[GatewayItems.RouteId]!;

        using var activity = PipelineTelemetry.ActivitySource.StartActivity("gateway.forward");
        activity?.SetTag("conduitsharp.route_id", routeId);

        if (!IdempotentMethods.Contains(context.Request.Method)
            || !_routes.TryGetValue(routeId, out var retry))
        {
            activity?.SetTag("conduitsharp.attempt", 1);
            context.Items[CanRetryKey] = false;
            await next(context);
            return;
        }

        context.Items[RetryOnKey] = retry.RetryOn;

        var feature = context.GetReverseProxyFeature();

        // Load balancing narrows AvailableDestinations to the single node it picked, so each
        // attempt must start from the full set to be able to fail over.
        var allDestinations = feature.AvailableDestinations;

        // Response headers a plugin set before forwarding are the client's, not the attempt's —
        // keep them across a reset.
        var pluginHeaders = context.Response.Headers.ToList();

        var attempt = 0;
        await retry.Pipeline.ExecuteAsync(async _ =>
        {
            attempt++;
            activity?.SetTag("conduitsharp.attempt", attempt);
            context.Items[CanRetryKey] = attempt < retry.Config.MaxAttempts;
            feature.AvailableDestinations = allDestinations;

            if (attempt > 1)
            {
                context.Response.Headers.Clear();
                foreach (var (name, values) in pluginHeaders)
                    context.Response.Headers[name] = values;
                context.Response.StatusCode = StatusCodes.Status200OK;
            }

            if (context.Request.Body.CanSeek)
                context.Request.Body.Position = 0;

            await next(context);

            var canStillRetry = attempt < retry.Config.MaxAttempts
                && !context.Response.HasStarted
                && !context.RequestAborted.IsCancellationRequested;

            return canStillRetry ? context.Response.StatusCode : DoNotRetry;
        }, context.RequestAborted);
    }
}

/// <summary>
/// Holds back the body of a failing upstream response while a retry is still possible, so the
/// forwarder returns without starting the client's response and <see cref="UpstreamRetry"/> can
/// try another node. On the final attempt the response is copied through untouched.
/// </summary>
internal sealed class SuppressRetriedResponseTransform : ITransformProvider
{
    public void ValidateRoute(TransformRouteValidationContext context) { }
    public void ValidateCluster(TransformClusterValidationContext context) { }

    public void Apply(TransformBuilderContext context) =>
        context.AddResponseTransform(transformContext =>
        {
            var httpContext = transformContext.HttpContext;

            if (httpContext.Items.TryGetValue(UpstreamRetry.CanRetryKey, out var canRetry)
                && canRetry is true
                && httpContext.Items[UpstreamRetry.RetryOnKey] is HashSet<int> retryOn
                && retryOn.Contains(httpContext.Response.StatusCode))
            {
                transformContext.SuppressResponseBody = true;
            }

            return default;
        });
}
