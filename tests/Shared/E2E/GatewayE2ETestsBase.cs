using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ConduitSharp.E2E.Shared;

/// <summary>
/// Surface a gateway-example E2E fixture exposes to the shared test suite.
/// The three example stacks (LegacyGateway, EmbeddedGateway, EmbeddedGatewayPrefixed)
/// serve the same routes.json contract on different ports/prefixes — the fixture
/// supplies what varies, <see cref="GatewayE2ETestsBase"/> supplies what doesn't.
/// </summary>
public interface IGatewayE2EFixture
{
    HttpClient Client { get; }
    string DemoJwt { get; }
    /// <summary>Absolute path to the example's root — for asserts on files (logs, traces).</summary>
    string ExampleRoot { get; }
    /// <summary>Cleartext HTTP/2 (h2c) endpoint for the gRPC route.</summary>
    string GrpcUrl { get; }
    /// <summary>Prefix for gateway-owned paths (health, erp, swagger): "" or "/api".</summary>
    string PathPrefix { get; }
    /// <summary>The two inventory upstream ports — asserted absent from served swagger.</summary>
    (string A, string B) InventoryUpstreamPorts { get; }
}

/// <summary>
/// The shared contract every gateway example must honor, exercised end to end against the
/// real gateway process each suite's fixture boots. One copy of each test — a change here
/// runs against all three stacks, instead of drifting across three near-identical files.
/// Suite-specific behavior (upload-route capture, prefix-only middleware) stays in the
/// derived classes.
/// </summary>
public abstract class GatewayE2ETestsBase(IGatewayE2EFixture fx)
{
    protected const string ApiKey = "demo-api-key-conduitsharp-example";

    protected IGatewayE2EFixture Fx => fx;

    /// <summary>Prefixes gateway-owned paths; upstream route paths are literal in routes.json.</summary>
    protected string P(string path) => fx.PathPrefix + path;

