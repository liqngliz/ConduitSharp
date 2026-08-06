import React, { useState, useMemo } from 'react';
import type { MetricsData } from '../utils/parser';
import { Flowchart, type FlowchartPrompt } from './SessionFlowchart';

export const ActiveFlow = React.memo<{ 
  metrics: MetricsData; 
  onSessionSelect?: (sessionId: string) => void;
}>(({ metrics, onSessionSelect }) => {
  const [isExpanded, setIsExpanded] = useState(false);

  // Extract and sort all prompts globally
  const { prompts, totals } = useMemo(() => {
    const allPrompts: FlowchartPrompt[] = [];
    const accTotals = { in: 0, cw: 0, cr: 0, out: 0 };

    Object.entries(metrics.sessions).forEach(([sessionId, session]) => {
      session.prompts.forEach(p => {
        allPrompts.push({
          ...p,
          sessionId,
          turnCount: session.turnCount
        });
        accTotals.in += p.in;
        accTotals.cw += p.cacheWrite;
        accTotals.cr += p.cacheRead;
        accTotals.out += p.out;
      });
    });

    // Sort by timestamp descending
    allPrompts.sort((a, b) => new Date(b.ts || 0).getTime() - new Date(a.ts || 0).getTime());

    return { prompts: allPrompts, totals: accTotals };
  }, [metrics.sessions]);

  if (prompts.length === 0) {
    return (
      <div className="space-y-4 mt-8 mb-8">
        <h2 className="text-2xl font-bold glow-text">Active Flow</h2>
        <div className="glass-panel p-16 flex flex-col items-center justify-center min-h-[300px]">
          <div className="text-primary animate-pulse flex items-center justify-center">
            <svg width="64" height="64" viewBox="0 0 100 100" fill="none" stroke="currentColor" strokeWidth="4">
              <path d="M 30,50 C 15,20 15,80 30,50 C 45,20 55,20 70,50 C 85,80 85,20 70,50 C 55,80 45,80 30,50 Z" />
            </svg>
          </div>
          <p className="text-gray-400 mt-4 text-lg font-mono tracking-widest">WAITING FOR PROMPTS...</p>
        </div>
      </div>
    );
  }

  // Node height is 125, gap is 25 in Flowchart
  // We want to show ~4.5 prompts when collapsed.
  // Height calculation: gap + 4 * (125 + 25) + (125 / 2) = 25 + 600 + 62.5 = 687.5
  // But Flowchart has some padding in its container.
  const collapsedHeight = 730; // roughly 4.5 nodes

  const needsCollapse = prompts.length >= 5;

  return (
    <div className="space-y-4 mt-8 mb-8">
      <div className="relative">
        <div 
          className="overflow-hidden transition-all duration-1000 ease-in-out relative"
          style={{ 
            maxHeight: isExpanded || !needsCollapse ? '10000px' : `${collapsedHeight}px`,
            WebkitMaskImage: !isExpanded && needsCollapse ? 'linear-gradient(to bottom, black 85%, transparent 100%)' : 'none',
            maskImage: !isExpanded && needsCollapse ? 'linear-gradient(to bottom, black 85%, transparent 100%)' : 'none'
          }}
        >
          <Flowchart 
            prompts={prompts} 
            totals={totals} 
            title="TOTAL TOKEN FLOW" 
            headerTitle="Active Flow"
            onPromptClick={onSessionSelect}
            isExpanded={isExpanded}
          />
        </div>
        
        {!isExpanded && needsCollapse && (
          <div className="absolute bottom-0 left-0 w-full h-32 bg-gradient-to-t from-background to-transparent flex items-end justify-center pb-4 z-10 pointer-events-none">
            <button 
              onClick={() => setIsExpanded(true)}
              className="glass-panel hover:bg-white/10 text-white px-6 py-2 rounded-full font-semibold shadow-[0_0_15px_rgba(255,255,255,0.1)] backdrop-blur-md transition-all hover:scale-105 border border-white/20 pointer-events-auto"
            >
              See {prompts.length - 4} more prompts ↓
            </button>
          </div>
        )}
      </div>

      {isExpanded && needsCollapse && (
        <div className="flex justify-center mt-4">
          <button 
            onClick={() => setIsExpanded(false)}
            className="text-gray-400 hover:text-white underline text-sm transition-colors"
          >
            Collapse Active Flow ↑
          </button>
        </div>
      )}
    </div>
  );
});
