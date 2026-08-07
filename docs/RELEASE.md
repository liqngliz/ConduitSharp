# Releasing

Two independent pipelines. The library ships on `v*`, Token Flow ships on `tokenflow-v*`.

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

## Token Flow: image and native binaries

Workflow: [.github/workflows/tokenflow.yml](../.github/workflows/tokenflow.yml)

No version file to bump. Independent of the library version.

```bash
git tag tokenflow-vX.Y.Z
git push origin tokenflow-vX.Y.Z
```

| job | output |
| :--- | :--- |
| `dashboard` | gate only: vitest with coverage, then `npm run build`. A frontend type error stops the release |
| `image` | `ghcr.io/liqngliz/token-flow` at `:X.Y.Z`, `:X.Y`, `:latest`, linux/amd64 + linux/arm64 |
| `tokenflow-binaries` | `conduitsharp-spend-<tag>-<rid>.{tar.gz,zip}` on the GitHub Release, for win-x64, linux-x64, osx-x64, osx-arm64 |

`live.sh` resolves the newest `tokenflow-v*` release and downloads the archive matching the host
RID, so the asset name must keep the `<tag>-<rid>` shape.

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
