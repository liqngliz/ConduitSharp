# examples/ConduitSharp.Spend/entrypoint.sh

Inline commentary for the file above. Read this before changing it; update it in the same change.

## entrypoint.sh

- Exists so a local model server on the host works with no extra flags. Docker Desktop resolves host.docker.internal already; plain Linux Docker does not, and the failure is a silent connection refused into a container with nothing listening, which is the worst kind.
- The gateway address comes from /proc/net/route field 3, little-endian hex, e.g. 010011AC for 172.17.0.1.
- Hex conversion is hand-written because the aspnet base image ships mawk, not gawk, and strtonum() is a gawk extension. The first version used it and failed with "function strtonum never defined", which would have made the whole fallback a no-op while looking fine.
- Guarded by getent, so it is inert on Docker Desktop where the name already resolves. Verified: zero entrypoint messages on Desktop, and "host.docker.internal -> 172.17.0.1" when resolution is forced to fail.
- If detection fails it warns and starts anyway, since every route except local works without it.
