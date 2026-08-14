# plugins/ConduitSharp.Plugin.BodyCapture/tests/ConduitSharp.Plugin.BodyCapture.Tests/BodyCapturePluginTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## BodyCapturePluginTests.Build

- (was line 12) The plugin delegates capture to the framework's HttpLogging middleware, which logs under its own category rather than through an injected ILogger<T>. So these tests assert on what the host's ILoggerFactory actually receives — the same records that reach OTLP → Loki in prod.
- (was line 20) HttpLogging's category is Warning-filtered by default

## BodyCapturePluginTests.ForwardByDrainingBody

- (was line 26) Models YARP's forward: the real request pipeline reads Request.Body to stream it upstream. Draining to Stream.Null is exactly what the in-memory upstream does, and it drives the HttpLogging capture stream the plugin installed.

## BodyCapturePluginTests.ForwardDrainThenReturn502

- (was line 31) How an upstream error actually surfaces: YARP's forward is terminal, reads the body to stream it, and on an upstream failure writes a 502 — it does NOT throw (see ConsecutiveFailuresHealth Policy, which detects failure by status code + IForwarderErrorFeature, not by catching).

## BodyCapturePluginTests.Return502BeforeReading

- (was line 40) Upstream unreachable — YARP fails to connect and writes 502 before the request body is read.

## BodyCapturePluginTests.ForwardDrainThenThrow

- (was line 47) A throw, by contrast, models what genuinely propagates through the plugin chain: a client abort mid-request, or a plugin throwing in its post-next() code — NOT an upstream error.

## BodyCapturePluginTests.RejectWithoutReading

- (was line 55) A later auth/RBAC plugin rejecting the request: writes 401/403 and returns WITHOUT reading the body or reaching the forward — the same short-circuit JwtAuthPlugin does. Only reachable when capture is ordered BEFORE auth; in the shipped routes auth is order 1 and capture order 10, so a rejection short-circuits before capture runs at all and neither branch sees the body.

## BodyCapturePluginTests.SeekableRequest

- (was line 65) Seekable body → the plugin takes its reuse branch (models a retry route or a body-reading plugin having already buffered the body upstream in the chain).
- (was line 73) CanSeek == true

## BodyCapturePluginTests.Request

- (was line 83) Non-seekable: models the streaming path, so the plugin takes its HttpLogging tee branch (the reuse branch fires only when the gateway has already buffered a seekable body — see the retry/binary reuse tests in the integration suite).

## BodyCapturePluginTests.ValidateConfig_FlatShape_Throws_WithMigrationHint

- (was line 108) The pre-2.0 flat shape captures nothing now, so it must fail the load, not no-op.

## BodyCapturePluginTests.ValidateConfig_CeilingIsConfigurable_RaisedCeilingAcceptsLargerMaxSize

- (was line 149) BodyCapture:MaxCaptureBytes lifts the default 32 KiB ceiling. A 48 KiB maxSize that the default rejects is accepted once the ceiling is raised to 64 KiB.
- (was line 159) 48 KiB is under the raised 64 KiB ceiling — no throw
- (was line 161) default still rejects

## BodyCapturePluginTests.Ctor_CeilingBeyondAddressableRange_ThrowsWithItsSize

