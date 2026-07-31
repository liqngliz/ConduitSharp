# tests/ConduitSharp.Integration.Tests/Gateway/CaptureMechanismTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## CaptureMechanismTests

- (was line 13) 4 MB — spans the 1 MiB spill threshold

## CaptureMechanismTests.StreamingRoute_DoesNotForceBuffering_NoSpill

- (was line 37) ReadsRequestBody=false → a plain route stays streaming; the tee never forces a spill. (That the tee actually captures is asserted deterministically by the full-capture RSS test in the E2E suite; HttpLogging's async end-of-request emission makes a count race here.)

## CaptureMechanismTests.RetryRoute_ReusesBuffer_CapturesBinary_AndReplays

- (was line 50) On a retry route the body is buffered (for the rewind), so the plugin reuses that seekable buffer — reading raw bytes, which captures a binary body the tee path would silently skip.
- (was line 55) replayed
