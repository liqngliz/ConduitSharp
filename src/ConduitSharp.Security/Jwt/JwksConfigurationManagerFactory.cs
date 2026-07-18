using System.Collections.Concurrent;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.Protocols;

namespace ConduitSharp.Security.Jwt;

public class JsonWebKeySetRetriever : IConfigurationRetriever<JsonWebKeySet>
{
    public async Task<JsonWebKeySet> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        var json = await retriever.GetDocumentAsync(address, cancel).ConfigureAwait(false);
        return new JsonWebKeySet(json);
    }
}

/// <summary>
/// Maintains cached instances of Microsoft's <see cref="ConfigurationManager{T}"/> per JWKS URI.
/// The ConfigurationManager natively handles background fetching, caching, and concurrent 
/// thundering-herd protections for JSON Web Key Sets.
/// </summary>
public class JwksConfigurationManagerFactory
{
    private readonly ConcurrentDictionary<string, IConfigurationManager<JsonWebKeySet>> _managers = new();
    private readonly IHttpClientFactory _httpClientFactory;

    public JwksConfigurationManagerFactory(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Gets or creates a ConfigurationManager for the specified JWKS URI.
    /// </summary>
    public virtual IConfigurationManager<JsonWebKeySet> GetManager(string jwksUri, TimeSpan automaticRefreshInterval)
    {
        return _managers.GetOrAdd(jwksUri, uri =>
        {
            var documentRetriever = new HttpDocumentRetriever(_httpClientFactory.CreateClient("jwks"))
            {
                RequireHttps = uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            };

            var manager = new ConfigurationManager<JsonWebKeySet>(
                uri,
                new JsonWebKeySetRetriever(),
                documentRetriever)
            {
                AutomaticRefreshInterval = automaticRefreshInterval
            };

            return manager;
        });
    }
}
