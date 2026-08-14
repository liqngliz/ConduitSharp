import type { MetricsData } from './parser';

export interface FlatPromptRow {
  key: string;
  prompt: string;
  turn: number;
  model: string;
  in: number;
  cacheRead: number;
  cacheWrite: number;
  think: number;
  out: number;
  total: number;
  read: number;
  ts: string;
  tools: number;
  hasToolCall: boolean;
  sessionId: string;
  sessionName: string;
  turnCount: number;
  trace?: string;
  traces: string[];
}

export interface WireEntry {
  time: string;
  path: string;
  traceId: string;
  direction: 'request' | 'response' | string;
  body: string;
}

export function flattenPrompts(sessions: MetricsData['sessions']): FlatPromptRow[] {
  return Object.entries(sessions).flatMap(([sessionId, s]) => {
    const sessionName = s.sessionName || sessionId;
    return s.prompts.map(p => {
      const traces = p.traces && p.traces.length > 0 ? p.traces : (p.trace ? [p.trace] : []);
      return {
        key: `${sessionId}-${p.ts}-${p.turn}`,
        prompt: p.prompt,
        turn: p.turn,
        model: p.model,
        in: p.in,
        cacheRead: p.cacheRead,
        cacheWrite: p.cacheWrite,
        think: p.think,
        out: p.out,
        total: p.total,
        read: p.in + p.cacheRead + p.cacheWrite,
        ts: p.ts,
        tools: p.tools,
        hasToolCall: p.hasToolCall,
        sessionId,
        sessionName,
        turnCount: s.turnCount,
        trace: p.trace,
        traces,
      };
    });
  });
}

export interface PromptSelectionTarget {
  sessionId: string;
  traceId?: string;
  ts?: string;
  turn?: number;
}

export function findPrompt(
  sessions: MetricsData['sessions'],
  selected: PromptSelectionTarget
): FlatPromptRow | undefined {
  const sess = sessions[selected.sessionId];
  if (!sess) return undefined;
  const flat = flattenPrompts({ [selected.sessionId]: sess });
  if (flat.length === 0) return undefined;

  if (selected.traceId) {
    const byTrace = flat.find(p => p.trace === selected.traceId || p.traces.includes(selected.traceId!));
    if (byTrace) return byTrace;
  }
  if (selected.ts) {
    const byTs = flat.find(p => p.ts === selected.ts || (p as any).firstTs === selected.ts);
    if (byTs) return byTs;
  }
  if (selected.turn !== undefined) {
    const byTurn = flat.find(p => p.turn === selected.turn);
    if (byTurn) return byTurn;
  }
  return flat[0];
}

export function findPromptByTrace(sessions: MetricsData['sessions'], traceId: string): FlatPromptRow | undefined {
  const all = flattenPrompts(sessions);
  return all.find(p => p.trace === traceId || p.traces.includes(traceId));
}

export function findFirstPrompt(sessions: MetricsData['sessions'], sessionId: string): FlatPromptRow | undefined {
  const all = flattenPrompts(sessions);
  return all.find(p => p.sessionId === sessionId);
}

export function extractUserPrompt(body: string): string | null {
  try {
    const data = JSON.parse(body);
    if (!data || !Array.isArray(data.messages)) return null;
    for (let i = data.messages.length - 1; i >= 0; i--) {
      const msg = data.messages[i];
      if (msg && msg.role === 'user') {
        if (typeof msg.content === 'string') {
          return msg.content;
        }
        if (Array.isArray(msg.content)) {
          const textParts = msg.content
            .filter((block: any) => block && block.type === 'text' && typeof block.text === 'string')
            .map((block: any) => block.text);
          if (textParts.length > 0) {
            return textParts.join('\n');
          }
        }
      }
    }
    return null;
  } catch {
    return null;
  }
}

export function formatWireBody(body: string): string {
  try {
    return JSON.stringify(JSON.parse(body), null, 2);
  } catch {
    return body;
  }
}
