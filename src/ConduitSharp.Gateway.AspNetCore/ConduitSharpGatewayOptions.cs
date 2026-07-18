using ConduitSharp.Core.Routing;
using ConduitSharp.Gateway.Configuration;
using ConduitSharp.Gateway.Routing;

namespace ConduitSharp.Gateway;

/// <summary>
/// Composition knobs for embedding the gateway with
/// <see cref="GatewayServiceCollectionExtensions.AddConduitSharpGateway"/> and
/// <see cref="GatewayApplicationBuilderExtensions.UseConduitSharpGateway"/>.
///
/// The defaults reproduce the standalone <c>ConduitSharp.Host</c> behaviour (everything on).
/// When embedding the gateway inside an app that already owns its own observability, health
/// checks, or routing surface, turn the corresponding pieces off so the gateway does not
/// collide with the host's wiring.
/// </summary>
public sealed class ConduitSharpGatewayOptions
{
    /// <summary>
    /// Configuration section bound to <see cref="GatewayOptions"/>. Default: <c>"Gateway"</c>.
    /// </summary>
    public string ConfigurationSectionName { get; set; } = GatewayOptions.SectionName;

    /// <summary>
    /// Routes supplied in-memory. When set this wins over <see cref="RoutesPath"/> and the
    /// <c>Gateway:RoutesPath</c> config value — no file is read. Ideal for embedding, tests,
    /// or building the route table in code.
    /// </summary>
    public GatewayRoutesConfiguration? Routes { get; set; }

    /// <summary>
    /// Path to a <c>routes.json</c> file. When null the path from bound
    /// <see cref="GatewayOptions.RoutesPath"/> is used. Ignored when <see cref="Routes"/> is set.
    /// </summary>
    public string? RoutesPath { get; set; }

    /// <summary>
    /// Register the gateway's OpenTelemetry tracing/metrics/logging exporters. Default: <c>true</c>.
    /// Set to <c>false</c> when the host app already calls <c>AddOpenTelemetry()</c> — the gateway's
    /// <see cref="ConduitSharp.Observability.Telemetry.GatewayTelemetry.SourceName"/> and
    /// <see cref="ConduitSharp.Core.Pipeline.PipelineTelemetry.SourceName"/> can then be added to the
    /// host's own tracer/meter providers instead.
    /// </summary>
    public bool ConfigureObservability { get; set; } = true;

    /// <summary>
    /// Scan the per-route plugin directory (<c>Gateway:PluginsPath</c>) for external
    /// <see cref="ConduitSharp.Core.Pipeline.IPipelinePlugin"/> assemblies and a drop-in
    /// <see cref="ConduitSharp.Traffic.Caching.ICacheService"/>. Default: <c>true</c>.
    /// Set to <c>false</c> to run only the built-in plugins plus whatever the host registers in DI.
    /// </summary>
    public bool EnablePluginDirectoryScan { get; set; } = true;

    /// <summary>
    /// Map the admin API (<c>POST /admin/routes/reload</c>, <c>DELETE /admin/cache/{routeId}</c>).
    /// Default: <c>true</c>. Reload validates the incoming table, rewrites routes.json atomically,
    /// then hot-swaps the route table in place — no process restart, in-flight requests unaffected.
    /// A reload cannot introduce new plugin DLLs or mTLS client certificates: those are resolved
    /// from DI at startup, so adding one still needs a restart.
    /// The endpoint is inert unless <c>Gateway:AdminKeyHash</c> is configured.
    /// </summary>
    public bool EnableAdminApi { get; set; } = true;

    /// <summary>
    /// Map the gateway's liveness/readiness endpoints (<c>/healthz</c>, <c>/readyz</c>).
    /// Default: <c>true</c>. Turn off when the host app owns its own health checks.
    /// </summary>
    public bool MapHealthEndpoints { get; set; } = true;

    /// <summary>
    /// Documents the path prefix the gateway's routes live under (e.g. <c>"/api"</c>). Routes are
    /// endpoints, so a path no route matches already falls through to the rest of the host
    /// pipeline whether or not this is set — the gateway never swallows paths it does not own.
    /// The gateway's <c>/healthz</c>, <c>/readyz</c> and admin routes are always mounted
    /// at the root regardless of this prefix. Swagger routes are mounted under this prefix.
    /// </summary>
    public string? PathPrefix { get; set; }
}
