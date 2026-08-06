# Token Flow dashboard

The React front end for [Token Flow](../README.md). Reads spend rows the gateway writes and
renders them live.

Build output goes to `../wwwroot`, which the gateway serves with `app.UseStaticFiles()`.
`wwwroot` is gitignored; the Docker image builds it in a node stage.

## Develop

The dashboard needs the gateway running for data. Start it first:

```bash
cd .. && dotnet run          # http://localhost:4000
```

Then:

```bash
npm install
npm run dev                  # http://localhost:5173
```

`vite.config.ts` proxies `/api` to `localhost:4000`, so the fetch and the SSE stream both reach
the gateway.

## Scripts

| script | does |
| :--- | :--- |
| `npm run dev` | Vite dev server on 5173, `/api` proxied to 4000 |
| `npm run build` | `tsc -b` then `vite build` into `../wwwroot` |
| `npm run build:profile` | Same, aliased to `react-dom/profiling` so React DevTools can record renders |
| `npm test` | vitest, 7 files |
| `npm run lint` | oxlint |

**Node 22.12 or newer.** oxlint's native bindings declare `engines: ^20.19.0 || >=22.12.0`, and
npm silently skips an optional dependency whose engines don't match, leaving `npm run lint` to
die on "Cannot find native binding".

## Data it reads

| endpoint | returns |
| :--- | :--- |
| `GET /api/spend` | dates that have rows |
| `GET /api/spend/{date}` | that day's rows |
| `GET /api/spend/stream` | SSE, one event per row as it lands |

A row is one turn: timestamp, session id, turn number, model, route, the four token classes
(input, output, cache-write, cache-read), tool count, and the prompt's first line.
`ConduitSharp.Plugin.TokenSpend` writes them.

## Layout

`Dashboard.tsx` owns every piece of state: the row list, the SSE subscription, the date range,
the route filter, and the focused session. Everything below it is presentational and memoized.

| file | renders |
| :--- | :--- |
| `Metrics.tsx` | totals per token class, and which class dominates |
| `ActiveFlow.tsx` + `SessionFlowchart.tsx` | Sankey of the live session, per-turn |
| `Insights.tsx` | most expensive prompt sequences, per-session ranking |
| `Charts.tsx` | Tokens per Day, Tokens by Model |
| `SessionsTable.tsx` | every session, sortable, click-to-focus |
| `WeightsControl.tsx` | per-model token weights, toggleable |
| `AnimatedNumber.tsx` | counts a value up by mutating `textContent` from a rAF loop |
| `utils/parser.ts` | `computeMetrics` and `computeInsights`, all the arithmetic |

Weighting is **normalized token weighting, not dollars.** Cache reads cost less than fresh input,
output costs more, and the weights make those comparable. No price is claimed.

`AnimatedNumber` writes the DOM node directly instead of calling `setState` per frame. At 60fps
with an SSE feed, per-frame state would re-render the tree ~60 times a second. Incoming SSE rows
are batched on a 250ms trailing timer for the same reason.
