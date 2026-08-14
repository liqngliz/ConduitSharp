# Prompt detail panel

Replace scroll-jump navigation with a right-side overlay. Click a prompt anywhere, panel slides in over the right 50%, page underneath keeps its scroll position.

Table and `/api/wire/{traceId}` already ship. This is a swap of the navigation mechanism plus one new component.

## Why

Jump scrolls away from the flowchart and there is no way back up. Selection changes the reading position instead of adding to it.

## Delete

| file | lines | what |
| :--- | :--- | :--- |
| `PromptsTable.tsx` | 186-214 | focus effect: `find`, `setTimeout`, `scrollIntoView`, `bg-white/20` flash |
| `PromptsTable.tsx` | 171-175 | `focusedPrompt` + `onPromptClear` props |
| `PromptsTable.tsx` | 296 | `id={\`prompt-row-${row.key}\`}` (nothing looks it up after this) |
| `Dashboard.tsx` | 117, 123 | `focusedPrompt` state, `clearPromptFocus` |
| `Dashboard.tsx` | 375 | `focusedPrompt` / `onPromptClear` on `<PromptsTable>` |

`PromptsTable` keeps its own inline expand. Two ways to see the same wire log is one too many: the row click opens the panel, the inline `expandedKey` / `activeTrace` / `<WireViewer>` block goes with it. `WireViewer` itself moves into the panel unchanged.

## Handler signature stays

Every caller already emits `(sessionId, traceId?)`:

| caller | line |
| :--- | :--- |
| `ActiveFlow` -> `Flowchart.onPromptClick` | `ActiveFlow.tsx:77` |
| `SessionsTable` -> `SessionFlowchart.onPromptClick` | `SessionsTable.tsx:173` |
| `Insights` top-prompt row | `Insights.tsx:175` |
| `SessionFlowchart` node hitbox | `SessionFlowchart.tsx:420` |
| `PromptsTable` row | new |

So `handlePromptSelect` in `Dashboard.tsx:119` is reused as-is. Rename the state `selectedPrompt`, drop the auto-clear.

## Resolve

Panel receives `{sessionId, traceId}` and resolves the row itself. Export from `PromptsTable.tsx` (or move both to `utils/`):

```
findPromptByTrace(sessions, traceId)   -> FlatPromptRow | undefined   // traces.includes(traceId)
findFirstPrompt(sessions, sessionId)   -> FlatPromptRow | undefined   // traceId absent (pre-trace rows)
```

`flattenPrompts` already exists at `PromptsTable.tsx:36` and is the right input.

## Panel

| aspect | value |
| :--- | :--- |
| position | `fixed inset-y-0 right-0 z-50 w-full md:w-1/2` |
| open/close | `translate-x-full` -> `translate-x-0`, `transition-transform duration-300 ease-out` |
| surface | `bg-surface/95 backdrop-blur-xl border-l border-white/10` (`surface` = `#1A1D2D`, `tailwind.config.js:11`) |
| scroll | `overflow-y-auto` on the panel, page behind untouched |
| close | X button + `Escape` keydown |
| backdrop | none |
| mount | always rendered, `translate-x-full` when `selected == null`. Avoids a mount/unmount flash on every selection swap |

No backdrop and no body scroll-lock is the point of the feature: the left half stays live, so clicking a different node in the flowchart swaps the panel content without ever closing it. A dimmed overlay would rebuild the same trap the jump created.

## Panel content, top to bottom

| section | source |
| :--- | :--- |
| header | close button, `sessionName`, `model`, `new Date(ts).toLocaleString()` |
| prompt text | see below, `<pre className="whitespace-pre-wrap break-words max-h-64 overflow-auto">` |
| metrics grid | `in` / `cacheWrite` / `cacheRead` / `think` / `out` / `total`, same colors as the table (`text-blue-500` / `text-pink-500` / `text-purple-500` / `text-emerald-400` / `text-amber-500` / `text-secondary`) |
| trace chips | `traces[]`, `{i+1}. {t.slice(0,8)}`, selected sets `activeTrace`. Render only when `traces.length > 1` |
| wire log | `<WireViewer traceId={activeTrace} />`, lifted verbatim from `PromptsTable.tsx:85-169` |

## Full prompt text

`row.prompt` is a **200-character prefix**, not the prompt. `maxPromptChars: 200` on all three routes (`Configuration/routes.json:49,126,201`). The plugin truncates before the spend row is written.

Full text lives in the wire log request body:

```
entries.find(e => e.direction === 'request')
  -> JSON.parse(body).messages
  -> last element with role === 'user'
  -> content is string | [{type:'text', text}, ...]   // both shapes ship
```

Render that when it parses, fall back to `row.prompt` + a "prefix only" note when it does not. The parse belongs next to `formatWireBody` (`PromptsTable.tsx:77`) as a second exported pure function, so it gets a test without a render harness.

This is the whole reason the panel exists. Do not ship it reading `row.prompt`.

## Revive the highlight

`highlightTraceId` is already plumbed through `SessionFlowchart` (`:35`, `:37`, `:348`, `:435`, `:458`) and currently wired to nothing. Pass `selectedPrompt?.traceId` down from `Dashboard` through `ActiveFlow` and `SessionsTable`. The node that owns the open panel pulses cyan, so the flowchart shows where you are while the panel is open.

Drop the node `id=` attributes at `SessionFlowchart.tsx:353`. `getElementById` was their only consumer.

## Edge cases

| case | behavior |
| :--- | :--- |
| `traces.length === 0` (pre-trace rows) | panel opens, metrics + prompt prefix render, wire section shows "no trace recorded". Do not make the row unclickable |
| trace resolves to no row | panel does not open. Should not happen once the Insights fold bug below is fixed |
| selection swapped while a fetch is in flight | `WireViewer`'s `cancelled` flag at `:91` already covers it, keyed on `traceId` |
| `Escape` with panel closed | no-op, listener guards on `selected != null` |
| narrow viewport | `w-full` under `md`, panel covers the page. Acceptable, close returns you to the same scroll position |

## Still open, separate fix

Insights hands over a trace that points at the wrong row. `topPrompts` folds by prompt text globally (`parser.ts:205`); `sessions[*].prompts` folds only consecutive runs within a session (`parser.ts:291`). Measured on 601 real rows: the top Insights entry reads 3,543,269 tokens, the row its trace resolves to reads 48,283.

The panel makes this more visible, not less: the header numbers will visibly disagree with the list the user clicked from. Fix by building `topPrompts` from the folded `sess.prompts` and deleting `promptsMap` (`parser.ts:87`, `205-229`, `331`). Changes the Insights ranking, so it is a decision, not a cleanup.

## Tests

Pure functions only, no render harness (nothing else in the dashboard has one for a table).

- `findPromptByTrace` -> hit via `traces[]` not just `trace`, miss returns undefined
- `extractUserPrompt(body)` -> string content, array-of-blocks content, malformed JSON, no user message
- `formatWireBody` -> unchanged, already covered

## Security

Unchanged: the wire log holds prompt text in the clear and `/api/wire/{traceId}` serves it with no auth. Local gateway only.
