import { describe, it, expect } from 'vitest';
import { 
  flattenPrompts, 
  formatWireBody, 
  findPrompt,
  findPromptByTrace, 
  findFirstPrompt, 
  extractUserPrompt 
} from './promptHelpers';
import type { MetricsData } from './parser';

describe('promptHelpers Pure Helpers', () => {
  const mockSessions: MetricsData['sessions'] = {
    sess1: {
      sessionName: 'Session One',
      turnCount: 2,
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
          prompt: 'Hello world',
          model: 'claude-3',
          in: 50,
          out: 20,
          cacheRead: 5,
          cacheWrite: 10,
          think: 2,
          total: 87,
          ts: '2026-08-14T01:00:00Z',
          tools: 0,
          hasToolCall: false,
          trace: 'trace-111',
          traces: ['trace-111']
        },
        {
          turn: 2,
          prompt: 'How are you?',
          model: 'claude-3',
          in: 50,
          out: 30,
          cacheRead: 5,
          cacheWrite: 10,
          think: 3,
          total: 98,
          ts: '2026-08-14T01:05:00Z',
          tools: 0,
          hasToolCall: false,
          trace: 'trace-222',
          traces: ['trace-222', 'trace-333']
        }
      ]
    },
    sess2: {
      sessionName: 'Session Two',
      turnCount: 1,
      in: 200,
      out: 100,
      cacheRead: 0,
      cacheWrite: 0,
      think: 0,
      route: 'codex',
      models: new Set(['codex-1']),
      tools: 1,
      prompts: [
        {
          turn: 1,
          prompt: 'Fix bug',
          model: 'codex-1',
          in: 200,
          out: 100,
          cacheRead: 0,
          cacheWrite: 0,
          think: 0,
          total: 300,
          ts: '2026-08-14T02:00:00Z',
          tools: 1,
          hasToolCall: true,
          trace: 'trace-444',
          traces: ['trace-444']
        }
      ]
    }
  };

  describe('findPrompt', () => {
    it('matches by traceId when present', () => {
      const found = findPrompt(mockSessions, { sessionId: 'sess1', traceId: 'trace-222' });
      expect(found).toBeDefined();
      expect(found?.prompt).toBe('How are you?');
      expect(found?.turn).toBe(2);
    });

    it('matches by ts when traceId is missing (pre-trace log rows)', () => {
      const found = findPrompt(mockSessions, { sessionId: 'sess1', ts: '2026-08-14T01:05:00Z' });
      expect(found).toBeDefined();
      expect(found?.prompt).toBe('How are you?');
      expect(found?.turn).toBe(2);
    });

    it('matches by turn when traceId and ts are missing', () => {
      const found = findPrompt(mockSessions, { sessionId: 'sess1', turn: 2 });
      expect(found).toBeDefined();
      expect(found?.prompt).toBe('How are you?');
      expect(found?.turn).toBe(2);
    });

    it('falls back to first prompt in session when no specific match found', () => {
      const found = findPrompt(mockSessions, { sessionId: 'sess1' });
      expect(found).toBeDefined();
      expect(found?.prompt).toBe('Hello world');
      expect(found?.turn).toBe(1);
    });

    it('returns undefined when session does not exist', () => {
      const found = findPrompt(mockSessions, { sessionId: 'non-existent' });
      expect(found).toBeUndefined();
    });
  });

  describe('flattenPrompts', () => {
    it('flattens all session prompts into a single FlatPromptRow array', () => {
      const flat = flattenPrompts(mockSessions);
      expect(flat).toHaveLength(3);
      expect(flat[0].prompt).toBe('Hello world');
      expect(flat[0].sessionId).toBe('sess1');
      expect(flat[0].sessionName).toBe('Session One');
      expect(flat[0].turnCount).toBe(2);
      expect(flat[2].prompt).toBe('Fix bug');
      expect(flat[2].sessionId).toBe('sess2');
      expect(flat[2].hasToolCall).toBe(true);
    });

    it('returns empty array when sessions map is empty', () => {
      expect(flattenPrompts({})).toEqual([]);
    });
  });

  describe('findPromptByTrace', () => {
    it('finds prompt matching direct trace', () => {
      const found = findPromptByTrace(mockSessions, 'trace-111');
      expect(found).toBeDefined();
      expect(found?.prompt).toBe('Hello world');
      expect(found?.sessionId).toBe('sess1');
    });

    it('finds prompt matching a trace inside traces array', () => {
      const found = findPromptByTrace(mockSessions, 'trace-333');
      expect(found).toBeDefined();
      expect(found?.prompt).toBe('How are you?');
      expect(found?.sessionId).toBe('sess1');
    });

    it('returns undefined if trace is not found', () => {
      expect(findPromptByTrace(mockSessions, 'non-existent-trace')).toBeUndefined();
    });
  });

  describe('findFirstPrompt', () => {
    it('finds the first prompt for a given session ID', () => {
      const found = findFirstPrompt(mockSessions, 'sess1');
      expect(found).toBeDefined();
      expect(found?.prompt).toBe('Hello world');
      expect(found?.turn).toBe(1);
    });

    it('returns undefined if session has no prompts or does not exist', () => {
      expect(findFirstPrompt(mockSessions, 'non-existent-session')).toBeUndefined();
    });
  });

  describe('extractUserPrompt', () => {
    it('extracts string content from the last user message', () => {
      const body = JSON.stringify({
        messages: [
          { role: 'user', content: 'First prompt' },
          { role: 'assistant', content: 'Reply' },
          { role: 'user', content: 'Second prompt with instructions' }
        ]
      });
      expect(extractUserPrompt(body)).toBe('Second prompt with instructions');
    });

    it('extracts and joins array text blocks from the last user message', () => {
      const body = JSON.stringify({
        messages: [
          {
            role: 'user',
            content: [
              { type: 'text', text: 'Part 1 of the prompt.' },
              { type: 'text', text: 'Part 2 of the prompt.' }
            ]
          }
        ]
      });
      expect(extractUserPrompt(body)).toBe('Part 1 of the prompt.\nPart 2 of the prompt.');
    });

    it('returns null if no user message exists', () => {
      const body = JSON.stringify({
        messages: [
          { role: 'assistant', content: 'Hello' }
        ]
      });
      expect(extractUserPrompt(body)).toBeNull();
    });

    it('returns null for malformed JSON', () => {
      expect(extractUserPrompt('invalid json {')).toBeNull();
    });

    it('returns null when messages array is missing or invalid', () => {
      expect(extractUserPrompt('{}')).toBeNull();
    });
  });

  describe('formatWireBody', () => {
    it('formats valid JSON cleanly with 2 spaces indentation', () => {
      const rawJson = '{"prompt":"hello","max_tokens":100}';
      const formatted = formatWireBody(rawJson);
      expect(formatted).toBe('{\n  "prompt": "hello",\n  "max_tokens": 100\n}');
    });

    it('passes non-JSON text (like SSE streams) through unchanged', () => {
      const sseText = 'event: message_start\ndata: {"type":"message_start"}';
      expect(formatWireBody(sseText)).toBe(sseText);
    });

    it('passes plain text strings through unchanged', () => {
      const plain = 'regular text line';
      expect(formatWireBody(plain)).toBe(plain);
    });
  });
});
