using Microsoft.AspNetCore.HttpLogging;

namespace ConduitSharp.Plugin.BodyCapture;

/// <summary>
/// Applies the route's request <c>maxSize</c> to what HttpLogging captures, and stamps the matched
/// route id + path into the same combined record so it is attributable in Loki.
/// </summary>
internal sealed class PerRouteBodyLimitInterceptor : IHttpLoggingInterceptor
{
    /// <summary>HttpContext.Items key the plugin stamps the route's request limit into,
    /// and this interceptor reads back. Declared here so the extracted types do not point
    /// at each other in both directions.</summary>
    internal const string LimitKey = "conduitsharp.body-capture.limit";

    public ValueTask OnRequestAsync(HttpLoggingInterceptorContext logging)
    {
        if (logging.HttpContext.Items.TryGetValue(LimitKey, out var value) && value is int limit)
            logging.RequestBodyLogLimit = limit;

        if (logging.HttpContext.Items.TryGetValue("ConduitSharp.RouteId", out var routeId) && routeId is string id)
            logging.AddParameter("conduitsharp.route_id", id);
        logging.AddParameter("conduitsharp.path", logging.HttpContext.Request.Path.Value ?? "");

        return default;
    }

    // Request bodies only — the response is captured by ResponsePrefixStream, not HttpLogging.
    public ValueTask OnResponseAsync(HttpLoggingInterceptorContext logging) => default;
}
