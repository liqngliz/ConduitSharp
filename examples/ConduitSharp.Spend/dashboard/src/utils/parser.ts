export interface SpendRecord {
  ts: string;
  route: string;
  model: string;
  servedModel: string;
  caller: string;
  in: number;
  out: number;
  cacheWrite: number;
  cacheRead: number;
  session: string;
  turn: number;
  tools: number;
  ms: number;
  streamed: boolean;
  prompt?: string;
  sessionName?: string;
}

export function parseJsonl(content: string): SpendRecord[] {
  return content
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
    .map((line) => {
      try {
        return JSON.parse(line) as SpendRecord;
      } catch (e) {
        console.error('Failed to parse line:', line);
        return null;
      }
    })
    .filter((record): record is SpendRecord => record !== null);
}

export interface MetricsData {
  totals: {
    in: number;
    out: number;
    cacheWrite: number;
    cacheRead: number;
    ms: number;
    messagesSent: number;
    costEstimates: { [model: string]: number };
  };
  sessions: Record<string, {
    turnCount: number;
    in: number;
    cacheRead: number;
    cacheWrite: number;
    out: number;
    sessionName: string;
    route: string;
    models: Set<string>;
    tools: number;
    prompts: { prompt: string; turn: number; model: string; in: number; cacheRead: number; cacheWrite: number; out: number; total: number; ts: string; tools: number }[];
  }>;
  dailyUsage: Record<string, { in: number; out: number }>;
  modelBreakdown: Record<string, { in: number; out: number }>;
  routeBreakdown: Record<string, { in: number; cacheRead: number; cacheWrite: number; out: number }>;
  topPrompts: { prompt: string; in: number; cacheRead: number; cacheWrite: number; out: number; totalTokens: number; session: string; turn: number; model: string }[];
}

