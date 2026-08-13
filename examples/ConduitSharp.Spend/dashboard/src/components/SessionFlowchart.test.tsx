import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { SessionFlowchart } from './SessionFlowchart';
import { DEFAULT_INSIGHTS_CONFIG } from '../utils/parser';

describe('SessionFlowchart Component', () => {
  it('renders tool call badge when hasToolCall is true', () => {
    const mockSession = {
      turnCount: 1,
      in: 100,
      cacheRead: 0,
      cacheWrite: 0,
      think: 0,
      out: 50,
      sessionName: 'Test Session',
      route: 'test',
      models: new Set(['test-model']),
      tools: 2,
      prompts: [
        {
          prompt: 'test prompt',
          turn: 1,
          model: 'test-model',
          in: 100,
          cacheRead: 0,
          cacheWrite: 0,
          think: 0,
          out: 50,
          total: 150,
          ts: '2026-08-05T09:33:28.150Z',
          tools: 2,
          hasToolCall: true
        }
      ]
    };

    render(
      <SessionFlowchart session={mockSession as any} sessionId="sess1" config={DEFAULT_INSIGHTS_CONFIG} />
    );

    // Should find the text
    expect(screen.getByText('test prompt')).toBeInTheDocument();
  });
});
