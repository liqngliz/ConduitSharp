# TokenFlow: npx wrapper

Status: built, unpublished. Package at [npm/tokenflow/](../../npm/tokenflow/), publish job in
`tokenflow.yml`. Blocked on the one manual publish that claims the name (see
[docs/RELEASE.md](../RELEASE.md)).

## Why

The tool path needs the .NET 10 SDK. TokenFlow's user proxies Claude Code or Codex traffic, so
they have Node and often no .NET SDK. `npx @liqngliz/tokenflow` reaches them with no other prerequisite.

## Shape

npm package carries the app as framework-dependent DLLs. The `bin` launcher fetches the ASP.NET Core
**runtime** (not the SDK) on first run, then spawns it against the DLL.

```
npm/tokenflow/
  package.json   bin: {"tokenflow": "tokenflow.js"}, files: ["tokenflow.js", "app"]
  tokenflow.js   dotnet-install.{sh,ps1} --runtime aspnetcore --channel 10.0
                 --install-dir ~/.tokenflow/dotnet --no-path, then spawn it on the dll
  app/           dotnet publish -o app -p:UseAppHost=false (gitignored, CI builds it)
```

**Launcher sits at the package root, not `bin/`.** `.gitignore:2` ignores `bin/`, and a negation
rule for one file is more moving parts than a flat path.

**Runtime caches in `~/.tokenflow/dotnet`, not inside the package.** npx unpacks to a fresh cache
directory per version, so an in-package runtime would re-download 46 MB on every bump.

No apphost is ever executed. The binary that runs is Microsoft's `dotnet` muxer, shipped signed and
notarized (`Developer ID Application: Microsoft Corporation (UBF8T346G9)`), which is what removes
the macOS signing problem rather than solving it.

## Verified

`npm pack`, then `npx --yes --package=<tgz> -- tokenflow --urls http://localhost:5093` under
`env -i` (no PATH, no `DOTNET_ROOT`), cache wiped first.

| | 2026-08-08, scratch build | 2026-08-09, `npm/tokenflow/` |
| :--- | :--- | :--- |
| host | macOS arm64 + `node:22` aarch64 container | macOS arm64 |
| tarball | 1.26 MB | 1.26 MB, 56 files |
| runtime | 10.0.10, 46.6 MB download, 114 MB on disk | same, 112 MB in `~/.tokenflow` |
| `GET /` | 200, `<title>TokenFlow</title>` | same |
| bundle | served | `/assets/index-Bd7yLvJM.js` 200, 253 KB |
| `GET /info`, `GET /api/spend` | correct | correct |
| plugins | `token-spend` + `body-capture-file` | same |
| errors | 0 | 0 |

Both runs are now `npm/tokenflow/smoke.sh`, wired into `smoke.yml` on every push. `CLEAN=1` asserts
`dotnet` absent from PATH, `DOTNET_ROOT` unset, no install dir, `dotnet --info` non-zero, all of it
**before** npx runs, since the launcher creates `~/.tokenflow` itself and would otherwise make the
check pass for the wrong reason.

| leg | runner | CLEAN | proves |
| :--- | :--- | :--- | :--- |
| Linux | `ubuntu-latest` in `container: node:22` | 1 | the runtime is fetched, not found |
| macOS | `macos-14` | 0 | `dotnet-install.sh` on arm64 |
| Windows | `windows-latest` | 0 | `dotnet-install.ps1`, the separate code path |

The smoke runs npx from a scratch dir with an absolute tarball path. From inside
`npm/tokenflow/`, npm exec sees the cwd project already satisfying `@liqngliz/tokenflow@0.0.0`,
installs nothing, finds no `.bin` shim, and exits 0 without starting the launcher: green on POSIX
(shebang resolves directly), silent no-op on Windows.

Only the container leg can assert absence: every GitHub-hosted runner ships a .NET 10 SDK, and
macOS runners cannot run containers. It stays SDK-free because `npx-build` publishes the DLLs in a
separate job and the matrix downloads them as an artifact. Still not covered: macOS and Windows
with no .NET anywhere. `tokenflow.js` only stats `~/.tokenflow/dotnet/dotnet`, never PATH or
`DOTNET_ROOT`, so a preinstalled SDK cannot turn those two green for the wrong reason.

## `UseAppHost=false` goes on the publish command, not the csproj

Without it, a framework-dependent publish emits an apphost next to the DLLs (77 KB ELF when built
on Linux CI). It has no working invocation inside the npm package: an apphost resolves its
framework from `DOTNET_ROOT` or a system-wide install, and the bootstrapped runtime sits in
`~/.tokenflow/dotnet`, on neither. A user who runs `./ConduitSharp.Spend` instead of the npm bin gets
`You must install .NET` on Linux and `cannot execute binary file` on macOS, where the ELF is not
even the host format.

