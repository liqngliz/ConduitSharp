import type { MetricsData } from '../utils/parser';
import { AnimatedNumber } from './AnimatedNumber';

export const Metrics: React.FC<{ metrics: MetricsData; routeName: string }> = ({ metrics, routeName }) => {
  const totalTokens = metrics.totals.in + metrics.totals.out + metrics.totals.cacheRead + metrics.totals.cacheWrite;
  const totalSessions = Object.keys(metrics.sessions).length;
  const totalPromptsSent = Object.values(metrics.sessions).reduce((sum, sess) => sum + sess.prompts.length, 0);

  const avgSessionTokens = totalSessions > 0 ? Math.round(totalTokens / totalSessions) : 0;
  const avgMessageTokens = totalPromptsSent > 0 ? Math.round(totalTokens / totalPromptsSent) : 0;
  const avgMessageIn = totalPromptsSent > 0 ? Math.round(metrics.totals.in / totalPromptsSent) : 0;
  const avgMessageCw = totalPromptsSent > 0 ? Math.round(metrics.totals.cacheWrite / totalPromptsSent) : 0;
  const avgMessageCr = totalPromptsSent > 0 ? Math.round(metrics.totals.cacheRead / totalPromptsSent) : 0;
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
          <p className="text-3xl font-bold mt-2"><AnimatedNumber value={totalTokens} compact /></p>
          <div className="text-gray-500 text-xs mt-2 font-mono flex flex-col items-center leading-relaxed">
            <div>
              In:<span className="text-blue-500"><AnimatedNumber value={metrics.totals.in} compact /></span> | 
              CW:<span className="text-pink-500"><AnimatedNumber value={metrics.totals.cacheWrite} compact /></span>
            </div>
            <div>
              CR:<span className="text-purple-500"><AnimatedNumber value={metrics.totals.cacheRead} compact /></span> | 
              Out:<span className="text-amber-500"><AnimatedNumber value={metrics.totals.out} compact /></span>
            </div>
          </div>
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
          <p className="text-3xl font-bold mt-2"><AnimatedNumber value={totalSessions} compact /></p>
          <p className="text-gray-500 text-xs mt-2">Each one used <span className="text-secondary"><AnimatedNumber value={avgSessionTokens} compact /></span> tokens average</p>
        </div>
        <div className="glass-panel p-6 animate-fade-in flex flex-col items-center text-center" style={{ animationDelay: '200ms' }}>
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
          <p className="text-gray-500 text-xs mt-2">Each cost on average <AnimatedNumber value={avgMessageTokens} compact /> average</p>
          <p className="text-gray-500 text-xs mt-1 font-mono">
            In:<span className="text-blue-500"><AnimatedNumber value={avgMessageIn} compact /></span> | 
            CW:<span className="text-pink-500"><AnimatedNumber value={avgMessageCw} compact /></span> | 
            CR:<span className="text-purple-500"><AnimatedNumber value={avgMessageCr} compact /></span>
          </p>
        </div>
        <div className="glass-panel p-6 animate-fade-in flex flex-col items-center text-center" style={{ animationDelay: '300ms' }}>
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
          <p className="text-gray-500 text-xs mt-2"><span className="text-amber-500"><AnimatedNumber value={wrotePercent} />%</span> of total -- most usage is {mostUsageInsight}</p>
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
};
