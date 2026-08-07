# Token Flow: npx wrapper

Status: planned, not built. `dotnet tool install -g ConduitSharp.TokenFlow` ships today.

## Why

The tool path needs the .NET 10 SDK. Token Flow's user proxies Claude Code or Codex traffic, so
they have Node and often no .NET SDK. `npx token-flow` reaches them with no other prerequisite.

## Shape

Single npm package. `postinstall` downloads the self-contained binary for the host RID off the
GitHub Release. npx runs lifecycle scripts by default.

```
npm/token-flow/
  package.json        bin: {"token-flow": "bin/token-flow.js"}, scripts.postinstall
  install.js          RID detect -> download -> checksum -> chmod
  bin/token-flow.js   spawn binary, forward argv + exit code
```

Alternative: `optionalDependencies`, one published package per platform (esbuild, biome, Tailwind
standalone use this). No install-time network, survives `--ignore-scripts`, costs 5 published
packages per release instead of 1. Move to it only if `--ignore-scripts` users complain.

## Prerequisites, in dependency order

| # | item | blocks because |
| :--- | :--- | :--- |
| 1 | `osx-arm64` ad-hoc signing in `tokenflow-binaries` | Unsigned arm64 Mach-O = `zsh: killed` on Apple Silicon. `PublishSingleFile` writes the bundle after the SDK signs the apphost, staling the signature ([dotnet/sdk#34917](https://github.com/dotnet/sdk/issues/34917)). Fix = `rcodesign sign` (Rust `apple-codesign`, runs on Linux) last, before `tar`. Untested for today's tarballs too |
| 2 | `linux-arm64` in the RID matrix | Image already ships `linux/arm64`. Graviton / Pi would 404 |
| 3 | `SHA256SUMS` as a release asset | `install.js` must verify. An unverified binary onto PATH is the `curl \| bash` failure mode |
| 4 | The name `token-flow` on npm | Unverified. Fallback `@liqngliz/token-flow`, also kills the squat risk |
| 5 | `NPM_TOKEN` secret, or npm Trusted Publishing (OIDC) | Prefer OIDC: matches how nuget.org is already wired, stores no long-lived secret |

## RID detection

`process.platform` + `process.arch`, never `uname`. `uname` is where the deleted `live.sh` failed:
Git Bash reports `mingw64_nt-10.0-x64`, matching nothing.

| platform / arch | RID | asset |
| :--- | :--- | :--- |
| `darwin` / `arm64` | `osx-arm64` | `.tar.gz` |
| `darwin` / `x64` | `osx-x64` | `.tar.gz` |
| `linux` / `x64` | `linux-x64` | `.tar.gz` |
| `linux` / `arm64` | `linux-arm64` | `.tar.gz` (blocked on #2) |
| `win32` / `x64` | `win-x64` | `.zip`, separate extract path |

Anything else exits with the `docker run` line, not a 404.

## Version pinning

npm version = source of truth. `install.js` fetches
`conduitsharp-spend-tokenflow-v${pkg.version}-<rid>.<ext>` directly: no GitHub API call, no
"latest" resolution, no rate limit, no fallback guess. `npx token-flow@1.2.0` means that exact
binary. `tokenflow.yml` publishes npm in the same tag-gated job that uploads the assets, version
from `${GITHUB_REF_NAME#tokenflow-v}`.

## Effort

| piece | size |
| :--- | :--- |
| `install.js` + `bin/token-flow.js` + `package.json` | ~120 lines |
| `rcodesign` step, `linux-arm64` RID, `SHA256SUMS` in `tokenflow.yml` | ~40 lines YAML |
| npm publish step + OIDC setup | ~15 lines + one-time npm config |
| Testing 5 platforms | the real cost. CI covers linux-x64 and win-x64; macOS arm64 needs a `macos-14` runner or a manual check |

~1 day, mostly prerequisite #1 and cross-platform testing, against ~1 hour for the `dotnet tool`
path. Worth building once someone without the .NET SDK asks.

## Out of scope

Homebrew formula, Scoop manifest, systemd unit, launchd plist. Each = a separate channel with its
own review; `docker run` covers the always-on case.
