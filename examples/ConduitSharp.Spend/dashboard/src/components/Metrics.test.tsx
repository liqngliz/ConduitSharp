import { render, screen } from '@testing-library/react';
import { Metrics } from './Metrics';
import type { MetricsData } from '../utils/parser';
import { describe, it, expect } from 'vitest';

const mockMetrics: MetricsData = {
  totals: {
    in: 1000,
    out: 500,
    cacheWrite: 0,
    cacheRead: 0,
    ms: 5000,
    messagesSent: 5,
    costEstimates: {},
  },
  sessions: {
    sess1: { turnCount: 1, in: 1000, cacheRead: 0, cacheWrite: 0, out: 500, sessionName: 'Test', route: 'claude', models: new Set(), tools: 0, prompts: [] }
  },
  dailyUsage: {},
  modelBreakdown: {},
  routeBreakdown: {},
  topPrompts: [
    { prompt: 'Hello world', in: 500, cacheRead: 0, cacheWrite: 0, out: 1000, totalTokens: 1500, session: 'sess1', turn: 1, model: 'claude-3-opus' },
  ],
};

const dummyProps = {
  startDate: '2026-08-01',
  setStartDate: () => {},
  endDate: '2026-08-07',
  setEndDate: () => {},
};

describe('Metrics Component', () => {
  it('renders total input tokens correctly', () => {
    render(<Metrics metrics={mockMetrics} routeName="Claude" {...dummyProps} />);
    expect(screen.getByText('1,500')).toBeInTheDocument();
  });

  it('renders top prompts', () => {
    render(<Metrics metrics={mockMetrics} routeName="Claude" {...dummyProps} />);
    expect(screen.getByText('Hello world')).toBeInTheDocument();
    expect(screen.getByText('Total: 1,500')).toBeInTheDocument();
  });

  it('shows Caching insight when cache is highest', () => {
    const metrics = { ...mockMetrics, totals: { ...mockMetrics.totals, in: 100, out: 50, cacheRead: 500, cacheWrite: 0 } };
    render(<Metrics metrics={metrics} routeName="Claude" {...dummyProps} />);
    expect(screen.getByText(/most usage is Caching/)).toBeInTheDocument();
  });

  it('shows Writing output insight when output is highest', () => {
    const metrics = { ...mockMetrics, totals: { ...mockMetrics.totals, in: 100, out: 1000, cacheRead: 10, cacheWrite: 0 } };
    render(<Metrics metrics={metrics} routeName="Claude" {...dummyProps} />);
    expect(screen.getByText(/most usage is Writing output/)).toBeInTheDocument();
  });
});
