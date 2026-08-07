# Releasing

Two independent pipelines. The library ships on `v*`, TokenFlow ships on `tokenflow-v*`.

## Library: NuGet packages, binaries, gateway image

Workflow: [.github/workflows/release.yml](../.github/workflows/release.yml)

1. Bump `<Version>` in `Directory.Build.props`. The tag must match it or `verify-version` fails.

2. Commit and tag:

```bash
git commit -am "chore: release vX.Y.Z"
git tag vX.Y.Z
git push origin main --tags
```

Publishes `ghcr.io/liqngliz/conduitsharp`, the win-x64 and linux-x64 archives on a GitHub Release,
and every `ConduitSharp.*` package to nuget.org.

## TokenFlow: tool, image, native binaries

Workflow: [.github/workflows/tokenflow.yml](../.github/workflows/tokenflow.yml)

No version file to bump. Independent of the library version.

```bash
git tag tokenflow-vX.Y.Z
git push origin tokenflow-vX.Y.Z
```

| job | output |
| :--- | :--- |
| `dashboard` | gate only: vitest with coverage, then `npm run build`. A frontend type error stops the release |
| `tool` | `ConduitSharp.TokenFlow` on nuget.org, version = tag minus `tokenflow-v`. `dotnet tool install -g` / `dnx` |
| `image` | `ghcr.io/liqngliz/tokenflow` at `:X.Y.Z`, `:X.Y`, `:latest`, linux/amd64 + linux/arm64 |
| `npm` | `tokenflow` on npmjs.com, version = tag minus `tokenflow-v`. `npx tokenflow` |
| `tokenflow-binaries` | `conduitsharp-spend-<tag>-<rid>.{tar.gz,zip}` on the GitHub Release, for win-x64, linux-x64, osx-x64, osx-arm64 |

**Before the first `tool` release:** nuget.org Trusted Publishing is per-package AND per-workflow.
Add a policy for `ConduitSharp.TokenFlow` / `liqngliz/ConduitSharp` / `tokenflow.yml`. The
`release.yml` policy does not cover it, and the push fails without one.

**Before the first `npm` release:** npm has no way to configure a trusted publisher for a package
that does not exist, so `tokenflow` needs one manual publish first.

```bash
cd examples/ConduitSharp.Spend/dashboard && npm ci && npm run build && cd -
dotnet publish examples/ConduitSharp.Spend -c Release -p:UseAppHost=false -o npm/tokenflow/app
cd npm/tokenflow && npm login && npm publish --access public
```

Then npmjs.com → Packages → tokenflow → Settings → Trusted publishing → GitHub Actions:

| field | value |
| :--- | :--- |
| organization or user | `liqngliz` |
| repository | `ConduitSharp` |
| workflow filename | `tokenflow.yml` (filename only, `.yml` required) |
| environment | blank |
| allowed actions | `npm publish` |

Case-sensitive, unvalidated on save. A mismatch surfaces at publish time as a 404, not a config
error ([npm/cli#9088](https://github.com/npm/cli/issues/9088)). `package.json` `repository.url` must
match the GitHub repo exactly. Requires npm CLI >= 11.5.1, which the job installs because runners
ship 10.x.

Optional after that: package **Settings → Publishing access → require 2FA and disallow tokens**,
which kills the bootstrap credential and leaves OIDC working.

**`osx-arm64` is unsigned and does not run on Apple Silicon.** No signing step exists in the job.
Fix and caveats: [docs/planning/token-flow-npx.md](planning/token-flow-npx.md).

## Re-run a workflow without a new tag

```bash
gh workflow run tokenflow.yml --ref main
```

Produces `:latest` only, no version tags. The binaries job is tag-gated and skips.

## Watch a run

```bash
gh run watch
gh run view --log-failed
```

## Delete a bad tag

```bash
git tag -d tokenflow-vX.Y.Z
git push origin :refs/tags/tokenflow-vX.Y.Z
```

A pushed NuGet package cannot be replaced. Bump the patch version and release again.
