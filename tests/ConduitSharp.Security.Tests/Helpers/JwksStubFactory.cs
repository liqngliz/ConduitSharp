using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.Protocols;
using ConduitSharp.Security.Jwt;


namespace ConduitSharp.Security.Tests.Helpers;

public sealed class StubConfigurationManager : IConfigurationManager<JsonWebKeySet>
{
    private readonly JsonWebKeySet? _keyset;
    private readonly Exception? _throwEx;

    public StubConfigurationManager(JsonWebKey? key, Exception? throwEx = null)
    {
        _throwEx = throwEx;
        if (key is not null)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(key);
            _keyset = new JsonWebKeySet("{\"keys\":[" + json + "]}");
        }
        else
        {
            _keyset = new JsonWebKeySet();
        }
    }

    public Task<JsonWebKeySet> GetConfigurationAsync(CancellationToken cancel)
    {
        if (_throwEx is not null) throw _throwEx;
        return Task.FromResult(_keyset!);
    }

    public void RequestRefresh() { }
}

public sealed class StubFactory : JwksConfigurationManagerFactory
{
    private readonly IConfigurationManager<JsonWebKeySet> _manager;
    public StubFactory(IConfigurationManager<JsonWebKeySet> manager) : base(null!) => _manager = manager;
    public override IConfigurationManager<JsonWebKeySet> GetManager(string jwksUri, TimeSpan ttl) => _manager;
}

/// <summary>Serves a different key set per JWKS URI, for multi-provider / per-route isolation tests.</summary>
public sealed class PerUriStubFactory : JwksConfigurationManagerFactory
{
    private readonly IReadOnlyDictionary<string, IConfigurationManager<JsonWebKeySet>> _managers;
    public PerUriStubFactory(IReadOnlyDictionary<string, IConfigurationManager<JsonWebKeySet>> managers)
        : base(null!) => _managers = managers;
    public override IConfigurationManager<JsonWebKeySet> GetManager(string jwksUri, TimeSpan ttl) => _managers[jwksUri];
}
