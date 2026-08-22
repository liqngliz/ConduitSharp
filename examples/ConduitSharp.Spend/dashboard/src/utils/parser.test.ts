import { describe, it, expect } from 'vitest';
import { parseJsonl, computeMetrics, computeInsights, DEFAULT_INSIGHTS_CONFIG, type SpendRecord, evaluatePromptFlags, calculateSMA, isToolHeavy } from './parser';

describe('JSONL Parser', () => {
  it('parses valid JSONL correctly', () => {
    const jsonl = `{"ts":"2026-08-02T21:41:47","route":"local","model":"gpt-mini","servedModel":"gpt-mini","caller":"me","in":100,"out":20,"cacheWrite":0,"cacheRead":0,"session":"sess1","turn":1,"tools":0,"ms":500,"streamed":true,"prompt":"hi"}
{"ts":"2026-08-02T21:42:00","route":"local","model":"gpt-mini","servedModel":"gpt-mini","caller":"me","in":120,"out":40,"cacheWrite":0,"cacheRead":0,"session":"sess1","turn":2,"tools":0,"ms":600,"streamed":true,"prompt":"hello again"}`;
    const result = parseJsonl(jsonl);
    expect(result).toHaveLength(2);
    expect(result[0].session).toBe('sess1');
  });

  it('skips invalid JSON lines', () => {
    const jsonl = `{"ts":"2026"
invalid line
{"ts":"2026","route":"local","model":"m","servedModel":"m","caller":"c","in":10,"out":10,"cacheWrite":0,"cacheRead":0,"session":"s","turn":1,"tools":0,"ms":100,"streamed":false}`;
    const result = parseJsonl(jsonl);
    expect(result).toHaveLength(1);
    expect(result[0].session).toBe('s');
  });
});

describe('Metrics Computation', () => {
  const records: SpendRecord[] = [
    { ts: "2026-08-01T10:00:00Z", route: "codex", model: "modelA", servedModel: "modelA", caller: "a", in: 100, out: 50, cacheWrite: 10, cacheRead: 5, session: "sess1", turn: 1, tools: 1, ms: 1000, streamed: true, prompt: "test prompt" },
    { ts: "2026-08-01T10:05:00Z", route: "codex", model: "modelB", servedModel: "modelB", caller: "a", in: 200, out: 20, cacheWrite: 0, cacheRead: 0, session: "sess1", turn: 2, tools: 0, ms: 1500, streamed: true, prompt: "test prompt 2" },
    { ts: "2026-08-02T10:00:00Z", route: "", model: "modelC", servedModel: "modelC", caller: "b", in: 10, out: 0, cacheWrite: 0, cacheRead: 0, session: "sess2", sessionName: "Cool Session", turn: 0, tools: 0, ms: 2000, streamed: false, prompt: "another prompt" }
  ];

  it('computes totals correctly', () => {
    const metrics = computeMetrics(records);
    expect(metrics.totals.in).toBe(310);
    expect(metrics.totals.out).toBe(70);
    expect(metrics.totals.messagesSent).toBe(3);

    // route breakdown empty string falls back to 'unknown'
    expect(metrics.routeBreakdown['unknown']).toBeDefined();
    expect(metrics.routeBreakdown['unknown'].in).toBe(10);
    expect(metrics.routeBreakdown['unknown'].cacheRead).toBe(0);
    expect(metrics.routeBreakdown['unknown'].cacheWrite).toBe(0);
    expect(metrics.totals.cacheRead).toBe(5);
    expect(metrics.totals.ms).toBe(4500);
  });

  it('aggregates sessions correctly', () => {
    const metrics = computeMetrics(records);
    expect(Object.keys(metrics.sessions)).toHaveLength(2);
    expect(metrics.sessions['sess1'].turnCount).toBe(2);
    expect(metrics.sessions['sess1'].in).toBe(300);
    expect(metrics.sessions['sess2'].sessionName).toBe('Cool Session');
  });

  it('computes daily usage correctly', () => {
    const metrics = computeMetrics(records);
    expect(metrics.dailyUsage['2026-08-01'].in).toBe(300);
    expect(metrics.dailyUsage['2026-08-02'].in).toBe(10);
  });

  it('ranks top prompts correctly', () => {
    const metrics = computeMetrics(records);
    expect(metrics.topPrompts[0].prompt).toBe('test prompt 2');
    expect(metrics.topPrompts[0].totalTokens).toBe(220);
  });

  it('splits out and think tokens correctly (out = rawOut - rawThink)', () => {
    const thinkRecords: SpendRecord[] = [
      { ts: "2026-08-01T10:00:00Z", route: "claude", model: "claude-3-7-sonnet", servedModel: "claude-3-7-sonnet", caller: "a", in: 100, out: 500, think: 125, cacheWrite: 0, cacheRead: 0, session: "sess-think", turn: 1, tools: 0, ms: 1000, streamed: true, prompt: "solve this" }
    ];
    const metrics = computeMetrics(thinkRecords);
    expect(metrics.totals.out).toBe(375);
    expect(metrics.totals.think).toBe(125);
    expect(metrics.totals.out + metrics.totals.think).toBe(500);
  });
});

