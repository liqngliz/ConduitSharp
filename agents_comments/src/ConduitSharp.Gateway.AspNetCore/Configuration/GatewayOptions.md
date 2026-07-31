# src/ConduitSharp.Gateway.AspNetCore/Configuration/GatewayOptions.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## ClientCertificateOptions.Path

- (was line 259) PFX file

## ClientCertificateOptions.StoreThumbprint

- (was line 265) Windows cert store

## RequestLimitsOptions

- Two budgets rather than one because they are bounded by different physical things, and confusing them is how a gateway gets OOM-killed instead of shedding.
- The tiering is the point: fast while RAM has room, slower on disk once it does not, and 503 only when neither does, so the gateway degrades in steps rather than falling off a cliff.
- These limits act at the gateway layer and are enforced for chunked bodies too, which Kestrel's transport-level limit does not cover.
- Defaults are sized for a small container, not for the host you develop on. Raise each deliberately, against the resource it meters.

## RequestLimitsOptions.MaxDiskBufferedBodyBytes

- Before v2.0.0 a 0 on the combined total disabled the check and meant 'buffer without bound'. Here 0 means the opposite, so an upgraded config carrying a 0 flips from unlimited to no-spill.
- The tmpfs trap: if SpillDirectory resolves to a tmpfs mount, which /tmp often is in containers, this 'disk' budget is really a second memory budget charged to the same cgroup. Either point the spill at real storage, or size it as memory and count it against the same limit as MaxRamBufferedBodyBytes.

## RequestLimitsOptions.SpillDirectory

- Measured: a RAM-backed tmpfs runs about 5x container overlayfs or a mounted volume, which are roughly equal to each other. That is a larger factor than anything in the buffering code.
- /tmp is tmpfs on many container images, and it cuts both ways. It makes the disk tier fast, and MaxDiskBufferedBodyBytes still bounds it, so spilling to tmpfs with a total budget that fits the pod is a legitimate configuration. But it does not relieve memory pressure, because the 'disk' tier is RAM: a budget sized assuming real storage will OOM the process where it would otherwise have degraded and shed. Pick tmpfs for speed with a memory-sized budget, or real storage for capacity, but pick rather than inheriting whatever /tmp happens to be.
