import { describe, it, expect } from 'vitest';
import { parseJsonl, computeMetrics, computeInsights, type SpendRecord } from './parser';

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
    expect(insights.routeDominance).toEqual({ route: "local", percent: 100 });
  });

  it('detects marathon sessions and tool heavy sessions', () => {
    const records: SpendRecord[] = [
      { ts: "2026-08-01T10:00:00Z", route: "local", model: "m", servedModel: "m", caller: "a", in: 100, out: 50, cacheWrite: 0, cacheRead: 0, session: "sess1", turn: 16, tools: 50, ms: 1000, streamed: true },
      { ts: '2026-08-03T10:00:00', route: '', model: 'claude-3', servedModel: 'claude-3', caller: 'x', in: 0, out: 0, cacheWrite: 0, cacheRead: 0, session: 's3', turn: 0, tools: 0, ms: 100, streamed: true }
    ];
    const metrics = computeMetrics(records);
    const insights = computeInsights(records, metrics);
    expect(insights.marathonSessions).toBe(1); // Turn 16 > 15
    expect(insights.toolHeavy).toBe(1); // Tools 50 > 16 * 3
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
