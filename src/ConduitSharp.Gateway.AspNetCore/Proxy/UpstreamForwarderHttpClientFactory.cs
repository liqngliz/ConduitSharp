using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using ConduitSharp.Gateway.Configuration;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Forwarder;

namespace ConduitSharp.Gateway.Proxy;

/// <summary>
/// Attaches per-route mTLS client certificates to the outbound handler.
///
/// YARP's <c>HttpClientConfig</c> covers <c>skipCertificateVerification</c>
/// (<c>DangerousAcceptAnyServerCertificate</c>, applied by the base class) but has no client
/// certificate. Clusters are keyed by route id, so a certificate configured for
/// <c>Gateway:Tls:ClientCertificates[n]:RouteId</c> applies to the cluster of the same name.
///
/// Certificates load once, when YARP builds the cluster's client at config load — a missing file
/// or thumbprint fails the gateway at startup, not on the first request.
/// </summary>
internal sealed class UpstreamForwarderHttpClientFactory : ForwarderHttpClientFactory
{
    private readonly Dictionary<string, ClientCertificateOptions> _certificatesByRouteId;

    public UpstreamForwarderHttpClientFactory(
        ILogger<ForwarderHttpClientFactory> logger,
        IOptions<GatewayOptions> options)
        : base(logger)
    {
        _certificatesByRouteId = options.Value.Tls.ClientCertificates
            .ToDictionary(c => c.RouteId, StringComparer.OrdinalIgnoreCase);
    }

    protected override void ConfigureHandler(ForwarderHttpClientContext context, SocketsHttpHandler handler)
    {
        base.ConfigureHandler(context, handler);

        if (!_certificatesByRouteId.TryGetValue(context.ClusterId, out var config)) return;

        handler.SslOptions.ClientCertificates ??= [];
        handler.SslOptions.ClientCertificates.Add(Load(config));
    }

    private static X509Certificate2 Load(ClientCertificateOptions config)
    {
        if (!string.IsNullOrEmpty(config.StoreThumbprint))
        {
            var location = Enum.Parse<StoreLocation>(config.StoreLocation);
            using var store = new X509Store(config.StoreName, location);
            store.Open(OpenFlags.ReadOnly);

            var matches = store.Certificates.Find(
                X509FindType.FindByThumbprint, config.StoreThumbprint, validOnly: false);

            return matches.Count > 0
                ? new X509Certificate2(matches[0])
                : throw new InvalidOperationException(
                    $"Certificate with thumbprint '{config.StoreThumbprint}' not found " +
                    $"in {config.StoreLocation}/{config.StoreName}.");
        }

        if (!string.IsNullOrEmpty(config.Path))
            return X509CertificateLoader.LoadPkcs12FromFile(config.Path, config.Password);

        throw new InvalidOperationException(
            $"Client certificate for route '{config.RouteId}' must specify either 'path' or 'storeThumbprint'.");
    }
}
