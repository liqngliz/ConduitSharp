# ConduitSharp.Plugin.BodyCaptureToFile

Captures a bounded prefix of request bodies, response bodies, or both, to a rolling JSONL file. A
bounded channel plus a background writer keeps the write off the request path.

Plugin variant: `body-capture-file`.

## Why it is different from `body-capture`

[`body-capture`](../ConduitSharp.Plugin.BodyCapture) emits through `ILogger`, so bodies land wherever
your logging pipeline points (Loki, console, OTLP). This one writes a file you can `grep`, `jq`, and
tail with no collector running.

## Record

One JSON object per line, one line per captured direction:

```json
{"time":"2026-08-14T09:12:33.4180000Z","path":"/v1/messages","traceId":"4bf92f...","direction":"request","body":"{\"model\":\"claude-opus-5\",...}"}
```

`traceId` = `Activity.Current?.TraceId ?? HttpContext.TraceIdentifier`. Same expression
[`token-spend`](../ConduitSharp.Plugin.TokenSpend) uses, fallback included, so a wire body joins to
its spend row on that field whether or not a tracer is listening. Truncated bodies carry a
`... (truncated)` suffix inside `body`.

## Config

```json
{
  "name": "custom", "variant": "body-capture-file", "order": 9,
  "config": {
    "request":  { "maxSize": 4096 },
    "response": { "maxSize": 8192 },
    "logPath": "%CONDUIT_SPEND_DATA%/conduit-wire.jsonl",
    "maxFileBytes": 134217728,
    "decompress": true
  }
}
```

| Setting | Required | Default | Meaning |
| :--- | :--- | :--- | :--- |
| `request.maxSize` | no | — | bytes of request body to capture; direction is off unless the block is present |
| `response.maxSize` | no | — | same, for the response |
| `logPath` | no | `/tmp/conduit-logs.json` | sink path; `%VAR%` expands on every platform |
| `maxFileBytes` | no | `134217728` (128 MiB) | roll threshold |
| `decompress` | no | `false` | gunzip/inflate before writing |

A direction block present without `maxSize` captures 4096 bytes. The removed flat shape
(`{"maxSize": N}`) throws at config validation.

An unset `%VAR%` is left literal rather than expanded to empty, so it fails loudly on the first
write instead of silently writing to a wrong path.

## decompress

Set it for any upstream that honours `Accept-Encoding`. Anthropic does. Without it a gzip response
is written through a UTF-8 conversion that replaces invalid sequences, and the result cannot be
decompressed afterwards: the bytes are gone, not merely encoded.

## Rollover

At `maxFileBytes` the sink moves to `<logPath>.1`, deleting any previous `.1`. One backup
generation, not a numbered series. A failed roll logs and the file keeps growing.

## Backpressure and failure

| | |
| :--- | :--- |
| queue | bounded channel, capacity `OTEL_BLRP_MAX_QUEUE_SIZE` (default `2048`), `FullMode=DropWrite` |
| under flood | entries drop, requests are never blocked or failed |
| retained RAM | capacity x the route's `maxSize` |
| writer crash | logs `request bodies are NO LONGER being captured`, gateway keeps serving |

## Sensitivity

**Bodies are stored in the clear.** The file contains whatever the caller sent and whatever the
model returned, including prompt text. Headers are not captured, so API keys stay out of it. Treat
the file as sensitive and delete it when you are done.

## License

Apache-2.0
