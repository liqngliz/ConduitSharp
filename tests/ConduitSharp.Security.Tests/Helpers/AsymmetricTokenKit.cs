using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;
using ConduitSharp.Security.Jwt;

namespace ConduitSharp.Security.Tests.Helpers;

/// <summary>
/// Real RSA/EC keys and signers for JWKS-path tests: builds JWKs from the public
/// halves and signs tokens with the private halves, so tests exercise the actual
/// signature verification instead of injected verifier stubs.
/// </summary>
internal static class AsymmetricTokenKit
{
    internal const string Kid = "test-key-1";

    private static readonly RSA   Rsa      = RSA.Create(2048);
    private static readonly RSA   WrongRsa = RSA.Create(2048);
    private static readonly ECDsa Ec       = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>JWK for the RSA signing key's public half.</summary>
    internal static JsonWebKey RsaJwk(string? kid = Kid)
    {
        var p = Rsa.ExportParameters(false);
        return new JsonWebKey
        {
            Kty = "RSA",
            Kid = kid,
            Use = "sig",
            N   = Base64UrlEncoder.Encode(p.Modulus!),
            E   = Base64UrlEncoder.Encode(p.Exponent!),
        };
    }

    /// <summary>JWK for the EC signing key's public half.</summary>
    internal static JsonWebKey EcJwk(string? kid = Kid)
    {
        var p = Ec.ExportParameters(false);
        return new JsonWebKey
        {
            Kty = "EC",
            Kid = kid,
            Use = "sig",
            Crv = "P-256",
            X   = Base64UrlEncoder.Encode(p.Q.X!),
            Y   = Base64UrlEncoder.Encode(p.Q.Y!),
        };
    }

    internal static string SignRs256(string payloadJson, string? kid = Kid) =>
        Sign(payloadJson, "RS256", kid, data =>
            Rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

    /// <summary>Structurally valid RS256 token signed by a key the JWKS does not contain.</summary>
    internal static string SignRs256WithWrongKey(string payloadJson, string? kid = Kid) =>
        Sign(payloadJson, "RS256", kid, data =>
            WrongRsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

    internal static string SignEs256(string payloadJson, string? kid = Kid) =>
        Sign(payloadJson, "ES256", kid, data =>
            Ec.SignData(data, HashAlgorithmName.SHA256)); // IEEE P1363 (R‖S), JWT's format

    /// <summary>Token with the given alg in the header but a garbage signature.</summary>
    internal static string UnsignedToken(string alg, string payloadJson = """{"sub":"u1"}""")
    {
        var header  = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes($$"""{"alg":"{{alg}}","typ":"JWT"}"""));
        var payload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payloadJson));
        return $"{header}.{payload}.fakesig";
    }

    /// <summary>
    /// Independent RSA key for multi-key tests (e.g. per-route JWKS isolation):
    /// JWK for the public half plus an RS256 signer for the private half.
    /// </summary>
    internal sealed class RsaKit
    {
        private readonly RSA _rsa = RSA.Create(2048);

        internal JsonWebKey Jwk(string? kid = Kid)
        {
            var p = _rsa.ExportParameters(false);
            return new JsonWebKey
            {
                Kty = "RSA",
                Kid = kid,
                Use = "sig",
                N   = Base64UrlEncoder.Encode(p.Modulus!),
                E   = Base64UrlEncoder.Encode(p.Exponent!),
            };
        }

        internal string SignRs256(string payloadJson, string? kid = Kid) =>
            Sign(payloadJson, "RS256", kid, data =>
                _rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    private static string Sign(string payloadJson, string alg, string? kid, Func<byte[], byte[]> signer)
    {
        var headerJson = kid is not null
            ? $$"""{"alg":"{{alg}}","typ":"JWT","kid":"{{kid}}"}"""
            : $$"""{"alg":"{{alg}}","typ":"JWT"}""";
        var header  = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(headerJson));
        var payload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payloadJson));
        var sig     = Base64UrlEncoder.Encode(signer(Encoding.ASCII.GetBytes($"{header}.{payload}")));
        return $"{header}.{payload}.{sig}";
    }
}
