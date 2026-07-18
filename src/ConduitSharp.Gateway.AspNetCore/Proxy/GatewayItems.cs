namespace ConduitSharp.Gateway.Proxy;

/// <summary>Keys the gateway puts on <see cref="HttpContext.Items"/>.</summary>
internal static class GatewayItems
{
    /// <summary>
    /// The matched route's id. This is the whole of what the plugin contract exposes about the
    /// route — every plugin that wanted "the route" only ever wanted to scope something by it
    /// (a cache key, a rate-limit bucket). Handing over the <c>GatewayRoute</c> object instead
    /// would put the gateway's whole config schema — and, since that schema is built on YARP's
    /// types, YARP itself — into every plugin author's dependency graph.
    /// </summary>
    internal const string RouteId = "ConduitSharp.RouteId";

    /// <summary>
    /// Continuation into the rest of YARP's proxy pipeline. A route's plugin chain is compiled once
    /// at startup, so its terminal step cannot close over the per-request <c>next</c> — it picks it
    /// up from here instead. Calling it forwards; not calling it short-circuits.
    /// </summary>
    internal const string ProxyNext = "ConduitSharp.ProxyNext";
}
