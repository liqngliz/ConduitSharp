import { render, screen, fireEvent } from '@testing-library/react';
import { SessionsTable } from './SessionsTable';
import { DEFAULT_INSIGHTS_CONFIG } from '../utils/parser';
import type { MetricsData } from '../utils/parser';
import { describe, it, expect } from 'vitest';

const mockMetrics: MetricsData = {
  totals: { in: 1000, out: 500, cacheWrite: 0, cacheRead: 0, think: 0, ms: 5000, messagesSent: 5 },
  sessions: {
    sess1: { turnCount: 10, in: 1000, cacheRead: 0, cacheWrite: 0, think: 0, out: 500, sessionName: 'Session 1', route: 'claude', models: new Set(['claude-3-opus']), tools: 0, prompts: [] },
    sess2: { turnCount: 5, in: 200, cacheRead: 50, cacheWrite: 50, think: 0, out: 100, sessionName: 'Test Session', route: 'gpt4', models: new Set(['gpt-4o']), tools: 0, prompts: [] }
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

  it('shows no sessions found when empty', () => {
    const emptyMetrics: MetricsData = {
      ...mockMetrics,
      sessions: {}
    };
    render(<SessionsTable metrics={emptyMetrics} config={DEFAULT_INSIGHTS_CONFIG} />);
    expect(screen.getByText('No sessions found.')).toBeInTheDocument();
  });

  it('filters sessions by search term', () => {
    render(<SessionsTable metrics={mockMetrics} config={DEFAULT_INSIGHTS_CONFIG} />);
    const input = screen.getByPlaceholderText('Search sessions...');
    expect(screen.getByText('Session 1')).toBeInTheDocument();
    expect(screen.getByText('Test Session')).toBeInTheDocument();

    fireEvent.change(input, { target: { value: 'Test' } });
    expect(screen.queryByText('Session 1')).not.toBeInTheDocument();
    expect(screen.getByText('Test Session')).toBeInTheDocument();
  });
});
