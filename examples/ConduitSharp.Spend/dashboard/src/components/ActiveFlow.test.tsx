import { render, screen, fireEvent } from '@testing-library/react';
import { ActiveFlow } from './ActiveFlow';
import { DEFAULT_INSIGHTS_CONFIG } from '../utils/parser';
import type { MetricsData } from '../utils/parser';
import { describe, it, expect, vi } from 'vitest';

describe('ActiveFlow Component', () => {
  const emptyMetrics: MetricsData = {
    totals: { in: 0, out: 0, cacheWrite: 0, cacheRead: 0, think: 0, ms: 0, messagesSent: 0 },
    sessions: {},
    dailyUsage: {},
    modelBreakdown: {},
    routeBreakdown: {},
    topPrompts: []
  };

  it('renders waiting state when there are no prompts', () => {
    render(<ActiveFlow metrics={emptyMetrics} config={DEFAULT_INSIGHTS_CONFIG} />);
    expect(screen.getByText('Active Flow')).toBeInTheDocument();
    expect(screen.getByText('WAITING FOR PROMPTS...')).toBeInTheDocument();
  });

  it('aggregates prompts from multiple sessions and renders flowchart', () => {
    const metrics: MetricsData = {
      ...emptyMetrics,
      sessions: {
        sess1: {
          sessionName: 'Session 1',
          turnCount: 1,
          in: 100,
          out: 50,
          cacheRead: 10,
          cacheWrite: 20,
          think: 5,
          route: 'claude',
          models: new Set(['claude-3']),
          tools: 0,
          prompts: [
            {
              turn: 1,
              prompt: 'First prompt from session 1',
              model: 'claude-3',
              in: 100,
              out: 50,
              cacheRead: 10,
              cacheWrite: 20,
              think: 5,
              total: 185,
              ts: '2026-08-14T01:00:00Z',
              tools: 0,
              hasToolCall: false,
              trace: 'trace-1',
              traces: ['trace-1']
            }
          ]
        },
        sess2: {
          sessionName: 'Session 2',
          turnCount: 1,
          in: 200,
          out: 100,
          cacheRead: 0,
          cacheWrite: 0,
          think: 0,
          route: 'codex',
          models: new Set(['codex-1']),
          tools: 0,
          prompts: [
            {
              turn: 1,
              prompt: 'Second prompt from session 2',
              model: 'codex-1',
              in: 200,
              out: 100,
              cacheRead: 0,
              cacheWrite: 0,
              think: 0,
              total: 300,
              ts: '2026-08-14T02:00:00Z',
              tools: 0,
              hasToolCall: false,
              trace: 'trace-2',
              traces: ['trace-2']
            }
          ]
        }
      }
    };

    const onSelect = vi.fn();
    render(<ActiveFlow metrics={metrics} config={DEFAULT_INSIGHTS_CONFIG} onSessionSelect={onSelect} />);

    expect(screen.getByText('TOTAL TOKEN FLOW')).toBeInTheDocument();
    expect(screen.getByText('First prompt from session 1')).toBeInTheDocument();
    expect(screen.getByText('Second prompt from session 2')).toBeInTheDocument();
  });

  it('handles expanding and collapsing when more than 4 prompts exist', () => {
    const prompts = Array.from({ length: 6 }, (_, i) => ({
      turn: i + 1,
      prompt: `Prompt ${i + 1}`,
      model: 'claude-3',
      in: 100,
      out: 50,
      cacheRead: 0,
      cacheWrite: 0,
      think: 0,
      total: 150,
      ts: `2026-08-14T0${i}:00:00Z`,
      tools: 0,
      hasToolCall: false,
      trace: `trace-${i + 1}`,
      traces: [`trace-${i + 1}`]
    }));

    const metrics: MetricsData = {
      ...emptyMetrics,
      sessions: {
        sess1: {
          sessionName: 'Session 1',
          turnCount: 6,
          in: 600,
          out: 300,
          cacheRead: 0,
          cacheWrite: 0,
          think: 0,
          route: 'claude',
          models: new Set(['claude-3']),
          tools: 0,
          prompts
        }
      }
    };

    render(<ActiveFlow metrics={metrics} config={DEFAULT_INSIGHTS_CONFIG} />);
    const expandBtn = screen.getByText(/See 2 more prompts/);
    expect(expandBtn).toBeInTheDocument();

    fireEvent.click(expandBtn);
    expect(screen.getByText(/Collapse Active Flow/)).toBeInTheDocument();

    fireEvent.click(screen.getByText(/Collapse Active Flow/));
    expect(screen.getByText(/See 2 more prompts/)).toBeInTheDocument();
  });
});
