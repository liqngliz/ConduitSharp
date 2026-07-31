# tests/ConduitSharp.BodyCaptureMemory.E2E.Tests/CaptureMemoryMatrixTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## CaptureMemoryMatrixTests

- (was line 24) excluded from `make test` (Category!=E2E) and self-documents the 500 MB weight

## CaptureMemoryMatrixTests.RunAsync

- (was line 45) Capturing the FULL body needs the 32 KiB ceiling lifted to the body size.

## CaptureMemoryMatrixTests.StreamingRoute_PrefixCapture_TeesCheap_NoDisk

- (was line 67) 1 — streaming route, bounded prefix: the tee path. Cheap, no disk, captures the text prefix.

## CaptureMemoryMatrixTests.RetryRoute_ReusesBuffer_CapturesBinary_AndReplays

- (was line 80) 2 — retry route, bounded prefix, BINARY body: proves the REUSE path ran. The tee skips binary, so a positive capture count on octet-stream can only come from the seekable-buffer reuse branch.
- (was line 90) body replayed

## CaptureMemoryMatrixTests.FullCapture_StreamingRoute_HoldsWholeBodyInRam

- (was line 95) 3 — SAFETY: full-body capture on a streaming route holds the entire body in RAM (no disk tier). RSS itself proves the tee buffered the body — 500 MB resident with zero spill.

## CaptureMemoryMatrixTests.RaisedBufferThreshold_KeepsBodyInRam_SkipsSpill

- (was line 110) 5 — gateway buffer clamp is now configurable (no capture plugin needed): a retry route with a raised MemoryBufferThreshold keeps the whole body in RAM instead of spilling. Proves the lift.
