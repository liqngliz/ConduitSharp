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
    think: 0,
    ms: 5000,
    messagesSent: 5,
  },
  sessions: {
    sess1: { turnCount: 1, in: 1000, cacheRead: 0, cacheWrite: 0, think: 0, out: 500, sessionName: 'Test', route: 'claude', models: new Set(), tools: 0, prompts: [] }
  },
  dailyUsage: {},
  modelBreakdown: {},
  routeBreakdown: {},
  topPrompts: [
    { prompt: 'Hello world', in: 500, cacheRead: 0, cacheWrite: 0, think: 0, out: 1000, totalTokens: 1500, session: 'sess1', turn: 1, model: 'claude-3-opus' },
  ],
};



describe('Metrics Component', () => {
  it('renders all metric titles correctly', () => {
    render(<Metrics metrics={mockMetrics} routeName="Claude" />);
    
    expect(screen.getAllByText(/Total Usage/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/Sessions/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/Prompts Sent/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/Claude Wrote/i).length).toBeGreaterThan(0);
  });

  it('shows Caching insight when cache is highest', () => {
    const metrics = { ...mockMetrics, totals: { ...mockMetrics.totals, in: 100, out: 50, cacheRead: 500, cacheWrite: 0 } };
    render(<Metrics metrics={metrics} routeName="Claude" />);
    expect(screen.getByText(/Most usage: Caching/)).toBeInTheDocument();
  });

  it('hides the thinking row when no route reported reasoning tokens', () => {
    render(<Metrics metrics={mockMetrics} routeName="Claude" />);
    expect(screen.queryByTestId('metric-think')).toBeNull();
  });

  it('shows thinking against Out, not against the total', () => {
    const metrics = { ...mockMetrics, totals: { ...mockMetrics.totals, out: 500, think: 125 } };
    render(<Metrics metrics={metrics} routeName="Claude" />);
    expect(screen.getByTestId('metric-think')).toHaveTextContent('25% of Out');
  });

  it('shows Writing output insight when output is highest', () => {
    const metrics = { ...mockMetrics, totals: { ...mockMetrics.totals, in: 100, out: 1000, cacheRead: 10, cacheWrite: 0 } };
    render(<Metrics metrics={metrics} routeName="Claude" />);
    expect(screen.getByText(/Most usage: Writing output/)).toBeInTheDocument();
  });
});
