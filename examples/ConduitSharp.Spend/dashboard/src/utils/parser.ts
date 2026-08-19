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
  think?: number;
  session: string;
  turn: number;
  tools: number;
  ms: number;
  streamed: boolean;
  prompt?: string;
  sessionName?: string;
  trace?: string;
}

export function parseJsonl(content: string): SpendRecord[] {
  return content
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
    .map((line) => {
      try {
        return JSON.parse(line) as SpendRecord;
      } catch {
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
    think: number;
    ms: number;
    messagesSent: number;
  };
  sessions: Record<string, {
    turnCount: number;
    in: number;
    cacheRead: number;
    cacheWrite: number;
    think: number;
    out: number;
    sessionName: string;
    route: string;
    models: Set<string>;
    tools: number;
    isToolHeavy?: boolean;
    prompts: { prompt: string; turn: number; model: string; in: number; cacheRead: number; cacheWrite: number; think: number; out: number; total: number; ts: string; firstTs?: string; tools: number; hasToolCall: boolean; trace?: string; traces?: string[]; }[];
  }>;
  dailyUsage: Record<string, { in: number; cacheRead: number; cacheWrite: number; think: number; out: number }>;
  modelBreakdown: Record<string, { in: number; cacheRead: number; cacheWrite: number; think: number; out: number }>;
  routeBreakdown: Record<string, { in: number; cacheRead: number; cacheWrite: number; think: number; out: number }>;
  topPrompts: { prompt: string; in: number; cacheRead: number; cacheWrite: number; think: number; out: number; totalTokens: number; session: string; turn: number; ts?: string; model: string; hasToolCall?: boolean; trace?: string; traces?: string[]; }[];
}

export interface TokenWeights {
  in: number;
  cw: number;
  cr: number;
  out: number;
}

export const DEFAULT_WEIGHTS: TokenWeights = { in: 1, cw: 1.25, cr: 0.1, out: 5 };

function applyWeights(record: SpendRecord, useWeights: boolean, weightsConfig: Record<string, TokenWeights>): SpendRecord {
  const rawThink = Math.min(record.think ?? 0, record.out);
  const rawOut = record.out - rawThink;

  if (useWeights) {
    const w = weightsConfig[record.model] || DEFAULT_WEIGHTS;
    return {
      ...record,
      in: record.in * (w.in ?? 1),
      cacheWrite: record.cacheWrite * (w.cw ?? 1.25),
      cacheRead: record.cacheRead * (w.cr ?? 0.1),
      out: rawOut * (w.out ?? 5),
      think: rawThink * (w.out ?? 5)
    };
  }
  return { ...record, out: rawOut, think: rawThink };
}

export function computeMetrics(records: SpendRecord[], config: InsightsConfig = DEFAULT_INSIGHTS_CONFIG, useWeights: boolean = false, weightsConfig: Record<string, TokenWeights> = {}): MetricsData {
  const metrics: MetricsData = {
    totals: { in: 0, out: 0, cacheWrite: 0, cacheRead: 0, think: 0, ms: 0, messagesSent: 0 },
    sessions: {},
    dailyUsage: {},
    modelBreakdown: {},
    routeBreakdown: {},
    topPrompts: [],
  };

  for (const rawRecord of records) {
    let record = applyWeights(rawRecord, useWeights, weightsConfig);

    // Totals
    metrics.totals.in += record.in;
    metrics.totals.out += record.out;
    metrics.totals.cacheWrite += record.cacheWrite;
    metrics.totals.cacheRead += record.cacheRead;
    metrics.totals.think += record.think ?? 0;
    metrics.totals.ms += record.ms;
    
    // Only count messages that actually consumed tokens
    if (record.in + record.out + record.cacheRead + record.cacheWrite + (record.think ?? 0) > 0) {
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
          think: 0,
          out: 0,
          sessionName: record.sessionName || record.session,
          route: record.route,
          models: new Set(),
          tools: 0,
          isToolHeavy: false,
          prompts: [],
        };
      }
      const sess = metrics.sessions[record.session];
      sess.turnCount = Math.max(sess.turnCount, record.turn);
      sess.in += record.in;
      sess.cacheRead += record.cacheRead;
      sess.cacheWrite += record.cacheWrite;
      sess.think += record.think ?? 0;
      sess.out += record.out;
      if (record.sessionName) sess.sessionName = record.sessionName;
      sess.models.add(record.model);
      sess.tools = Math.max(sess.tools, record.tools);

      // Each log line is exactly one prompt captured by the API gateway
      sess.prompts.push({
        prompt: record.prompt || '',
        ts: record.ts,
        firstTs: record.ts,
        turn: record.turn,
        model: record.model || 'Unknown',
        in: record.in,
        cacheRead: record.cacheRead,
        cacheWrite: record.cacheWrite,
        think: record.think ?? 0,
        out: record.out,
        total: record.in + record.out + record.cacheRead + record.cacheWrite + (record.think ?? 0),
        tools: record.tools || 0,
        hasToolCall: false,
        trace: record.trace,
        traces: record.trace ? [record.trace] : []
      });
    }

    const day = record.ts.split('T')[0];
    if (!metrics.dailyUsage[day]) metrics.dailyUsage[day] = { in: 0, cacheRead: 0, cacheWrite: 0, think: 0, out: 0 };
    metrics.dailyUsage[day].in += record.in;
    metrics.dailyUsage[day].cacheRead += record.cacheRead;
    metrics.dailyUsage[day].cacheWrite += record.cacheWrite;
    metrics.dailyUsage[day].think += record.think ?? 0;
    metrics.dailyUsage[day].out += record.out;

    const model = (record.model && record.model.trim() !== '') ? record.model : 'unknown';
    metrics.modelBreakdown[model] ??= { in: 0, cacheRead: 0, cacheWrite: 0, think: 0, out: 0 };
    metrics.modelBreakdown[model].in += record.in;
    metrics.modelBreakdown[model].cacheRead += record.cacheRead;
    metrics.modelBreakdown[model].cacheWrite += record.cacheWrite;
    metrics.modelBreakdown[model].think += record.think ?? 0;
    metrics.modelBreakdown[model].out += record.out;

    const route = record.route || 'unknown';
    if (!metrics.routeBreakdown[route]) metrics.routeBreakdown[route] = { in: 0, cacheRead: 0, cacheWrite: 0, think: 0, out: 0 };
    metrics.routeBreakdown[route].in += record.in;
    metrics.routeBreakdown[route].cacheRead += record.cacheRead;
    metrics.routeBreakdown[route].cacheWrite += record.cacheWrite;
    metrics.routeBreakdown[route].think += record.think ?? 0;
    metrics.routeBreakdown[route].out += record.out;
  }

  // Sort prompts by timestamp for all sessions, remove 0-token sessions, and fold identical consecutive prompts
  for (const [id, sess] of Object.entries(metrics.sessions)) {
    if (sess.in + sess.out + sess.think + sess.cacheRead + sess.cacheWrite === 0) {
      delete metrics.sessions[id];
      continue;
    }
    sess.prompts.sort((a, b) => new Date(a.ts).getTime() - new Date(b.ts).getTime());
    
    // A single session can have multiple concurrent conversation streams (e.g. main stream, summarizer subagents).
    // To correctly detect tool calls, we partition prompts into streams.
    // A stream is a chain of non-decreasing turns and non-decreasing tools.
    const streams: { lastTurn: number; lastTools: number }[] = [];
    
    for (const p of sess.prompts) {
      let bestStream: typeof streams[0] | null = null;
      for (const s of streams) {
        if (s.lastTurn <= p.turn && s.lastTools <= p.tools) {
          if (!bestStream || s.lastTools > bestStream.lastTools) {
            bestStream = s;
          }
        }
      }

      if (bestStream) {
        p.hasToolCall = p.tools > bestStream.lastTools;
        bestStream.lastTurn = p.turn;
        bestStream.lastTools = p.tools;
      } else {
        streams.push({ lastTurn: p.turn, lastTools: p.tools });
        p.hasToolCall = p.tools > 0;
      }
    }

    const foldedPrompts: typeof sess.prompts = [];
    let pre: typeof sess.prompts[0] | null = null;
    let preOrigDate = 0;
    
    for (const cur of sess.prompts) {
      if (!pre) {
        pre = { ...cur, traces: cur.traces ? [...cur.traces] : (cur.trace ? [cur.trace] : []) };
        preOrigDate = new Date(cur.ts).getTime();
        foldedPrompts.push(pre);
        continue;
      }
      
      const curDate = new Date(cur.ts).getTime();
      
      // Fold identical consecutive prompts regardless of turn boundaries, except empty prompts
      if (curDate >= preOrigDate && cur.prompt !== '' && cur.prompt === pre.prompt) {
        pre.in += cur.in;
        pre.out += cur.out;
        pre.think += cur.think;
        pre.cacheRead += cur.cacheRead;
        pre.cacheWrite += cur.cacheWrite;
        pre.total += cur.total;
        pre.tools = Math.max(pre.tools, cur.tools);
        pre.hasToolCall = pre.hasToolCall || cur.hasToolCall;
        if (cur.trace) {
          if (!pre.traces) pre.traces = pre.trace ? [pre.trace] : [];
          if (!pre.traces.includes(cur.trace)) {
            pre.traces.push(cur.trace);
          }
        }
        pre.trace = pre.trace || cur.trace;
        pre.ts = cur.ts;
        
        preOrigDate = curDate;
      } else {
        pre = { ...cur, traces: cur.traces ? [...cur.traces] : (cur.trace ? [cur.trace] : []) };
        preOrigDate = curDate;
        foldedPrompts.push(pre);
      }
    }
    sess.prompts = foldedPrompts.filter(p => p.total > 0);
    
    // Calculate if session is tool heavy
    let sessToolTokens = 0;
    let sessChatTokens = 0;
    for (const p of sess.prompts) {
      if (p.hasToolCall) {
        sessToolTokens += p.total;
      } else {
        sessChatTokens += p.total;
      }
    }
    sess.isToolHeavy = isToolHeavy(sessToolTokens, sessChatTokens, config);
  }

  metrics.topPrompts = Object.entries(metrics.sessions)
    .flatMap(([sessionId, sess]) => sess.prompts.map(p => ({
      prompt: p.prompt,
      in: p.in,
      cacheRead: p.cacheRead,
      cacheWrite: p.cacheWrite,
      think: p.think,
      out: p.out,
      totalTokens: p.total,
      session: sessionId,
      turn: p.turn,
      ts: p.ts,
      model: p.model,
      hasToolCall: p.hasToolCall,
      trace: p.trace,
      traces: p.traces
    })))
    .sort((a, b) => b.totalTokens - a.totalTokens)
    .slice(0, 10);

  return metrics;
}

