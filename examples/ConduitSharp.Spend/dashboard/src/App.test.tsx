import { render, screen } from '@testing-library/react';
import App from './App';
import { describe, it, expect, vi, beforeAll, afterAll, beforeEach } from 'vitest';

describe('App Component', () => {
  beforeAll(() => {
    (globalThis as any).EventSource = class {
      close() {}
      addEventListener() {}
      removeEventListener() {}
    };
  });

  afterAll(() => {
    delete (globalThis as any).EventSource;
  });

  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('mounts Dashboard and renders header', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation((url) => {
      if (url === '/api/spend') {
        return Promise.resolve({
          ok: true,
          json: () => Promise.resolve([])
        } as Response);
      }
      return Promise.resolve({
        ok: true,
        text: () => Promise.resolve('')
      } as Response);
    });

    render(<App />);
    expect(await screen.findByText(/tokens visualized/i)).toBeInTheDocument();
  });
});
