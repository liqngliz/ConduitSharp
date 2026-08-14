# Advanced configurations

## Other ways to install

The README covers `npx`. These are the rest.

```bash
# Docker, runs detached and restarts with the machine
docker run -d --restart unless-stopped --name tokenflow \
  -p 5050:5050 -v "$PWD/logs:/data" \
  ghcr.io/liqngliz/tokenflow

# .NET 10 SDK, puts `tokenflow` on PATH
dotnet tool install -g ConduitSharp.TokenFlow && tokenflow

# .NET 10 SDK, installs nothing, runs once
dnx ConduitSharp.TokenFlow
```

Docker writes to the mounted `./logs`. The other two write to `~/.conduit-spend/`, same as `npx`.

## Change location of logs

Spend rows and the wire log share one directory.

| | spend rows + wire log | override |
| :--- | :--- | :--- |
| npx / tool / dnx | `~/.conduit-spend/` | `CONDUIT_SPEND_DATA` |
| docker | `./logs` (the mount) | remount `/data` |

```bash
CONDUIT_SPEND_DATA=/path/to/logs npx @liqngliz/tokenflow

docker run -d -p 5050:5050 -v "/path/to/logs:/data" ghcr.io/liqngliz/tokenflow
```

Day files are never rewritten or deleted. Point this at a disk you are happy to grow.

## Change ports

Default is 5050. On the host it is `--urls`, on Docker it is the left half of `-p`.

```bash
# Node
npx @liqngliz/tokenflow --urls http://localhost:5020

# .NET, installed on PATH
tokenflow --urls http://localhost:5020

# .NET, no install
dnx ConduitSharp.TokenFlow --urls http://localhost:5020

# Docker, host 5020 to the container's 5050
docker run -d -p 5020:5050 -v "$PWD/logs:/data" ghcr.io/liqngliz/tokenflow
```

`ASPNETCORE_URLS` works in place of the flag. Update the base URL in each client config to match.

## Your own routes

Three routes shipped:

| route | for | forwards to | tested with |
| :--- | :--- | :--- | :--- |
| `/llm/claude` | Claude Code | `api.anthropic.com` | VS Code extension |
| `/llm/codex` | Codex | `chatgpt.com` | VS Code extension |
| `/llm/local` | LM Studio, Ollama, anything OpenAI-compatible | `127.0.0.1:1234` | Cline + LM Studio |

The gateway reads `Configuration/routes.json` from its install directory. Edit that file, or copy it
somewhere of your own and set `Gateway__RoutesPath` to the absolute path:

```bash
# Node
Gateway__RoutesPath="$PWD/routes.json" npx @liqngliz/tokenflow

# .NET, installed on PATH
Gateway__RoutesPath="$PWD/routes.json" tokenflow

# .NET, no install
Gateway__RoutesPath="$PWD/routes.json" dnx ConduitSharp.TokenFlow
```

| install | shipped file | edit in place |
| :--- | :--- | :--- |
| `dotnet tool` | `~/.dotnet/tools/.store/conduitsharp.tokenflow/<ver>/conduitsharp.tokenflow/<ver>/tools/net10.0/any/Configuration/` | holds until you update the tool |
| `npx` | `~/.npm/_npx/<hash>/node_modules/@liqngliz/tokenflow/app/Configuration/` | no, the hash changes when npx re-resolves |
| `dnx` | temporary | no, discarded after the run |
| image | mounted, see below | n/a |

## Known limits

**Codex polls `/models` every three minutes** with an empty GET, so most rows in the spend file
have `turn: 0` and no tokens. Filter on `turn > 0` for real traffic.

**The shipped `codex` route is for a ChatGPT subscription.** It points at the ChatGPT backend.
`api.openai.com` needs its own route: the OAuth token a subscription issues does not carry the
scopes that endpoint requires.

**Two capture buffers per request.** Both plugins declare their footprint, so the gateway reserves
`token-spend` + `body-capture-file` against `MaxRamBufferedBodyBytes` and sheds with a 503 at the
ceiling rather than growing unchecked. This example raises that budget from the 64 MiB default to
512 MiB to leave room.

**A compressed route needs `decompress: true`.** `body-capture-file` writes bodies as text, so
without it a gzip response lands with its invalid byte sequences replaced and can never be
decompressed again: the bytes are gone, not merely encoded. The shipped `claude` route sets it
([routes.json:76](Configuration/routes.json#L76)), `codex` and `local` arrive uncompressed. Set it on
any route you add whose upstream honours `Accept-Encoding`. Spend rows are unaffected either way,
`token-spend` buffers and decodes its own copy.


## Docker only

Does not apply to `npx`, the tool, or `dnx`.

```bash
# Gateway output. Startup errors, plugin registration and route validation failures land here.
docker logs -f tokenflow

# Stop and delete.
docker rm -f tokenflow
```

### Your own routes, mounted

```bash
docker run --rm --entrypoint cat ghcr.io/liqngliz/tokenflow \
  Configuration/routes.json > routes.json
```

Then add `-v "$PWD/routes.json:/app/Configuration/routes.json:ro"` to your `docker run`.

Take the file out of the image, not off GitHub. Inside a container `127.0.0.1` is the container, so
the build rewrites the local-model address to `host.docker.internal:1234` ([Dockerfile:43](Dockerfile#L43)).
The repo copy is host-shaped and its `local` route 502s once mounted.

### Reaching a model server on your machine

Works out of the box on macOS and Windows, on the server's default loopback binding.

**On Linux** add the host mapping Docker Desktop provides and Linux does not, and bind your model
server to `0.0.0.0` rather than `127.0.0.1`, because a loopback-only service refuses connections
from a container. `host-gateway` needs Docker 20.10+.

```bash
--add-host=host.docker.internal:host-gateway
```