export interface InsightsConfig {
  vaguePromptLength: number;
  vagueMinInput: number;
  inputHeavyMinInput: number;
  inputHeavyMaxOutput: number;
  marathonMinTurns: number;
  toolHeavyPercent: number;
}

export const DEFAULT_INSIGHTS_CONFIG: InsightsConfig = {
  vaguePromptLength: 30,
  vagueMinInput: 1000,
  inputHeavyMinInput: 5000,
  inputHeavyMaxOutput: 100,
  marathonMinTurns: 15,
  toolHeavyPercent: 50,
};

export interface InsightsData {
  vaguePrompts: number;
  marathonSessions: number;
  inputHeavy: number;
  toolHeavy: number;
  globalToolPrompts: number;
  globalChatPrompts: number;
  avgToolTokens: number;
  avgChatTokens: number;
  avgVagueTokens: number;
  avgNonVagueTokens: number;
  avgMarathonPromptTokens: number;
  avgNonMarathonPromptTokens: number;
  avgInputHeavyTokens: number;
  avgNonInputHeavyTokens: number;
  modelDominance: { model: string; percent: number } | null;
}

export function computeInsights(records: SpendRecord[], metrics: MetricsData, config: InsightsConfig = DEFAULT_INSIGHTS_CONFIG, useWeights: boolean = false, weightsConfig: Record<string, TokenWeights> = {}): InsightsData {
  let vaguePrompts = 0;
  let vagueTokensSum = 0;
  let nonVaguePromptsCount = 0;
  let nonVagueTokensSum = 0;

  let inputHeavy = 0;
  let inputHeavyTokensSum = 0;
  let nonInputHeavyCount = 0;
  let nonInputHeavyTokensSum = 0;

  let toolHeavy = 0;

  const counted = records.filter(r => r.in + r.out + r.cacheRead + r.cacheWrite + (r.think ?? 0) > 0);

  for (const rawRecord of counted) {
    let record = applyWeights(rawRecord, useWeights, weightsConfig);

    const totalIn = record.in + record.cacheWrite + record.cacheRead;
    const total = totalIn + record.out + (record.think ?? 0);
    
    const isVague = record.prompt && record.prompt.length < config.vaguePromptLength && totalIn > config.vagueMinInput;
    if (isVague) {
      vaguePrompts++;
      vagueTokensSum += total;
    } else {
      nonVaguePromptsCount++;
      nonVagueTokensSum += total;
    }
    
    const isInputHeavy = totalIn > config.inputHeavyMinInput && (record.out + (record.think ?? 0)) < config.inputHeavyMaxOutput;
    if (isInputHeavy) {
      inputHeavy++;
      inputHeavyTokensSum += total;
    } else {
      nonInputHeavyCount++;
      nonInputHeavyTokensSum += total;
    }
  }

  let marathonSessions = 0;
  let marathonPromptsCount = 0;
  let marathonTokensSum = 0;
  let nonMarathonPromptsCount = 0;
  let nonMarathonTokensSum = 0;

  let globalToolTokens = 0;
  let globalToolPrompts = 0;
  let globalChatTokens = 0;
  let globalChatPrompts = 0;

  for (const sess of Object.values(metrics.sessions)) {
    const isMarathon = sess.turnCount > config.marathonMinTurns;
    if (isMarathon) marathonSessions++;
    
    let sessToolTokens = 0;
    let sessChatTokens = 0;
    
    for (const p of sess.prompts) {
      if (isMarathon) {
        marathonPromptsCount++;
        marathonTokensSum += p.total;
      } else {
        nonMarathonPromptsCount++;
        nonMarathonTokensSum += p.total;
      }

      if (p.hasToolCall) {
        sessToolTokens += p.total;
        globalToolTokens += p.total;
        globalToolPrompts++;
      } else {
        sessChatTokens += p.total;
        globalChatTokens += p.total;
        globalChatPrompts++;
      }
    }
    
    if (isToolHeavy(sessToolTokens, sessChatTokens, config)) {
      toolHeavy++;
    }
  }

  const avgToolTokens = globalToolPrompts > 0 ? Math.round(globalToolTokens / globalToolPrompts) : 0;
  const avgChatTokens = globalChatPrompts > 0 ? Math.round(globalChatTokens / globalChatPrompts) : 0;
  const avgVagueTokens = vaguePrompts > 0 ? Math.round(vagueTokensSum / vaguePrompts) : 0;
  const avgNonVagueTokens = nonVaguePromptsCount > 0 ? Math.round(nonVagueTokensSum / nonVaguePromptsCount) : 0;
  const avgInputHeavyTokens = inputHeavy > 0 ? Math.round(inputHeavyTokensSum / inputHeavy) : 0;
  const avgNonInputHeavyTokens = nonInputHeavyCount > 0 ? Math.round(nonInputHeavyTokensSum / nonInputHeavyCount) : 0;
  const avgMarathonPromptTokens = marathonPromptsCount > 0 ? Math.round(marathonTokensSum / marathonPromptsCount) : 0;
  const avgNonMarathonPromptTokens = nonMarathonPromptsCount > 0 ? Math.round(nonMarathonTokensSum / nonMarathonPromptsCount) : 0;

  let modelDominance: { model: string; percent: number } | null = null;
  const totalTokens = metrics.totals.in + metrics.totals.out + metrics.totals.cacheRead + metrics.totals.cacheWrite + metrics.totals.think;
  if (totalTokens > 0) {
    for (const [model, counts] of Object.entries(metrics.modelBreakdown)) {
      const pct = (counts.in + counts.out + counts.cacheRead + counts.cacheWrite + counts.think) / totalTokens;
      if (pct > 0.6) {
        modelDominance = { model, percent: Math.round(pct * 100) };
        break;
      }
    }
  }

  return {
    vaguePrompts,
    marathonSessions,
    inputHeavy,
    toolHeavy,
    globalToolPrompts,
    globalChatPrompts,
    avgToolTokens,
    avgChatTokens,
    avgVagueTokens,
    avgNonVagueTokens,
    avgMarathonPromptTokens,
    avgNonMarathonPromptTokens,
    avgInputHeavyTokens,
    avgNonInputHeavyTokens,
    modelDominance,
  };
}

