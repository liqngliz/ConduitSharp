import React, { useState, useEffect, useMemo, useRef } from 'react';
import type { MetricsData, InsightsConfig } from '../utils/parser';
import { DEFAULT_INSIGHTS_CONFIG } from '../utils/parser';
import { findPrompt, extractUserPrompt, formatWireBody, type WireEntry, type PromptSelectionTarget, type FlatPromptRow } from '../utils/promptHelpers';
import { X, Wrench, Brain, SportShoe } from 'lucide-react';

const formatCompact = (num: number) => Intl.NumberFormat('en-US', { notation: 'compact', maximumSignificantDigits: 3 }).format(num);

export interface PromptPanelProps {
  selected: PromptSelectionTarget | null;
  sessions: MetricsData['sessions'];
  config?: InsightsConfig;
  onClose: () => void;
}

export const PromptPanel: React.FC<PromptPanelProps> = ({ selected, sessions, config, onClose }) => {
  const [activeTrace, setActiveTrace] = useState<string | null>(null);
  const [entries, setEntries] = useState<WireEntry[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const row = useMemo(() => {
    if (!selected) return null;
    return findPrompt(sessions, selected) || null;
  }, [selected, sessions]);

  const lastRowRef = useRef<FlatPromptRow | null>(null);
  if (row) {
    lastRowRef.current = row;
  }
  const displayRow = row || lastRowRef.current;

  // Sync active trace with selected prompt
  useEffect(() => {
    if (selected?.traceId) {
      setActiveTrace(selected.traceId);
    } else if (row) {
      setActiveTrace(row.traces[0] || row.trace || null);
    } else if (!selected) {
      // Don't clear activeTrace during close transition to keep wire log rendered
    } else {
      setActiveTrace(null);
    }
  }, [selected, row]);

  // Handle escape key
  useEffect(() => {
    if (!selected) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [selected, onClose]);

  // Fetch wire log when activeTrace changes
  useEffect(() => {
    if (!activeTrace) {
      setEntries(null);
      setError(null);
      setLoading(false);
      return;
    }
    let cancelled = false;
    setLoading(true);
    setError(null);
    setEntries(null);

    fetch(`/api/wire/${activeTrace}`)
      .then(async (res) => {
        if (cancelled) return;
        if (res.status === 404) {
          setError('Not in the wire log. Body capture may have been off for this call.');
          setLoading(false);
          return;
        }
        if (!res.ok) {
          setError(`Failed to fetch wire log (${res.status} ${res.statusText})`);
          setLoading(false);
          return;
        }
        const text = await res.text();
        if (cancelled) return;
        try {
          const data: WireEntry[] = JSON.parse(text);
          setEntries(data);
        } catch {
          setError('Wire log response was not valid JSON.');
        }
        setLoading(false);
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err.message || 'Error fetching wire log');
        setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [activeTrace]);

  const fullPrompt = useMemo(() => {
    if (!entries) return null;
    const req = entries.find(e => e.direction === 'request');
    if (!req) return null;
    return extractUserPrompt(req.body);
  }, [entries]);

  const isOpen = selected !== null;

  return (
    <div
      className={`fixed inset-y-0 right-0 z-50 w-full md:w-1/2 bg-surface/95 backdrop-blur-xl border-l border-white/10 shadow-2xl transition-transform duration-300 ease-out flex flex-col overflow-x-hidden ${
        isOpen ? 'translate-x-0' : 'translate-x-full pointer-events-none'
      }`}
    >
      {displayRow ? (
        <div className="flex-1 overflow-y-auto p-6 space-y-6">
          {/* Header */}
          <div className="flex items-center justify-between gap-3 border-b border-white/10 pb-4 min-w-0">
            <div className="space-y-1 min-w-0 flex-1">
              <div className="flex items-center gap-2 min-w-0">
                {displayRow.hasToolCall && <Wrench size={16} className="text-emerald-500 shrink-0" />}
                {displayRow.think > 0 && <Brain size={16} className="text-emerald-400 shrink-0" />}
                <h2 className="text-sm md:text-base font-mono font-bold text-white truncate min-w-0" title={displayRow.sessionId}>
                  {displayRow.sessionId}
                </h2>
                {displayRow.turnCount > (config?.marathonMinTurns ?? DEFAULT_INSIGHTS_CONFIG.marathonMinTurns) && (
                  <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-[10px] font-mono font-semibold bg-purple-950/50 text-purple-400 border border-purple-800/40 shrink-0" title={`Marathon Session (${displayRow.turnCount} turns)`}>
                    <SportShoe size={12} className="text-purple-400" />
                    <span>Marathon</span>
                  </span>
                )}
              </div>
              <div className="flex flex-wrap items-center gap-2 text-xs font-mono text-gray-400 min-w-0">
                <span className="text-gray-300 font-semibold truncate">{displayRow.model}</span>
                <span>•</span>
                <span className="whitespace-nowrap">{displayRow.ts ? new Date(displayRow.ts).toLocaleString() : 'Unknown time'}</span>
                {displayRow.turn > 0 && (
                  <>
                    <span>•</span>
                    <span className="whitespace-nowrap">Turn {displayRow.turn}</span>
                  </>
                )}
              </div>
            </div>
            <button
              type="button"
              onClick={onClose}
              className="p-2 text-gray-400 hover:text-white rounded-lg bg-white/5 hover:bg-white/10 border border-white/10 transition-all shrink-0 ml-2"
              aria-label="Close panel"
              title="Close panel (Esc)"
            >
              <X size={18} />
            </button>
          </div>

          {/* Metrics Grid */}
          <div className="grid grid-cols-3 sm:grid-cols-6 gap-2 text-xs font-mono bg-white/5 rounded-lg p-3 border border-white/5">
            <div>
              <span className="text-gray-400 block text-[10px] uppercase">Input</span>
              <span className="text-blue-500 font-bold">{formatCompact(displayRow.in)}</span>
            </div>
            <div>
              <span className="text-gray-400 block text-[10px] uppercase">Cache W</span>
              <span className="text-pink-500 font-bold">{formatCompact(displayRow.cacheWrite)}</span>
            </div>
            <div>
              <span className="text-gray-400 block text-[10px] uppercase">Cache R</span>
              <span className="text-purple-500 font-bold">{formatCompact(displayRow.cacheRead)}</span>
            </div>
            <div>
              <span className="text-gray-400 block text-[10px] uppercase">Think</span>
              <span className="text-emerald-400 font-bold">{formatCompact(displayRow.think)}</span>
            </div>
            <div>
              <span className="text-gray-400 block text-[10px] uppercase">Output</span>
              <span className="text-amber-500 font-bold">{formatCompact(displayRow.out)}</span>
            </div>
            <div>
              <span className="text-gray-400 block text-[10px] uppercase">Total</span>
              <span className="text-secondary font-bold">{formatCompact(displayRow.total)}</span>
            </div>
          </div>

          {/* Trace Chips */}
          {displayRow.traces.length > 1 && (
            <div className="space-y-2">
              <span className="text-xs text-gray-400 font-mono">Folded Traces ({displayRow.traces.length}):</span>
              <div className="flex flex-wrap gap-1.5 items-center">
                {displayRow.traces.map((t, i) => {
                  const isSelected = activeTrace === t;
                  return (
                    <button
                      key={t}
                      type="button"
                      onClick={() => setActiveTrace(t)}
                      className={`px-2.5 py-1 rounded text-xs font-mono transition-all ${
                        isSelected
                          ? 'bg-secondary text-black font-bold shadow-[0_0_10px_rgba(6,182,212,0.4)]'
                          : 'bg-white/5 text-gray-400 hover:bg-white/10 hover:text-gray-200 border border-white/10'
                      }`}
                    >
                      {i + 1}. {t.slice(0, 8)}
                    </button>
                  );
                })}
              </div>
            </div>
          )}

          {/* Full Prompt Text */}
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <h3 className="text-xs font-bold uppercase tracking-wider text-gray-400">User Prompt</h3>
              {!fullPrompt && <span className="text-[11px] text-gray-500 italic">(prefix only)</span>}
            </div>
            <pre className="whitespace-pre-wrap break-words max-h-64 overflow-auto bg-black/40 border border-white/5 rounded-lg p-3 text-xs font-mono text-gray-200">
              {fullPrompt || displayRow.prompt}
            </pre>
          </div>

          {/* Wire Log */}
          <div className="space-y-3">
            <div className="flex items-center justify-between">
              <h3 className="text-xs font-bold uppercase tracking-wider text-gray-400">Wire Log</h3>
              {activeTrace && <span className="text-[11px] font-mono text-gray-500">Trace: {activeTrace}</span>}
            </div>

            {loading && (
              <div className="p-4 text-xs font-mono text-gray-400 animate-pulse bg-white/5 rounded border border-white/5">
                Loading wire log for {activeTrace?.slice(0, 8)}...
              </div>
            )}

            {error && (
              <div className="p-4 text-xs font-mono text-amber-400/90 bg-amber-500/10 rounded border border-amber-500/20">
                {error}
              </div>
            )}

            {entries && entries.length > 0 && (
              <div className="space-y-4">
                {entries.map((entry, idx) => (
                  <div key={idx} className="space-y-1.5">
                    <div className="flex items-center justify-between text-xs font-mono text-gray-400 px-1">
                      <div className="flex items-center gap-2">
                        <span
                          className={`px-1.5 py-0.5 rounded text-[10px] font-bold uppercase ${
                            entry.direction === 'request'
                              ? 'bg-blue-500/20 text-blue-400'
                              : 'bg-emerald-500/20 text-emerald-400'
                          }`}
                        >
                          {entry.direction}
                        </span>
                        <span className="text-gray-300 truncate">{entry.path}</span>
                      </div>
                      <span className="text-gray-500 text-[11px]">{entry.body?.length || 0} chars</span>
                    </div>
                    <pre className="bg-black/40 border border-white/5 rounded p-3 text-[11px] font-mono max-h-96 overflow-auto whitespace-pre-wrap break-all">
                      {formatWireBody(entry.body)}
                    </pre>
                  </div>
                ))}
              </div>
            )}

            {!loading && !error && (!entries || entries.length === 0) && (
              <div className="p-4 text-xs font-mono text-gray-500 bg-white/5 rounded border border-white/5">
                {displayRow.traces.length === 0 ? 'No trace recorded for this call.' : 'No wire log entries found.'}
              </div>
            )}
          </div>
        </div>
      ) : selected ? (
        <div className="flex-1 overflow-y-auto p-6 space-y-6">
          <div className="flex items-center justify-between gap-3 border-b border-white/10 pb-4 min-w-0">
            <h2 className="text-sm md:text-base font-mono font-bold text-white truncate min-w-0">
              {selected.sessionId}
            </h2>
            <button
              type="button"
              onClick={onClose}
              className="p-2 text-gray-400 hover:text-white rounded-lg bg-white/5 hover:bg-white/10 border border-white/10 transition-all shrink-0 ml-2"
              aria-label="Close panel"
              title="Close panel (Esc)"
            >
              <X size={18} />
            </button>
          </div>
          <div className="p-8 text-center text-gray-400 font-mono text-sm">
            Prompt details not found for this selection.
          </div>
        </div>
      ) : null}
    </div>
  );
};
