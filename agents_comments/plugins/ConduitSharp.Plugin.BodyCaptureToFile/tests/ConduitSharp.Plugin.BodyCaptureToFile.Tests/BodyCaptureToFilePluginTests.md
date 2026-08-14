# plugins/ConduitSharp.Plugin.BodyCaptureToFile/tests/ConduitSharp.Plugin.BodyCaptureToFile.Tests/BodyCaptureToFilePluginTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## BodyCaptureToFilePluginTests.CaptureMemoryBytes_DeclaresTheRentedBuffer_SoTheGatewayCanBudgetIt

- (was line 36) The plugin rents a maxSize buffer per request and holds it until the background writer drains the queue. Undeclared, that multiplies by concurrency with nothing to shed it — declaring it puts the RAM under MaxRamBufferedBodyBytes and the gateway's 503.
- (was line 42) sum of both directions

## BodyCaptureToFilePluginTests.CaptureMemoryBytes_WithoutMaxSize_DeclaresTheDefault_NotZero

- (was line 48) Zero would mean "this plugin holds no memory of its own", which is exactly the silent gap this closes: a route omitting maxSize still rents the 4 KiB default.

## BodyCaptureToFilePluginTests.ValidateConfig_ValidMaxSize_DoesNotThrow

- (was line 62) Should not throw

## BodyCaptureToFilePluginTests.ExecuteAsync_CapturesFullBody_WrittenToDisk

- (was line 104) Give the background thread a moment to flush

## BodyCaptureToFilePluginTests.ExecuteAsync_TruncatesBody_WrittenToDisk

- (was line 132) Give the background thread a moment to flush

## BodyCaptureToFilePluginTests.ExecuteAsync_ConcurrentRequests_MultipleWrites

- (was line 161) Give the background thread time to flush all 100 requests

## BodyCaptureToFilePluginTests.ExecuteAsync_BoundsCapture_WhenNoMaxSizeConfigured

- (was line 183) Omitting maxSize used to copy the whole body twice (MemoryStream + a pooled rent sized to it), both outside the gateway's buffering budget. The default is what stops that.

## BodyCaptureToFilePluginTests.ExecuteAsync_RollsFile_WhenMaxFileBytesExceeded

- (was line 205) Without a roll the sink grows until the disk (or the tmpfs cap) stops it, and the writer dies on ENOSPC with every request still succeeding. Tiny cap so a handful of entries trip it.
- (was line 220) let the writer drain between entries so it can roll
- (was line 228) The live file is bounded by the roll — and right after one it may not exist at all until the next entry recreates it (FileMode.Append). Either way it must not hold everything.

## BodyCaptureToFilePluginTests.ExecuteAsync_CapturesResponseBody_WrittenToDisk_WithDirection

- (was line 243) no request block, so request is not captured
- (was line 256) client got every byte
