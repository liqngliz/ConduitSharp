# tests/ConduitSharp.Integration.Tests/Gateway/BufferedPathBodyLimitTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## BufferedPathBodyLimitTests.CapturedMaxRequestBodySize

- (was line 22) Used to capture what the gateway set the MaxRequestBodySize feature to

## BufferedPathBodyLimitTests.BufferedRoute_WithLimit_SetsFeatureLimit

- (was line 38) Reset

## BufferedPathBodyLimitTests.BothPaths_WithNegativeLimit_DoNotModifyFeature

- (was line 180) Our mock feature returns -2

## BufferedPathBodyLimitTests.BufferedRoute_AboveKestrelDefault_EndToEnd_HonorsConfiguredLimit

- (was line 217) 1. Configure the route with limit 40MB (above Kestrel's 30MB default)
- (was line 238) 2. Start a real Kestrel host on a random port
- (was line 253) 3. Get the bound port
- (was line 256) 4. Send a 35MB body (below 40MB but above 30MB Kestrel limit) If the bug is present, Kestrel will throw 413 Payload Too Large. If the bug is fixed, it will forward successfully and return 200 OK.
- (was line 263) 35 MB
- (was line 268) Prove that the request made it through Kestrel's 30MB default and was successfully forwarded!
- (was line 271) Clean up
