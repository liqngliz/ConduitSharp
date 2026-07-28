namespace ConduitSharp.Gateway.Configuration;

/// <summary>
/// Gateway host settings bound from the <c>Gateway</c> section of <c>appsettings.json</c>.
/// All values can be overridden via environment variables using the double-underscore separator:
/// <c>Gateway__AdminKeyHash=abc123...</c>, <c>Gateway__Observability__Otlp__Enabled=true</c>.
/// </summary>
public sealed class GatewayOptions
{
    /// <summary>Configuration section these options bind from. Default: <c>"Gateway"</c>.</summary>
    public const string SectionName = "Gateway";

    /// <summary>Path to routes.json. Default: <c>Configuration/routes.json</c> next to the binary.</summary>
    public string RoutesPath { get; init; } =
        Path.Combine(AppContext.BaseDirectory, "Configuration", "routes.json");

    /// <summary>
    /// Root directory used to resolve relative paths in routes.json (specFile, scriptPath, etc.).
    /// Default: the binary directory (<c>AppContext.BaseDirectory</c>).
    /// Override when running in dev mode: <c>Gateway__BasePath=/path/to/gateway</c>.
    /// </summary>
    public string BasePath { get; init; } = AppContext.BaseDirectory;

    /// <summary>
    /// Directory scanned for external plugin DLLs at startup. Default: <c>plugins/</c> next to the binary.
    /// Override via environment variable: <c>Gateway__PluginsPath=/path/to/plugins</c>.
    /// </summary>
    public string PluginsPath { get; init; } =
        Path.Combine(AppContext.BaseDirectory, "plugins");

    /// <summary>
    /// SHA-256 hex hash of the secret required in the <c>X-Admin-Key</c> request header.
    /// Admin API is disabled entirely when this is null or empty.
    /// Never store the raw key — only the hash.
    /// Generate with: <c>echo -n "my-key" | sha256sum</c> (Linux) or
    /// <c>[System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes("my-key")) | ForEach-Object { '{0:x2}' -f $_ }</c> (PowerShell).
    /// </summary>
    public string? AdminKeyHash { get; init; }

    /// <summary>OpenTelemetry exporter settings (OTLP, console, file).</summary>
    public ObservabilityOptions Observability { get; init; } = new();
    /// <summary>Per-route upstream mTLS client-certificate settings.</summary>
    public TlsOptions Tls { get; init; } = new();
    /// <summary>Request body size and buffering limits.</summary>
    public RequestLimitsOptions RequestLimits { get; init; } = new();
    /// <summary>Host-level Swagger aggregation settings.</summary>
    public SwaggerHostOptions Swagger { get; init; } = new();
    /// <summary>Response-cache backend settings.</summary>
    public CacheHostOptions Cache { get; init; } = new();

    /// <summary>
    /// Grace period, in seconds, that the host waits for in-flight requests to finish
    /// when shutting down (including an admin route reload, which restarts the process).
    /// In-flight requests are drained rather than cut off mid-response. Default: <c>30</c>.
    /// </summary>
    public int ShutdownTimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// Response-cache backend settings. The built-in cache is in-process; a drop-in
/// <c>ICacheService</c> (e.g. <c>ConduitSharp.Cache.RedisProtocol</c>) reads its own
/// connection settings directly from configuration (e.g.
/// <c>Gateway:Cache:Redis:ConnectionString</c>) rather than through a type declared here —
/// core's config schema doesn't version in lockstep with backends it never uses itself.
/// </summary>
public sealed class CacheHostOptions
{
    /// <summary>
    /// Maximum approximate bytes of response bodies held by the built-in in-process cache.
    /// Cache keys include attacker-controlled query strings and vary headers, so the cache
    /// is capped like request-body buffering is (<see cref="RequestLimitsOptions"/>).
    /// When exceeded, entries closest to expiry are evicted first. Zero or negative
    /// disables the cap. Ignored by drop-in backends (e.g. Redis). Default: 64 MiB.
    /// </summary>
    public long MaxTotalBytes { get; init; } = 64 * 1024 * 1024;
}

/// <summary>Host-level Swagger aggregation settings (distinct from the per-route <c>swagger</c> block).</summary>
public sealed class SwaggerHostOptions
{
    /// <summary>
    /// Hostnames the Swagger aggregator may fetch <c>fetchFrom</c> specs from,
    /// in addition to the always-allowed set: loopback addresses and each route's
    /// own upstream node hosts. Anything else is refused with 403 before any
    /// network call — this is the SSRF guard for spec fetching.
    /// </summary>
    public List<string> AllowedSpecHosts { get; init; } = [];

