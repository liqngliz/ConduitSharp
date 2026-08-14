# TokenSpendPlugin Refactoring Plan

Decompose the 623-line `TokenSpendPlugin.cs` into focused, highly testable static components.

## Motivation & Benefits
Currently, `TokenSpendPlugin.cs` handles 6 distinct responsibilities:
1. ASP.NET Core pipeline orchestration
2. HTTP Response decompression (GZip, Brotli, ZLib)
3. SSE stream sniffing and frame usage extraction
4. JSON dotted-path token usage calculation
5. LLM Request parsing (OpenAI Chat, Anthropic, Codex Responses API)
6. Client Key & JWT Bearer claim resolution

Decomposing these concerns into dedicated static classes makes each unit **100% testable in isolation** without needing `HttpContext` mocks or complex middleware setups. `TokenSpendPlugin.cs` will shrink from 623 lines to **~60 lines**.

---

## Proposed Component Breakdown

### 1. `ResponseDecoder.cs`
**File**: `plugins/ConduitSharp.Plugin.TokenSpend/src/ConduitSharp.Plugin.TokenSpend/ResponseDecoder.cs`
- `byte[] Decode(byte[] body, string contentEncoding)`
- `bool IsEventStream(string? contentType)`
- `bool LooksLikeEventStream(ReadOnlySpan<byte> body)`
- **Testing**: Direct unit tests with raw gzipped/brotli byte buffers and sample SSE headers.

### 2. `SseUsageExtractor.cs`
**File**: `plugins/ConduitSharp.Plugin.TokenSpend/src/ConduitSharp.Plugin.TokenSpend/SseUsageExtractor.cs`
- `Task<SpendRecord> WithStreamedUsageAsync(SpendRecord row, byte[] body, TokenSpendConfig config)`
- `SpendRecord? UsageFromFrame(SpendRecord row, string data, TokenSpendConfig config)`
- **Testing**: Unit tests passing raw SSE frame strings (Anthropic `message_delta`, OpenAI `response.completed`).

### 3. `JsonUsageExtractor.cs`
**File**: `plugins/ConduitSharp.Plugin.TokenSpend/src/ConduitSharp.Plugin.TokenSpend/JsonUsageExtractor.cs`
- `SpendRecord WithUsage(SpendRecord row, ReadOnlySpan<byte> body, TokenSpendConfig config)`
- `long Column(JsonElement root, IReadOnlyList<string> add, IReadOnlyList<string> subtract)`
- `long SumPaths(JsonElement root, IReadOnlyList<string> paths)`
- `bool TryGetByPath(JsonElement root, string path, out long value)`
- `bool TryGetStringByPath(JsonElement root, string path, out string value)`
- **Testing**: Unit tests validating nested JSON dotted paths, subtraction, and path fallback logic.

### 4. `RequestFactsExtractor.cs`
**File**: `plugins/ConduitSharp.Plugin.TokenSpend/src/ConduitSharp.Plugin.TokenSpend/RequestFactsExtractor.cs`
- `Task<RequestFacts> ReadRequestAsync(HttpContext context, TokenSpendConfig config)`
- `RequestFacts ParseRequest(ReadOnlySpan<byte> body, TokenSpendConfig config)`
- `int ToolBlocks(JsonElement message, string? role)`
- `string TextOf(JsonElement message)`
- `string WithoutPreamble(string text, TokenSpendConfig config)`
- `IReadOnlyDictionary<string, string>? ReadMetadata(...)`
- `string? ReadSessionId(...)`
- **Testing**: Unit tests asserting model extraction, tool counting, session naming, and preamble stripping across OpenAI, Anthropic, and Codex payloads.

### 5. `ClientKeyResolver.cs`
**File**: `plugins/ConduitSharp.Plugin.TokenSpend/src/ConduitSharp.Plugin.TokenSpend/ClientKeyResolver.cs`
- `string ResolveClientKey(HttpContext context, TokenSpendConfig config)`
- `string? ClaimFromBearer(HttpContext context, string claim)`
- **Testing**: Unit tests with custom key headers and base64-encoded JWT bearer tokens.

### 6. Slim `TokenSpendPlugin.cs` (~60 lines)
**File**: `plugins/ConduitSharp.Plugin.TokenSpend/src/ConduitSharp.Plugin.TokenSpend/TokenSpendPlugin.cs`
- Retains only `IPipelinePlugin` interface implementation, config validation, and `ExecuteAsync` orchestration.

---

## Verification Plan

### Automated Tests
1. Run `dotnet test plugins/ConduitSharp.Plugin.TokenSpend/tests/ConduitSharp.Plugin.TokenSpend.Tests` to verify all 39 existing unit tests pass without regressions.
2. Add dedicated test files for each new extractor:
   - `ResponseDecoderTests.cs`
   - `SseUsageExtractorTests.cs`
   - `JsonUsageExtractorTests.cs`
   - `RequestFactsExtractorTests.cs`
   - `ClientKeyResolverTests.cs`
3. Run full solution test suite: `dotnet test --filter "FullyQualifiedName!~E2E" ConduitSharp.sln`.
