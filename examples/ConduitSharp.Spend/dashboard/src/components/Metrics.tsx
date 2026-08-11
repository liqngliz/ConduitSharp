import React from 'react';
import type { MetricsData } from '../utils/parser';
import { Brain } from 'lucide-react';
import { AnimatedNumber } from './AnimatedNumber';

export const Metrics = React.memo(({ metrics, routeName }: { metrics: MetricsData; routeName: string }) => {
  const totalTokens = metrics.totals.in + metrics.totals.out + metrics.totals.cacheRead + metrics.totals.cacheWrite;
  const totalSessions = Object.keys(metrics.sessions).length;
  const totalPromptsSent = Object.values(metrics.sessions).reduce((sum, sess) => sum + sess.prompts.length, 0);

  const avgSessionTokens = totalSessions > 0 ? Math.round(totalTokens / totalSessions) : 0;
  const avgMessageTokens = totalPromptsSent > 0 ? Math.round(totalTokens / totalPromptsSent) : 0;
  const avgMessageIn = totalPromptsSent > 0 ? Math.round(metrics.totals.in / totalPromptsSent) : 0;
  const avgMessageCw = totalPromptsSent > 0 ? Math.round(metrics.totals.cacheWrite / totalPromptsSent) : 0;
  const avgMessageCr = totalPromptsSent > 0 ? Math.round(metrics.totals.cacheRead / totalPromptsSent) : 0;
  const wrotePercent = totalTokens > 0 ? Math.round((metrics.totals.out / totalTokens) * 100) : 0;
  // Reasoning is billed inside Out, so it is shown against Out and never added to the total.
  const thinkPercent = metrics.totals.out > 0 ? Math.round((metrics.totals.think / metrics.totals.out) * 100) : 0;

  let mostUsageInsight = "Reading context";
  const cacheTokens = metrics.totals.cacheRead + metrics.totals.cacheWrite;
  if (cacheTokens > metrics.totals.in && cacheTokens > metrics.totals.out) {
    mostUsageInsight = "Caching";
  } else if (metrics.totals.out > metrics.totals.in && metrics.totals.out > cacheTokens) {
    mostUsageInsight = "Writing output";
  }

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-1 md:grid-cols-4 gap-4 items-stretch">
        <div className="glass-panel p-6 animate-fade-in flex flex-col items-center text-center h-full" data-testid="metric-totals">
          <h3 className="text-gray-400 text-sm font-medium flex items-center justify-center gap-1">
            Total Usage (Tokens)
            <span className="relative group cursor-help text-xs bg-white/10 rounded-full w-4 h-4 flex items-center justify-center">
              ?
              <span className="absolute bottom-full mb-2 hidden group-hover:block w-48 p-2 bg-gray-800 text-white text-xs rounded shadow-lg z-10 left-1/2 -translate-x-1/2 whitespace-normal">
                Total tokens processed (Input + Output + Cache)
              </span>
            </span>
          </h3>
          <p className="text-3xl font-bold mt-2"><AnimatedNumber value={totalTokens} compact /></p>
          <div className="text-[13px] text-gray-400 mt-3 font-mono flex flex-col items-center gap-1 leading-relaxed">
            <span className="flex items-center gap-1">
              In: <span className="text-blue-500"><AnimatedNumber value={metrics.totals.in} compact /></span>
              <span className="text-gray-600">|</span>
              CW: <span className="text-pink-500"><AnimatedNumber value={metrics.totals.cacheWrite} compact /></span>
            </span>
            <span className="flex items-center gap-1">
              CR: <span className="text-purple-500"><AnimatedNumber value={metrics.totals.cacheRead} compact /></span>
              <span className="text-gray-600">|</span>
              Out: <span className="text-amber-500"><AnimatedNumber value={metrics.totals.out} compact /></span>
            </span>
            {metrics.totals.think > 0 && (
              <span className="flex items-center gap-1" data-testid="metric-think">
                <Brain size={12} className="text-emerald-400" />
                Think: <span className="text-emerald-400"><AnimatedNumber value={metrics.totals.think} compact /></span>
                <span className="text-gray-500">({thinkPercent}% of Out)</span>
              </span>
            )}
          </div>
        </div>
        <div className="glass-panel p-6 animate-fade-in flex flex-col items-center text-center h-full" style={{ animationDelay: '100ms' }}>
          <h3 className="text-gray-400 text-sm font-medium flex items-center justify-center gap-1">
            Sessions
            <span className="relative group cursor-help text-xs bg-white/10 rounded-full w-4 h-4 flex items-center justify-center">
              ?
              <span className="absolute bottom-full mb-2 hidden group-hover:block w-48 p-2 bg-gray-800 text-white text-xs rounded shadow-lg z-10 left-1/2 -translate-x-1/2 whitespace-normal">
                Count of unique sessions
              </span>
            </span>
          </h3>
          <p className="text-3xl font-bold mt-2"><AnimatedNumber value={totalSessions} compact /></p>
          <div className="text-[13px] text-gray-400 mt-3 flex flex-col items-center gap-1 leading-relaxed min-h-[44px] justify-center">
            <span>Each used <span className="text-secondary font-medium"><AnimatedNumber value={avgSessionTokens} compact /></span> avg</span>
          </div>
        </div>
        <div className="glass-panel p-6 animate-fade-in flex flex-col items-center text-center h-full" style={{ animationDelay: '200ms' }}>
          <h3 className="text-gray-400 text-sm font-medium flex items-center justify-center gap-1">
            Prompts Sent
            <span className="relative group cursor-help text-xs bg-white/10 rounded-full w-4 h-4 flex items-center justify-center">
              ?
              <span className="absolute bottom-full mb-2 hidden group-hover:block w-48 p-2 bg-gray-800 text-white text-xs rounded shadow-lg z-10 left-1/2 -translate-x-1/2 whitespace-normal">
                Total prompts sent
              </span>
            </span>
          </h3>
          <p className="text-3xl font-bold mt-2"><AnimatedNumber value={totalPromptsSent} compact /></p>
          <div className="text-[13px] text-gray-400 mt-3 font-mono flex flex-col items-center gap-1 leading-relaxed min-h-[44px]">
            <span className="flex items-center gap-1">
              Avg: <span className="text-primary"><AnimatedNumber value={avgMessageTokens} compact /></span>
            </span>
            <span className="flex items-center gap-1">
              In: <span className="text-blue-500"><AnimatedNumber value={avgMessageIn} compact /></span>
              <span className="text-gray-600">|</span>
              CW: <span className="text-pink-500"><AnimatedNumber value={avgMessageCw} compact /></span>
              <span className="text-gray-600">|</span>
              CR: <span className="text-purple-500"><AnimatedNumber value={avgMessageCr} compact /></span>
            </span>
          </div>
        </div>
        <div className="glass-panel p-6 animate-fade-in flex flex-col items-center text-center h-full" style={{ animationDelay: '300ms' }}>
          <h3 className="text-gray-400 text-sm font-medium flex items-center justify-center gap-1">
            {routeName === 'All Agents' ? 'Tokens Out' : `${routeName.charAt(0).toUpperCase() + routeName.slice(1)} Wrote (Tokens)`}
            <span className="relative group cursor-help text-xs bg-white/10 rounded-full w-4 h-4 flex items-center justify-center">
              ?
              <span className="absolute bottom-full mb-2 hidden group-hover:block w-48 p-2 bg-gray-800 text-white text-xs rounded shadow-lg z-10 left-1/2 -translate-x-1/2 whitespace-normal">
                Total output tokens generated by the model
              </span>
            </span>
          </h3>
          <p className="text-3xl font-bold mt-2"><AnimatedNumber value={metrics.totals.out} compact /></p>
          <div className="text-[13px] text-gray-400 mt-3 flex flex-col items-center gap-1 leading-relaxed min-h-[44px] justify-center">
            <span><span className="text-amber-500 font-medium"><AnimatedNumber value={wrotePercent} />%</span> of total</span>
            <span>Most usage: {mostUsageInsight}</span>
          </div>
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

    </div>
  );
});