    /// <summary>
    /// Description text shown for the injected OpenAPI bearer (JWT) security scheme, e.g.
    /// to point testers at how to obtain a token for this deployment. Defaults to a generic
    /// description — set this per-deployment rather than baking example-specific
    /// instructions (like a demo token script) into the gateway itself.
    /// </summary>
    public string BearerDescription { get; init; } = "JWT bearer token.";
}

/// <summary>
/// Caps on request body buffering. There are <b>two independent resource budgets</b>, one per
/// resource, because they are bounded by different physical things and confusing them is how a
/// gateway gets OOM-killed instead of shedding:
///
/// <list type="bullet">
/// <item><see cref="MaxRamBufferedBodyBytes"/> — total RAM. Size against available memory.</item>
/// <item><see cref="MaxDiskBufferedBodyBytes"/> — total spill-file bytes. Size against free disk.</item>
/// </list>
///
/// A buffered body is held in RAM up to <see cref="RamBufferThresholdBytes"/> and charged to the RAM
/// budget; beyond that it spills to a temp file and is charged to the disk budget instead. So the
/// gateway degrades in tiers rather than falling off a cliff: fast while RAM has room, slower on disk
/// once it doesn't, and <b>503 only when neither has room</b>.
///
/// Body-capture prefixes are RAM-only (they never spill) and are charged to
/// <see cref="MaxRamBufferedBodyBytes"/>.
///
/// Kestrel's transport-level limit (~28.6 MB by default) still applies first; these limits act at
/// the gateway layer and are enforced for chunked bodies too.
///
/// Defaults are sized for a small container (256–512 MiB), not for the host you develop on:
/// 64 MiB of RAM and 64 MiB of spill. Raise each deliberately, against the resource it meters.
/// </summary>
public sealed class RequestLimitsOptions
{
    /// <summary>
    /// Maximum bytes buffered for a single request body. Requests over the limit
    /// are rejected with 413 Payload Too Large. Zero or negative disables the check.
    /// Default: 8 MiB.
    /// </summary>
    public long MaxRequestBodyBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>
    /// Total RAM, across all in-flight requests, that buffered bodies and body-capture prefixes may
    /// hold at once. Default: 64 MiB.
    ///
    /// <b>Size this against memory actually available to the process</b> —
    /// <c>GC.GetGCMemoryInfo().TotalAvailableMemoryBytes</c> reports the cgroup/container limit where
    /// one is set. This is the number that decides whether an overload degrades or OOM-kills.
    ///
    /// A body that cannot get RAM here is not refused: it spills to disk and is charged to
    /// <see cref="MaxDiskBufferedBodyBytes"/> instead. <c>0</c> means "no RAM for bodies" — everything
    /// spills. Negative is rejected at startup.
    /// </summary>
    public long MaxRamBufferedBodyBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>
    /// Total spill-file bytes, across all in-flight requests, that buffered bodies may occupy on
    /// <see cref="SpillDirectory"/>. Default: 64 MiB.
    ///
    /// Size this against free space on the spill path. A body that can get neither RAM nor disk is
    /// shed with <b>503 Service Unavailable</b> (retryable) — this budget is the load-shed trigger for
    /// bodies too large for the RAM budget. <c>0</c> means "no spilling" — a body must fit the RAM
    /// budget or be shed. Negative is rejected at startup.
    ///
    /// <b>There is no "unlimited" value.</b> Before v2.0.0 a <c>0</c> on the combined total disabled
    /// the check and meant "buffer without bound"; here it means the opposite. For effectively
    /// unbounded spilling, set a value large enough never to bind rather than <c>0</c>.
    ///
    /// <b>The tmpfs trap.</b> If <see cref="SpillDirectory"/> resolves to a <c>tmpfs</c> mount — which
    /// <c>/tmp</c> often is in containers — then this "disk" budget is really a second memory budget,
    /// charged to the same cgroup. Either point the spill at real storage, or size this as if it were
    /// memory and count it against the same limit as <see cref="MaxRamBufferedBodyBytes"/>.
    /// </summary>
    public long MaxDiskBufferedBodyBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>
    /// Per-request RAM ceiling for a buffered body; bytes beyond it spill transparently to a temp
    /// file (nginx-style). A request only gets this much if <see cref="MaxRamBufferedBodyBytes"/>
    /// still has headroom — otherwise it spills from the first byte.
    /// Floored at 4 KiB; no upper cap. Default: 1 MiB.
    ///
    /// Up to 1 MiB, <c>FileBufferingReadStream</c> serves the threshold from <c>ArrayPool</c>. Above
    /// it, the buffer becomes a bare <c>MemoryStream</c> that doubles as it grows, allocating ~2x the
    /// body on the large object heap. That is the cost of raising this past 1 MiB — paid to let bodies
    /// that fit in the raised ceiling skip the disk spill. It is a deliberate operator trade, so the
    /// ceiling is configurable rather than clamped; <see cref="MaxRamBufferedBodyBytes"/> still bounds
    /// the aggregate RAM footprint regardless of what a single body is allowed.
    /// </summary>
    public long RamBufferThresholdBytes { get; init; } = 1024 * 1024;

