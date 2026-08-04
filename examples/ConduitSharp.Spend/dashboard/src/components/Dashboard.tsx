import { useState, useEffect, useMemo } from 'react';
import { computeMetrics, computeInsights, type SpendRecord } from '../utils/parser';
import { Metrics } from './Metrics';
import { Insights } from './Insights';
import { Charts } from './Charts';
import { SessionsTable } from './SessionsTable';

export const Dashboard: React.FC = () => {
  const [records, setRecords] = useState<SpendRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeRoute, setActiveRoute] = useState<string>('all');
  const [routes, setRoutes] = useState<string[]>([]);

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
        setRecords(parsed);
        const uniqueRoutes = Array.from(new Set(parsed.map(r => r.route || 'unknown')));
        setRoutes(uniqueRoutes);
        setLoading(false);
      })
      .catch(err => {
        console.error(err);
        setError('Could not load logs from /api/spend.');
        setLoading(false);
      });
  }, []);

  const filteredRecords = useMemo(() => {
    return records.filter(r => {
      const date = r.ts.split('T')[0];
      const matchDate = date >= startDate && date <= endDate;
      const matchRoute = activeRoute === 'all' || (r.route || 'unknown') === activeRoute;
      return matchDate && matchRoute;
    });
  }, [records, activeRoute, startDate, endDate]);

  const metrics = useMemo(() => computeMetrics(filteredRecords), [filteredRecords]);
  const insights = useMemo(() => computeInsights(filteredRecords, metrics), [filteredRecords, metrics]);

  if (loading) return <div className="p-8 text-center animate-pulse">Loading logs...</div>;
  if (error) return <div className="p-8 text-center text-danger">{error}</div>;

  return (
    <div className="max-w-7xl mx-auto p-4 md:p-8">
      <header className="flex flex-col items-center justify-center mb-8 text-center">
        <h1 className="text-4xl font-bold glow-text tracking-tight mb-2">
          Your {activeRoute === 'all' ? 'AI' : activeRoute} tokens visualized.
        </h1>
        <p className="text-gray-400 mb-6">See under the hood where your tokens go!</p>
        <div className="flex gap-2 bg-surface/50 p-1 rounded-xl backdrop-blur border border-white/10">
          <button
            onClick={() => setActiveRoute('all')}
            className={`px-4 py-2 rounded-lg transition-all font-medium text-sm ${activeRoute === 'all' ? 'bg-primary text-white shadow-lg' : 'text-gray-400 hover:text-white'}`}
          >
            All Agents
          </button>
          {routes.map(route => (
            <button
              key={route}
              onClick={() => setActiveRoute(route)}
              className={`px-4 py-2 rounded-lg transition-all font-medium text-sm ${activeRoute === route ? 'bg-primary text-white shadow-lg' : 'text-gray-400 hover:text-white'}`}
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
      <Insights insights={insights} />
      <Charts metrics={metrics} />
      <SessionsTable metrics={metrics} />
    </div>
  );
};
