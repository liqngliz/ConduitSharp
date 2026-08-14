import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { PromptPanel } from './PromptPanel';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { MetricsData } from '../utils/parser';

describe('PromptPanel Component', () => {
  const mockSessions: MetricsData['sessions'] = {
    sess1: {
      sessionName: 'Test Session',
      turnCount: 20,
      in: 1000,
      out: 500,
      cacheRead: 200,
      cacheWrite: 100,
      think: 50,
      route: 'claude',
      models: new Set(['claude-3-5-sonnet']),
      tools: 1,
      prompts: [
        {
          turn: 1,
          prompt: 'First prompt prefix',
          model: 'claude-3-5-sonnet',
          in: 1000,
          out: 500,
          cacheRead: 200,
          cacheWrite: 100,
          think: 50,
          total: 1850,
          ts: '2026-08-14T10:00:00Z',
          tools: 1,
          hasToolCall: true,
          trace: 'trace-1',
          traces: ['trace-1', 'trace-2']
        }
      ]
    }
  };

  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('renders nothing / closed style when selected is null', () => {
    const { container } = render(
      <PromptPanel selected={null} sessions={mockSessions} onClose={() => {}} />
    );
    const panel = container.firstChild as HTMLElement;
    expect(panel.className).toContain('translate-x-full');
  });

  it('renders prompt details and badges when selected', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      status: 200,
      text: async () => JSON.stringify([
        {
          direction: 'request',
          path: '/v1/messages',
          body: JSON.stringify({ messages: [{ role: 'user', content: 'Full extracted prompt from wire log' }] })
        },
        {
          direction: 'response',
          path: '/v1/messages',
          body: JSON.stringify({ content: [{ type: 'text', text: 'Assistant reply' }] })
        }
      ])
    } as Response);

    render(
      <PromptPanel
        selected={{ sessionId: 'sess1', traceId: 'trace-1' }}
        sessions={mockSessions}
        onClose={() => {}}
      />
    );

    expect(screen.getByText('sess1')).toBeInTheDocument();
    expect(screen.getByText('Marathon')).toBeInTheDocument();
    expect(screen.getByText('claude-3-5-sonnet')).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.getByText('Full extracted prompt from wire log')).toBeInTheDocument();
    });

    expect(screen.getByText('1. trace-1')).toBeInTheDocument();
    expect(screen.getByText('2. trace-2')).toBeInTheDocument();
  });

  it('switches trace and refetches wire log on folded trace chip click', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      status: 200,
      text: async () => JSON.stringify([])
    } as Response);

    render(
      <PromptPanel
        selected={{ sessionId: 'sess1', traceId: 'trace-1' }}
        sessions={mockSessions}
        onClose={() => {}}
      />
    );

    const chip2 = screen.getByText('2. trace-2');
    fireEvent.click(chip2);

    await waitFor(() => {
      expect(fetchSpy).toHaveBeenCalledWith('/api/wire/trace-2');
    });
  });

  it('handles 404 wire log response gracefully', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: false,
      status: 404,
      statusText: 'Not Found',
      text: async () => ''
    } as Response);

    render(
      <PromptPanel
        selected={{ sessionId: 'sess1', traceId: 'trace-1' }}
        sessions={mockSessions}
        onClose={() => {}}
      />
    );

    await waitFor(() => {
      expect(screen.getByText(/Body capture may have been off for this call/)).toBeInTheDocument();
    });
  });

  it('calls onClose when close button clicked or Escape key pressed', () => {
    const onClose = vi.fn();
    render(
      <PromptPanel
        selected={{ sessionId: 'sess1', traceId: 'trace-1' }}
        sessions={mockSessions}
        onClose={onClose}
      />
    );

    const closeBtn = screen.getByLabelText('Close panel');
    fireEvent.click(closeBtn);
    expect(onClose).toHaveBeenCalledTimes(1);

    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it('renders fallback empty state when session prompt cannot be resolved', () => {
    const { rerender } = render(
      <PromptPanel
        selected={{ sessionId: 'sess1', traceId: 'trace-1' }}
        sessions={mockSessions}
        onClose={() => {}}
      />
    );

    expect(screen.getByText('sess1')).toBeInTheDocument();
    expect(screen.getByText('First prompt prefix')).toBeInTheDocument();

    // Rerender with an unresolvable selection
    rerender(
      <PromptPanel
        selected={{ sessionId: 'unknown-session' }}
        sessions={mockSessions}
        onClose={() => {}}
      />
    );

    expect(screen.getByText('unknown-session')).toBeInTheDocument();
    expect(screen.getByText('Prompt details not found for this selection.')).toBeInTheDocument();
    expect(screen.queryByText('First prompt prefix')).not.toBeInTheDocument();
  });

  it('sets aria-hidden and inert when closed', () => {
    const { container, rerender } = render(
      <PromptPanel
        selected={{ sessionId: 'sess1', traceId: 'trace-1' }}
        sessions={mockSessions}
        onClose={() => {}}
      />
    );

    const panel = container.firstChild as HTMLElement;
    expect(panel.getAttribute('aria-hidden')).toBe('false');

    rerender(
      <PromptPanel
        selected={null}
        sessions={mockSessions}
        onClose={() => {}}
      />
    );

    expect(panel.getAttribute('aria-hidden')).toBe('true');
  });
});
