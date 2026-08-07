# TokenFlow: npx wrapper

Status: built, unpublished. Package at [npm/tokenflow/](../../npm/tokenflow/), publish job in
`tokenflow.yml`. Blocked on the one manual publish that claims the name (see
[docs/RELEASE.md](../RELEASE.md)).

## Why

The tool path needs the .NET 10 SDK. TokenFlow's user proxies Claude Code or Codex traffic, so
they have Node and often no .NET SDK. `npx tokenflow` reaches them with no other prerequisite.

## Shape

npm package carries the app as framework-dependent DLLs. `postinstall` fetches the ASP.NET Core
**runtime** (not the SDK) into the package directory. `bin` spawns that runtime against the DLL.

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

The container run is what proves no .NET is needed: `dotnet` not on PATH, no dotnet directories,
host SDK not mounted. Not covered: macOS with no .NET installed anywhere (the Mac runs have an SDK
present, the no-.NET run is Linux). A `macos-14` matrix job closes it.

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

One left, manual, blocks the first release: npm cannot configure a trusted publisher for a package
that does not exist, so `tokenflow` needs one `npm login && npm publish` before OIDC can take over.
Steps and the field values: [docs/RELEASE.md](../RELEASE.md).

## Name: `tokenflow`

`token-flow` is taken on npm (checked 2026-08-08). `tokenflow` is free, and nothing had shipped
under `token-flow` yet: no `tokenflow-v*` tag, no `ConduitSharp.TokenFlow` on nuget.org, no
`ghcr.io/liqngliz/token-flow`. So every channel uses `tokenflow` and there is no second bin entry
to maintain.

| channel | name |
| :--- | :--- |
| npm package + bin | `tokenflow` |
| `dotnet tool` command | `tokenflow` (`ToolCommandName`) |
| image | `ghcr.io/liqngliz/tokenflow` |
| NuGet package id | `ConduitSharp.TokenFlow` (unchanged) |
| tag prefix, workflow file | `tokenflow-v*`, `tokenflow.yml` (unchanged) |

`TokenFlow` is the product name too, one word everywhere: `<title>`, `/info`, headings. The
verification above predates the rename and ran under the bin name `token-flow`; nothing about the
runtime bootstrap depends on it.

## First-run cost

46.6 MB over the wire, 114 MB on disk, ~1 minute. Later runs are instant.

**Fetch the runtime from `bin/tokenflow.js` on first run, not from `postinstall`.** npm hides
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

## Out of scope

Homebrew formula, Scoop manifest, systemd unit, launchd plist. Each = a separate channel with its
own review; `docker run` covers the always-on case.
