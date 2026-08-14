# tests/ConduitSharp.Observability.Tests/StructuredRequestLoggerTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## StructuredRequestLoggerTests.OnRequestCompleted_2xxStatus_LogsAtInformation

- (was line 24) 2xx — logged at Information

## StructuredRequestLoggerTests.OnRequestCompleted_3xxAnd4xxStatus_LogsAtInformation

- (was line 43) 3xx / 4xx — logged at Information (not errors)

## StructuredRequestLoggerTests.OnRequestCompleted_5xxStatus_LogsAtError

- (was line 62) 5xx — logged at Error (matches OTel span status, which is also Error-only-on-5xx)

## StructuredRequestLoggerTests.OnRequestCompleted_LogMessage_ContainsRequestId

- (was line 81) Log message contains key fields
