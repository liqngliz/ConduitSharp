using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ConduitSharp.Mtls.E2E.Tests;

/// <summary>
/// Generates the CA + upstream server cert + gateway client cert used by the mTLS stack,
/// entirely with .NET's X509 APIs — no <c>openssl</c> dependency, so it runs on any OS.
/// Mirrors what <c>tests/ConduitSharp.Mtls.E2E.Tests/assets/generate-certs.sh</c> produces:
///   ca.crt, server.crt, server.key (PEM for nginx) and client.pfx (PKCS#12 for the gateway).
/// </summary>
internal static class CertificateMaterial
{
    public const string ServerDnsName = "upstream"; // must match the compose service name
    public const string ClientPfxPassword = "testpass";

    public static void Generate(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter  = DateTimeOffset.UtcNow.AddDays(2);

        // --- Certificate authority (self-signed, can sign other certs) ---
        using var caKey = RSA.Create(2048);
        var caReq = new CertificateRequest("CN=conduit-test-ca", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, false, 0, critical: true));
        caReq.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        using var caCert = caReq.CreateSelfSigned(notBefore, notAfter);
        File.WriteAllText(Path.Combine(outputDir, "ca.crt"), caCert.ExportCertificatePem());

        var caSignatureGenerator = X509SignatureGenerator.CreateForRSA(caKey, RSASignaturePadding.Pkcs1);
        var caName = caCert.SubjectName;

        // --- Upstream server cert (SAN=upstream), signed by the CA ---
        using var serverKey = RSA.Create(2048);
        var serverReq = new CertificateRequest("CN=upstream", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        serverReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        serverReq.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        serverReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false)); // serverAuth
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(ServerDnsName);
        san.AddDnsName("localhost");
        serverReq.CertificateExtensions.Add(san.Build());
        using var serverCert = serverReq.Create(caName, caSignatureGenerator, notBefore, notAfter, NextSerial());
        File.WriteAllText(Path.Combine(outputDir, "server.crt"), serverCert.ExportCertificatePem());
        File.WriteAllText(Path.Combine(outputDir, "server.key"), serverKey.ExportPkcs8PrivateKeyPem());

        // --- Gateway client cert, signed by the CA, exported as a password-protected PKCS#12 ---
        using var clientKey = RSA.Create(2048);
        var clientReq = new CertificateRequest("CN=conduit-gateway-client", clientKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        clientReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        clientReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        clientReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.2") }, false)); // clientAuth
        using var clientCertPublicOnly = clientReq.Create(caName, caSignatureGenerator, notBefore, notAfter, NextSerial());
        using var clientCert = clientCertPublicOnly.CopyWithPrivateKey(clientKey);
        File.WriteAllBytes(Path.Combine(outputDir, "client.pfx"),
            clientCert.Export(X509ContentType.Pkcs12, ClientPfxPassword));
    }

    // A positive, random 16-byte serial number.
    private static byte[] NextSerial()
    {
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        return serial;
    }
}
