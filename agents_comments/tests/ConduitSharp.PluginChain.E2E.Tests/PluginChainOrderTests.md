# tests/ConduitSharp.PluginChain.E2E.Tests/PluginChainOrderTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## PluginChainOrderTests.Request_down_response_up_and_cache_hit_stops_at_cache_from_real_process_logs

- (was line 28) Request 1: cache miss. Down 1,2,3,(cache),5 then up 5,(cache),3,2,1. Forward reached.
- (was line 39) Request 2: same path, cache HIT. Down 1,2,3 then cache answers and unwinds 3,2,1. probe-e never runs; the forward is never reached.
- (was line 51) forward NOT reached on the hit
- (was line 53) Request 3: different path, cache miss again. Full chain, forward reached a second time.

## PluginChainOrderTests.SettleAsync

- (was line 65) The probe log records arrive over a redirected stdout pipe, so they trail the HTTP response slightly. Poll until the sequence stops growing.
