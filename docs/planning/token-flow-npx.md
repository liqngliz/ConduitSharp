# Token Flow: npx wrapper

Status: planned, not built. `dotnet tool install -g ConduitSharp.TokenFlow` ships today.

## Why

The tool path needs the .NET 10 SDK. Token Flow's user proxies Claude Code or Codex traffic, so
they have Node and often no .NET SDK. `npx token-flow` reaches them with no other prerequisite.

## Shape

npm package carries the app as framework-dependent DLLs. `postinstall` fetches the ASP.NET Core
**runtime** (not the SDK) into the package directory. `bin` spawns that runtime against the DLL.

```
npm/token-flow/
  package.json        bin: {"token-flow": "bin/token-flow.js"}, scripts.postinstall
  install.js          dotnet-install.{sh,ps1} --runtime aspnetcore --channel 10.0
                      --install-dir <pkg>/.dotnet --no-path
  bin/token-flow.js   spawn(<pkg>/.dotnet/dotnet, [app/ConduitSharp.Spend.dll, ...argv])
  app/                dotnet publish -o app -p:UseAppHost=false
```

No apphost is ever executed. The binary that runs is Microsoft's `dotnet` muxer, shipped signed and
notarized (`Developer ID Application: Microsoft Corporation (UBF8T346G9)`), which is what removes
the macOS signing problem rather than solving it.

## Verified 2026-08-08

Built the real package (`package.json` + `install.js` + `bin/token-flow.js` + `publish -o app
-p:UseAppHost=false`), `npm pack`, ran it two ways.

**A. macOS arm64**, `env -i` (no PATH, no `DOTNET_ROOT`), cwd `/tmp`, isolated runtime.
**B. `node:22` container, aarch64**, `dotnet` not on PATH, no dotnet directories, host SDK not
mounted. `npx --yes --package=<tgz> -- token-flow`.

| | |
| :--- | :--- |
| npm tarball | 1.26 MB (3.1 MB unpacked, 468 KB of it wwwroot) |
| runtime | 10.0.10, 46.6 MB download, 114 MB on disk, no sudo, no PATH edit |
| `GET /` | 200, `<title>Token Flow</title>`, both runs |
| `GET /info`, `GET /api/spend` | correct |
| plugins | `token-spend` + `body-capture-file` registered |
| errors | 0 |

Not covered: macOS with no .NET installed anywhere. A is macOS but has an SDK present; B has no
.NET but is Linux. Needs a clean Mac to close.

## `UseAppHost=false` goes on the publish command, not the csproj

Without it, a framework-dependent publish emits an apphost next to the DLLs (77 KB ELF when built
on Linux CI). It has no working invocation inside the npm package: an apphost resolves its
framework from `DOTNET_ROOT` or a system-wide install, and the bundled runtime sits in
`<pkg>/.dotnet`, on neither. A user who runs `./ConduitSharp.Spend` instead of the npm bin gets
`You must install .NET` on Linux and `cannot execute binary file` on macOS, where the ELF is not
even the host format.

Do not move it into `ConduitSharp.Spend.csproj`. `tokenflow-binaries` publishes
`--self-contained -p:PublishSingleFile=true`, which requires the apphost; a project-level property
would break that job.

`Dockerfile:30` publishes the same framework-dependent shape and runs
`ENTRYPOINT ["dotnet", "ConduitSharp.Spend.dll"]`, so it carries the same dead apphost. Same
one-flag fix, not required for npx.

## Prerequisites

| # | item | note |
| :--- | :--- | :--- |
| 1 | The name `token-flow` on npm | Unverified. Fallback `@liqngliz/token-flow`, also kills the squat risk |
| 2 | `NPM_TOKEN` secret, or npm Trusted Publishing (OIDC) | Prefer OIDC: matches how nuget.org is already wired, stores no long-lived secret |

## First-run cost

46.6 MB over the wire, 114 MB on disk, ~1 minute. Later runs are instant.

**Fetch the runtime from `bin/token-flow.js` on first run, not from `postinstall`.** npm hides
lifecycle-script output unless the user passes `--foreground-scripts`, so a `postinstall` download
is a silent minute that reads as a hang (verified: the progress lines are invisible under both
`npm i -g` and `npx`). Fetching from the bin puts the output on the user's terminal and survives
`--ignore-scripts`.

Idempotent either way: skip when `<pkg>/.dotnet/dotnet` already exists.

## Platform handling

`process.platform === 'win32'` selects `dotnet-install.ps1` over `dotnet-install.sh`, and
`.dotnet/dotnet.exe` over `.dotnet/dotnet`. That is the entire platform matrix. One portable
publish covers every RID, so no `process.arch` detection, no per-platform assets, no checksums
(the Microsoft script verifies its own payload).

## Rejected: download the native binary from the GitHub Release

The `<tag>-<rid>` tarballs are self-contained single-file apphosts. On Apple Silicon an unsigned
arm64 Mach-O is SIGKILLed by the kernel, and `PublishSingleFile` writes the bundle after the SDK
signs the apphost, staling the signature ([dotnet/sdk#34917](https://github.com/dotnet/sdk/issues/34917)).
Fixing that means `rcodesign sign` (Rust `apple-codesign`, runs on Linux) as the last step before
`tar`, plus a `linux-arm64` RID, plus `SHA256SUMS`, plus a 5-platform test matrix. The runtime
bootstrap above needs none of it.

Independent of npx, the `osx-arm64` tarball is broken today for exactly this reason and still needs
that signing step if it is meant to work.

## Effort

| piece | size |
| :--- | :--- |
| `install.js` + `bin/token-flow.js` + `package.json` | ~60 lines |
| npm publish step in `tokenflow.yml`, version from `${GITHUB_REF_NAME#tokenflow-v}` | ~15 lines + one-time npm config |
| Testing | macOS arm64 + linux-x64 + win-x64 |

~2 hours, against ~1 hour for the `dotnet tool` path and ~1 day for the native-binary shape.

## Out of scope

Homebrew formula, Scoop manifest, systemd unit, launchd plist. Each = a separate channel with its
own review; `docker run` covers the always-on case.
