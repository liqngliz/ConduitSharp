import React, { useMemo } from 'react';
import { calculateSMA, type MetricsData } from '../utils/parser';
import { LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer, Legend, Brush } from 'recharts';
import { Infinity as InfinityIcon, RotateCcw } from 'lucide-react';

const COLORS = ['#8b5cf6', '#3b82f6', '#10b981', '#f59e0b', '#ef4444', '#ec4899', '#6366f1'];
const formatCompact = (num: number) => Intl.NumberFormat('en-US', { notation: 'compact', maximumSignificantDigits: 3 }).format(num);

export function calculateBrushStartIndex(dataLength: number, startRatio: number): number {
  if (dataLength <= 0) return 0;
  const maxIdx = dataLength - 1;
  return Math.max(0, Math.min(maxIdx, Math.round(maxIdx * startRatio)));
}

export function calculateBrushStartRatio(startIndex: number, dataLength: number): number {
  if (dataLength <= 1) return 0;
  const maxIdx = dataLength - 1;
  return Math.max(0, Math.min(1, startIndex / maxIdx));
}

export const Charts: React.FC<{ 
  metrics: MetricsData; 
  smaConfig: { intervalMinutes: number; smaPeriod: number };
  setSmaConfig: (cfg: { intervalMinutes: number; smaPeriod: number }) => void;
}> = React.memo(({ metrics, smaConfig, setSmaConfig }) => {
  const [startDate, setStartDate] = React.useState(() => {
    const d = new Date();
    d.setHours(d.getHours() - 6);
    return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
  });
  const [brushStartRatio, setBrushStartRatio] = React.useState(0.75);

  const allPrompts = useMemo(() => {
    return Object.values(metrics.sessions).flatMap(s => s.prompts);
  }, [metrics]);
  
  const { data: smaData, actualIntervalMinutes } = useMemo(() => {
    return calculateSMA(allPrompts, smaConfig.intervalMinutes, smaConfig.smaPeriod, startDate);
  }, [allPrompts, smaConfig.intervalMinutes, smaConfig.smaPeriod, startDate]);

  const brushStartIndex = useMemo(() => {
    return calculateBrushStartIndex(smaData.length, brushStartRatio);
  }, [smaData.length, brushStartRatio]);
  // Format daily usage data
  const dailyData = Object.entries(metrics.dailyUsage)
    .map(([date, counts]) => ({
      date,
      In: counts.in,
      CW: counts.cacheWrite,
      CR: counts.cacheRead,
      Think: counts.think,
      Out: counts.out,
      Total: counts.in + counts.cacheWrite + counts.cacheRead + counts.think + counts.out
    }))
    .sort((a, b) => a.date.localeCompare(b.date));

  // Format model breakdown data
  const modelData = Object.entries(metrics.modelBreakdown)
    .map(([model, counts]) => ({
      model,
      In: counts.in,
      CW: counts.cacheWrite,
      CR: counts.cacheRead,
      Think: counts.think,
      Out: counts.out,
      Total: counts.in + counts.cacheWrite + counts.cacheRead + counts.think + counts.out
    }))
    .filter(d => d.Total > 0)
    .sort((a, b) => b.Total - a.Total);

  const totalAllTokens = modelData.reduce((acc, curr) => acc + curr.Total, 0);

  const maxDaily = Math.max(...dailyData.map(d => d.Total), 1);

  return (
    <div className="space-y-6 mt-8">
      <h2 className="text-2xl font-bold glow-text">Usage Charts</h2>
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        
        {/* Daily Usage Chart */}
        <div className="glass-panel p-6 animate-slide-up flex flex-col" data-testid="chart-daily">
          <div className="flex flex-col sm:flex-row justify-between items-center sm:items-start mb-8 gap-4">
            <h3 className="text-xl font-semibold text-center sm:text-left">Tokens per Day</h3>
            <div className="flex gap-3 text-xs font-mono bg-black/20 p-2 rounded-lg border border-white/5">
              <span className="flex items-center gap-1.5"><div className="w-2.5 h-2.5 rounded-full bg-blue-500 shadow-[0_0_8px_rgba(59,130,246,0.6)]"></div>In</span>
              <span className="flex items-center gap-1.5"><div className="w-2.5 h-2.5 rounded-full bg-pink-500 shadow-[0_0_8px_rgba(236,72,153,0.6)]"></div>CW</span>
              <span className="flex items-center gap-1.5"><div className="w-2.5 h-2.5 rounded-full bg-purple-500 shadow-[0_0_8px_rgba(139,92,246,0.6)]"></div>CR</span>
              <span className="flex items-center gap-1.5"><div className="w-2.5 h-2.5 rounded-full bg-emerald-500 shadow-[0_0_8px_rgba(16,185,129,0.6)]"></div>Think</span>
              <span className="flex items-center gap-1.5"><div className="w-2.5 h-2.5 rounded-full bg-amber-500 shadow-[0_0_8px_rgba(245,158,11,0.6)]"></div>Out</span>
            </div>
          </div>
          
          <div className="flex-1 flex items-end gap-1 sm:gap-2 h-52 mt-auto relative px-2">
            {dailyData.length > 0 ? dailyData.map((d) => {
              const heightPct = (d.Total / maxDaily) * 100;
              // Prevent division by zero
              const safeTotal = d.Total || 1;
              
              return (
                <div key={d.date} className="flex-1 flex flex-col justify-end items-center group relative h-full">
                  {/* Tooltip */}
                  <div className="absolute bottom-[calc(100%+10px)] opacity-0 group-hover:opacity-100 transition-all duration-200 bg-surface/95 backdrop-blur-xl border border-white/10 text-xs rounded-xl p-4 z-10 pointer-events-none shadow-2xl scale-95 group-hover:scale-100 origin-bottom min-w-[140px]">
                    <div className="font-bold mb-3 text-gray-200 border-b border-white/10 pb-2">{new Date(d.date).toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric', timeZone: 'UTC' })}</div>
                    <div className="flex justify-between gap-4 mb-1"><span className="text-gray-400">In:</span> <span className="text-blue-400 font-mono">{formatCompact(d.In || 0)}</span></div>
                    <div className="flex justify-between gap-4 mb-1"><span className="text-gray-400">CW:</span> <span className="text-pink-400 font-mono">{formatCompact(d.CW || 0)}</span></div>
                    <div className="flex justify-between gap-4 mb-1"><span className="text-gray-400">CR:</span> <span className="text-purple-400 font-mono">{formatCompact(d.CR || 0)}</span></div>
                    <div className="flex justify-between gap-4 mb-1"><span className="text-gray-400">Think:</span> <span className="text-emerald-400 font-mono">{formatCompact(d.Think || 0)}</span></div>
                    <div className="flex justify-between gap-4 mb-2"><span className="text-gray-400">Out:</span> <span className="text-amber-400 font-mono">{formatCompact(d.Out || 0)}</span></div>
                    <div className="flex justify-between gap-4 pt-2 border-t border-white/10"><span className="text-gray-300 font-bold">Total:</span> <span className="text-secondary font-mono font-bold">{formatCompact(d.Total || 0)}</span></div>
                  </div>
                  
                  {/* Stacked Bar */}
                  <div className="w-full max-w-[40px] flex flex-col justify-end rounded-t-md overflow-hidden relative group-hover:brightness-125 transition-all cursor-pointer shadow-[0_0_15px_rgba(0,0,0,0.2)] group-hover:shadow-[0_0_20px_rgba(255,255,255,0.1)]" style={{ height: `${heightPct}%`, minHeight: heightPct > 0 ? '4px' : '0' }}>
                    <div className="w-full bg-amber-500/90 transition-all duration-1000 hover:bg-amber-400" style={{ height: `${((d.Out || 0) / safeTotal) * 100}%` }}></div>
                    <div className="w-full bg-emerald-500/90 transition-all duration-1000 hover:bg-emerald-400" style={{ height: `${((d.Think || 0) / safeTotal) * 100}%` }}></div>
                    <div className="w-full bg-purple-500/90 transition-all duration-1000 hover:bg-purple-400" style={{ height: `${((d.CR || 0) / safeTotal) * 100}%` }}></div>
                    <div className="w-full bg-pink-500/90 transition-all duration-1000 hover:bg-pink-400" style={{ height: `${((d.CW || 0) / safeTotal) * 100}%` }}></div>
                    <div className="w-full bg-blue-500/90 transition-all duration-1000 hover:bg-blue-400" style={{ height: `${((d.In || 0) / safeTotal) * 100}%` }}></div>
                  </div>
                  
                  <div className="text-[10px] text-gray-500 mt-3 truncate w-full text-center font-mono">
                    {new Date(d.date).toLocaleDateString(undefined, { month: 'short', day: 'numeric', timeZone: 'UTC' })}
                  </div>
                </div>
              );
            }) : <div className="text-gray-500 w-full text-center mt-20">No data available</div>}
          </div>
        </div>

        <div className="glass-panel p-6 animate-slide-up flex flex-col" style={{ animationDelay: '100ms' }} data-testid="chart-model">
          <h3 className="text-xl font-semibold mb-4 text-center">Tokens by Model</h3>
          <div className="w-full h-72 flex flex-col overflow-y-auto custom-scrollbar pr-2 relative">
            
            <div className="flex items-center justify-between mb-4 border-b border-white/10 pb-2">
              <span className="font-bold text-gray-300 text-sm">Total Usage</span>
              <span className="font-bold text-secondary">{formatCompact(totalAllTokens)}</span>
            </div>
            
            <div className="flex-1 space-y-4">
              {modelData.map((d, idx) => {
                const percent = totalAllTokens > 0 ? (d.Total / totalAllTokens) * 100 : 0;
                return (
                  <div key={d.model} className="w-full group">
                    <div className="flex justify-between text-sm mb-1 items-center">
                      <div className="flex items-center gap-2 overflow-hidden pr-2">
                        <div className="w-3 h-3 rounded-sm shrink-0 transition-transform group-hover:scale-125" style={{ backgroundColor: COLORS[idx % COLORS.length] }}></div>
                        <span className="font-mono text-gray-300 truncate">{d.model}</span>
                      </div>
                      <span className="text-gray-400 font-mono text-xs whitespace-nowrap">
                        {formatCompact(d.Total)} <span className="text-gray-500 font-bold ml-1">{percent.toFixed(1)}%</span>
                      </span>
                    </div>
                    <div className="w-full bg-black/40 rounded-full h-2 overflow-hidden shadow-inner relative">
                      <div 
                        className="h-full rounded-full transition-all duration-1000 ease-out relative"
                        style={{ width: `${percent}%`, backgroundColor: COLORS[idx % COLORS.length] }}
                      >
                        <div className="absolute inset-0 bg-white/20" style={{ maskImage: 'linear-gradient(to right, transparent, black)', WebkitMaskImage: 'linear-gradient(to right, transparent, black)' }}></div>
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
        </div>
        {/* Token Trend SMA Chart */}
        <div className="glass-panel p-6 animate-slide-up flex flex-col lg:col-span-2" data-testid="chart-sma">
          <div className="flex flex-col sm:flex-row justify-between items-center sm:items-start mb-6 gap-4 border-b border-white/10 pb-4">
            <h3 className="text-xl font-semibold text-center sm:text-left">Token Trend (SMA)</h3>
            
            <div className="flex flex-wrap gap-4 items-center">
              <div className="flex items-center gap-2">
                <label className="text-xs text-gray-400">Start</label>
                <input 
                  type="datetime-local" 
                  value={startDate} 
                  onChange={e => setStartDate(e.target.value)}
                  className="bg-black/40 border border-white/10 rounded px-2 py-1 text-sm focus:border-blue-500 focus:outline-none transition-colors text-gray-300"
                />
              </div>
              <div className="flex items-center gap-2">
                <label className="text-xs text-gray-400">Time (min)</label>
                <input 
                  type="number" 
                  min="1"
                  value={smaConfig.intervalMinutes} 
                  onChange={e => setSmaConfig({...smaConfig, intervalMinutes: Math.max(1, Number(e.target.value) || 1)})}
                  className="bg-black/40 border border-white/10 rounded px-2 py-1 text-sm w-16 focus:border-blue-500 focus:outline-none transition-colors text-right"
                />
                {actualIntervalMinutes !== smaConfig.intervalMinutes && (
                  <span className="text-[10px] text-amber-400">(actual: {Math.round(actualIntervalMinutes)})</span>
                )}
              </div>
              <div className="flex items-center gap-2">
                <label className="text-xs text-gray-400">Period (intervals)</label>
                <input 
                  type="number" 
                  min="1"
                  value={smaConfig.smaPeriod} 
                  onChange={e => setSmaConfig({...smaConfig, smaPeriod: Math.max(1, Number(e.target.value) || 1)})}
                  className="bg-black/40 border border-white/10 rounded px-2 py-1 text-sm w-16 focus:border-blue-500 focus:outline-none transition-colors text-right"
                />
              </div>
              <button 
                onClick={() => {
                  const d = new Date();
                  d.setHours(d.getHours() - 6);
                  setStartDate(new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16));
                  setSmaConfig({ 
                    intervalMinutes: 1, 
                    smaPeriod: 5
                  });
                  setBrushStartRatio(0.75);
                }}
                className="p-1.5 ml-1 bg-black/40 border border-white/10 rounded hover:bg-white/10 text-gray-400 hover:text-white transition-colors"
                title="Reset to Defaults"
              >
                <RotateCcw size={16} />
              </button>
            </div>
          </div>
          
          <div className="w-full h-96 relative flex items-center justify-center">
            {smaData.length === 0 ? (
              <div className="flex flex-col items-center justify-center text-gray-500 gap-3">
                <InfinityIcon size={48} className="text-gray-600 animate-pulse" />
                <p className="text-sm">waiting for more data to assert trend</p>
              </div>
            ) : (
              <ResponsiveContainer width="100%" height="100%">
                <LineChart data={smaData} margin={{ top: 5, right: 55, bottom: 5, left: 10 }}>
                  <XAxis 
                    dataKey="time" 
                    tickFormatter={(tick) => new Date(tick).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} 
                    stroke="#4b5563"
                    tick={{ fill: '#9ca3af', fontSize: 12 }}
                  />
                  <YAxis 
                    width={45}
                    stroke="#4b5563" 
                    tick={{ fill: '#9ca3af', fontSize: 12 }}
                    tickFormatter={formatCompact}
                  />
                  <Tooltip 
                    contentStyle={{ backgroundColor: '#1f2937', border: '1px solid rgba(255,255,255,0.1)', borderRadius: '0.5rem' }}
                    labelFormatter={(label: any) => new Date(label).toLocaleString()}
                    formatter={(value: any, name: any) => [formatCompact(Number(value)), name]}
                    itemSorter={(item: any) => -Number(item.value)}
                  />
                  <Legend />
                  <Line type="monotone" dataKey="Total" stroke="#06b6d4" strokeWidth={2} dot={false} activeDot={{ r: 4 }} />
                  <Line type="monotone" dataKey="In" stroke="#3b82f6" strokeWidth={2} dot={false} />
                  <Line type="monotone" dataKey="CW" stroke="#ec4899" strokeWidth={2} dot={false} />
                  <Line type="monotone" dataKey="CR" stroke="#8b5cf6" strokeWidth={2} dot={false} />
                  <Line type="monotone" dataKey="Think" stroke="#10b981" strokeWidth={2} dot={false} />
                  <Line type="monotone" dataKey="Out" stroke="#f59e0b" strokeWidth={2} dot={false} />
                  <Brush 
                    dataKey="time" 
                    height={28} 
                    stroke="#06b6d4" 
                    fill="#111827"
                    startIndex={brushStartIndex}
                    onChange={(e) => {
                      if (e && e.startIndex !== undefined && smaData.length > 1) {
                        setBrushStartRatio(calculateBrushStartRatio(e.startIndex, smaData.length));
                      }
                    }}
                    tickFormatter={(tick) => new Date(tick).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                    travellerWidth={8}
                  />
                </LineChart>
              </ResponsiveContainer>
            )}
          </div>
        </div>

      </div>
    </div>
  );
});
