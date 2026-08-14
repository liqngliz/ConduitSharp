# tests/ConduitSharp.PluginChain.E2E.Tests/GatewayHostProcessFixture.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## GatewayHostProcessFixture.InitializeAsync

- (was line 43) 1. Publish the probe project into the plugins folder as a dropped-in DLL.
- (was line 46) 2. Real upstream: 200 with a small cacheable body; counts hits so a cache hit is provable.
- (was line 59) 3. routes.json: probe-a(1), probe-b(2), probe-c(3), cache(4), probe-e(5), then the forward.
- (was line 82) 4. Spawn the host, pointed at the temp routes + plugins, logging JSON to stdout.

## GatewayHostProcessFixture.OnStdout

- (was line 116) The Json console formatter emits one JSON object per record. Pull probe records out.
- (was line 128) build noise / non-JSON lines

## GatewayHostProcessFixture.WaitForHealthAsync

- (was line 143) not up yet

## GatewayHostProcessFixture.DisposeAsync

- (was line 160) best effort
