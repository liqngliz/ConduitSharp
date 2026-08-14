# Real-Time Token Spend Streaming: Frontend Plan

**Assigned Agent / Model**: Gemini  
**Goal**: Consume the `GET /api/spend/stream` Server-Sent Events (SSE) stream in `Dashboard.tsx`, appending new incoming `SpendRecord` entries into React state to update metrics, tables, and flowcharts in real-time.

---

## Technical Design

1. **Initial State vs. Stream Connection**:
   - Keep existing `fetchData()` logic to populate initial historical records for the selected date (`/api/spend/{date}`).
   - Immediately after initial load, establish an `EventSource` connection to `/api/spend/stream`.

2. **Incoming Event Handling**:
   - On `eventSource.onmessage`:
     - Parse JSON string `event.data` into `SpendRecord`.
     - Update React state: `setRecords(prev => [...prev, newRecord])`.
   - Because metrics (`computeMetrics`) and insights (`computeInsights`) are derived via `useMemo` from `records`, the dashboard components will automatically and efficiently update.

3. **Lifecycle & Cleanup**:
   - Close the `EventSource` on component unmount or when changing selected dates (`eventSource.close()`).
   - Handle connection errors (`eventSource.onerror`) gracefully without crashing the UI.

---

## File Changes Overview

### 1. `examples/ConduitSharp.Spend/dashboard/src/components/Dashboard.tsx`
- Add `EventSource` integration in `useEffect`.
- Append incoming streamed records to `records` state.
- Ensure proper teardown on unmount or date switch.

---

## Verification Plan

1. **Automated Unit Tests**:
   - Run `npx vitest run` in `examples/ConduitSharp.Spend/dashboard` to verify parser & component tests pass.
2. **Build Verification**:
   - Run `npm run build` in `examples/ConduitSharp.Spend/dashboard`.
3. **Manual E2E Verification**:
   - Open dashboard in browser.
   - Send live proxy request or append new `SpendRecord` to JSONL file.
   - Confirm metrics, top prompts, and session flowchart update reactively in real-time.
