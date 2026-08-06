import { useState, useRef, useEffect } from 'react';
import type { InsightsData, MetricsData } from '../utils/parser';

export const Insights: React.FC<{ insights: InsightsData; topPrompts: MetricsData['topPrompts']; sessions: MetricsData['sessions'] }> = ({ insights, topPrompts, sessions }) => {
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
  }, [isExpanded, topPrompts]);
  return (
    <div className="space-y-6 mt-8">
      <h2 className="text-2xl font-bold glow-text">AI Insights</h2>
      
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        
        <div className="glass-panel p-6 animate-slide-up" data-testid="insight-vague">
          <h3 className="text-gray-400 text-sm font-medium">Vague Prompts</h3>
          <p className="text-3xl font-bold mt-2 text-danger">{insights.vaguePrompts} ❓</p>
          <p className="text-xs text-gray-500 mt-2">Short prompts causing high token input.</p>
          <p className="text-[10px] text-gray-400 mt-1">Avg Vague: {insights.avgVagueTokens?.toLocaleString(undefined, { maximumFractionDigits: 0 }) || 0} | Avg Non-Vague: {insights.avgNonVagueTokens?.toLocaleString(undefined, { maximumFractionDigits: 0 }) || 0}</p>
        </div>

        <div className="glass-panel p-6 animate-slide-up" style={{ animationDelay: '100ms' }} data-testid="insight-marathon">
          <h3 className="text-gray-400 text-sm font-medium">Marathon Sessions</h3>
          <p className="text-3xl font-bold mt-2 text-primary">{insights.marathonSessions} 🏃‍♂️</p>
          <p className="text-xs text-gray-500 mt-2">Sessions with &gt;15 turns.</p>
          <p className="text-[10px] text-gray-400 mt-1">Avg Marathon Prompt: {insights.avgMarathonPromptTokens?.toLocaleString(undefined, { maximumFractionDigits: 0 }) || 0} | Avg Non-Marathon: {insights.avgNonMarathonPromptTokens?.toLocaleString(undefined, { maximumFractionDigits: 0 }) || 0}</p>
        </div>

        <div className="glass-panel p-6 animate-slide-up" style={{ animationDelay: '200ms' }} data-testid="insight-input">
          <h3 className="text-gray-400 text-sm font-medium">Input Heavy Requests</h3>
          <p className="text-3xl font-bold mt-2 text-secondary">{insights.inputHeavy} 🏋️‍♂️</p>
          <p className="text-xs text-gray-500 mt-2">Requests with &gt;5k input, low output.</p>
          <p className="text-[10px] text-gray-400 mt-1">Avg Heavy: {insights.avgInputHeavyTokens?.toLocaleString(undefined, { maximumFractionDigits: 0 }) || 0} | Avg Normal: {insights.avgNonInputHeavyTokens?.toLocaleString(undefined, { maximumFractionDigits: 0 }) || 0}</p>
        </div>

        <div className="glass-panel p-6 animate-slide-up" style={{ animationDelay: '300ms' }} data-testid="insight-tool">
          <h3 className="text-gray-400 text-sm font-medium">Tool Heavy Sessions</h3>
          <p className="text-3xl font-bold mt-2 text-success">{insights.toolHeavy} 🔧</p>
          <p className="text-xs text-gray-500 mt-2">High ratio of tool prompt tokens vs no-tool prompt tokens.</p>
          <p className="text-[10px] text-gray-400 mt-1">Avg Tool: {insights.avgToolTokens?.toLocaleString(undefined, { maximumFractionDigits: 0 }) || 0} | Avg No-Tool: {insights.avgChatTokens?.toLocaleString(undefined, { maximumFractionDigits: 0 }) || 0}</p>
          <p className="text-[10px] text-gray-400 mt-1">Tool Prompts: {insights.globalToolPrompts?.toLocaleString(undefined, { maximumFractionDigits: 0 }) || 0} | No-Tool Prompts: {insights.globalChatPrompts?.toLocaleString(undefined, { maximumFractionDigits: 0 }) || 0}</p>
        </div>

      </div>

      {insights.modelDominance && (
        <div className="glass-panel p-4 border-primary/50 animate-fade-in" data-testid="insight-dominance">
          <p className="text-sm">
            <span className="font-bold text-primary">Model Dominance Detected:</span> The <span className="font-mono text-secondary">{insights.modelDominance.model}</span> model is consuming {insights.modelDominance.percent}% of total tokens.
          </p>
        </div>
      )}

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
            {topPrompts.length > 0 ? topPrompts.map((p, idx) => {
              const totalIn = p.in + p.cacheRead + p.cacheWrite;
              const isVague = p.prompt.length < 30 && totalIn > 1000;
              const isInputHeavy = totalIn > 5000 && p.out < 100;
              
              const sess = sessions[p.session];
              const isMarathon = sess?.turnCount > 15;
              const hasToolCall = sess?.prompts.find(pr => pr.prompt === p.prompt)?.hasToolCall;
              
              let prefix = '';
              let promptColor = 'text-gray-300';
              
              if (isVague && isInputHeavy) {
                prefix = '❓🏋️‍♂️ ';
                promptColor = 'text-danger';
              } else if (isVague) {
                prefix = '❓ ';
                promptColor = 'text-danger';
              } else if (isInputHeavy) {
                prefix = '🏋️‍♂️ ';
                promptColor = 'text-secondary';
              } else if (hasToolCall) {
                prefix = '🔧 ';
                promptColor = 'text-success';
              }

              return (
              <li key={idx} className="bg-white/5 rounded-lg p-3 flex flex-col md:flex-row justify-between items-start md:items-center gap-2" data-testid={`top-prompt-${idx}`}>
                <div className="flex flex-col w-full md:w-1/2 overflow-hidden">
                  <span className={`truncate font-medium ${promptColor}`} title={p.prompt}>{prefix}{p.prompt}</span>
                  <div className="text-xs text-gray-500 font-mono mt-1">
                    <span className={isMarathon ? 'text-primary font-bold' : ''}>{p.session}{isMarathon ? ' 🏃‍♂️' : ''}</span> model:{p.model}
                  </div>
                </div>
                <div className="flex gap-4 text-xs font-mono w-full md:w-auto justify-between md:justify-end">
                  <span className="text-gray-400">In: {p.in.toLocaleString(undefined, { maximumFractionDigits: 0 })}</span>
                  <span className="text-gray-400">CR: {p.cacheRead.toLocaleString(undefined, { maximumFractionDigits: 0 })}</span>
                  <span className="text-gray-400">CW: {p.cacheWrite.toLocaleString(undefined, { maximumFractionDigits: 0 })}</span>
                  <span className="text-gray-400">Out: {p.out.toLocaleString(undefined, { maximumFractionDigits: 0 })}</span>
                  <span className="text-secondary font-bold">Total: {p.totalTokens.toLocaleString(undefined, { maximumFractionDigits: 0 })}</span>
                  <span className="text-gray-500 ml-2">({Math.round((p.out / Math.max(1, p.totalTokens)) * 100)}% written)</span>
                </div>
              </li>
            )}) : <li className="text-gray-500">No prompts found</li>}
          </ul>
        </div>
        {!isExpanded && topPrompts.length > 3 && (
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
