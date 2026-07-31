# tests/ConduitSharp.Integration.Tests/Pipeline/PluginChainOrderE2ETests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## PluginChainOrderE2ETests.Request_runs_down_response_runs_up_and_cache_hit_stops_at_cache

- (was line 69) Request 1: cache miss. Down 1,2,3,(cache),5 then up 5,(cache),3,2,1. Forward is reached.
- (was line 78) forward ran once
- (was line 80) Request 2: same path, cache HIT. Down 1,2,3 then the cache answers and unwinds 3,2,1. probe-e never runs and the forward is never reached.
- (was line 92) still 1 — forward NOT reached on the hit
- (was line 94) Request 3: different path, cache miss again. Full chain, forward reached a second time.

## PluginChainOrderE2ETests.OrderProbe

- (was line 106) A plugin that logs "enter" before next and "exit" after, through the host's real ILoggerFactory under category "Probe.{id}". Nothing is recorded in-plugin; the order comes from the log sink.

## PluginChainOrderE2ETests.SequenceLog

- (was line 123) Records probe log messages in emission order as "{id}:{message}", e.g. "a:enter".
