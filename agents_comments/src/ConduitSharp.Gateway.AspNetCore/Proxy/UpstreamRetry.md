# src/ConduitSharp.Gateway.AspNetCore/Proxy/UpstreamRetry.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## UpstreamRetry

- (was line 33) Polly outcome for "done — do not retry" regardless of the response status (last attempt, response already streaming, or client gone).

## UpstreamRetry.InvokeAsync

- (was line 94) Look the route up first: whether a non-idempotent method may retry lives in its config.
- (was line 108) Load balancing narrows AvailableDestinations to the single node it picked, so each attempt must start from the full set to be able to fail over.
- (was line 112) Response headers a plugin set before forwarding are the client's, not the attempt's — keep them across a reset.
