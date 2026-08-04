import { useState, useRef, useEffect } from 'react';
import type { MetricsData } from '../utils/parser';

export const Metrics: React.FC<{ metrics: MetricsData; routeName: string }> = ({ metrics, routeName }) => {
  const [isExpanded, setIsExpanded] = useState(false);
  const [isAtBottom, setIsAtBottom] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);

  const checkScroll = () => {
    if (scrollRef.current) {
      const { scrollTop, scrollHeight, clientHeight } = scrollRef.current;
      setIsAtBottom(scrollHeight <= clientHeight || scrollHeight - scrollTop - clientHeight < 10);
    }
  };

  useEffect(() => {
    checkScroll();
  }, [isExpanded, metrics.topPrompts]);
  const totalTokens = metrics.totals.in + metrics.totals.out + metrics.totals.cacheRead + metrics.totals.cacheWrite;
  const totalSessions = Object.keys(metrics.sessions).length;
  const avgSessionTokens = totalSessions > 0 ? Math.round(totalTokens / totalSessions) : 0;
  const avgMessageTokens = metrics.totals.messagesSent > 0 ? Math.round(totalTokens / metrics.totals.messagesSent) : 0;
  const wrotePercent = totalTokens > 0 ? Math.round((metrics.totals.out / totalTokens) * 100) : 0;

  let mostUsageInsight = "Reading context";
  const cacheTokens = metrics.totals.cacheRead + metrics.totals.cacheWrite;
  if (cacheTokens > metrics.totals.in && cacheTokens > metrics.totals.out) {
    mostUsageInsight = "Caching";
  } else if (metrics.totals.out > metrics.totals.in && metrics.totals.out > cacheTokens) {
    mostUsageInsight = "Writing output";
  }

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
        <div className="glass-panel p-6 animate-fade-in flex flex-col items-center text-center" data-testid="metric-totals">
          <h3 className="text-gray-400 text-sm font-medium flex items-center justify-center gap-1">
            Total Usage (Tokens)
            <span className="relative group cursor-help text-xs bg-white/10 rounded-full w-4 h-4 flex items-center justify-center">
              ?
              <span className="absolute bottom-full mb-2 hidden group-hover:block w-48 p-2 bg-gray-800 text-white text-xs rounded shadow-lg z-10 left-1/2 -translate-x-1/2 whitespace-normal">
                Total tokens processed (Input + Output + Cache)
              </span>
            </span>
          </h3>
          <p className="text-3xl font-bold mt-2">{totalTokens.toLocaleString()}</p>
          <p className="text-gray-500 text-xs mt-2">{metrics.totals.in.toLocaleString()} fresh + {metrics.totals.cacheRead.toLocaleString()} cr + {metrics.totals.cacheWrite.toLocaleString()} cw + {metrics.totals.out.toLocaleString()} written</p>
        </div>
        <div className="glass-panel p-6 animate-fade-in flex flex-col items-center text-center" style={{ animationDelay: '100ms' }}>
          <h3 className="text-gray-400 text-sm font-medium flex items-center justify-center gap-1">
            Sessions
            <span className="relative group cursor-help text-xs bg-white/10 rounded-full w-4 h-4 flex items-center justify-center">
              ?
              <span className="absolute bottom-full mb-2 hidden group-hover:block w-48 p-2 bg-gray-800 text-white text-xs rounded shadow-lg z-10 left-1/2 -translate-x-1/2 whitespace-normal">
                Count of unique sessions
              </span>
            </span>
          </h3>
          <p className="text-3xl font-bold mt-2">{totalSessions}</p>
          <p className="text-gray-500 text-xs mt-2">Each one used {avgSessionTokens.toLocaleString()} tokens average</p>
        </div>
        <div className="glass-panel p-6 animate-fade-in flex flex-col items-center text-center" style={{ animationDelay: '200ms' }}>
          <h3 className="text-gray-400 text-sm font-medium flex items-center justify-center gap-1">
            Messages Sent
            <span className="relative group cursor-help text-xs bg-white/10 rounded-full w-4 h-4 flex items-center justify-center">
              ?
              <span className="absolute bottom-full mb-2 hidden group-hover:block w-48 p-2 bg-gray-800 text-white text-xs rounded shadow-lg z-10 left-1/2 -translate-x-1/2 whitespace-normal">
                Total API requests made (tokens &gt; 0 or turn &gt; 0)
              </span>
            </span>
          </h3>
          <p className="text-3xl font-bold mt-2">{metrics.totals.messagesSent.toLocaleString()}</p>
          <p className="text-gray-500 text-xs mt-2">Each one cost {avgMessageTokens.toLocaleString()} tokens average</p>
        </div>
        <div className="glass-panel p-6 animate-fade-in flex flex-col items-center text-center" style={{ animationDelay: '300ms' }}>
          <h3 className="text-gray-400 text-sm font-medium flex items-center justify-center gap-1">
            {routeName === 'All Agents' ? 'All Agents Wrote (Tokens)' : `${routeName.charAt(0).toUpperCase() + routeName.slice(1)} Wrote (Tokens)`}
            <span className="relative group cursor-help text-xs bg-white/10 rounded-full w-4 h-4 flex items-center justify-center">
              ?
              <span className="absolute bottom-full mb-2 hidden group-hover:block w-48 p-2 bg-gray-800 text-white text-xs rounded shadow-lg z-10 left-1/2 -translate-x-1/2 whitespace-normal">
                Total output tokens generated by the model
              </span>
            </span>
          </h3>
          <p className="text-3xl font-bold mt-2">{metrics.totals.out.toLocaleString()}</p>
          <p className="text-gray-500 text-xs mt-2">{wrotePercent}% of total -- most usage is {mostUsageInsight}</p>
        </div>
      </div>
      <div className="text-center mt-2">
        <p className="text-gray-500 text-xs flex items-center justify-center gap-1">
          Tokens are how AI measures cost-roughly 0.75 token per word
          <span className="relative group cursor-help bg-white/10 rounded-full w-3 h-3 flex items-center justify-center text-[10px]">
            ?
            <span className="absolute bottom-full mb-2 hidden group-hover:block w-48 p-2 bg-gray-800 text-white text-xs rounded shadow-lg z-10 left-1/2 -translate-x-1/2 whitespace-normal">
              1 token ≈ 0.75 words
            </span>
          </span>
        </p>
      </div>

      <div className="glass-panel p-6 animate-slide-up relative" style={{ animationDelay: '400ms' }}>
        <h3 className="text-xl font-semibold mb-4 flex justify-between items-center">
          Most Expensive Prompts
          {isExpanded && (
            <button onClick={() => setIsExpanded(false)} className="text-sm text-gray-400 hover:text-white transition-colors">Collapse</button>
          )}
        </h3>
        <div 
          ref={scrollRef}
          onScroll={checkScroll}
          className={`pr-2 custom-scrollbar ${isExpanded ? 'overflow-y-auto max-h-[440px]' : 'overflow-hidden max-h-[280px]'}`}
          style={!isAtBottom ? { 
            maskImage: 'linear-gradient(to bottom, black 80%, transparent 100%)',
            WebkitMaskImage: 'linear-gradient(to bottom, black 80%, transparent 100%)'
          } : undefined}
        >
          <ul className={`space-y-3 ${!isExpanded ? 'pb-8' : ''}`}>
            {metrics.topPrompts.length > 0 ? metrics.topPrompts.map((p, idx) => (
              <li key={idx} className="bg-white/5 rounded-lg p-3 flex flex-col md:flex-row justify-between items-start md:items-center gap-2" data-testid={`top-prompt-${idx}`}>
                <div className="flex flex-col w-full md:w-1/2 overflow-hidden">
                  <span className="truncate text-gray-300" title={p.prompt}>{p.prompt}</span>
                  <div className="text-xs text-gray-500 font-mono mt-1">session:{p.session} turn:{p.turn} model:{p.model}</div>
                </div>
                <div className="flex gap-4 text-xs font-mono w-full md:w-auto justify-between md:justify-end">
                  <span className="text-gray-400">In: {p.in.toLocaleString()}</span>
                  <span className="text-gray-400">CR: {p.cacheRead.toLocaleString()}</span>
                  <span className="text-gray-400">CW: {p.cacheWrite.toLocaleString()}</span>
                  <span className="text-gray-400">Out: {p.out.toLocaleString()}</span>
                  <span className="text-secondary font-bold">Total: {p.totalTokens.toLocaleString()}</span>
                  <span className="text-gray-500 ml-2">({Math.round((p.out / Math.max(1, p.totalTokens)) * 100)}% written)</span>
                </div>
              </li>
            )) : <li className="text-gray-500">No prompts found</li>}
          </ul>
        </div>
        {!isExpanded && metrics.topPrompts.length > 3 && (
          <div className="absolute bottom-0 left-0 w-full flex justify-center pb-4 pointer-events-none">
            <button 
              onClick={() => setIsExpanded(true)}
              className="pointer-events-auto bg-surface border border-white/10 hover:bg-white/20 text-gray-200 px-5 py-1.5 rounded-full text-sm font-medium transition-all shadow-lg"
            >
              See more
            </button>
          </div>
        )}
      </div>

    </div>
  );
};
