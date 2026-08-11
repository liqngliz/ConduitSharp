import { useState, useEffect, useMemo, useRef, useCallback } from 'react';
import { Calendar, ChevronDown, RotateCcw } from 'lucide-react';
import { computeMetrics, computeInsights, DEFAULT_INSIGHTS_CONFIG, type SpendRecord, type InsightsConfig } from '../utils/parser';
import { Metrics } from './Metrics';
import { Insights } from './Insights';
import { ActiveFlow } from './ActiveFlow';
import { Charts } from './Charts';
import { SessionsTable } from './SessionsTable';
import { WeightsControl } from './WeightsControl';

const formatDateDisplay = (ds: string) => {
  if (!ds) return '';
  const [y, m, d] = ds.split('-');
  return `${d}.${m}.${y}`;
};

const safeJSONParse = (key: string, defaultVal: any) => {
  const saved = localStorage.getItem(key);
  if (!saved) return defaultVal;
  try {
    return JSON.parse(saved);
  } catch {
    console.error(`Corrupt localStorage for ${key}, falling back to default`);
    return defaultVal;
  }
};

const recordKey = (r: SpendRecord) => r.trace || `${r.ts}-${r.session}-${r.turn}`;

export const Dashboard: React.FC = () => {
  const [records, setRecords] = useState<SpendRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeRoute, setActiveRoute] = useState<string>('all');
  const seenIds = useRef<Set<string>>(new Set());
  const isMounted = useRef(true);
  
  useEffect(() => {
    isMounted.current = true;
    return () => { isMounted.current = false; };
  }, []);
  
  const [lookbackConfig, setLookbackConfig] = useState<{ days: number }>(() => 
    safeJSONParse('conduit_lookback_config', { days: 7 })
  );
  
  const [lookbackInputStr, setLookbackInputStr] = useState(lookbackConfig.days.toString());
  useEffect(() => {
    setLookbackInputStr(lookbackConfig.days.toString());
  }, [lookbackConfig.days]);

  const [dateConfig, setDateConfig] = useState(() => {
    const d = new Date();
    const end = d.toISOString().split('T')[0];
    const initialLookback = safeJSONParse('conduit_lookback_config', { days: 7 }).days;
    d.setDate(d.getDate() - initialLookback);
    const start = d.toISOString().split('T')[0];
    return { start, end, isDefault: true };
  });

  useEffect(() => {
    localStorage.setItem('conduit_lookback_config', JSON.stringify(lookbackConfig));
  }, [lookbackConfig]);

  const [showDatePicker, setShowDatePicker] = useState(false);
  const datePickerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (datePickerRef.current && !datePickerRef.current.contains(e.target as Node)) {
        setShowDatePicker(false);
      }
    };
    if (showDatePicker) document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [showDatePicker]);

  useEffect(() => {
    if (!dateConfig.isDefault) return;
    const interval = setInterval(() => {
      const today = new Date().toISOString().split('T')[0];
      if (today !== dateConfig.end) {
        const d = new Date();
        d.setDate(d.getDate() - lookbackConfig.days);
        setDateConfig({ start: d.toISOString().split('T')[0], end: today, isDefault: true });
      }
    }, 60000);
    return () => clearInterval(interval);
  }, [dateConfig.isDefault, dateConfig.end, lookbackConfig.days]);
  const [weightsConfig, setWeightsConfig] = useState<Record<string, any>>(() => safeJSONParse('conduit_weights_config', {}));
  const [useWeights, setUseWeights] = useState<boolean>(() => safeJSONParse('conduit_use_weights', true));
  const [insightsConfig, setInsightsConfig] = useState<InsightsConfig>(() => ({ ...DEFAULT_INSIGHTS_CONFIG, ...safeJSONParse('conduit_insights_config', {}) }));
  const [smaConfig, setSmaConfig] = useState<{intervalMinutes: number, smaPeriod: number}>(() => safeJSONParse('conduit_sma_config', { intervalMinutes: 1, smaPeriod: 5 }));

  useEffect(() => {
    localStorage.setItem('conduit_weights_config', JSON.stringify(weightsConfig));
  }, [weightsConfig]);

  useEffect(() => {
    localStorage.setItem('conduit_use_weights', JSON.stringify(useWeights));
  }, [useWeights]);

  useEffect(() => {
    const cleanConfig = { ...insightsConfig };
    for (const k of Object.keys(cleanConfig) as (keyof InsightsConfig)[]) {
       if ((cleanConfig[k] as any) === '' || isNaN(Number(cleanConfig[k]))) {
           cleanConfig[k] = DEFAULT_INSIGHTS_CONFIG[k] as never;
       }
    }
    localStorage.setItem('conduit_insights_config', JSON.stringify(cleanConfig));
  }, [insightsConfig]);

  useEffect(() => {
    localStorage.setItem('conduit_sma_config', JSON.stringify(smaConfig));
  }, [smaConfig]);
  const [focusedSession, setFocusedSession] = useState<string | null>(null);

  const clearFocus = useCallback(() => setFocusedSession(null), []);

  useEffect(() => {
    setLoading(true);
    fetch('/api/spend')
      .then(res => {
        if (!res.ok) throw new Error('Failed to fetch dates');
        return res.json();
      })
      .then((dates: string[]) => {
        const targetDates = dates.filter(d => d >= dateConfig.start && d <= dateConfig.end);
        return Promise.all(
          targetDates.map(date => fetch(`/api/spend/${date}`).then(r => r.json()))
        );
      })
      .then((results: SpendRecord[][]) => {
        const parsed = results.flat();
        
        const newSet = new Set<string>();
        parsed.forEach(r => newSet.add(recordKey(r)));
        newSet.forEach(k => seenIds.current.add(k));
        
        setRecords(prev => {
          const map = new Map<string, SpendRecord>();
          prev.forEach(r => map.set(recordKey(r), r));
          parsed.forEach(r => map.set(recordKey(r), r));
          
          return Array.from(map.values());
        });
        
        setLoading(false);
      })
      .catch(err => {
        console.error(err);
        setError('Could not load logs from /api/spend.');
        setLoading(false);
      });
  }, [dateConfig.start, dateConfig.end]);

  useEffect(() => {
    const eventSource = new EventSource('/api/spend/stream');
    
    let buffer: SpendRecord[] = [];
    let flushTimeout: ReturnType<typeof setTimeout> | null = null;
    
    const flushBuffer = () => {
      if (buffer.length === 0) return;
      
      const newRecords = [...buffer];
      buffer = []; // clear early
      
      setRecords(prev => {
        const have = new Set(prev.map(r => recordKey(r)));
        return [...prev, ...newRecords.filter(r => !have.has(recordKey(r)))];
      });
      
      flushTimeout = null;
    };

    eventSource.onmessage = (event) => {
      try {
        const record = JSON.parse(event.data);
        const recordDate = record.ts.split('T')[0];
        if (recordDate < dateConfig.start || recordDate > dateConfig.end) return;

        const key = recordKey(record);
        if (seenIds.current.has(key)) return;
        seenIds.current.add(key);
        
        buffer.push(record);
        if (!flushTimeout) {
          flushTimeout = setTimeout(flushBuffer, 250);
        }
      } catch (err) {
        console.error('Failed to parse incoming SSE record', err, event.data);
      }
    };

    eventSource.onerror = (err) => {
      console.error('SSE connection error:', err);
    };

    return () => {
      eventSource.close();
      if (flushTimeout) clearTimeout(flushTimeout);
      if (isMounted.current && buffer.length > 0) flushBuffer();
    };
  }, [dateConfig.start, dateConfig.end]);



  const availableRoutes = useMemo(() => {
    const datesFiltered = records.filter(r => {
      const date = r.ts.split('T')[0];
      return date >= dateConfig.start && date <= dateConfig.end;
    });
    return Array.from(new Set(datesFiltered.map(r => r.route || 'unknown')));
  }, [records, dateConfig.start, dateConfig.end]);

  const filteredRecords = useMemo(() => {
    return records.filter(r => {
      const date = r.ts.split('T')[0];
      const matchDate = date >= dateConfig.start && date <= dateConfig.end;
      const matchRoute = activeRoute === 'all' || (r.route || 'unknown') === activeRoute;
      return matchDate && matchRoute;
    });
  }, [records, activeRoute, dateConfig.start, dateConfig.end]);

  const metrics = useMemo(() => computeMetrics(filteredRecords, insightsConfig, useWeights, weightsConfig), [filteredRecords, insightsConfig, useWeights, weightsConfig]);
  const insights = useMemo(() => computeInsights(filteredRecords, metrics, insightsConfig, useWeights, weightsConfig), [filteredRecords, metrics, insightsConfig, useWeights, weightsConfig]);

  if (loading) return <div className="p-8 text-center animate-pulse">Loading logs...</div>;
  if (error) return <div className="p-8 text-center text-danger">{error}</div>;

  return (
    <div className="max-w-7xl mx-auto p-4 md:p-8">
      <header className="flex flex-col items-center justify-center mb-8 text-center">
        <h1 className="text-5xl font-extrabold glow-text tracking-tighter mb-4">
          Your {activeRoute === 'all' ? 'AI' : activeRoute} tokens visualized.
        </h1>
        <p className="text-gray-300 max-w-2xl text-lg font-medium mb-8">See under the hood where your tokens go!</p>
        <div className="flex flex-wrap justify-center gap-4">
          <button
            onClick={() => setActiveRoute('all')}
            className={`px-5 py-2 rounded-full text-sm font-semibold transition-all ${
              activeRoute === 'all' 
                ? 'bg-blue-600 text-white shadow-[0_0_15px_rgba(37,99,235,0.5)]' 
                : 'bg-white/5 text-gray-400 hover:bg-white/10 hover:text-gray-200'
            }`}
          >
            All Agents
          </button>
          {availableRoutes.map(route => (
            <button
              key={route}
              onClick={() => setActiveRoute(route)}
              className={`px-5 py-2 rounded-full text-sm font-semibold transition-all ${
                activeRoute === route 
                  ? 'bg-blue-600 text-white shadow-[0_0_15px_rgba(37,99,235,0.5)]' 
                  : 'bg-white/5 text-gray-400 hover:bg-white/10 hover:text-gray-200'
              }`}
            >
              {route}
            </button>
          ))}
        </div>

        <div className="flex flex-wrap items-center justify-center mt-[10px]">
          <div className="relative" ref={datePickerRef}>
            <button 
              onClick={() => setShowDatePicker(!showDatePicker)}
              className="flex items-center gap-2 bg-surface/50 px-4 py-2 rounded-xl backdrop-blur border border-white/10 text-gray-300 hover:text-white transition-colors text-sm font-medium focus:outline-none"
            >
              <Calendar size={16} className="text-blue-500" />
              {formatDateDisplay(dateConfig.start)} - {formatDateDisplay(dateConfig.end)}
              <ChevronDown size={14} className="ml-1 text-gray-500" />
            </button>
            
            {showDatePicker && (
              <div className="absolute top-full mt-2 left-1/2 -translate-x-1/2 bg-[#1f2937] border border-white/10 rounded-xl shadow-2xl p-4 w-72 z-50 animate-in fade-in zoom-in-95 duration-200">
                <div className="flex flex-col gap-4">
                  <div className="flex flex-col gap-1">
                    <label className="text-xs text-gray-400 font-semibold uppercase tracking-wider">Start Date</label>
                    <input 
                      type="date" 
                      max={dateConfig.end}
                      value={dateConfig.start} 
                      onChange={(e) => setDateConfig({ ...dateConfig, start: e.target.value, isDefault: false })}
                      onClick={(e) => 'showPicker' in e.target && (e.target as HTMLInputElement).showPicker()}
                      className="bg-black/40 border border-white/10 rounded px-3 py-2 text-sm focus:border-blue-500 focus:outline-none transition-colors text-gray-200 w-full cursor-pointer"
                    />
                  </div>
                  <div className="flex flex-col gap-1">
                    <label className="text-xs text-gray-400 font-semibold uppercase tracking-wider">End Date</label>
                    <input 
                      type="date" 
                      min={dateConfig.start}
                      value={dateConfig.end} 
                      onChange={(e) => setDateConfig({ ...dateConfig, end: e.target.value, isDefault: false })}
                      onClick={(e) => 'showPicker' in e.target && (e.target as HTMLInputElement).showPicker()}
                      className="bg-black/40 border border-white/10 rounded px-3 py-2 text-sm focus:border-blue-500 focus:outline-none transition-colors text-gray-200 w-full cursor-pointer"
                    />
                  </div>
                  
                  <div className="pt-2 mt-2 border-t border-white/10">
                    <div className="flex flex-col gap-1 mb-4">
                      <div className="flex items-center justify-between">
                        <label className="text-xs text-gray-400 font-semibold uppercase tracking-wider">Default Lookback (Days)</label>
                        <button 
                          title="Reset to factory default (7 days)"
                          onClick={() => {
                            setLookbackConfig({ days: 7 });
                            const d = new Date();
                            const end = d.toISOString().split('T')[0];
                            d.setDate(d.getDate() - 7);
                            setDateConfig({ start: d.toISOString().split('T')[0], end, isDefault: true });
                          }}
                          className="text-gray-500 hover:text-white transition-colors"
                        >
                          <RotateCcw size={12} />
                        </button>
                      </div>
                      <input 
                        type="number" 
                        min="0"
                        value={lookbackInputStr} 
                        onChange={(e) => setLookbackInputStr(e.target.value)}
                        onBlur={() => {
                          const parsed = parseInt(lookbackInputStr);
                          const val = isNaN(parsed) ? 0 : Math.max(0, parsed);
                          setLookbackInputStr(val.toString());
                          if (val !== lookbackConfig.days) {
                            setLookbackConfig({ days: val });
                            const d = new Date();
                            const end = d.toISOString().split('T')[0];
                            d.setDate(d.getDate() - val);
                            setDateConfig({ start: d.toISOString().split('T')[0], end, isDefault: true });
                          }
                        }}
                        onKeyDown={(e) => {
                          if (e.key === 'Enter') e.currentTarget.blur();
                        }}
                        className="bg-black/40 border border-white/10 rounded px-3 py-2 text-sm focus:border-blue-500 focus:outline-none transition-colors text-gray-200 w-full"
                      />
                    </div>

                    <button 
                      onClick={() => {
                        const d = new Date();
                        const end = d.toISOString().split('T')[0];
                        d.setDate(d.getDate() - lookbackConfig.days);
                        const start = d.toISOString().split('T')[0];
                        setDateConfig({ start, end, isDefault: true });
                        setShowDatePicker(false);
                      }}
                      className="w-full flex items-center justify-center gap-2 py-2 rounded bg-white/5 hover:bg-white/10 text-gray-300 transition-colors text-sm font-medium"
                    >
                      <RotateCcw size={14} />
                      {lookbackConfig.days === 0 ? 'Set to just today' : `Set to last ${lookbackConfig.days} days`}
                    </button>
                  </div>
                </div>
              </div>
            )}
          </div>
        </div>
      </header>
      <Metrics metrics={metrics} routeName={activeRoute === 'all' ? 'All Agents' : activeRoute} />
      <ActiveFlow metrics={metrics} config={insightsConfig} onSessionSelect={setFocusedSession} />
      <Insights insights={insights} topPrompts={metrics.topPrompts} sessions={metrics.sessions} config={insightsConfig} setConfig={setInsightsConfig} onSessionSelect={setFocusedSession} />
      <Charts metrics={metrics} smaConfig={smaConfig} setSmaConfig={setSmaConfig} />
      <SessionsTable metrics={metrics} config={insightsConfig} focusedSession={focusedSession} onSessionClear={clearFocus} />
      <WeightsControl 
        models={Object.keys(metrics.modelBreakdown)}
        weightsConfig={weightsConfig}
        setWeightsConfig={setWeightsConfig}
        useWeights={useWeights}
        setUseWeights={setUseWeights}
      />
    </div>
  );
};
