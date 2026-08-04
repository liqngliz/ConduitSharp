import type { InsightsData } from '../utils/parser';

export const Insights: React.FC<{ insights: InsightsData }> = ({ insights }) => {
  return (
    <div className="space-y-6 mt-8">
      <h2 className="text-2xl font-bold glow-text">AI Insights</h2>
      
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        
        <div className="glass-panel p-6 animate-slide-up" data-testid="insight-vague">
          <h3 className="text-gray-400 text-sm font-medium">Vague Prompts</h3>
          <p className="text-3xl font-bold mt-2 text-danger">{insights.vaguePrompts}</p>
          <p className="text-xs text-gray-500 mt-2">Short prompts causing high token input.</p>
        </div>

        <div className="glass-panel p-6 animate-slide-up" style={{ animationDelay: '100ms' }} data-testid="insight-marathon">
          <h3 className="text-gray-400 text-sm font-medium">Marathon Sessions</h3>
          <p className="text-3xl font-bold mt-2 text-primary">{insights.marathonSessions}</p>
          <p className="text-xs text-gray-500 mt-2">Sessions with &gt;15 turns.</p>
        </div>

        <div className="glass-panel p-6 animate-slide-up" style={{ animationDelay: '200ms' }} data-testid="insight-input">
          <h3 className="text-gray-400 text-sm font-medium">Input Heavy Requests</h3>
          <p className="text-3xl font-bold mt-2 text-secondary">{insights.inputHeavy}</p>
          <p className="text-xs text-gray-500 mt-2">Requests with &gt;5k input, low output.</p>
        </div>

        <div className="glass-panel p-6 animate-slide-up" style={{ animationDelay: '300ms' }} data-testid="insight-tool">
          <h3 className="text-gray-400 text-sm font-medium">Tool Heavy Sessions</h3>
          <p className="text-3xl font-bold mt-2 text-success">{insights.toolHeavy}</p>
          <p className="text-xs text-gray-500 mt-2">High ratio of tool use vs chat turns.</p>
        </div>

      </div>

      {insights.routeDominance && (
        <div className="glass-panel p-4 border-primary/50 animate-fade-in" data-testid="insight-dominance">
          <p className="text-sm">
            <span className="font-bold text-primary">Route Dominance Detected:</span> The <span className="font-mono text-secondary">{insights.routeDominance.route}</span> route is consuming {insights.routeDominance.percent}% of total tokens.
          </p>
        </div>
      )}
    </div>
  );
};
