# src/ConduitSharp.Gateway.AspNetCore/Proxy/UpstreamProtocol.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## UpstreamProtocol

- (was line 25) Keyed on the cluster model, so a config change (which produces a new model) drops the derived one with it, and the HttpMessageInvoker is shared rather than rebuilt.
