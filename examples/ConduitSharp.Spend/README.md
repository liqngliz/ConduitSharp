# Token Flow

Track token spend across any AI agent, ships pre-configured with Anthropic and Codex and Local LLM routes.  

One command to launch: Docker run pull image, mount volume, start server. Zero install.

## Run

One command, no clone:

```bash
docker run -d --restart unless-stopped --name token-flow \
  -p 5050:5050 -v "$PWD/logs:/data" \
  ghcr.io/liqngliz/token-flow
```

Serves the dashboard on `http://localhost:5050`. Spend rows and the wire log land in `./logs`.
`GET /info` prints the setup lines below.

On macOS and Windows a local model server on the host is reachable out of the box, on its default
loopback binding, with no extra flags.

```bash
# Follow the gateway's output. Startup errors, plugin registration and route
# validation failures all show up here.
docker logs -f token-flow

# Stop and delete it.
docker rm -f token-flow
```

**On Linux** Add the host mapping, which Docker Desktop provides
automatically and Linux does not:

```bash
--add-host=host.docker.internal:host-gateway
```

and bind your model server to `0.0.0.0` rather than `127.0.0.1`, because a loopback-only service
refuses connections from a container. Docker 20.10+ is required for `host-gateway`.

### Your own routes

Three routes ship in the image:

| route | for | forwards to |
| :--- | :--- | :--- |
| `/llm/claude` | Claude Code | `api.anthropic.com` |
| `/llm/codex` | Codex | `chatgpt.com` |
| `/llm/local` | LM Studio, Ollama, anything OpenAI-compatible | `host.docker.internal:1234` |

To run different ones, pull the shipped config out, edit it, and mount it back:

```bash
docker run --rm --entrypoint cat ghcr.io/liqngliz/token-flow \
  Configuration/routes.json > routes.json
```

Then add `-v "$PWD/routes.json:/app/Configuration/routes.json:ro"` to your `docker run`.

Inside a container `127.0.0.1` is the container, so a service on your machine is
`host.docker.internal`.

## Point each tool at it

### Claude Code

File: `~/.claude/settings.json`

```json
{
  "env": {
    "ANTHROPIC_BASE_URL": "http://localhost:5050/llm/claude"
  }
}
```

Restart Claude Code after editing. A `.claude/settings.json` inside a repo overrides the one in your
home directory, which is how you give a single project its own route.

Anthropic gzips its SSE stream, so `token-spend` decompresses before reading usage. Anthropic also
splits the columns across two frames, input and cache in `message_start` and output in
`message_delta`, which is why the plugin keeps the largest total per column rather than the last.

### Codex (VS Code extension)

File: `~/.codex/config.toml`

```toml
model_provider = "conduit"

[model_providers.conduit]
name     = "ConduitSharp"
base_url = "http://localhost:5050/llm/codex/backend-api/codex"
wire_api = "responses"
```

Restart the extension after editing.

**No `env_key`.** With it omitted, `resolve_provider_auth` falls through to whatever Codex is
already signed in with, so a ChatGPT-plan session works with no API key. With it set, Codex reads
that environment variable instead.

### LM Studio

```bash
OPENAI_BASE_URL=http://localhost:5050/llm/local/v1
```

The `local` route points at `host.docker.internal:1234`, LM Studio's default port on the host. For
Ollama or another local server, change that destination in your own `routes.json` (see above).

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

`session` is the client's own conversation id where it sends one, read from `sessionField`:
`metadata.user_id.session_id` for Claude Code, `client_metadata.thread_id` for Codex. A route without
it, like `local`, falls back to a hash of the conversation's first user message, which splits a chat
whenever the client compacts and merges chats that open with the same synthetic preamble.

**Wire log**, the actual bodies both ways, at `logs/conduit-wire.jsonl`. This is the one to read
when you want to know what a provider really sends rather than what its docs claim.

## Adding a project

One route per project, so each can carry its own budget, provider, and capture settings. Copy a
block in your `routes.json`, change `id`, the `path`, and the `PathRemovePrefix`, then point that
project's tool at the new prefix. Per-repo `.envrc` under direnv makes the base URL set itself when
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

**`codex` and `claude` have been exercised against live traffic.** `local` is configured but
unverified.

**The wire log mangles compressed responses.** `body-capture-file` stores bodies as text, so a gzip
response comes back with its invalid byte sequences replaced and cannot be decompressed. Anthropic
compresses, so `claude` response bodies are unreadable there. Requests are unaffected, as are `codex`
and `local` responses, which arrive uncompressed. Spend rows are unaffected either way: `token-spend`
buffers and decodes its own copy.

## Turning capture off

`body-capture-file` is here to inspect wire formats, not to run continuously. Once you have what you
need, drop its plugin block from each route in your `routes.json` and keep only `token-spend`.