describe('Insights Computation', () => {
  it('detects vague and input heavy prompts', () => {
    const records: SpendRecord[] = [
      { ts: "2026-08-01T10:00:00Z", route: "local", model: "m", servedModel: "m", caller: "a", in: 6000, out: 50, cacheWrite: 0, cacheRead: 0, session: "sess1", turn: 1, tools: 0, ms: 1000, streamed: true, prompt: "short" }
    ];
    const metrics = computeMetrics(records);
    const insights = computeInsights(records, metrics);
    expect(insights.vaguePrompts).toBe(1); // Length 5 (< 30) and in 6000 (> 1000)
    expect(insights.inputHeavy).toBe(1); // in 6000 (> 5000), out 50 (< 100)
    expect(insights.modelDominance).toEqual({ model: "m", percent: 100 });
  });

  it('respects custom InsightsConfig thresholds', () => {
    const records: SpendRecord[] = [
      { ts: "2026-08-01T10:00:00Z", route: "local", model: "m", servedModel: "m", caller: "a", in: 6000, out: 50, cacheWrite: 0, cacheRead: 0, session: "sess1", turn: 16, tools: 0, ms: 1000, streamed: true, prompt: "short" }
    ];
    // With DEFAULT: vague = 1, inputHeavy = 1, marathon = 1
    const defaultMetrics = computeMetrics(records);
    const defaultInsights = computeInsights(records, defaultMetrics, DEFAULT_INSIGHTS_CONFIG);
    expect(defaultInsights.vaguePrompts).toBe(1);
    expect(defaultInsights.inputHeavy).toBe(1);
    expect(defaultInsights.marathonSessions).toBe(1);

    // Custom config that disables all three
    const customConfig = { ...DEFAULT_INSIGHTS_CONFIG, vaguePromptLength: 4, inputHeavyMinInput: 7000, marathonMinTurns: 20 };
    const customMetrics = computeMetrics(records, customConfig);
    const customInsights = computeInsights(records, customMetrics, customConfig);
    expect(customInsights.vaguePrompts).toBe(0);
    expect(customInsights.inputHeavy).toBe(0);
    expect(customInsights.marathonSessions).toBe(0);
  });

  it('detects model dominance', () => {
    const records: SpendRecord[] = [
      { ts: "2026-08-01T10:00:00Z", route: "local", model: "m", servedModel: "m", caller: "a", in: 1000, out: 0, cacheWrite: 0, cacheRead: 0, session: "sess1", turn: 1, tools: 0, ms: 100, streamed: true },
      { ts: '2026-08-03T10:00:00', route: '', model: 'claude-3', servedModel: 'claude-3', caller: 'x', in: 100, out: 0, cacheWrite: 0, cacheRead: 0, session: 's3', turn: 0, tools: 0, ms: 100, streamed: true }
    ];
    const metrics = computeMetrics(records);
    const insights = computeInsights(records, metrics);
    // m is ~91% of tokens
    expect(insights.modelDominance).toEqual({ model: "m", percent: 91 });
  });

  it('detects marathon sessions', () => {
    const records: SpendRecord[] = [
      { ts: "2026-08-01T10:00:00Z", route: "local", model: "m", servedModel: "m", caller: "a", in: 100, out: 50, cacheWrite: 0, cacheRead: 0, session: "sess1", turn: 16, tools: 0, ms: 1000, streamed: true },
      { ts: '2026-08-03T10:00:00', route: '', model: 'claude-3', servedModel: 'claude-3', caller: 'x', in: 0, out: 0, cacheWrite: 0, cacheRead: 0, session: 's3', turn: 0, tools: 0, ms: 100, streamed: true }
    ];
    const metrics = computeMetrics(records);
    const insights = computeInsights(records, metrics);
    expect(insights.marathonSessions).toBe(1); // Turn 16 > 15
  });

  it('detects tool heavy sessions based on token ratio and computes averages', () => {
    const records: SpendRecord[] = [
      // sess1: Tool Heavy (tool tokens > chat tokens)
      { ts: "2026-08-01T10:00:00Z", route: "local", model: "m", servedModel: "m", caller: "a", in: 50, out: 10, cacheWrite: 0, cacheRead: 0, session: "sess1", turn: 1, tools: 0, ms: 1000, streamed: true, prompt: "chat" }, // 60 total
      { ts: "2026-08-01T10:05:00Z", route: "local", model: "m", servedModel: "m", caller: "a", in: 100, out: 20, cacheWrite: 0, cacheRead: 0, session: "sess1", turn: 2, tools: 1, ms: 1000, streamed: true, prompt: "tool1" }, // 120 total, has tool
      
      // sess2: Not Tool Heavy (chat tokens > tool tokens)
      { ts: "2026-08-02T10:00:00Z", route: "local", model: "m", servedModel: "m", caller: "b", in: 200, out: 50, cacheWrite: 0, cacheRead: 0, session: "sess2", turn: 1, tools: 0, ms: 1000, streamed: true, prompt: "chat" }, // 250 total
      { ts: "2026-08-02T10:05:00Z", route: "local", model: "m", servedModel: "m", caller: "b", in: 10, out: 10, cacheWrite: 0, cacheRead: 0, session: "sess2", turn: 2, tools: 1, ms: 1000, streamed: true, prompt: "tool1" } // 20 total, has tool
    ];
    
    // We expect:
    // sess1 chat tokens = 60
    // sess1 tool tokens = 120 (ratio > 1, so toolHeavy++)
    // sess2 chat tokens = 250
    // sess2 tool tokens = 20 (ratio < 1)
    
    // avgToolTokens = (120 + 20) / 2 = 70
    // avgChatTokens = (60 + 250) / 2 = 155
    
    const metrics = computeMetrics(records);
    const insights = computeInsights(records, metrics);
    
    expect(insights.toolHeavy).toBe(1);
    expect(insights.avgToolTokens).toBe(70);
    expect(insights.avgChatTokens).toBe(155);
  });

  it('correctly parses and aggregates prompt details including tools', () => {
    const jsonl = `{"ts":"2026-08-02T10:47:28.2579274+00:00","route":"codex","model":"gpt-5.4-mini","servedModel":"","caller":"270e78987e088dad54ff89a7","in":100,"out":50,"cacheWrite":0,"cacheRead":0,"session":"6ec6632e83c51207","turn":6,"tools":14,"ms":12877,"streamed":false,"prompt":"hey, what are you good for and how much do tokens cost at free tier?\\n"}`;
    const records = parseJsonl(jsonl);
    const metrics = computeMetrics(records);
    
    const sess = metrics.sessions['6ec6632e83c51207'];
    expect(sess).toBeDefined();
    expect(sess.prompts).toHaveLength(1);
    
    const p = sess.prompts[0];
    expect(p.ts).toBe('2026-08-02T10:47:28.2579274+00:00');
    expect(p.prompt).toBe('hey, what are you good for and how much do tokens cost at free tier?\n');
    expect(p.model).toBe('gpt-5.4-mini');
    expect(p.in).toBe(100);
    expect(p.out).toBe(50);
    expect(p.cacheRead).toBe(0);
    expect(p.cacheWrite).toBe(0);
    expect(p.tools).toBe(14);
  });
});

