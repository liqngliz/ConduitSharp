# ConduitSharp.Spend

A gateway wired for measuring what your coding agents cost. Three routes, one per provider, each
running `token-spend` (durable per-request token history) and `body-capture-file` (the raw request
and response bodies, for checking what a provider actually sends).

Point a tool's base URL at a route and its traffic is metered without the tool knowing.

## Run

```bash
dotnet run --project examples/ConduitSharp.Spend
```

Listens on `http://localhost:4000`. `GET /` prints the setup lines below with the live paths.

Two environment variables, both optional:

| variable | default |
| :--- | :--- |
| `CONDUIT_SPEND_DATA` | `~/.conduit-spend` |
| `ASPNETCORE_URLS` | `http://localhost:5000`, so set it to `:4000` to match the docs |

## Or in Docker

```bash
cd examples/ConduitSharp.Spend
docker compose up -d
```

Serves the same thing on `:4000`, with spend rows and the wire log both landing in
`examples/ConduitSharp.Spend/logs/` on the host. To run the image directly instead:

```bash
docker build -f examples/ConduitSharp.Spend/Dockerfile -t conduit-spend .
docker run -d --restart unless-stopped -p 4000:4000 \
  -v "$PWD/logs:/data" --add-host host.docker.internal:host-gateway conduit-spend
```

The container uses `Configuration/routes.docker.json`, which differs from the local one in two
ways it has to: the `local` route points at `host.docker.internal:1234` because `127.0.0.1` inside
a container is the container itself, and the wire log writes to `/data` so both outputs share the
one mounted volume. The `--add-host` line is what makes that hostname resolve on Linux, where
Docker does not provide it by default.

Build context is the repo root, since the project references `src/` and `plugins/` directly.

## Point each tool at it

**Claude Code**

```bash
ANTHROPIC_BASE_URL=http://localhost:4000/llm/claude claude
```

**Codex** in `~/.codex/config.toml`. A custom provider block, not an override of the built-in
`openai` one, which Codex ignores ([openai/codex#11698](https://github.com/openai/codex/issues/11698)):

```toml
model          = "gpt-5.3-codex"
model_provider = "conduit"

[model_providers.conduit]
name     = "ConduitSharp"
base_url = "http://localhost:4000/llm/codex"
env_key  = "OPENAI_API_KEY"
wire_api = "responses"
```

**LM Studio** or anything else speaking the OpenAI wire format:

```bash
OPENAI_BASE_URL=http://localhost:4000/llm/local/v1
```

The `local` route points at `127.0.0.1:1234`, LM Studio's default. Change the destination in
`Configuration/routes.json` for Ollama or another local server.

## What you get

**Spend rows**, one JSON object per request, one file per UTC day:

```bash
cat ~/.conduit-spend/spend-$(date -u +%F).jsonl
```

```json
{"ts":"2026-08-01T21:31:41Z","route":"local","model":"qwen-2.5","caller":"6200a49014e7…",
 "in":40,"out":110,"cacheWrite":0,"cacheRead":0,"session":"9916b6fc15f66f36",
 "turn":3,"tools":0,"ms":24,"streamed":false,"prompt":"second ask"}
```

**Wire log**, the actual bodies both ways, at `/tmp/conduit-wire.jsonl`. This is the one to read
when you want to know what a provider really sends rather than what its docs claim.

## Adding a project

One route per project, so each can carry its own budget, provider, and capture settings. Copy a
block in `routes.json`, change `id`, the `path`, and the `PathRemovePrefix`, then point that
project's tool at the new prefix. Per-repo `.envrc` under direnv makes the base URL set itself when
you `cd` in.

## Known limits

**Streaming is recorded but not counted.** An SSE response is not one JSON document, so the row
lands with `"streamed": true` and zero tokens. **Claude Code streams by default**, so on the
`claude` route expect real `turn`, `model`, `session` and `ms` with zero token counts until SSE
parsing is added. The `local` and `codex` routes measure fully when the client does not stream.

**Codex loses conversation shape.** The Responses API sends `input` where Chat Completions sends
`messages`, and the request parser only looks for `messages`. Token counts, model and timing are
correct; `turn`, `session`, `tools` and `prompt` come back empty. Verified by test, fix is small.

**The wire log holds prompt text in the clear.** It captures bodies, so it contains whatever you
typed. It does not capture headers, so API keys stay out of it, but treat the file as sensitive and
delete it when you are done. `token-spend` itself stores only a salted hash of the caller and a
bounded prompt prefix, and only because `capturePrompts` is on in this example.

**Two capture buffers per request.** Both plugins declare their footprint, so the gateway reserves
`token-spend` + `body-capture-file` against `MaxRamBufferedBodyBytes` and sheds with a 503 at the
ceiling rather than growing unchecked. This example raises that budget to 128 MiB to leave room.

## Turning capture off

`body-capture-file` is here to inspect wire formats, not to run continuously. Once you have what you
need, drop its plugin block from each route in `routes.json` and keep only `token-spend`.
