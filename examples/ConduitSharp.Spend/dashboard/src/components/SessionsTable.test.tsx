import { render, screen } from '@testing-library/react';
import { SessionsTable } from './SessionsTable';
import { DEFAULT_INSIGHTS_CONFIG } from '../utils/parser';
import type { MetricsData } from '../utils/parser';
import { describe, it, expect } from 'vitest';
import userEvent from '@testing-library/user-event';

const mockMetrics: MetricsData = {
  totals: { in: 1000, out: 500, cacheWrite: 0, cacheRead: 0, think: 0, ms: 5000, messagesSent: 5 },
  sessions: {
    sess1: { turnCount: 10, in: 1000, cacheRead: 0, cacheWrite: 0, out: 500, sessionName: 'Session 1', route: 'claude', models: new Set(['claude-3-opus']), tools: 0, prompts: [] },
    sess2: { turnCount: 5, in: 200, cacheRead: 50, cacheWrite: 50, out: 100, sessionName: 'Test Session', route: 'gpt4', models: new Set(['gpt-4o']), tools: 0, prompts: [] }
  },
  dailyUsage: {},
  modelBreakdown: {},
  routeBreakdown: {},
  topPrompts: [],
};

describe('SessionsTable Component', () => {
  it('renders sessions', () => {
    render(<SessionsTable metrics={mockMetrics} config={DEFAULT_INSIGHTS_CONFIG} />);
    expect(screen.getByText('Test Session')).toBeInTheDocument();
  });

  it('shows no sessions match when search has no results', async () => {
    const user = userEvent.setup();
    render(<SessionsTable metrics={mockMetrics} config={DEFAULT_INSIGHTS_CONFIG} />);
    const input = screen.getByPlaceholderText('Search sessions...');
    await user.type(input, 'Nonexistent');
    expect(screen.getByText('No sessions match your search.')).toBeInTheDocument();
  });
});
