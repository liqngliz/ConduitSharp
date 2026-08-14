import { useState } from 'react';
import React from 'react';
import type { InsightsData, MetricsData, InsightsConfig } from '../utils/parser';
import { evaluatePromptFlags, DEFAULT_INSIGHTS_CONFIG } from '../utils/parser';
import { AnimatedNumber } from './AnimatedNumber';
import { MessageCircleQuestion, SportShoe, Dumbbell, Wrench, Brain, Settings2, RotateCcw } from 'lucide-react';

const formatCompact = (num: number) => Intl.NumberFormat('en-US', { notation: 'compact', maximumSignificantDigits: 3 }).format(num);

export const Insights = React.memo(({ insights, topPrompts, sessions, config, setConfig, onSessionSelect }: { insights: InsightsData; topPrompts: MetricsData['topPrompts']; sessions: MetricsData['sessions']; config: InsightsConfig; setConfig: (c: InsightsConfig) => void; onSessionSelect?: (sessionId: string, traceId?: string, ts?: string, turn?: number) => void }) => {
  const [isExpanded, setIsExpanded] = useState(false);
  const [showSettings, setShowSettings] = useState(false);

  return (
    <div className="space-y-6 mt-8">
      <div className="flex justify-between items-center">
        <h2 className="text-2xl font-bold glow-text">AI Insights</h2>
        <button 
          onClick={() => setShowSettings(!showSettings)}
          className={`p-2 rounded-full transition-colors ${showSettings ? 'bg-primary/20 text-primary' : 'bg-white/5 text-gray-400 hover:text-white hover:bg-white/10'}`}
          title="Configure Insight Thresholds"
        >
          <Settings2 size={20} />
        </button>
      </div>

      {showSettings && (
        <div className="glass-panel p-6 animate-fade-in mb-4 border border-white/10">
          <div className="flex justify-between items-center mb-4">
            <h3 className="text-lg font-semibold text-gray-200">Insight Thresholds</h3>
            <button 
              onClick={() => setConfig(DEFAULT_INSIGHTS_CONFIG)}
              className="flex items-center gap-1.5 text-xs text-gray-400 hover:text-white transition-colors bg-white/5 hover:bg-white/10 px-3 py-1.5 rounded"
              title="Reset to default thresholds"
            >
              <RotateCcw size={12} />
              Reset to Defaults
            </button>
          </div>
          <div className="grid grid-cols-1 md:grid-cols-4 gap-6">
            <div className="space-y-3">
              <h4 className="text-sm font-medium text-danger">Vague Prompts</h4>
              <div className="flex flex-col gap-1">
                <label className="text-xs text-gray-400">Max Characters</label>
                <input type="number" value={config.vaguePromptLength} onChange={e => setConfig({...config, vaguePromptLength: e.target.value === '' ? '' as any : Number(e.target.value)})} onBlur={() => { if ((config.vaguePromptLength as any) === '') setConfig({...config, vaguePromptLength: DEFAULT_INSIGHTS_CONFIG.vaguePromptLength}); }} className="bg-black/40 border border-white/10 rounded px-3 py-1.5 text-sm w-full focus:border-danger focus:outline-none transition-colors" />
              </div>
              <div className="flex flex-col gap-1">
                <label className="text-xs text-gray-400">Min Input Tokens</label>
                <input type="number" value={config.vagueMinInput} onChange={e => setConfig({...config, vagueMinInput: e.target.value === '' ? '' as any : Number(e.target.value)})} onBlur={() => { if ((config.vagueMinInput as any) === '') setConfig({...config, vagueMinInput: DEFAULT_INSIGHTS_CONFIG.vagueMinInput}); }} className="bg-black/40 border border-white/10 rounded px-3 py-1.5 text-sm w-full focus:border-danger focus:outline-none transition-colors" />
              </div>
            </div>
            <div className="space-y-3">
              <h4 className="text-sm font-medium text-primary">Marathon Sessions</h4>
              <div className="flex flex-col gap-1">
                <label className="text-xs text-gray-400">Min Turns</label>
                <input type="number" value={config.marathonMinTurns} onChange={e => setConfig({...config, marathonMinTurns: e.target.value === '' ? '' as any : Number(e.target.value)})} onBlur={() => { if ((config.marathonMinTurns as any) === '') setConfig({...config, marathonMinTurns: DEFAULT_INSIGHTS_CONFIG.marathonMinTurns}); }} className="bg-black/40 border border-white/10 rounded px-3 py-1.5 text-sm w-full focus:border-primary focus:outline-none transition-colors" />
              </div>
            </div>
            <div className="space-y-3">
              <h4 className="text-sm font-medium text-secondary">Input Heavy</h4>
              <div className="flex flex-col gap-1">
                <label className="text-xs text-gray-400">Min Input Tokens</label>
                <input type="number" value={config.inputHeavyMinInput} onChange={e => setConfig({...config, inputHeavyMinInput: e.target.value === '' ? '' as any : Number(e.target.value)})} onBlur={() => { if ((config.inputHeavyMinInput as any) === '') setConfig({...config, inputHeavyMinInput: DEFAULT_INSIGHTS_CONFIG.inputHeavyMinInput}); }} className="bg-black/40 border border-white/10 rounded px-3 py-1.5 text-sm w-full focus:border-secondary focus:outline-none transition-colors" />
              </div>
              <div className="flex flex-col gap-1">
                <label className="text-xs text-gray-400">Max Output Tokens</label>
                <input type="number" value={config.inputHeavyMaxOutput} onChange={e => setConfig({...config, inputHeavyMaxOutput: e.target.value === '' ? '' as any : Number(e.target.value)})} onBlur={() => { if ((config.inputHeavyMaxOutput as any) === '') setConfig({...config, inputHeavyMaxOutput: DEFAULT_INSIGHTS_CONFIG.inputHeavyMaxOutput}); }} className="bg-black/40 border border-white/10 rounded px-3 py-1.5 text-sm w-full focus:border-secondary focus:outline-none transition-colors" />
              </div>
            </div>
            <div className="space-y-3">
              <h4 className="text-sm font-medium text-success">Tool Heavy</h4>
              <div className="flex flex-col gap-1">
                <label className="text-xs text-gray-400">Min Tool % of Session by token count</label>
                <div className="relative">
                  <input type="number" min="0" max="100" value={config.toolHeavyPercent} onChange={e => setConfig({...config, toolHeavyPercent: e.target.value === '' ? '' as any : Number(e.target.value)})} onBlur={() => { if ((config.toolHeavyPercent as any) === '') setConfig({...config, toolHeavyPercent: DEFAULT_INSIGHTS_CONFIG.toolHeavyPercent}); }} className="bg-black/40 border border-white/10 rounded pl-3 pr-6 py-1.5 text-sm w-full focus:border-success focus:outline-none transition-colors" />
                  <span className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 text-xs">%</span>
                </div>
              </div>
            </div>
          </div>
        </div>
      )}
      
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        
        <div className="glass-panel p-6 animate-slide-up" data-testid="insight-vague">
          <h3 className="text-gray-400 text-sm font-medium">Vague Prompts</h3>
          <p className="text-3xl font-bold mt-2 text-danger flex items-center gap-2">{insights.vaguePrompts} <MessageCircleQuestion size={24} className="opacity-80" /></p>
          <p className="text-xs text-gray-500 mt-2">&lt;{config.vaguePromptLength} chars causing &gt;{formatCompact(config.vagueMinInput)} tokens.</p>
          <p className="text-[10px] text-gray-400 mt-1">Avg Vague: <AnimatedNumber value={insights.avgVagueTokens || 0} compact /> | Avg Non-Vague: <AnimatedNumber value={insights.avgNonVagueTokens || 0} compact /></p>
        </div>

        <div className="glass-panel p-6 animate-slide-up" style={{ animationDelay: '100ms' }} data-testid="insight-marathon">
          <h3 className="text-gray-400 text-sm font-medium">Marathon Sessions</h3>
          <p className="text-3xl font-bold mt-2 text-primary flex items-center gap-2">{insights.marathonSessions} <SportShoe size={24} className="opacity-80" /></p>
          <p className="text-xs text-gray-500 mt-2">Sessions with &gt;{config.marathonMinTurns} turns.</p>
          <p className="text-[10px] text-gray-400 mt-1">Avg Marathon Prompt: <AnimatedNumber value={insights.avgMarathonPromptTokens || 0} compact /> | Avg Non-Marathon: <AnimatedNumber value={insights.avgNonMarathonPromptTokens || 0} compact /></p>
        </div>

        <div className="glass-panel p-6 animate-slide-up" style={{ animationDelay: '200ms' }} data-testid="insight-input">
          <h3 className="text-gray-400 text-sm font-medium">Input Heavy Requests</h3>
          <p className="text-3xl font-bold mt-2 text-secondary flex items-center gap-2">{insights.inputHeavy} <Dumbbell size={24} className="opacity-80" /></p>
          <p className="text-xs text-gray-500 mt-2">&gt;{formatCompact(config.inputHeavyMinInput)} input, &lt;{config.inputHeavyMaxOutput} output.</p>
          <p className="text-[10px] text-gray-400 mt-1">Avg Heavy: <AnimatedNumber value={insights.avgInputHeavyTokens || 0} compact /> | Avg Normal: <AnimatedNumber value={insights.avgNonInputHeavyTokens || 0} compact /></p>
        </div>

        <div className="glass-panel p-6 animate-slide-up" style={{ animationDelay: '300ms' }} data-testid="insight-tool">
          <h3 className="text-gray-400 text-sm font-medium">Tool Heavy Sessions</h3>
          <p className="text-3xl font-bold mt-2 text-success flex items-center gap-2">{insights.toolHeavy} <Wrench size={24} className="opacity-80" /></p>
          <p className="text-xs text-gray-500 mt-2">Sessions with &gt;= {config.toolHeavyPercent}% tool tokens.</p>
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
              const sess = sessions[p.session];
              
              const { isVague, isInputHeavy, isMarathon } = evaluatePromptFlags(
                p.prompt, 
                totalIn, 
                p.out + (p.think || 0), 
                sess?.turnCount || 1, 
                config
              );
              
              const hasToolCall = p.hasToolCall;
              
              const prefixIcons: React.ReactNode[] = [];
              let promptColor = 'text-gray-300';
              
              if (hasToolCall) {
                prefixIcons.push(<Wrench key="wrench" size={14} className="text-emerald-500 shrink-0" />);
                promptColor = 'text-success';
              }
              if (p.think > 0) {
                prefixIcons.push(<Brain key="brain" size={14} className="text-emerald-400 shrink-0" />);
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
                onClick={() => onSessionSelect?.(p.session, p.trace, p.ts, p.turn)}
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
                <div className="grid grid-cols-3 md:grid-cols-7 gap-2 text-xs font-mono w-full md:w-[60%] items-center">
                  <span className="text-gray-400 truncate">In: <span className="text-blue-500">{formatCompact(p.in)}</span></span>
                  <span className="text-gray-400 truncate">CW: <span className="text-pink-500">{formatCompact(p.cacheWrite)}</span></span>
                  <span className="text-gray-400 truncate">CR: <span className="text-purple-500">{formatCompact(p.cacheRead)}</span></span>
                  <span className="text-gray-400 truncate">Think: <span className="text-emerald-400">{formatCompact(p.think)}</span></span>
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
