# ConduitSharp Dashboard Implementation Plan

## Objective
Build a rich, interactive React + Tailwind single-page application (SPA) to parse and visualize `ConduitSharp.Spend` JSONL logs. The UI will be written in TypeScript, thoroughly tested with coverage, and served statically via Kestrel from the `wwwroot` directory.

## Architecture

1. **Location**: `examples/ConduitSharp.Spend/dashboard`
2. **Tech Stack**: 
   - React 18, TypeScript, Vite
   - TailwindCSS (Premium dark mode, glassmorphism, gradients, micro-animations)
   - Vitest + React Testing Library (for unit tests and coverage)
3. **Data Ingestion**: 
   - The React app will use `fetch()` to read JSONL logs from a folder hosted by Kestrel (e.g., `/logs/spend-YYYY-MM-DD.jsonl`).
4. **Separation of Concerns (Routes)**:
   - All metrics and insights will be separated by the API route used (`claude`, `codex`, `local`). Users will be able to toggle or view side-by-side comparisons of these routes.

## Proposed Components and Metrics

The application will be broken down into highly testable, isolated TSX components:

### `Dashboard.tsx`
- **Responsibility**: Main container. Fetches the JSONL file, parses it line-by-line, and passes the parsed data down to the metrics and insights components. Handles the route toggles (claude/codex/local).

### `Metrics.tsx`
Given a filtered dataset for a specific route, calculates and displays:
- **Totals**: Sum `in`, `out`, `cacheWrite`, `cacheRead`, `ms`.
- **Sessions**: Grouped by `session` ID (using the readable `sessionName` as the title).
- **Daily Usage**: Grouped by timestamp `ts`.
- **Model Breakdown**: Grouped by `model`.
- **Top Prompts**: Grouped by `sessionName` (or `prompt`), sorted by total tokens.

### `Insights.tsx`
Calculates and displays advanced analytical insights:
- **Vague prompts**: Prompt length < 30 chars but high input tokens.
- **Context growth**: Track input tokens across turns within the same session.
- **Marathon sessions**: Identify sessions with unusually high max turn counts.
- **Input heavy**: Sessions/requests with high input tokens compared to output tokens.
- **Day pattern**: Group requests by day of the week to show usage trends.
- **Model mismatch**: Very expensive models used for single-turn or low-turn sessions.
- **Tool heavy**: High tool usage count relative to turn count.
- **Route dominance**: Flag if a single route consumes > 60% of total tokens.
- **Conversation efficiency**: Token cost per turn in short vs long sessions.
- **% System Prompt**: Ratio of system/context tokens to actual user tokens.

## Testing Strategy

- **Framework**: Vitest with `@testing-library/react`.
- **Coverage**: Use `@vitest/coverage-v8` to generate coverage reports on test runs.
- **Approach**: 
  - Each component (`Metrics`, `Insights`, parsers) will have a corresponding `.test.tsx` file.
  - Test inputs (mock parsed JSONL records) and outputs (rendered DOM elements or calculated values) will be strictly asserted.
  - The CI/local test script will be `npm run test -- --coverage` to ensure visibility of gaps.

## Execution Steps

1. Create the `dashboard` directory and initialize `vite` (react-ts template).
2. Install Tailwind CSS and Vitest dependencies.
3. Configure Vitest for coverage reporting.
4. Build the JSONL parser and test it.
5. Build the UI components, style them, and write their tests.
6. Configure the `ConduitSharp.Spend` .NET app to serve `wwwroot` and expose the `/logs` directory (if not already done).
7. Build the React app into `wwwroot`.

## User Review Required
> [!IMPORTANT]
> Please review this revised plan. If approved, we will begin scaffolding the Vite app and setting up the test environment.
