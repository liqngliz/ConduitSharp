# tests/Shared/E2E/GatewayProcessFixture.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## GatewayProcessFixture

- (was line 22) HS256 signing key from every example's routes.json (base64) — same demo credentials.

## GatewayProcessFixture.ExampleDirName

- (was line 25) ---- What each concrete stack supplies -----------------------------------

## GatewayProcessFixture.GatewayUrl

- (was line 33) ---- Derived / shared ----------------------------------------------------

## GatewayProcessFixture.InitializeAsync

- (was line 43) ---- IAsyncLifetime ------------------------------------------------------

## GatewayProcessFixture.CleanAsync

- (was line 66) ---- Launcher ------------------------------------------------------------

## GatewayProcessFixture.StopAsync

- (was line 103) Ignore errors — nothing may be running on the first clean.

## GatewayProcessFixture.RunAsync

- (was line 107) Runs an external process and waits for it to exit.
- (was line 125) Read both streams concurrently (sequential reads can deadlock when stderr fills its pipe buffer), and gate on process EXIT rather than stream EOF: long-lived grandchildren — the services themselves, or MSBuild node-reuse workers spawned by dotnet publish — inherit the launcher's stdout pipe and keep it open long after the launcher exits, so waiting for EOF hangs forever.

## GatewayProcessFixture.WaitForGatewayAsync

- (was line 144) ---- Readiness -----------------------------------------------------------
- (was line 160) not ready yet
- (was line 165) Dump gateway log to help diagnose startup failures.

## GatewayProcessFixture.AssertYarpForwarderIsServing

- (was line 176) The readiness probe above already forwarded /health upstream, so YARP's forwarder must have logged it. Guards against the gateway silently falling back to some other engine — forwarding is YARP's ForwarderMiddleware now, not a swappable "http-proxy" plugin.

## GatewayProcessFixture.MintDemoJwt

- (was line 187) ---- JWT minting — same algorithm as generate-token.sh / generate-token.ps1

## GatewayProcessFixture.LocateExampleRoot

- (was line 213) ---- Solution-root discovery ---------------------------------------------
