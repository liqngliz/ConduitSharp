import { render, screen, waitFor, act } from '@testing-library/react';
import { Dashboard } from './Dashboard';
import { describe, it, expect, vi, beforeAll, afterAll, afterEach } from 'vitest';
import userEvent from '@testing-library/user-event';

beforeAll(() => {
  vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
  vi.setSystemTime(new Date('2026-08-04T12:00:00Z'));
  global.EventSource = vi.fn().mockImplementation(() => ({
    close: vi.fn(),
  })) as any;
});

afterAll(() => {
  vi.useRealTimers();
  delete (global as any).EventSource;
});

afterEach(() => {
  vi.setSystemTime(new Date('2026-08-04T12:00:00Z'));
});

describe('Dashboard Component', () => {
  it('shows loading state initially', () => {
    // Mock fetch that doesn't resolve immediately
    global.fetch = vi.fn().mockImplementation(() => new Promise(() => {}));
    render(<Dashboard />);
    expect(screen.getByText('Loading logs...')).toBeInTheDocument();
  });

  it('renders metrics after fetching', async () => {
    const mockJsonl = `{"ts":"2026-08-02T21:41:47","route":"local","model":"gpt-mini","servedModel":"gpt-mini","caller":"me","in":100,"out":20,"cacheWrite":0,"cacheRead":0,"session":"sess1","turn":1,"tools":0,"ms":500,"streamed":true,"prompt":"hi"}`;
    
    global.fetch = vi.fn().mockImplementation((url) => {
      if (url === '/api/spend') {
        return Promise.resolve({
          ok: true,
          json: () => Promise.resolve(['2026-08-02'])
        });
      }
      return Promise.resolve({
        ok: true,
        json: () => Promise.resolve([JSON.parse(mockJsonl)])
      });
    });

    render(<Dashboard />);

    await waitFor(() => {
      expect(screen.getByText(/tokens visualized/i)).toBeInTheDocument();
    });

    expect(screen.getAllByText('200')[0]).toBeInTheDocument(); // Total Usage is now 100 + (20*5)
    expect(screen.getAllByText('local')[0]).toBeInTheDocument(); // Route button
  });

  it('shows error state on fetch failure', async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error('Network error'));
    render(<Dashboard />);
    await waitFor(() => {
      expect(screen.getByText('Could not load logs from /api/spend.')).toBeInTheDocument();
    });
  });

  it('filters range-scoped fetches correctly', async () => {
    // Current date is mocked to 2026-08-04. Lookback default is 7 days, so range is 2026-07-28 to 2026-08-04.
    const fetchSpy = vi.fn().mockImplementation((url) => {
      if (url === '/api/spend') {
        // Return dates including some out of range
        return Promise.resolve({
          ok: true,
          json: () => Promise.resolve(['2026-07-27', '2026-07-28', '2026-08-05'])
        });
      }
      return Promise.resolve({ ok: true, json: () => Promise.resolve([]) });
    });
    global.fetch = fetchSpy;

    render(<Dashboard />);
    
    await waitFor(() => {
      expect(screen.getByText(/tokens visualized/i)).toBeInTheDocument();
    });

    // Should only fetch the dates within range, so 2026-07-28 but not 2026-07-27 or 2026-08-05.
    expect(fetchSpy).toHaveBeenCalledWith('/api/spend/2026-07-28');
    expect(fetchSpy).not.toHaveBeenCalledWith('/api/spend/2026-07-27');
    expect(fetchSpy).not.toHaveBeenCalledWith('/api/spend/2026-08-05');
  });

  it('filters incoming SSE records outside the active date window', async () => {
    const fetchSpy = vi.fn().mockImplementation((url) => {
      if (url === '/api/spend') return Promise.resolve({ ok: true, json: () => Promise.resolve([]) });
      return Promise.resolve({ ok: true, json: () => Promise.resolve([]) });
    });
    global.fetch = fetchSpy;

    let onMessageCb: (event: any) => void = () => {};
    global.EventSource = vi.fn().mockImplementation(() => ({
      close: vi.fn(),
      set onmessage(cb: any) {
        onMessageCb = cb;
      }
    })) as any;

    render(<Dashboard />);
    await waitFor(() => expect(screen.getByText(/tokens visualized/i)).toBeInTheDocument());

    // Window is 2026-07-28 to 2026-08-04
    // Valid record
    onMessageCb({ data: JSON.stringify({ ts: '2026-08-04T12:00:00', route: 'valid-route', in: 10, out: 10, turn: 1 }) });
    
    // Invalid record (too early)
    onMessageCb({ data: JSON.stringify({ ts: '2026-07-27T12:00:00', route: 'invalid-route', in: 10, out: 10, turn: 2 }) });

    // Fast-forward to trigger the 250ms debounce flush
    await new Promise(r => setTimeout(r, 300));

    await waitFor(() => {
      // The valid route should appear in the buttons
      expect(screen.getByRole('button', { name: 'valid-route' })).toBeInTheDocument();
    });

    // The invalid route should be dropped by the filter
    expect(screen.queryByRole('button', { name: 'invalid-route' })).not.toBeInTheDocument();
  });

  it('advances the rollover interval dynamically past midnight', async () => {
    const fetchSpy = vi.fn().mockImplementation((url) => {
      if (url === '/api/spend') {
        return Promise.resolve({ ok: true, json: () => Promise.resolve(['2026-08-04', '2026-08-05']) });
      }
      return Promise.resolve({ ok: true, json: () => Promise.resolve([]) });
    });
    global.fetch = fetchSpy;

    vi.setSystemTime(new Date('2026-08-04T23:59:00Z'));

    render(<Dashboard />);
    await waitFor(() => expect(screen.getByText(/tokens visualized/i)).toBeInTheDocument());

    // Clear calls from initial mount
    fetchSpy.mockClear();

    await act(async () => {
      vi.setSystemTime(new Date('2026-08-05T00:01:00Z'));
      vi.advanceTimersByTime(65000); // Wait for the 60s interval
    });

    // The component should realize the day shifted to 2026-08-05, update dateConfig, and re-fetch!
    await waitFor(() => {
      expect(fetchSpy).toHaveBeenCalledWith('/api/spend/2026-08-05');
    });
  });

  it('filters records by route', async () => {
    const user = userEvent.setup();
    const mockJsonl = `{"ts":"2026-08-02T21:41:47","route":"local","model":"gpt-mini","servedModel":"gpt-mini","caller":"me","in":100,"out":20,"cacheWrite":0,"cacheRead":0,"session":"sess1","turn":1,"tools":0,"ms":500,"streamed":true,"prompt":"hi"}`;
    global.fetch = vi.fn().mockImplementation((url) => {
      if (url === '/api/spend') {
        return Promise.resolve({ ok: true, json: () => Promise.resolve(['2026-08-02']) });
      }
      return Promise.resolve({ ok: true, json: () => Promise.resolve([JSON.parse(mockJsonl)]) });
    });
    render(<Dashboard />);
    await waitFor(() => expect(screen.getByText(/tokens visualized/i)).toBeInTheDocument());
    
    // click on the local button
    const localBtn = screen.getAllByText('local')[0];
    await user.click(localBtn);
    
    expect(screen.getByText('Your local tokens visualized.')).toBeInTheDocument();
  });
});
