# plugins/ConduitSharp.Plugin.BodyCapture/src/ConduitSharp.Plugin.BodyCapture/PerRouteBodyLimitInterceptor.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## PerRouteBodyLimitInterceptor.OnResponseAsync

- (was line 28) Request bodies only — the response is captured by ResponsePrefixStream, not HttpLogging.
