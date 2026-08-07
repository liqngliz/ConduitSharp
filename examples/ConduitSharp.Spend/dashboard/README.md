# Token Flow dashboard

React UI for [Token Flow](../README.md). Reads/renders gateway spend rows live. Build goes to `../wwwroot` (gitignored, Docker builds it).

## Run

Gateway first:
```bash
cd .. && dotnet run          # localhost:5050
```
Dashboard:
```bash
npm install && npm run dev   # localhost:5173
```
`vite.config.ts` proxies `/api` to 5050.

## Scripts

| script | does |
|---|---|
| `npm run dev` | dev server, `/api` proxy |
| `npm run build` | tsc -> vite -> `../wwwroot` |
| `npm run build:profile` | build with React DevTools profiling |
| `npm test` | vitest |
| `npm run lint` | oxlint (requires Node >= 22.12 or silent install fail) |

## API

| endpoint | returns |
|---|---|
| `GET /api/spend` | dates with rows |
| `GET /api/spend/{date}` | rows for date |
| `GET /api/spend/stream` | SSE row stream |

Row = 1 turn. TS, session, turn, model, route, input/out/CW/CR tokens, tool count, prompt snippet. Written by `ConduitSharp.Plugin.TokenSpend`.

## Code

`Dashboard.tsx` owns all state, SSE, filters. Everything else presentational + memoized.

- `Metrics.tsx`: Totals, dominant class
- `ActiveFlow.tsx` / `SessionFlowchart.tsx`: Live Sankey
- `Insights.tsx`: Expensive/vague prompt ranking
- `Charts.tsx`: Time/Model charts
- `SessionsTable.tsx`: Sortable list
- `WeightsControl.tsx`: Token normalization (not dollars)
- `AnimatedNumber.tsx`: rAF direct DOM mutate (skips React)
- `utils/parser.ts`: Arithmetic

**Perf notes**: SSE coalesced at 250ms. `AnimatedNumber` mutates DOM directly. Components `React.memo`'d. Zero state thrash on 60fps stream.