- (was line 173) A captured prefix is addressed by an int (HttpLogging's RequestBodyLogLimit), so an oversized ceiling must say so plainly rather than die as an opaque conversion error.
- (was line 176) 4 GiB

## BodyCapturePluginTests.ExecuteAsync_ClampsCaptureToCeiling_EvenIfValidateConfigWasSkipped

- (was line 183) Defence in depth: the ceiling bounds heap, so it must not depend on a host remembering to call ValidateConfig. A 1 MB request never yields more than a 32 KiB prefix.

## BodyCapturePluginTests.ReadsRequestBody_IsFalse_SoRouteKeepsStreaming

- (was line 197) The whole point: this plugin must NOT force the gateway's buffering path.

## BodyCapturePluginTests.ExecuteAsync_ForwardsBodyIntact_ToTheUpstream

- (was line 229) The capture must be transparent: the downstream reader (the "upstream") sees every byte, regardless of the small capture cap.

## BodyCapturePluginTests.ExecuteAsync_DoesNotLogBody_ForUnrecognizedMediaType

- (was line 258) Behaviour inherited from HttpLogging's MediaTypeOptions: only text-ish bodies are logged. A binary upload streams through untouched and unlogged rather than spraying bytes at Loki.

## BodyCapturePluginTests.ExecuteAsync_LogsUnderPluginCategory_NotHttpLoggings

- (was line 272) Guards a silent-failure footgun: HttpLogging logs bodies at Information under "Microsoft.AspNetCore.HttpLogging.*", and every stock appsettings.json in this repo filters "Microsoft.AspNetCore" to Warning. If the category leaked through unrenamed, production would drop every captured body while these tests still passed.

## BodyCapturePluginTests.ExecuteAsync_BodiesSurvive_TheStockAspNetCoreWarningFilter

- (was line 288) End-to-end proof of the above, through a real filter pipeline configured exactly like the repo's appsettings.json ("Microsoft.AspNetCore": "Warning").

## BodyCapturePluginTests.ExecuteAsync_LogRecordCarriesRouteIdAndPath_AlongsideBody

- (was line 307) A captured body with no route attribution is unattributable in Loki. The interceptor stamps the gateway's route id (context.Items) and the path into the SAME combined record.

## BodyCapturePluginTests.ExecuteAsync_ConcurrentRoutes_EachRecordPairsItsOwnRouteAndBody

- (was line 324) One singleton plugin, many in-flight requests across "routes": every combined record must pair route-i with body-i — a cross-pairing means per-request state leaked.

## BodyCapturePluginTests.TeePath_UpstreamReadsBodyThenReturns502_BodyIsCaptured_ItWasForwarded

- (was line 347) Capture is scoped to its potential cause: the body is logged only when it entered the causal chain — i.e. was actually forwarded (read). A body that never reached upstream (502 before read, connection refused/dropped) or was rejected before the forward is not the cause of that outcome, and logging it would be storing payloads for failures they did not cause. The tee's read-gated capture IS that scoping, not a limitation of it.
- (was line 353) The forward is terminal (chain.Run) and turns upstream failure into a 502 RESPONSE, not an exception, so the realistic cases below are 502-returning forwards; a throw models the narrower client-abort / plugin-throw path.
- (was line 361) The body was read and sent upstream, then upstream failed → 502. Here the body IS a potential cause (a malformed payload upstream rejected), so it is in scope and logged.

## BodyCapturePluginTests.TeePath_UpstreamUnreachable_Returns502WithoutReadingBody_BodyNotCaptured_NotItsCause

- (was line 375) Upstream unreachable: 502 written before the body is read. This is a transport/availability failure — the body did not cause it and is not needed to diagnose it — so it is correctly out of scope and not logged. Read-gated capture doing its job, not a gap.

## BodyCapturePluginTests.ReusePath_UpstreamUnreachable_Returns502WithoutReadingBody_LogsAnyway_OverScopes

- (was line 390) Counterpoint: the reuse branch logs the prefix BEFORE next(), unconditionally, because that is where the already-buffered seekable body is in hand. So it records a body for a request that never forwarded — a body that was not the cause. By the scope-to-cause rule this is a mild over-capture, tolerated because it is the free path on routes that already buffer, and because in the shipped ordering auth (order 1) has narrowed the traffic before capture runs.

## BodyCapturePluginTests.TeePath_ReadsBodyThenThrows_BodyCaptured_ModelsClientAbortNotUpstreamError

- (was line 407) A throw is NOT how upstream failure surfaces (that is a 502). It models a client abort after the body was sent, or a plugin throwing post-next(). The body was read and forwarded, so it was in the causal chain — HttpLogging still emits the record on the exception path.

## BodyCapturePluginTests.TeePath_LaterAuthRejects_BodyNotCaptured

- (was line 420) Auth rejection (401 / 403). Same scope-to-cause rule: for a rejected request the AUTH decision is the cause, not the payload, so the body is out of scope. In the shipped routes auth is order 1 and capture order 10, so a rejection short-circuits before capture even runs — the body is never logged regardless of branch. These tests cover the deliberate forensic arrangement (capture ordered before auth to record what a rejected caller sent); only the reuse branch, by logging pre-forward, actually widens scope to include it.
- (was line 433) Even ordered before auth, the tee needs the forward to read the body — a rejection short-circuits before that, so nothing is teed. Read-gated capture keeps the rejected payload out of scope; deliberately auditing rejected bodies is not possible on the streaming path.

## BodyCapturePluginTests.ReusePath_OrderedBeforeAuth_RejectedBodyIsCaptured

- (was line 452) Widening scope on purpose: the reuse branch logs before next(), so ordering capture ahead of auth on a buffered route DOES record what a rejected caller sent. This is a deliberate forensic choice (attack-payload capture), not the default — it stores bodies that were not the cause of the rejection, which is exactly what the scope-to-cause rule warns against, so enable it only where that trade is intended. (Requires a buffered/seekable route; a plain streaming route falls to the tee above and captures nothing.)

## BodyCapturePluginTests.ForwardWritingResponse

- (was line 469) Response capture (§2a) — the bounded write-through tee on Response.Body
- (was line 472) A forward that writes a response body, the way YARP writes the upstream's response.

## BodyCapturePluginTests.Response_capturesBinary_UnlikeTheRequestTee

- (was line 506) The response tee reads raw bytes, so it captures a binary response — the request streaming path (HttpLogging) would skip it. Content type does not gate response capture.

## BodyCapturePluginTests.Response_isTransparent_ClientGetsEveryByte

- (was line 535) original restored after the forward

## BodyCapturePluginTests.Request_and_response_bothCaptured_inOneConfig

- (was line 547) forward reads the request (tees it)

## BodyCapturePluginTests.CaptureMemoryBytes_ceilingCapsEachDirection

- (was line 586) maxSize above the ceiling clamps to it, per direction, so the reservation stays honest.
