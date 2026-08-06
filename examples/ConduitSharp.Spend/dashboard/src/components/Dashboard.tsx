import { useState, useEffect, useMemo, useRef, useCallback } from 'react';
import { computeMetrics, computeInsights, type SpendRecord } from '../utils/parser';
import { Metrics } from './Metrics';
import { Insights } from './Insights';
import { ActiveFlow } from './ActiveFlow';
import { Charts } from './Charts';
import { SessionsTable } from './SessionsTable';
import { WeightsControl } from './WeightsControl';

export const Dashboard: React.FC = () => {
  const [records, setRecords] = useState<SpendRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeRoute, setActiveRoute] = useState<string>('all');
  const [routes, setRoutes] = useState<string[]>([]);
  const seenIds = useRef<Set<string>>(new Set());
  const [weightsConfig, setWeightsConfig] = useState<Record<string, any>>({});
  const [useWeights, setUseWeights] = useState<boolean>(true);
  const [focusedSession, setFocusedSession] = useState<string | null>(null);

  const clearFocus = useCallback(() => setFocusedSession(null), []);

  const [startDate, setStartDate] = useState<string>(() => {
    const d = new Date();
    d.setDate(d.getDate() - 7);
    return d.toISOString().split('T')[0];
  });
  const [endDate, setEndDate] = useState<string>(() => {
    const d = new Date();
    return d.toISOString().split('T')[0];
  });

  useEffect(() => {
    fetch('/api/spend')
      .then(res => {
        if (!res.ok) throw new Error('Failed to fetch dates');
        return res.json();
      })
      .then((dates: string[]) => {
        return Promise.all(
          dates.map(date => fetch(`/api/spend/${date}`).then(r => r.json()))
        );
      })
      .then((results: SpendRecord[][]) => {
        const parsed = results.flat();
        
        const newSet = new Set<string>();
        parsed.forEach(r => newSet.add(`${r.ts}-${r.session}-${r.turn}`));
        
        setRecords(prev => {
          const merged = [...parsed, ...prev.filter(r => !newSet.has(`${r.ts}-${r.session}-${r.turn}`))];
          // Merging into newSet which is mutated by reference in the next line, but it's safe 
          // because we immediately add all elements from newSet into seenIds.current
          merged.forEach(r => newSet.add(`${r.ts}-${r.session}-${r.turn}`));
          return merged;
        });
        newSet.forEach(k => seenIds.current.add(k));

        setRoutes(prev => {
          const newRoutes = parsed.map(r => r.route || 'unknown');
          return Array.from(new Set([...prev, ...newRoutes]));
        });
        
        setLoading(false);
      })
      .catch(err => {
        console.error(err);
        setError('Could not load logs from /api/spend.');
        setLoading(false);
      });
  }, []);

  useEffect(() => {
    const eventSource = new EventSource('/api/spend/stream');
    
    let buffer: SpendRecord[] = [];
    let flushTimeout: ReturnType<typeof setTimeout> | null = null;
    
    const flushBuffer = () => {
      if (buffer.length === 0) return;
      
      const newRecords = [...buffer];
      buffer = []; // clear early
      
      setRecords(prev => [...prev, ...newRecords]);
      
      const routesToAdd = Array.from(new Set(newRecords.map(r => r.route || 'unknown')));
      setRoutes(prev => {
        const missing = routesToAdd.filter(r => !prev.includes(r));
        return missing.length > 0 ? [...prev, ...missing] : prev;
      });
      
      flushTimeout = null;
    };

    eventSource.onmessage = (event) => {
      try {
        const record = JSON.parse(event.data);
        const key = `${record.ts}-${record.session}-${record.turn}`;
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
      if (flushTimeout) {
        clearTimeout(flushTimeout);
        flushBuffer();
      }
      eventSource.close();
    };
  }, []);

  const filteredRecords = useMemo(() => {
    return records.filter(r => {
      const date = r.ts.split('T')[0];
      const matchDate = date >= startDate && date <= endDate;
      const matchRoute = activeRoute === 'all' || (r.route || 'unknown') === activeRoute;
      return matchDate && matchRoute;
    });
  }, [records, activeRoute, startDate, endDate]);

  const metrics = useMemo(() => computeMetrics(filteredRecords, useWeights, weightsConfig), [filteredRecords, useWeights, weightsConfig]);
  const insights = useMemo(() => computeInsights(filteredRecords, metrics, useWeights, weightsConfig), [filteredRecords, metrics, useWeights, weightsConfig]);

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
          {routes.map(route => (
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
          <div className="flex items-center bg-surface/50 p-1 rounded-xl backdrop-blur border border-white/10 scale-[0.8] origin-top">
            <div className="flex items-center pl-2 pr-1">
              <input 
                type="date" 
                id="startDate"
                max={endDate}
                value={startDate} 
                onChange={(e) => setStartDate(e.target.value)}
                onClick={(e) => 'showPicker' in e.target && (e.target as HTMLInputElement).showPicker()}
                className="bg-transparent border-transparent rounded px-0 py-1.5 text-sm text-gray-400 hover:text-white transition-all font-medium focus:outline-none cursor-pointer custom-date-input w-[110px]"
                aria-label="Start Date"
              />
              <span className="text-gray-500 font-medium px-2">-</span>
              <input 
                type="date" 
                id="endDate"
                min={startDate}
                value={endDate} 
                onChange={(e) => setEndDate(e.target.value)}
                onClick={(e) => 'showPicker' in e.target && (e.target as HTMLInputElement).showPicker()}
                className="bg-transparent border-transparent rounded px-0 py-1.5 text-sm text-gray-400 hover:text-white transition-all font-medium focus:outline-none cursor-pointer custom-date-input w-[110px]"
                aria-label="End Date"
              />
            </div>
          </div>
        </div>
      </header>
      <Metrics metrics={metrics} routeName={activeRoute === 'all' ? 'All Agents' : activeRoute} />
      <ActiveFlow metrics={metrics} onSessionSelect={setFocusedSession} />
      <Insights insights={insights} topPrompts={metrics.topPrompts} sessions={metrics.sessions} onSessionSelect={setFocusedSession} />
      <Charts metrics={metrics} />
      <SessionsTable metrics={metrics} focusedSession={focusedSession} onSessionClear={clearFocus} />
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