    [Fact]
    public async Task GetHealth_Returns200()
    {
        var response = await fx.Client.GetAsync(P("/health"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetInventory_WithApiKey_Returns200()
    {
        var request = ApiKeyRequest(HttpMethod.Get, "/api/inventory");

        var response = await fx.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostInventory_WithApiKey_Returns200()
    {
        var request = ApiKeyRequest(HttpMethod.Post, "/api/inventory");
        request.Content = new StringContent(
            """{"name":"widget","quantity":10}""",
            Encoding.UTF8, "application/json");

        var response = await fx.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetInventory_NoKey_Returns401()
    {
        var response = await fx.Client.GetAsync("/api/inventory");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetInventory_WrongKey_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/inventory");
        request.Headers.Add("X-Api-Key", "definitely-not-the-right-key");

        var response = await fx.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteInventory_MethodNotInRoute_Returns405()
    {
        var request = ApiKeyRequest(HttpMethod.Delete, "/api/inventory/1");

        var response = await fx.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task GetOrders_WithJwt_Returns200()
    {
        var request = JwtRequest(HttpMethod.Get, "/api/orders");

        var response = await fx.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostOrders_WithJwt_BodyCaptureLogsBody()
    {
        var request = JwtRequest(HttpMethod.Post, "/api/orders");
        request.Content = new StringContent(
            """{"customerId":"c-123","sku":"widget","qty":5,"unitPrice":10}""",
            Encoding.UTF8, "application/json");

        var response = await fx.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var logPath = Path.Combine(fx.ExampleRoot, "logs", "gateway.log");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        bool found = false;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(logPath))
            {
                var content = await ReadSharedAsync(logPath);
                if (content.Contains("""RequestBody: {"customerId":"c-123","sku":"widget","qty":5,"unitPrice":10}""", StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }
            await Task.Delay(500);
        }

        Assert.True(found, "BodyCapture log not found in gateway.log within 15s");
    }

    [Fact]
    public async Task GetOrders_NoToken_Returns401()
    {
        var response = await fx.Client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetOrders_MalformedToken_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "this.is.garbage");

        var response = await fx.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetOrders_WrongSignatureToken_Returns401()
    {
        var wrongToken = BuildToken(signingKeyBase64: Convert.ToBase64String(
            Encoding.UTF8.GetBytes("a-completely-different-secret-key")));
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", wrongToken);

        var response = await fx.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetErpValue_WithJwt_Returns200_OrSkippedIfNoPwsh()
    {
        if (!IsPwshAvailable())
        {
            return;
        }

        var request = JwtRequest(HttpMethod.Get, P("/erp/reports/summary"));

        var response = await fx.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetErpValue_NoToken_Returns401_OrSkippedIfNoPwsh()
    {
        if (!IsPwshAvailable()) return;

        var response = await fx.Client.GetAsync(P("/erp/reports/summary"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetErpValue_ValidTokenWrongRole_Returns403_OrSkippedIfNoPwsh()
    {
        if (!IsPwshAvailable()) return;

        var token   = MintTokenWithRole("intern");
        var request = new HttpRequestMessage(HttpMethod.Get, P("/erp/reports/summary"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await fx.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetErpValue_CalledTwice_SecondResponseIsCached()
    {
        if (!IsPwshAvailable()) return;

        var request1 = JwtRequest(HttpMethod.Get, P("/erp/reports/summary"));
        var response1 = await fx.Client.SendAsync(request1);
        if (!response1.IsSuccessStatusCode) return;

        var body1 = await response1.Content.ReadAsStringAsync();

        var request2 = JwtRequest(HttpMethod.Get, P("/erp/reports/summary"));
        var response2 = await fx.Client.SendAsync(request2);
        var body2 = await response2.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal(body1, body2);
    }

    [Fact]
    public async Task UnmatchedPath_Returns404()
    {
        var response = await fx.Client.GetAsync("/this/does/not/exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SwaggerSpec_InventoryRoute_Returns200OrBadGateway()
    {
        var response = await fx.Client.GetAsync(P("/swagger/inventory-service.json"));

        Assert.True(
            response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.BadGateway,
            $"Expected 200 or 502, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task SwaggerSpec_OrderRoute_Returns200OrBadGateway()
    {
        var response = await fx.Client.GetAsync(P("/swagger/order-service.json"));

        Assert.True(
            response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.BadGateway,
            $"Expected 200 or 502, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task SwaggerSpec_UnknownRoute_Returns404()
    {
        var response = await fx.Client.GetAsync(P("/swagger/does-not-exist.json"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostInventory_BodyOverRouteLimit_Returns413()
    {
        var request = ApiKeyRequest(HttpMethod.Post, "/api/inventory");
        request.Content = new ByteArrayContent(new byte[2 * 1024 * 1024]);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.ExpectContinue = true;

        var response = await fx.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task PostOrders_BodyOverGlobalLimit_Returns413()
    {
        var request = JwtRequest(HttpMethod.Post, "/api/orders");
        request.Content = new ByteArrayContent(new byte[9 * 1024 * 1024]);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.ExpectContinue = true;

        var response = await fx.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task PostInventory_OversizedBody_ErrorBodyIsGeneric()
    {
        var request = ApiKeyRequest(HttpMethod.Post, "/api/inventory");
        request.Content = new ByteArrayContent(new byte[2 * 1024 * 1024]);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.ExpectContinue = true;

        var response = await fx.Client.SendAsync(request);
        var body     = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.DoesNotContain("1048576", body);
        Assert.DoesNotContain("/Users", body);
        Assert.DoesNotContain("Exception", body);
    }

    [Fact]
    public async Task SwaggerSpec_ServedSpec_DoesNotLeakUpstreamTopology()
    {
        var response = await fx.Client.GetAsync(P("/swagger/inventory-service.json"));
        if (response.StatusCode != HttpStatusCode.OK) return;

        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(fx.InventoryUpstreamPorts.A, body);
        Assert.DoesNotContain(fx.InventoryUpstreamPorts.B, body);
    }

    [Fact]
    public async Task PostInventory_SlowUpload_AbortsDueToDataRateLimit()
    {
        var request = ApiKeyRequest(HttpMethod.Post, "/api/inventory");
        request.Content = new SlowHttpContent();

        await Assert.ThrowsAnyAsync<Exception>(() => fx.Client.SendAsync(request));
    }

    private sealed class SlowHttpContent : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            var chunk = new byte[100];
            for (var i = 0; i < 15; i++)
            {
                await stream.WriteAsync(chunk, 0, chunk.Length);
                await stream.FlushAsync();
                await Task.Delay(500);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 1500;
            return true;
        }
    }

    [Fact]
    public async Task GrpcSayHello_ThroughGateway_RepliesOverHttp2()
    {
        var httpHandler = fx.PathPrefix.Length == 0
            ? (HttpMessageHandler)new SocketsHttpHandler()
            : new PrefixDelegatingHandler(fx.PathPrefix) { InnerHandler = new SocketsHttpHandler() };
        using var channel = Grpc.Net.Client.GrpcChannel.ForAddress(fx.GrpcUrl,
            new Grpc.Net.Client.GrpcChannelOptions { HttpHandler = httpHandler });
        var client = new GreeterService.Protos.Greeter.GreeterClient(channel);

        var reply = await client.SayHelloAsync(
            new GreeterService.Protos.HelloRequest { Name = "ConduitSharp" },
            deadline: DateTime.UtcNow.AddSeconds(10));

        Assert.Equal("Hello, ConduitSharp!", reply.Message);
        Assert.Equal("HTTP/2", reply.Protocol);
    }

    private sealed class PrefixDelegatingHandler(string prefix) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri != null && !request.RequestUri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal))
            {
                var builder = new UriBuilder(request.RequestUri);
                builder.Path = prefix + builder.Path;
                request.RequestUri = builder.Uri;
            }
            return base.SendAsync(request, cancellationToken);
        }
    }

    [Fact]
    public async Task GrpcPath_OverHttp1_IsNotServedByGrpcRoute()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/greet.Greeter/SayHello");
        request.Content = new ByteArrayContent([]);

        var response = await fx.Client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Traces_AfterGatewayTraffic_GatewayRequestSpanIsWrittenToTraceFile()
    {
        var request  = ApiKeyRequest(HttpMethod.Get, "/api/inventory");
        var response = await fx.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tracesPath = Path.Combine(fx.ExampleRoot, "logs", "otel-traces.jsonl");
        var deadline   = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(tracesPath))
            {
                var content = await ReadSharedAsync(tracesPath);
                if (content.Contains("\"gateway.request\""))
                    return;
            }
            await Task.Delay(500);
        }

        Assert.Fail($"No gateway.request span appeared in {tracesPath} within 15s — " +
                    "the file exporter pipeline (span creation → processor → exporter) is broken.");
    }

    [Fact]
    public async Task Traces_GatewayRequestSpan_CarriesAlignedInstrumentationScope()
    {
        var response = await fx.Client.SendAsync(ApiKeyRequest(HttpMethod.Get, "/api/inventory"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tracesPath = Path.Combine(fx.ExampleRoot, "logs", "otel-traces.jsonl");
        var deadline   = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(tracesPath))
            {
                var span = FindSpan(await ReadSharedAsync(tracesPath), "gateway.request");
                if (span is not null)
                {
                    Assert.Equal("ConduitSharp.Gateway", span.Value.GetProperty("scopeName").GetString());

                    var scopeVersion = span.Value.GetProperty("scopeVersion").GetString();
                    Assert.False(string.IsNullOrEmpty(scopeVersion));
                    Assert.DoesNotContain("+", scopeVersion!);
                    Assert.NotEqual("0.1.0", scopeVersion);
                    Assert.Matches(@"^\d+\.\d+\.\d+", scopeVersion!);
                    return;
                }
            }
            await Task.Delay(500);
        }

        Assert.Fail($"No gateway.request span with instrumentation scope appeared in {tracesPath} within 15s.");
    }

    private static JsonElement? FindSpan(string jsonl, string name)
    {
        foreach (var line in jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }
            using (doc)
                if (doc.RootElement.TryGetProperty("name", out var n) && n.GetString() == name)
                    return doc.RootElement.Clone();
        }
        return null;
    }

    /// <summary>Reads a file another process is appending to (log/trace tails).</summary>
    protected static async Task<string> ReadSharedAsync(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    protected HttpRequestMessage ApiKeyRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Api-Key", ApiKey);
        return request;
    }

    protected HttpRequestMessage JwtRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fx.DemoJwt);
        return request;
    }

    protected static bool IsPwshAvailable() =>
        Environment.GetEnvironmentVariable("PATH")
            ?.Split(Path.PathSeparator)
            .Any(dir => File.Exists(Path.Combine(dir, "pwsh")) ||
                        File.Exists(Path.Combine(dir, "pwsh.exe")))
        ?? false;

    private const string DemoSigningKeyBase64 = "ZGVtby1zaWduaW5nLWtleS1jb25kdWl0c2hhcnAtZXhhbXBsZS0zMmNo";

    protected static string MintTokenWithRole(string role)
    {
        var keyBytes = Convert.FromBase64String(DemoSigningKeyBase64);
        var now      = DateTimeOffset.UtcNow;

        var header  = B64Url("""{"alg":"HS256","typ":"JWT"}""");
        var payload = B64Url(
            $"{{\"sub\":\"test\",\"iss\":\"conduitsharp-demo\",\"aud\":\"conduitsharp-demo\"," +
            $"\"iat\":{now.ToUnixTimeSeconds()},\"exp\":{now.AddHours(1).ToUnixTimeSeconds()}," +
            $"\"role\":\"{role}\"}}");

        return SignHs256(header, payload, keyBytes);
    }

    protected static string BuildToken(string signingKeyBase64)
    {
        var keyBytes = Convert.FromBase64String(signingKeyBase64);
        var now      = DateTimeOffset.UtcNow;

        var header  = B64Url("""{"alg":"HS256","typ":"JWT"}""");
        var payload = B64Url(
            $"{{\"sub\":\"test\",\"iss\":\"conduitsharp-demo\",\"aud\":\"conduitsharp-demo\"," +
            $"\"iat\":{now.ToUnixTimeSeconds()},\"exp\":{now.AddHours(1).ToUnixTimeSeconds()}}}");

        return SignHs256(header, payload, keyBytes);
    }

    private static string B64Url(string s) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string SignHs256(string header, string payload, byte[] keyBytes)
    {
        var input = Encoding.ASCII.GetBytes($"{header}.{payload}");
        using var hmac = new HMACSHA256(keyBytes);
        var sig = Convert.ToBase64String(hmac.ComputeHash(input))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{header}.{payload}.{sig}";
    }
}