describe('Token Weights', () => {
  const records: SpendRecord[] = [
    { ts: "2026-08-01T10:00:00Z", route: "local", model: "modelA", servedModel: "modelA", caller: "a", in: 100, out: 50, cacheWrite: 10, cacheRead: 5, session: "sess1", turn: 1, tools: 0, ms: 1000, streamed: true, prompt: "test" },
    { ts: "2026-08-01T10:05:00Z", route: "local", model: "modelB", servedModel: "modelB", caller: "a", in: 200, out: 20, cacheWrite: 0, cacheRead: 0, session: "sess1", turn: 2, tools: 0, ms: 1500, streamed: true, prompt: "test 2" }
  ];

  it('useWeights=false yields raw totals', () => {
    const rawMetrics = computeMetrics(records);
    const configuredMetrics = computeMetrics(records, DEFAULT_INSIGHTS_CONFIG, false, { "modelA": { in: 5, cw: 5, cr: 5, out: 5 } });
    
    expect(rawMetrics.totals).toEqual(configuredMetrics.totals);
  });

  it('useWeights=true with no config uses requested defaults', () => {
    const defaultMetrics = computeMetrics(records, DEFAULT_INSIGHTS_CONFIG, true, {});
    // records: 
    // modelA: 100 in, 50 out, 10 cw, 5 cr
    // modelB: 200 in, 20 out, 0 cw, 0 cr
    // in = 1 * 300 = 300
    // out = 5 * 70 = 350
    // cw = 1.25 * 10 = 12.5
    // cr = 0.1 * 5 = 0.5
    expect(defaultMetrics.totals.in).toBe(300);
    expect(defaultMetrics.totals.out).toBe(350);
    expect(defaultMetrics.totals.cacheWrite).toBe(12.5);
    expect(defaultMetrics.totals.cacheRead).toBe(0.5);
  });

  it('multiplies tokens by model-specific weights when useWeights=true', () => {
    const weightsConfig = {
      "modelA": { in: 2, cw: 1, cr: 1, out: 5 }, // 100*2=200 in, 50*5=250 out, 10 cw, 5 cr = 465
      "modelB": { in: 1, cw: 1, cr: 1, out: 1 }  // 200 in, 20 out = 220
    };
    
    const metrics = computeMetrics(records, DEFAULT_INSIGHTS_CONFIG, true, weightsConfig);
    
    // Total should be modelA + modelB = (200 + 200) = 400 in, (250 + 20) = 270 out
    expect(metrics.totals.in).toBe(400);
    expect(metrics.totals.out).toBe(270);
    expect(metrics.totals.cacheWrite).toBe(10);
    expect(metrics.totals.cacheRead).toBe(5);

    // Session total should reflect weighted sum
    expect(metrics.sessions["sess1"].in).toBe(400);
    expect(metrics.sessions["sess1"].out).toBe(270);
    expect(metrics.sessions["sess1"].prompts[0].total).toBe(465); // modelA total
  });
});
describe('evaluatePromptFlags', () => {
  it('identifies vague prompts based on config', () => {
    const flags = evaluatePromptFlags('short', 2000, 10, 1, DEFAULT_INSIGHTS_CONFIG);
    expect(flags.isVague).toBe(true); // length 5 < 30, in 2000 > 1000

    const customConfig = { ...DEFAULT_INSIGHTS_CONFIG, vaguePromptLength: 4 };
    const flagsCustom = evaluatePromptFlags('short', 2000, 10, 1, customConfig);
    expect(flagsCustom.isVague).toBe(false); // length 5 > 4
  });

  it('identifies input heavy prompts based on config', () => {
    const flags = evaluatePromptFlags('prompt', 6000, 50, 1, DEFAULT_INSIGHTS_CONFIG);
    expect(flags.isInputHeavy).toBe(true); // in 6000 > 5000, out 50 < 100
  });

  it('identifies marathon sessions based on config', () => {
    const flags = evaluatePromptFlags('prompt', 10, 10, 16, DEFAULT_INSIGHTS_CONFIG);
    expect(flags.isMarathon).toBe(true); // turn 16 > 15
  });
});