    /// <summary>
    /// Directory for temp-file spill. Null or empty uses the system temp path.
    ///
    /// Worth setting explicitly, and worth measuring: the disk tier is only as fast as this path.
    /// A RAM-backed <c>tmpfs</c> measures ~5x container overlayfs or a mounted volume (which are
    /// about equal to each other), which is a larger factor than anything in the buffering code.
    ///
    /// <c>/tmp</c> is <c>tmpfs</c> on many container images, and that cuts both ways. It makes the
    /// disk tier fast, and <see cref="MaxDiskBufferedBodyBytes"/> still bounds it — so spilling to
    /// tmpfs with a total budget that fits in the pod is a deliberate, legitimate configuration. But
    /// it does not relieve memory pressure, because the "disk" tier is RAM: a budget sized on the
    /// assumption that spill lands on real storage will OOM the process where it would otherwise
    /// have degraded and shed. Pick tmpfs for speed with a memory-sized budget, or real storage for
    /// capacity — but pick, rather than inheriting whatever <c>/tmp</c> happens to be.
    /// </summary>
    public string? SpillDirectory { get; init; }
}

/// <summary>OpenTelemetry exporter settings: OTLP (collector), console, and file.</summary>
public sealed class ObservabilityOptions
{
    /// <summary>OTLP exporter (e.g. to a collector, Jaeger, or Grafana).</summary>
    public OtlpOptions Otlp { get; init; } = new();
    /// <summary>Console exporter for local development.</summary>
    public ConsoleExporterOptions Console { get; init; } = new();
    /// <summary>File exporter that writes spans as JSON lines.</summary>
    public FileExporterOptions File { get; init; } = new();
}

/// <summary>Console exporter settings.</summary>
public sealed class ConsoleExporterOptions
{
    /// <summary>Write traces and metrics to stdout. For local development without a collector.</summary>
    public bool Enabled { get; init; } = false;
}

/// <summary>File exporter settings — writes completed spans as JSON lines.</summary>
public sealed class FileExporterOptions
{
    /// <summary>Write completed spans as JSON lines to TracesPath. No collector required.</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>Path for trace output. Relative paths are resolved from <c>Gateway:BasePath</c>.</summary>
    public string TracesPath { get; init; } = "logs/otel-traces.jsonl";
}

/// <summary>OTLP exporter settings for traces and metrics.</summary>
public sealed class OtlpOptions
{
    /// <summary>
    /// Enable OTLP export of traces and metrics. Default: false.
    /// OTLP is also auto-enabled when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set (Aspire/OTel convention),
    /// so this flag is only needed when exporting to an endpoint configured via <see cref="Endpoint"/>.
    /// </summary>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// OTLP collector endpoint. When empty, the OTel SDK falls back to the
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> environment variable.
    /// </summary>
    public string? Endpoint { get; init; }
}

/// <summary>Upstream TLS settings — per-route mTLS client certificates.</summary>
public sealed class TlsOptions
{
    /// <summary>Per-route mTLS client certificates. One entry per route that requires a client cert.</summary>
    public List<ClientCertificateOptions> ClientCertificates { get; init; } = [];
}

/// <summary>
/// mTLS client certificate for a specific upstream route.
/// Specify either <c>Path</c> + <c>Password</c> (PFX file) or <c>StoreThumbprint</c> (Windows cert store).
/// </summary>
public sealed class ClientCertificateOptions
{
    /// <summary>The route ID this certificate applies to. Must match a route <c>id</c> in routes.json.</summary>
    public string RouteId { get; init; } = "";

    // PFX file
    /// <summary>Path to the PFX/PKCS#12 file. Use <c>Password</c> if the file is password-protected.</summary>
    public string? Path { get; init; }
    /// <summary>Password for the PFX file. Use an environment variable override to avoid storing it in plaintext.</summary>
    public string? Password { get; init; }

    // Windows cert store
    /// <summary>SHA-1 thumbprint of a certificate already installed in the Windows certificate store.</summary>
    public string? StoreThumbprint { get; init; }
    /// <summary>Windows store name. Default: <c>My</c> (Personal).</summary>
    public string StoreName { get; init; } = "My";
    /// <summary>Windows store location. Default: <c>LocalMachine</c>.</summary>
    public string StoreLocation { get; init; } = "LocalMachine";
}