export const evaluatePromptFlags = (promptStr: string, totalIn: number, out: number, turnCount: number, config: InsightsConfig) => {
  const isVague = promptStr.length > 0 && promptStr.length < config.vaguePromptLength && totalIn > config.vagueMinInput;
  const isInputHeavy = totalIn > config.inputHeavyMinInput && out < config.inputHeavyMaxOutput;
  const isMarathon = turnCount > config.marathonMinTurns;

  return { isVague, isInputHeavy, isMarathon };
};

export const isToolHeavy = (toolTokens: number, chatTokens: number, config: InsightsConfig) => {
  return toolTokens > 0 && toolTokens * 100 >= (toolTokens + chatTokens) * config.toolHeavyPercent;
};

export interface SMAPrompt {
  ts: string;
  in: number;
  out: number;
  think: number;
  cacheRead: number;
  cacheWrite: number;
  total: number;
}

export function calculateSMA(prompts: SMAPrompt[], intervalMinutes: number, period: number, explicitStartStr?: string, explicitEndStr?: string) {
  const intervalMs = intervalMinutes * 60 * 1000;
  const historyRequiredMs = (period - 1) * intervalMs;

  const allSorted = [...(prompts || [])].sort((a, b) => new Date(a.ts).getTime() - new Date(b.ts).getTime());
  
  let minTime = Date.now();
  if (explicitStartStr) {
    minTime = new Date(explicitStartStr).getTime() - historyRequiredMs;
  } else if (allSorted.length > 0) {
    minTime = new Date(allSorted[0].ts).getTime();
  }

  let maxTime = Date.now();
  if (explicitEndStr) {
    maxTime = new Date(explicitEndStr).getTime();
  } else if (allSorted.length > 0) {
    maxTime = explicitStartStr ? Math.max(Date.now(), new Date(allSorted[allSorted.length - 1].ts).getTime()) : new Date(allSorted[allSorted.length - 1].ts).getTime();
  }
  
  if (minTime >= maxTime || (allSorted.length === 0 && !explicitStartStr)) return { data: [], actualIntervalMinutes: intervalMinutes };
  
  let actualIntervalMs = intervalMs;
  let numBuckets = Math.max(1, Math.ceil((maxTime - minTime + 1) / actualIntervalMs));
  
  if (numBuckets > 5000) {
    actualIntervalMs = Math.ceil((maxTime - minTime + 1) / 5000);
    numBuckets = 5000;
  }
  
  const buckets = Array(numBuckets).fill(null).map((_, i) => ({
    time: minTime + i * actualIntervalMs,
    in: 0,
    cw: 0,
    cr: 0,
    think: 0,
    out: 0,
    total: 0
  }));
  
  for (const p of allSorted) {
    const pTime = new Date(p.ts).getTime();
    const bucketIdx = Math.floor((pTime - minTime) / actualIntervalMs);
    if (bucketIdx >= 0 && bucketIdx < numBuckets) {
      buckets[bucketIdx].in += p.in;
      buckets[bucketIdx].cw += p.cacheWrite;
      buckets[bucketIdx].cr += p.cacheRead;
      buckets[bucketIdx].think += p.think ?? 0;
      buckets[bucketIdx].out += p.out;
      buckets[bucketIdx].total += p.total;
    }
  }
  
  const smaData = [];
  for (let i = 0; i < buckets.length; i++) {
    if (i >= period - 1) {
      let sumIn = 0, sumCw = 0, sumCr = 0, sumThink = 0, sumOut = 0, sumTotal = 0;
      for (let j = 0; j < period; j++) {
        const b = buckets[i - j];
        sumIn += b.in;
        sumCw += b.cw;
        sumCr += b.cr;
        sumThink += b.think;
        sumOut += b.out;
        sumTotal += b.total;
      }
      smaData.push({
        time: buckets[i].time,
        In: Math.round(sumIn / period),
        CW: Math.round(sumCw / period),
        CR: Math.round(sumCr / period),
        Think: Math.round(sumThink / period),
        Out: Math.round(sumOut / period),
        Total: Math.round(sumTotal / period)
      });
    }
  }
  
  return { data: smaData, actualIntervalMinutes: actualIntervalMs / 60000 };
}