Moving it into `ConduitSharp.Spend.csproj` is now possible and would also cover the Dockerfile.
Blocked until 2026-08-09 by `tokenflow-binaries`, whose `--self-contained -p:PublishSingleFile=true`
requires the apphost; that job is deleted. Left on the command line, one flag either way.

`Dockerfile:30` publishes the same framework-dependent shape and runs
`ENTRYPOINT ["dotnet", "ConduitSharp.Spend.dll"]`, so it carries the same dead apphost. Same
one-flag fix, not required for npx.

## Prerequisites

One left, manual, blocks the first release: npm cannot configure a trusted publisher for a package
that does not exist, so `@liqngliz/tokenflow` needs one `npm publish` before OIDC can take over.
Steps and the field values: [docs/RELEASE.md](../RELEASE.md).

## Name: `@liqngliz/tokenflow` on npm, `tokenflow` everywhere else

Unscoped `tokenflow` is unavailable. npm's similarity guard strips punctuation, so it reads as
`token-flow`, which exists, and the publish 403s with `Package name too similar to existing
package`. A registry lookup returns 404 for the name and does not predict this; only the publish
does. Scoping is npm's own suggested remedy and removes the squat risk on the scope.

| channel | name |
| :--- | :--- |
| npm package | `@liqngliz/tokenflow` |
| npm bin | `tokenflow` (npx matches a scoped package against its post-scope segment, so `npx @liqngliz/tokenflow` needs no `--package`) |
| `dotnet tool` command | `tokenflow` (`ToolCommandName`) |
| image | `ghcr.io/liqngliz/tokenflow` |
| NuGet package id | `ConduitSharp.TokenFlow` |
| tag prefix, workflow file | `tokenflow-v*`, `tokenflow.yml` |

`TokenFlow` is the product name, one word: `<title>`, `/info`, headings.

## First-run cost

46.6 MB over the wire, 114 MB on disk, ~1 minute. Later runs are instant.

**Fetch the runtime from `tokenflow.js` on first run, not from `postinstall`.** npm hides
lifecycle-script output unless the user passes `--foreground-scripts`, so a `postinstall` download
is a silent minute that reads as a hang (verified: the progress lines are invisible under both
`npm i -g` and `npx`). Fetching from the bin puts the output on the user's terminal and survives
`--ignore-scripts`.

Idempotent either way: skip when `~/.tokenflow/dotnet/dotnet` already exists.

## Platform handling

`process.platform === 'win32'` selects `dotnet-install.ps1` over `dotnet-install.sh`, and
`dotnet.exe` over `dotnet`. That is the entire platform matrix. One portable
publish covers every RID, so no `process.arch` detection, no per-platform assets, no checksums
(the Microsoft script verifies its own payload).

## Rejected: download the native binary from the GitHub Release

The `<tag>-<rid>` tarballs are self-contained single-file apphosts. On Apple Silicon an unsigned
arm64 Mach-O is SIGKILLed by the kernel, and `PublishSingleFile` writes the bundle after the SDK
signs the apphost, staling the signature ([dotnet/sdk#34917](https://github.com/dotnet/sdk/issues/34917)).
Fixing that means `rcodesign sign` (Rust `apple-codesign`, runs on Linux) as the last step before
`tar`, plus a `linux-arm64` RID, plus `SHA256SUMS`, plus a 5-platform test matrix. The runtime
bootstrap above needs none of it.

**`tokenflow-binaries` was deleted entirely, 2026-08-09.** Every surviving RID also fought a
platform defender, and none of them added reach over the three channels that already exist.

| RID | defender | user cost |
| :--- | :--- | :--- |
| `win-x64` | SmartScreen, unsigned + zero reputation | "Windows protected your PC", More info → Run anyway |
| `osx-x64` | Gatekeeper, only when the download carries `com.apple.quarantine` | `xattr -dr com.apple.quarantine` (curl + `tar xz` sets no quarantine; Safari + Archive Utility does) |
| `osx-arm64` | kernel, unsigned arm64 Mach-O | `zsh: killed`, no click-through |
| `linux-x64` | none | none |

Buying out of the Windows warning = an OV cert (reputation accrues over weeks) or EV (immediate,
hardware token, ~$300+/yr). Restoring any of it means the signing step above.

## Out of scope

Homebrew formula, Scoop manifest, systemd unit, launchd plist. Each = a separate channel with its
own review; `docker run` covers the always-on case.