export function computeMetrics(records: SpendRecord[]): MetricsData {
  const metrics: MetricsData = {
    totals: { in: 0, out: 0, cacheWrite: 0, cacheRead: 0, ms: 0, messagesSent: 0, costEstimates: {} },
    sessions: {},
    dailyUsage: {},
    modelBreakdown: {},
    routeBreakdown: {},
    topPrompts: [],
  };

  const promptsMap: Record<string, { in: number; cacheRead: number; cacheWrite: number; out: number; total: number; session: string; turn: number; model: string }> = {};

  for (const record of records) {
    // Totals
    metrics.totals.in += record.in;
    metrics.totals.out += record.out;
    metrics.totals.cacheWrite += record.cacheWrite;
    metrics.totals.cacheRead += record.cacheRead;
    metrics.totals.ms += record.ms;
    
    // Only count messages that actually consumed tokens
    if (record.in + record.out + record.cacheRead + record.cacheWrite > 0) {
      metrics.totals.messagesSent += 1;
    }

    // Sessions
    if (record.session) {
      if (!metrics.sessions[record.session]) {
        metrics.sessions[record.session] = {
          turnCount: 0,
          in: 0,
          cacheRead: 0,
          cacheWrite: 0,
          out: 0,
          sessionName: record.sessionName || record.session,
          route: record.route,
          models: new Set(),
          tools: 0,
          prompts: [],
        };
      }
      const sess = metrics.sessions[record.session];
      sess.turnCount = Math.max(sess.turnCount, record.turn);
      sess.in += record.in;
      sess.cacheRead += record.cacheRead;
      sess.cacheWrite += record.cacheWrite;
      sess.out += record.out;
      if (record.sessionName) sess.sessionName = record.sessionName;
      sess.models.add(record.model);
      sess.tools = Math.max(sess.tools, record.tools);

      // Each log line is exactly one prompt captured by the API gateway
      sess.prompts.push({
        prompt: record.prompt || '',
        ts: record.ts,
        turn: record.turn,
        model: record.model || 'Unknown',
        in: record.in,
        cacheRead: record.cacheRead,
        cacheWrite: record.cacheWrite,
        out: record.out,
        total: record.in + record.out + record.cacheRead + record.cacheWrite,
        tools: record.tools || 0
      });
    }

    // Daily
    const day = record.ts.split('T')[0];
    if (!metrics.dailyUsage[day]) metrics.dailyUsage[day] = { in: 0, out: 0 };
    metrics.dailyUsage[day].in += record.in;
    metrics.dailyUsage[day].out += record.out;

    // Model
    const model = record.model;
    if (model && model.trim() !== '' && model.toLowerCase() !== 'unknown') {
      if (!metrics.modelBreakdown[model]) metrics.modelBreakdown[model] = { in: 0, out: 0 };
      metrics.modelBreakdown[model].in += record.in;
      metrics.modelBreakdown[model].out += record.out;
    }

    // Route
    const route = record.route || 'unknown';
    if (!metrics.routeBreakdown[route]) metrics.routeBreakdown[route] = { in: 0, cacheRead: 0, cacheWrite: 0, out: 0 };
    metrics.routeBreakdown[route].in += record.in;
    metrics.routeBreakdown[route].cacheRead += record.cacheRead;
    metrics.routeBreakdown[route].cacheWrite += record.cacheWrite;
    metrics.routeBreakdown[route].out += record.out;

    // Prompts
    if (record.prompt && (record.in + record.out + record.cacheRead + record.cacheWrite > 0)) {
      if (!promptsMap[record.prompt]) {
        promptsMap[record.prompt] = { in: 0, cacheRead: 0, cacheWrite: 0, out: 0, total: 0, session: record.session || '', turn: record.turn || 0, model: record.model || '' };
      }
      const p = promptsMap[record.prompt];
      p.in += record.in;
      p.cacheRead += record.cacheRead;
      p.cacheWrite += record.cacheWrite;
      p.out += record.out;
      p.total += (record.in + record.cacheRead + record.cacheWrite + record.out);
    }
  }

  // Sort prompts by timestamp for all sessions, remove 0-token sessions, and fold identical consecutive prompts
  for (const [id, sess] of Object.entries(metrics.sessions)) {
    if (sess.in + sess.out + sess.cacheRead + sess.cacheWrite === 0) {
      delete metrics.sessions[id];
      continue;
    }
    sess.prompts.sort((a, b) => new Date(a.ts).getTime() - new Date(b.ts).getTime());
    
    const foldedPrompts: typeof sess.prompts = [];
    let pre: typeof sess.prompts[0] | null = null;
    let preOrigDate = 0;
    let preOrigTurn = 0;
    
    for (const cur of sess.prompts) {
      if (!pre) {
        pre = { ...cur };
        preOrigDate = new Date(cur.ts).getTime();
        preOrigTurn = cur.turn;
        foldedPrompts.push(pre);
        continue;
      }
      
      const curDate = new Date(cur.ts).getTime();
      
      if (curDate > preOrigDate && cur.turn > preOrigTurn && cur.prompt === pre.prompt) {
        pre.in += cur.in;
        pre.out += cur.out;
        pre.cacheRead += cur.cacheRead;
        pre.cacheWrite += cur.cacheWrite;
        pre.total += cur.total;
        pre.tools = Math.max(pre.tools, cur.tools);
        
        preOrigDate = curDate;
        preOrigTurn = cur.turn;
      } else {
        pre = { ...cur };
        preOrigDate = curDate;
        preOrigTurn = cur.turn;
        foldedPrompts.push(pre);
      }
    }
    sess.prompts = foldedPrompts.filter(p => p.total > 0);
  }

  metrics.topPrompts = Object.entries(promptsMap)
    .map(([prompt, stats]) => ({ prompt, in: stats.in, cacheRead: stats.cacheRead, cacheWrite: stats.cacheWrite, out: stats.out, totalTokens: stats.total, session: stats.session, turn: stats.turn, model: stats.model }))
    .sort((a, b) => b.totalTokens - a.totalTokens)
    .slice(0, 10);

  return metrics;
}

export interface InsightsData {
  vaguePrompts: number;
  marathonSessions: number;
  inputHeavy: number;
  toolHeavy: number;
  routeDominance: { route: string; percent: number } | null;
}

export function computeInsights(records: SpendRecord[], metrics: MetricsData): InsightsData {
  let vaguePrompts = 0;
  let inputHeavy = 0;
  let toolHeavy = 0;

  for (const record of records) {
    if (record.prompt && record.prompt.length < 30 && record.in > 1000) {
      vaguePrompts++;
    }
    if (record.in > 5000 && record.out < 100) {
      inputHeavy++;
    }
  }

  let marathonSessions = 0;
  for (const sess of Object.values(metrics.sessions)) {
    if (sess.turnCount > 15) marathonSessions++;
    if (sess.tools > sess.turnCount * 3 && sess.turnCount > 0) toolHeavy++;
  }

  let routeDominance: { route: string; percent: number } | null = null;
  const totalTokens = metrics.totals.in + metrics.totals.out + metrics.totals.cacheRead + metrics.totals.cacheWrite;
  if (totalTokens > 0) {
    for (const [route, counts] of Object.entries(metrics.routeBreakdown)) {
      const pct = (counts.in + counts.out + counts.cacheRead + counts.cacheWrite) / totalTokens;
      if (pct > 0.6) {
        routeDominance = { route, percent: Math.round(pct * 100) };
        break;
      }
    }
  }

  return {
    vaguePrompts,
    marathonSessions,
    inputHeavy,
    toolHeavy,
    routeDominance,
  };
}
