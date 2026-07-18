using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using ConduitSharp.Security.Jwt;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace ConduitSharp.Benchmarks;

/// <summary>
/// JwksJwtAuthPlugin hot path: bearer extraction → RS256 signature verification →
/// optional requiredClaims RBAC. Key material served by an in-memory JWKS
/// (no network), same as the security test suite. Per-op DefaultHttpContext
/// allocation is included in B/op — constant across both variants.
/// </summary>
[MemoryDiagnoser]
public class JwtBenchmarks
{
    [Params(false, true)]
    public bool RequiredClaims;

    private JwksJwtAuthPlugin _plugin = null!;
    private JsonElement _config;
    private string _bearer = null!;
    private static readonly RequestDelegate Next = _ => Task.CompletedTask;

    [GlobalSetup]
    public void Setup()
    {
        using var rsa = RSA.Create(2048);
        var p = rsa.ExportParameters(false);
        var jwk = new JsonWebKey
        {
            Kty = "RSA",
            Kid = "bench-key",
            Use = "sig",
            N   = Base64UrlEncoder.Encode(p.Modulus!),
            E   = Base64UrlEncoder.Encode(p.Exponent!),
        };

        _plugin = new JwksJwtAuthPlugin(
            new JwksJwtAuthHandler(new StubJwksFactory(jwk)));

        _config = JsonSerializer.SerializeToElement(new JwksJwtAuthConfig
        {
            JwksUri        = "https://bench.example.com/.well-known/jwks.json",
            RequiredClaims = RequiredClaims
                ? [new RequiredClaim { Claim = "roles", AnyOf = ["Admin"] }]
                : [],
        });

        _bearer = "Bearer " + SignRs256(rsa, """{"sub":"bench","roles":["Admin"]}""", "bench-key");

        // sanity: token must validate (next() reached, no error status written)
        var ctx = MakeContext();
        _plugin.ExecuteAsync(ctx, _config, Next).GetAwaiter().GetResult();
        if (ctx.Response.StatusCode != 200)
            throw new InvalidOperationException($"JWT setup broken: {ctx.Response.StatusCode}");
    }

    [Benchmark]
    public Task Validate() => _plugin.ExecuteAsync(MakeContext(), _config, Next);

    private HttpContext MakeContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = _bearer;
        return context;
    }

    private static string SignRs256(RSA rsa, string payloadJson, string kid)
    {
        var header  = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(
            $$"""{"alg":"RS256","typ":"JWT","kid":"{{kid}}"}"""));
        var payload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payloadJson));
        var sig     = Base64UrlEncoder.Encode(rsa.SignData(
            Encoding.ASCII.GetBytes($"{header}.{payload}"),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        return $"{header}.{payload}.{sig}";
    }
}

/// <summary>Serves a fixed JWKS from memory — the JWKS fetch never hits the network.</summary>
internal sealed class StubJwksFactory : JwksConfigurationManagerFactory
{
    private readonly JsonWebKeySet _keys;

    public StubJwksFactory(JsonWebKey key) : base(null!) =>
        _keys = new JsonWebKeySet("{\"keys\":[" + JsonSerializer.Serialize(key) + "]}");

    public override IConfigurationManager<JsonWebKeySet> GetManager(string jwksUri, TimeSpan ttl) =>
        new StaticManager(_keys);

    private sealed class StaticManager(JsonWebKeySet keys) : IConfigurationManager<JsonWebKeySet>
    {
        public Task<JsonWebKeySet> GetConfigurationAsync(CancellationToken cancel) =>
            Task.FromResult(keys);

        public void RequestRefresh() { }
    }
}