describe('isToolHeavy', () => {
  it('flags exactly at the threshold boundary as true', () => {
    // 100 tool, 100 chat -> 50% tool ratio. With config 50, it should be true.
    expect(isToolHeavy(100, 100, { ...DEFAULT_INSIGHTS_CONFIG, toolHeavyPercent: 50 })).toBe(true);
    // 99 tool, 101 chat -> < 50% tool ratio. Should be false.
    expect(isToolHeavy(99, 101, { ...DEFAULT_INSIGHTS_CONFIG, toolHeavyPercent: 50 })).toBe(false);
  });
});

describe('calculateSMA', () => {
  it('buckets and calculates simple moving average over periods', () => {
    const prompts = [
      { ts: "2026-08-01T10:00:00Z", in: 10, cacheWrite: 0, cacheRead: 0, out: 5, total: 15 },
      { ts: "2026-08-01T10:01:00Z", in: 20, cacheWrite: 0, cacheRead: 0, out: 5, total: 25 },
      { ts: "2026-08-01T10:02:00Z", in: 30, cacheWrite: 0, cacheRead: 0, out: 5, total: 35 }
    ] as any;
    
    // Interval 1 min, Period 2
    const { data: sma } = calculateSMA(prompts, 1, 2);
    
    // Buckets:
    // 10:00: in 10
    // 10:01: in 20
    // 10:02: in 30
    
    // For Period 2, output starts at index 1 (10:01)
    // 10:01 In: (10 + 20) / 2 = 15
    // 10:02 In: (20 + 30) / 2 = 25
    expect(sma).toHaveLength(2);
    expect(sma[0].In).toBe(15);
    expect(sma[1].In).toBe(25);
  });

  it('preserves first trace id when folding identical consecutive prompts', () => {
    const records: SpendRecord[] = [
      { ts: "2026-08-01T10:00:00Z", route: "claude", model: "m", servedModel: "m", caller: "a", in: 10, out: 5, cacheWrite: 0, cacheRead: 0, session: "sess1", turn: 1, tools: 0, ms: 100, streamed: true, prompt: "repeated", trace: "trace-first" },
      { ts: "2026-08-01T10:01:00Z", route: "claude", model: "m", servedModel: "m", caller: "a", in: 20, out: 10, cacheWrite: 0, cacheRead: 0, session: "sess1", turn: 2, tools: 0, ms: 100, streamed: true, prompt: "repeated", trace: "trace-second" }
    ];

    const metrics = computeMetrics(records);
    expect(metrics.sessions['sess1'].prompts).toHaveLength(1);
    expect(metrics.sessions['sess1'].prompts[0].trace).toBe('trace-first');
    expect(metrics.sessions['sess1'].prompts[0].traces).toEqual(['trace-first', 'trace-second']);
  });
});

