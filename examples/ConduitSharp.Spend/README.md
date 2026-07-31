# ConduitSharp.Spend

A gateway wired for measuring what your coding agents cost. Three routes, one per provider, each
running `token-spend` (durable per-request token history) and `body-capture-file` (the raw request
and response bodies, for checking what a provider actually sends).

Point a tool's base URL at a route and its traffic is metered without the tool knowing.
## Run

One command, no clone:

```bash
docker run -d --restart unless-stopped --name conduit-spend \
  -p 4000:4000 \
  -v "$PWD/logs:/data" \
  --add-host host.docker.internal:host-gateway \
  ghcr.io/liqngliz/conduit-spend
```

Serves on `http://localhost:4000`. Spend rows and the wire log land in `./logs`. `GET /` prints
the setup lines below.

```bash
docker logs -f conduit-spend
docker rm -f conduit-spend
```

`--add-host` is only needed on Linux, where Docker does not provide `host.docker.internal`, and
only matters for the `local` route reaching a model server on the host.

### Your own routes

Three routes ship in the image. To run different ones, mount a file over the baked-in config:

```bash
-v "$PWD/routes.json:/app/Configuration/routes.json:ro"
```

Start from `Configuration/routes.docker.json` in this repo. Inside a container `127.0.0.1` is the
container, so a service on your machine is `host.docker.internal`.

## Point each tool at it

### Claude Code

```bash
ANTHROPIC_BASE_URL=http://localhost:4000/llm/claude claude
```

### Codex (VS Code extension)

File: `~/.codex/config.toml`

```toml
model_provider = "conduit"

[model_providers.conduit]
name     = "ConduitSharp"
base_url = "http://localhost:4000/llm/codex/backend-api/codex"
wire_api = "responses"
```

Restart the extension after editing.

**No `env_key`.** With it omitted, `resolve_provider_auth` falls through to whatever Codex is
already signed in with, so a ChatGPT-plan session works with no API key. With it set, Codex reads
that environment variable instead.

### LM Studio

```bash
OPENAI_BASE_URL=http://localhost:4000/llm/local/v1
```

The `local` route points at `host.docker.internal:1234`, LM Studio's default port on the host.
Change the destination in `Configuration/routes.docker.json` for Ollama or another local server.

## What you get

**Spend rows**, one JSON object per request, one file per UTC day:

```bash
cat logs/spend-$(date -u +%F).jsonl
```

```json
{"ts":"2026-08-01T21:31:41Z","route":"local","model":"qwen-2.5","caller":"6200a49014e7…",
 "in":40,"out":110,"cacheWrite":0,"cacheRead":0,"session":"9916b6fc15f66f36",
 "turn":3,"tools":0,"ms":24,"streamed":false,"prompt":"second ask"}
```

**Wire log**, the actual bodies both ways, at `logs/conduit-wire.jsonl`. This is the one to read
when you want to know what a provider really sends rather than what its docs claim.

## Adding a project

One route per project, so each can carry its own budget, provider, and capture settings. Copy a
block in `Configuration/routes.docker.json`, change `id`, the `path`, and the
`PathRemovePrefix`, then point that project's tool at the new prefix. Per-repo `.envrc` under direnv makes the base URL set itself when
you `cd` in.

## Known limits

**Codex polls `/models` every three minutes** with an empty GET, so most rows in the spend file
have `turn: 0` and no tokens. Filter on `turn > 0` for real traffic.

**`api.openai.com` is not usable on a ChatGPT plan.** It authenticates the OAuth token, then refuses
with `Missing scopes: api.responses.write`. The route here points at the ChatGPT backend instead,
which is what a plan entitles you to. An API-key user would want a second route at
`https://api.openai.com` with `/v1` in the base URL.

**The wire log holds prompt text in the clear.** It captures bodies, so it contains whatever you
typed. It does not capture headers, so API keys stay out of it, but treat the file as sensitive and
delete it when you are done. `token-spend` itself stores only a salted hash of the caller and a
bounded prompt prefix, and only because `capturePrompts` is on in this example.

**Two capture buffers per request.** Both plugins declare their footprint, so the gateway reserves
`token-spend` + `body-capture-file` against `MaxRamBufferedBodyBytes` and sheds with a 503 at the
ceiling rather than growing unchecked. This example raises that budget to 128 MiB to leave room.

**Only `codex` has been exercised against live traffic.** `claude` and `local` are configured but
unverified.

## Turning capture off

`body-capture-file` is here to inspect wire formats, not to run continuously. Once you have what you
need, drop its plugin block from each route in `routes.json` and keep only `token-spend`.
