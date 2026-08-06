import { useState } from 'react';
import React from 'react';
import type { InsightsData, MetricsData } from '../utils/parser';
import { AnimatedNumber } from './AnimatedNumber';
import { MessageCircleQuestion, SportShoe, Dumbbell, Wrench } from 'lucide-react';

const formatCompact = (num: number) => Intl.NumberFormat('en-US', { notation: 'compact', maximumSignificantDigits: 3 }).format(num);

export const Insights = React.memo(({ insights, topPrompts, sessions, onSessionSelect }: { insights: InsightsData; topPrompts: MetricsData['topPrompts']; sessions: MetricsData['sessions']; onSessionSelect?: (sessionId: string) => void }) => {
  const [isExpanded, setIsExpanded] = useState(false);

  return (
    <div className="space-y-6 mt-8">
      <h2 className="text-2xl font-bold glow-text">AI Insights</h2>
      
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        
        <div className="glass-panel p-6 animate-slide-up" data-testid="insight-vague">
          <h3 className="text-gray-400 text-sm font-medium">Vague Prompts</h3>
          <p className="text-3xl font-bold mt-2 text-danger flex items-center gap-2">{insights.vaguePrompts} <MessageCircleQuestion size={24} className="opacity-80" /></p>
          <p className="text-xs text-gray-500 mt-2">Short prompts causing high token input.</p>
          <p className="text-[10px] text-gray-400 mt-1">Avg Vague: <AnimatedNumber value={insights.avgVagueTokens || 0} compact /> | Avg Non-Vague: <AnimatedNumber value={insights.avgNonVagueTokens || 0} compact /></p>
        </div>

        <div className="glass-panel p-6 animate-slide-up" style={{ animationDelay: '100ms' }} data-testid="insight-marathon">
          <h3 className="text-gray-400 text-sm font-medium">Marathon Sessions</h3>
          <p className="text-3xl font-bold mt-2 text-primary flex items-center gap-2">{insights.marathonSessions} <SportShoe size={24} className="opacity-80" /></p>
          <p className="text-xs text-gray-500 mt-2">Sessions with &gt;15 turns.</p>
          <p className="text-[10px] text-gray-400 mt-1">Avg Marathon Prompt: <AnimatedNumber value={insights.avgMarathonPromptTokens || 0} compact /> | Avg Non-Marathon: <AnimatedNumber value={insights.avgNonMarathonPromptTokens || 0} compact /></p>
        </div>

        <div className="glass-panel p-6 animate-slide-up" style={{ animationDelay: '200ms' }} data-testid="insight-input">
          <h3 className="text-gray-400 text-sm font-medium">Input Heavy Requests</h3>
          <p className="text-3xl font-bold mt-2 text-secondary flex items-center gap-2">{insights.inputHeavy} <Dumbbell size={24} className="opacity-80" /></p>
          <p className="text-xs text-gray-500 mt-2">Requests with &gt;5k input, low output.</p>
          <p className="text-[10px] text-gray-400 mt-1">Avg Heavy: <AnimatedNumber value={insights.avgInputHeavyTokens || 0} compact /> | Avg Normal: <AnimatedNumber value={insights.avgNonInputHeavyTokens || 0} compact /></p>
        </div>

        <div className="glass-panel p-6 animate-slide-up" style={{ animationDelay: '300ms' }} data-testid="insight-tool">
          <h3 className="text-gray-400 text-sm font-medium">Tool Heavy Sessions</h3>
          <p className="text-3xl font-bold mt-2 text-success flex items-center gap-2">{insights.toolHeavy} <Wrench size={24} className="opacity-80" /></p>
          <p className="text-xs text-gray-500 mt-2">High ratio of tool prompt tokens vs no-tool prompt tokens.</p>
          <p className="text-[10px] text-gray-400 mt-1">Avg Tool: <AnimatedNumber value={insights.avgToolTokens || 0} compact /> | Avg No-Tool: <AnimatedNumber value={insights.avgChatTokens || 0} compact /></p>
          <p className="text-[10px] text-gray-400 mt-1">Tool Prompts: <AnimatedNumber value={insights.globalToolPrompts || 0} compact /> | No-Tool Prompts: <AnimatedNumber value={insights.globalChatPrompts || 0} compact /></p>
        </div>

      </div>

      {insights.modelDominance && (
        <div className="glass-panel px-6 py-4 border-primary/50 animate-fade-in" data-testid="insight-dominance">
          <p className="text-sm">
            <span className="font-bold text-primary">Model Dominance Detected:</span> The <span className="font-mono text-secondary">{insights.modelDominance.model}</span> model is consuming {insights.modelDominance.percent}% of total tokens.
          </p>
        </div>
      )}

      <div className="glass-panel p-6 animate-slide-up relative" style={{ animationDelay: '400ms' }}>
        <h3 className="text-xl font-semibold mb-4 flex justify-between items-center">
          Most Expensive Prompt Sequences
          {isExpanded && (
            <button onClick={() => setIsExpanded(false)} className="text-sm text-gray-400 hover:text-white transition-colors">Collapse</button>
          )}
        </h3>
        <div 
          className={`pr-2 ${!isExpanded ? 'overflow-hidden max-h-[280px] custom-scrollbar' : ''}`}
          style={!isExpanded ? { 
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
              const hasToolCall = p.hasToolCall;
              
              const prefixIcons: React.ReactNode[] = [];
              let promptColor = 'text-gray-300';
              
              if (hasToolCall) {
                prefixIcons.push(<Wrench key="wrench" size={14} className="text-emerald-500 shrink-0" />);
                promptColor = 'text-success';
              }
              if (isInputHeavy) {
                prefixIcons.push(<Dumbbell key="dumbbell" size={14} className="text-cyan-500 shrink-0" />);
                promptColor = 'text-secondary';
              }
              if (isVague) {
                prefixIcons.push(<MessageCircleQuestion key="vague" size={14} className="text-rose-500 shrink-0" />);
                promptColor = 'text-danger';
              }

              return (
              <li 
                key={idx} 
                className="bg-white/5 rounded-lg p-3 flex flex-col md:flex-row justify-between items-start md:items-center gap-2 hover:bg-white/10 transition-colors cursor-pointer" 
                data-testid={`top-prompt-${idx}`}
                onClick={() => onSessionSelect?.(p.session)}
              >
                <div className="flex flex-col w-full md:w-[40%] overflow-hidden pr-4">
                  <div className={`flex items-center gap-1.5 truncate font-medium ${promptColor}`} title={p.prompt}>
                    {prefixIcons}
                    <span className="truncate">{p.prompt.length > 45 ? p.prompt.substring(0, 45) + '...' : p.prompt}</span>
                  </div>
                  <div className="text-xs text-gray-500 font-mono mt-1 flex items-center gap-1.5">
                    <span className={isMarathon ? 'text-primary font-bold' : ''}>{p.session}</span>
                    {isMarathon && <SportShoe size={12} className="text-primary shrink-0" />}
                  </div>

                </div>
                <div className="grid grid-cols-3 md:grid-cols-6 gap-2 text-xs font-mono w-full md:w-[60%] items-center">
                  <span className="text-gray-400 truncate">In: <span className="text-blue-500">{formatCompact(p.in)}</span></span>
                  <span className="text-gray-400 truncate">CR: <span className="text-purple-500">{formatCompact(p.cacheRead)}</span></span>
                  <span className="text-gray-400 truncate">CW: <span className="text-pink-500">{formatCompact(p.cacheWrite)}</span></span>
                  <span className="text-gray-400 truncate">Out: <span className="text-amber-500">{formatCompact(p.out)}</span></span>
                  <span className="text-gray-400 font-bold truncate">Tot: <span className="text-secondary">{formatCompact(p.totalTokens)}</span></span>
                  <span className="text-gray-500 truncate">{p.model}</span>
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
});