describe('Defect fixes', () => {
  // #5 — an empty prompt is missing data, not evidence that two rows are the same prompt
  it('#5: empty prompts never fold together', () => {
    const records: SpendRecord[] = [
      // 0-token empty prompt should be discarded
      { ts: "2026-08-01T10:00:00Z", route: "codex", model: "", servedModel: "", caller: "a", in: 0, out: 0, cacheWrite: 0, cacheRead: 0, session: "sess1", turn: 1, tools: 0, ms: 100, streamed: true, prompt: "" },
      // two empty prompts in one session stay two rows
      { ts: "2026-08-01T10:01:00Z", route: "claude", model: "m", servedModel: "m", caller: "a", in: 100, out: 50, cacheWrite: 0, cacheRead: 0, session: "sess2", turn: 1, tools: 0, ms: 100, streamed: true, prompt: "" },
      { ts: "2026-08-01T10:02:00Z", route: "claude", model: "m", servedModel: "m", caller: "a", in: 200, out: 60, cacheWrite: 0, cacheRead: 0, session: "sess2", turn: 2, tools: 0, ms: 100, streamed: true, prompt: "" },
    ];
    const metrics = computeMetrics(records);
    // sess1 should be completely discarded or have 0 prompts
    if (metrics.sessions['sess1']) {
      expect(metrics.sessions['sess1'].prompts).toHaveLength(0);
    } else {
      expect(metrics.sessions['sess1']).toBeUndefined();
    }
    // sess2 keeps both rows: 100 in and 200 in are separate turns
    expect(metrics.sessions['sess2'].prompts).toHaveLength(2);
  });

  // #6 — records without a model still get weighted with DEFAULT_WEIGHTS
  it('#6: model-less records get DEFAULT_WEIGHTS when weighting is on', () => {
    const records: SpendRecord[] = [
      { ts: "2026-08-01T10:00:00Z", route: "claude", model: "", servedModel: "", caller: "a", in: 100, out: 50, cacheWrite: 0, cacheRead: 0, session: "sess1", turn: 1, tools: 0, ms: 100, streamed: true, prompt: "test" },
    ];
    // DEFAULT_WEIGHTS: in=1, cw=1.25, cr=0.1, out=5
    const metrics = computeMetrics(records, DEFAULT_INSIGHTS_CONFIG, true, {});
    expect(metrics.totals.in).toBe(100);   // 100 * 1
    expect(metrics.totals.out).toBe(250);  // 50 * 5
  });

  // #7 + #13 — every record reaches modelBreakdown, so it sums to metrics.totals
  it('#7+#13: blank model lands in modelBreakdown unconditionally', () => {
    const records: SpendRecord[] = [
      // Valid unknown model (prompt not empty, tokens > 0)
      { ts: "2026-08-01T10:00:00Z", route: "local", model: "", servedModel: "", caller: "b", in: 100, out: 50, cacheWrite: 0, cacheRead: 0, session: "sess1", turn: 1, tools: 0, ms: 100, streamed: true, prompt: "test" },
      // Empty prompt unknown model (prompt is empty, tokens > 0)
      { ts: "2026-08-01T10:01:00Z", route: "local", model: "", servedModel: "", caller: "c", in: 50, out: 10, cacheWrite: 0, cacheRead: 0, session: "sess2", turn: 1, tools: 0, ms: 100, streamed: true, prompt: "" },
    ];
    const metrics = computeMetrics(records);

    // "unknown" must exist in modelBreakdown
    expect(metrics.modelBreakdown['unknown']).toBeDefined();
    
    // both records, empty prompt or not
    expect(metrics.modelBreakdown['unknown'].in).toBe(150);
    // and the breakdown sums to the totals the headline shows
    const sum = Object.values(metrics.modelBreakdown).reduce((a, v) => a + v.in + v.out + v.cacheRead + v.cacheWrite + v.think, 0);
    const totals = metrics.totals;
    expect(sum).toBe(totals.in + totals.out + totals.cacheRead + totals.cacheWrite + totals.think);
  });

  // #10 — evaluatePromptFlags must not flag empty prompts as vague
  it('#10: empty prompt is not flagged vague by evaluatePromptFlags', () => {
    const flags = evaluatePromptFlags('', 5000, 100, 1, DEFAULT_INSIGHTS_CONFIG);
    expect(flags.isVague).toBe(false);
  });

  // #11 — zero-token records must not dilute insight averages
  it('#11: zero-token records are excluded from insight averages', () => {
    const records: SpendRecord[] = [
      { ts: "2026-08-01T10:00:00Z", route: "claude", model: "m", servedModel: "m", caller: "a", in: 100, out: 50, cacheWrite: 0, cacheRead: 0, session: "sess1", turn: 1, tools: 0, ms: 100, streamed: true, prompt: "real prompt" },
      // zero-token poll record
      { ts: "2026-08-01T10:01:00Z", route: "codex", model: "m", servedModel: "m", caller: "a", in: 0, out: 0, cacheWrite: 0, cacheRead: 0, session: "sess2", turn: 0, tools: 0, ms: 50, streamed: false, prompt: "" },
    ];
    const metrics = computeMetrics(records);
    const insights = computeInsights(records, metrics);

    // Only the first record has tokens; the zero-token record must not inflate the count
    // avgNonVagueTokens should be 150 (one record: 100 in + 50 out), not 75 (150 / 2)
    expect(insights.avgNonVagueTokens).toBe(150);
  });

  // #12 — calculateSMA returns actual interval when bucket count is capped
  it('#12: calculateSMA returns actualIntervalMinutes reflecting the 5000-bucket cap', () => {
    // Create a range that would exceed 5000 one-minute buckets
    // 10 days = 14400 minutes >> 5000
    const prompts = [
      { ts: "2026-08-01T00:00:00Z", in: 10, out: 5, think: 0, cacheRead: 0, cacheWrite: 0, total: 15 },
      { ts: "2026-08-10T23:59:00Z", in: 20, out: 10, think: 0, cacheRead: 0, cacheWrite: 0, total: 30 },
    ] as any;

    const { data, actualIntervalMinutes } = calculateSMA(prompts, 1, 2);
    expect(data.length).toBeLessThanOrEqual(5000);
    expect(actualIntervalMinutes).toBeGreaterThan(1);
  });

  // #12 — SMA returns the user-set interval when no capping occurs
  it('#12: calculateSMA returns user-set interval when under 5000 buckets', () => {
    const prompts = [
      { ts: "2026-08-01T10:00:00Z", in: 10, out: 5, think: 0, cacheRead: 0, cacheWrite: 0, total: 15 },
      { ts: "2026-08-01T10:02:00Z", in: 20, out: 10, think: 0, cacheRead: 0, cacheWrite: 0, total: 30 },
    ] as any;

    const { actualIntervalMinutes } = calculateSMA(prompts, 1, 2);
    expect(actualIntervalMinutes).toBe(1);
  });
});
