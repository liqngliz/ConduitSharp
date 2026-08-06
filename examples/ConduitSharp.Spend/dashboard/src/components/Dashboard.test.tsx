import { render, screen, waitFor } from '@testing-library/react';
import { Dashboard } from './Dashboard';
import { describe, it, expect, vi, beforeAll, afterAll } from 'vitest';
import userEvent from '@testing-library/user-event';

beforeAll(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date('2026-08-04T12:00:00Z'));
  global.EventSource = vi.fn().mockImplementation(() => ({
    close: vi.fn(),
  })) as any;
});

afterAll(() => {
  vi.useRealTimers();
  delete (global as any).EventSource;
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
