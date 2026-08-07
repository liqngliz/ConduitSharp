import React, { useState } from 'react';
import type { MetricsData } from '../utils/parser';
import { AnimatedNumber } from './AnimatedNumber';
import { MessageCircleQuestion, SportShoe, Dumbbell, Wrench } from 'lucide-react';

type SortOrder = 'latest' | 'oldest' | 'total' | 'in' | 'cw' | 'cr' | 'out';

export interface FlowchartPrompt {
  ts: string;
  firstTs?: string;
  total: number;
  prompt: string;
  in: number;
  cacheRead: number;
  cacheWrite: number;
  out: number;
  hasToolCall?: boolean;
  model: string;
  sessionId: string;
  turnCount: number;
}

export const Flowchart: React.FC<{
  prompts: FlowchartPrompt[];
  totals: { in: number; cw: number; cr: number; out: number };
  title: string;
  headerTitle?: React.ReactNode;
  onPromptClick?: (sessionId: string) => void;
  isExpanded?: boolean;
}> = ({ prompts, totals, title, headerTitle, onPromptClick, isExpanded = true }) => {
  const [sortOrder, setSortOrder] = useState<SortOrder>('latest');

  const totalTokens = totals.in + totals.out + totals.cr + totals.cw;
  if (totalTokens === 0 || prompts.length === 0) return (
    <div className="p-8 text-center text-gray-500">No token data available for this session.</div>
  );

  const sortedPrompts = [...prompts].sort((a, b) => {
    if (sortOrder === 'latest') {
      return new Date(b.ts || 0).getTime() - new Date(a.ts || 0).getTime();
    }
    if (sortOrder === 'oldest') {
      return new Date(a.ts || 0).getTime() - new Date(b.ts || 0).getTime();
    }
    if (sortOrder === 'total') return b.total - a.total;
    if (sortOrder === 'in') return b.in - a.in;
    if (sortOrder === 'cw') return b.cacheWrite - a.cacheWrite;
    if (sortOrder === 'cr') return b.cacheRead - a.cacheRead;
    if (sortOrder === 'out') return b.out - a.out;
    return 0;
  });

  const nodeHeight = 125;
  const gap = 25;

  const midTargetHeight = 4 * (nodeHeight + gap) * 0.8;

  const getThickness = (tokens: number) => {
    if (tokens === 0) return 0;
    return Math.max(2, (tokens / totalTokens) * 100); // max 100px thick
  };

  const getMidHeight = (tokens: number) => {
    if (tokens === 0) return 0;
    return Math.max(40, (tokens / totalTokens) * (midTargetHeight - 45));
  };

  const hIn = getMidHeight(totals.in);
  const hCw = getMidHeight(totals.cw);
  const hCr = getMidHeight(totals.cr);
  const hOut = getMidHeight(totals.out);

  const activeNodesCount = [hIn, hCw, hCr, hOut].filter(h => h > 0).length;
  const actualMidCombined = hIn + hCw + hCr + hOut + Math.max(0, activeNodesCount - 1) * 15;
  const maxTotalHeight = 2 * (nodeHeight + gap) * 0.8;
  const totalHeight = Math.min(maxTotalHeight, actualMidCombined * 0.8);

  const rawH = Math.max(
    300, 
    sortedPrompts.length * (nodeHeight + gap) + gap,
    actualMidCombined + gap * 2,
    totalHeight + gap * 2
  );
  
  // Unclamped viewBox height to allow natural page scrolling
  const H = rawH;
  const W = 1000;

  const midY_start = gap;
  let currentY = midY_start;
  
  const midYIn = currentY;
  if (hIn > 0) currentY += hIn + 15;
  const midYCw = currentY;
  if (hCw > 0) currentY += hCw + 15;
  const midYCr = currentY;
  if (hCr > 0) currentY += hCr + 15;
  const midYOut = currentY;

  const totalY = gap;

  const createPath = (x1: number, y1: number, x2: number, y2: number) => {
    const dx = Math.abs(x2 - x1) * 0.5;
    return `M ${x1} ${y1} C ${x1 + dx} ${y1}, ${x2 - dx} ${y2}, ${x2} ${y2}`;
  };

  return (
    <div className="w-full bg-gray-900/40 rounded-xl border border-white/5 animate-fade-in px-6 py-4 my-2 shadow-inner">
      <div className="flex justify-between items-center mb-2">
        <div>
          {headerTitle && (
            <h3 className="text-xl font-semibold text-white">
              {headerTitle}
            </h3>
          )}
        </div>
        <select 
          value={sortOrder} 
          onChange={(e) => setSortOrder(e.target.value as SortOrder)}
          className="bg-gray-800 border border-gray-700 text-gray-300 text-xs rounded-lg focus:ring-blue-500 focus:border-blue-500 block p-1.5 cursor-pointer outline-none"
        >
          <option value="latest">Sort: Latest</option>
          <option value="oldest">Sort: Oldest</option>
          <option value="total">Sort: Total</option>
          <option value="in">Sort: IN</option>
          <option value="cw">Sort: CW</option>
          <option value="cr">Sort: CR</option>
          <option value="out">Sort: OUT</option>
        </select>
      </div>
      <svg 
          viewBox={`0 0 ${W} ${H}`} 
          className="w-full min-w-[800px] drop-shadow-lg font-sans" 
          style={{ transition: 'all 0.8s ease-in-out', height: 'auto' }}
        >
          
          {/* Draw Paths First (so they are under nodes) */}
          {sortedPrompts.map((p, i) => {
          const y = gap + i * (nodeHeight + gap);
          
          const inThick = getThickness(p.in);
          const cwThick = getThickness(p.cacheWrite);
          const crThick = getThickness(p.cacheRead);
          const outThick = getThickness(p.out);

          const totalNodeThick = inThick + cwThick + crThick + outThick;
          let currentPathY = y + (nodeHeight / 2) - (totalNodeThick / 2);

          const yIn = currentPathY + inThick / 2; currentPathY += inThick;
          const yCw = currentPathY + cwThick / 2; currentPathY += cwThick;
          const yCr = currentPathY + crThick / 2; currentPathY += crThick;
          const yOut = currentPathY + outThick / 2;

          return (
            <g key={`paths-left-${p.sessionId}-${p.firstTs}`} style={{ mixBlendMode: 'screen' }}>
              {p.in > 0 && (
                <path 
                  d={createPath(340, yIn, 440, midYIn + hIn / 2)} 
                  fill="none" stroke="#3b82f6" strokeWidth={inThick} strokeOpacity="0.5"
                  style={{ transition: 'd 0.8s ease-in-out, stroke-opacity 0.8s' }} className="hover:stroke-opacity-100"
                />
              )}
              {p.cacheWrite > 0 && (
                <path 
                  d={createPath(340, yCw, 440, midYCw + hCw / 2)} 
                  fill="none" stroke="#ec4899" strokeWidth={cwThick} strokeOpacity="0.5"
                  style={{ transition: 'd 0.8s ease-in-out, stroke-opacity 0.8s' }} className="hover:stroke-opacity-100"
                />
              )}
              {p.cacheRead > 0 && (
                <path 
                  d={createPath(340, yCr, 440, midYCr + hCr / 2)} 
                  fill="none" stroke="#8b5cf6" strokeWidth={crThick} strokeOpacity="0.5"
                  style={{ transition: 'd 0.8s ease-in-out, stroke-opacity 0.8s' }} className="hover:stroke-opacity-100"
                />
              )}
              {p.out > 0 && (
                <path 
                  d={createPath(340, yOut, 440, midYOut + hOut / 2)} 
                  fill="none" stroke="#f59e0b" strokeWidth={outThick} strokeOpacity="0.5"
                  style={{ transition: 'd 0.8s ease-in-out, stroke-opacity 0.8s' }} className="hover:stroke-opacity-100"
                />
              )}
            </g>
          );
        })}

        {/* Paths from Middle to Right */}
        <g style={{ mixBlendMode: 'screen' }}>
          {(() => {
            const tInThick = getThickness(totals.in);
            const tCwThick = getThickness(totals.cw);
            const tCrThick = getThickness(totals.cr);
            const tOutThick = getThickness(totals.out);
            const totalRightThick = tInThick + tCwThick + tCrThick + tOutThick;
            let rightPathY = totalY + (totalHeight / 2) - (totalRightThick / 2);
            
            const rYIn = rightPathY + tInThick / 2; rightPathY += tInThick;
            const rYCw = rightPathY + tCwThick / 2; rightPathY += tCwThick;
            const rYCr = rightPathY + tCrThick / 2; rightPathY += tCrThick;
            const rYOut = rightPathY + tOutThick / 2;

            return (
              <>
                {hIn > 0 && <path d={createPath(560, midYIn + hIn / 2, 760, rYIn)} fill="none" stroke="#3b82f6" strokeWidth={tInThick} strokeOpacity="0.5" style={{ transition: 'd 0.8s ease-in-out' }} />}
                {hCw > 0 && <path d={createPath(560, midYCw + hCw / 2, 760, rYCw)} fill="none" stroke="#ec4899" strokeWidth={tCwThick} strokeOpacity="0.5" style={{ transition: 'd 0.8s ease-in-out' }} />}
                {hCr > 0 && <path d={createPath(560, midYCr + hCr / 2, 760, rYCr)} fill="none" stroke="#8b5cf6" strokeWidth={tCrThick} strokeOpacity="0.5" style={{ transition: 'd 0.8s ease-in-out' }} />}
                {hOut > 0 && <path d={createPath(560, midYOut + hOut / 2, 760, rYOut)} fill="none" stroke="#f59e0b" strokeWidth={tOutThick} strokeOpacity="0.5" style={{ transition: 'd 0.8s ease-in-out' }} />}
              </>
            );
          })()}
        </g>

        {/* Middle Nodes (IN, CW, CR, OUT) */}
        {hIn > 0 && (
          <g transform={`translate(440, ${midYIn})`} style={{ transition: 'transform 0.8s ease-in-out' }}>
            <rect width="120" height={hIn} rx="6" fill="#1f2937" stroke="#3b82f6" strokeWidth="1" style={{ transition: 'height 0.8s ease-in-out' }} />
            <g transform={`translate(0, ${hIn / 2})`} style={{ transition: 'transform 0.8s ease-in-out' }}>
              <text x="60" y="-4" fill="#9ca3af" fontSize="10" textAnchor="middle" fontWeight="bold">IN</text>
              <text x="60" y="12" fill="#f3f4f6" fontSize="14" textAnchor="middle" fontWeight="bold"><AnimatedNumber value={totals.in} compact as="tspan" /></text>
            </g>
          </g>
        )}
        {hCw > 0 && (
          <g transform={`translate(440, ${midYCw})`} style={{ transition: 'transform 0.8s ease-in-out' }}>
            <rect width="120" height={hCw} rx="6" fill="#1f2937" stroke="#ec4899" strokeWidth="1" style={{ transition: 'height 0.8s ease-in-out' }} />
            <g transform={`translate(0, ${hCw / 2})`} style={{ transition: 'transform 0.8s ease-in-out' }}>
              <text x="60" y="-4" fill="#9ca3af" fontSize="10" textAnchor="middle" fontWeight="bold">CW</text>
              <text x="60" y="12" fill="#f3f4f6" fontSize="14" textAnchor="middle" fontWeight="bold"><AnimatedNumber value={totals.cw} compact as="tspan" /></text>
            </g>
          </g>
        )}
        {hCr > 0 && (
          <g transform={`translate(440, ${midYCr})`} style={{ transition: 'transform 0.8s ease-in-out' }}>
            <rect width="120" height={hCr} rx="6" fill="#1f2937" stroke="#8b5cf6" strokeWidth="1" style={{ transition: 'height 0.8s ease-in-out' }} />
            <g transform={`translate(0, ${hCr / 2})`} style={{ transition: 'transform 0.8s ease-in-out' }}>
              <text x="60" y="-4" fill="#9ca3af" fontSize="10" textAnchor="middle" fontWeight="bold">CR</text>
              <text x="60" y="12" fill="#f3f4f6" fontSize="14" textAnchor="middle" fontWeight="bold"><AnimatedNumber value={totals.cr} compact as="tspan" /></text>
            </g>
          </g>
        )}
        {hOut > 0 && (
          <g transform={`translate(440, ${midYOut})`} style={{ transition: 'transform 0.8s ease-in-out' }}>
            <rect width="120" height={hOut} rx="6" fill="#1f2937" stroke="#f59e0b" strokeWidth="1" style={{ transition: 'height 0.8s ease-in-out' }} />
            <g transform={`translate(0, ${hOut / 2})`} style={{ transition: 'transform 0.8s ease-in-out' }}>
              <text x="60" y="-4" fill="#9ca3af" fontSize="10" textAnchor="middle" fontWeight="bold">OUT</text>
              <text x="60" y="12" fill="#f3f4f6" fontSize="14" textAnchor="middle" fontWeight="bold"><AnimatedNumber value={totals.out} compact as="tspan" /></text>
            </g>
          </g>
        )}

        {/* Right Node (Total) */}
        <g transform={`translate(760, ${totalY})`} style={{ transition: 'transform 0.8s ease-in-out' }}>
          <rect width="220" height={totalHeight} rx="12" fill="#083344" stroke="rgb(6, 182, 212)" strokeWidth="1" style={{ filter: 'drop-shadow(0 0 15px rgba(6, 182, 212, 0.4))', transition: 'height 0.8s ease-in-out' }} />
          <g transform={`translate(0, ${totalHeight/2})`} style={{ transition: 'transform 0.8s ease-in-out' }}>
            <text x="110" y="-10" fill="#9ca3af" fontSize="14" textAnchor="middle" fontWeight="600" letterSpacing="1">{title}</text>
            <text x="110" y="20" fill="#f3f4f6" fontSize="28" textAnchor="middle" fontWeight="bold">
              <AnimatedNumber value={totalTokens} compact as="tspan" />
            </text>
          </g>
        </g>

        {/* Left Nodes (Prompts) */}
        {sortedPrompts.map((p, i) => {
          const y = gap + i * (nodeHeight + gap);
          const rawPrompt = p.prompt || 'Empty Prompt';
          const totalIn = p.in + p.cacheRead + p.cacheWrite;
          const isVague = rawPrompt.length < 30 && totalIn > 1000;
          const isInputHeavy = totalIn > 5000 && p.out < 100;
          
          const prefixIcons: React.ReactNode[] = [];
          let shortenLength = 25;
          let promptColor = '#f3f4f6';
          let iconX = 12;
          
          if (p.hasToolCall) {
            prefixIcons.push(<Wrench key="wrench" x={iconX} y={32} width={14} height={14} className="text-emerald-500" />);
            iconX += 18;
            shortenLength -= 3;
            promptColor = '#10B981';
          }
          if (isInputHeavy) {
            prefixIcons.push(<Dumbbell key="dumbbell" x={iconX} y={32} width={14} height={14} className="text-cyan-500" />);
            iconX += 18;
            shortenLength -= 3;
            promptColor = '#06B6D4';
          }
          if (isVague) {
            prefixIcons.push(<MessageCircleQuestion key="vague" x={iconX} y={32} width={14} height={14} className="text-rose-500" />);
            iconX += 18;
            shortenLength -= 3;
            promptColor = '#F43F5E';
          }
          
          const truncated = rawPrompt.length > shortenLength ? rawPrompt.substring(0, shortenLength) + '...' : rawPrompt;
          
          let timeStr = '';
          if (p.ts) {
            const d = new Date(p.ts);
            timeStr = d.toISOString().replace('T', ' ').substring(0, 19) + ' UTC';
          }

          return (
            <g 
              key={`node-${p.sessionId}-${p.firstTs || p.ts}`} 
              transform={`translate(20, ${y})`}
              style={{ transition: 'transform 0.8s ease-in-out' }}
            >
              <rect width="320" height={nodeHeight} rx="8" fill="#1f2937" stroke="#374151" strokeWidth="1" />
              <text x="12" y="25" fill="#f3f4f6" fontSize="14" fontWeight="600">
                {timeStr}
              </text>
              <text x={320 - 12} y="25" fill="#f3f4f6" fontSize="15" textAnchor="end" fontWeight="bold">
                <AnimatedNumber value={p.total} compact as="tspan" disableAnimation={!isExpanded && i >= 4} />
              </text>
              {prefixIcons.length > 0 ? (
                <g>
                  {prefixIcons}
                  <text x={iconX} y={43} fill={promptColor} fontSize="14">
                    {truncated}
                  </text>
                </g>
              ) : (
                <text x="12" y="43" fill={promptColor} fontSize="14">
                  {truncated}
                </text>
              )}
              <text x="12" y="65" fill="#9ca3af" fontSize="12" className="font-mono">
                {p.model || 'Unknown'}
              </text>
              
              {p.turnCount > 15 ? (
                <g>
                  <text x="12" y="85" fill="#6D28D9" fontSize="10" fontWeight="bold" className="font-mono">
                    {p.sessionId}
                  </text>
                  <SportShoe x={12 + p.sessionId.length * 6 + 6} y={75} width={12} height={12} className="text-purple-600" />
                </g>
              ) : (
                <text x="12" y="85" fill="#6b7280" fontSize="10" className="font-mono font-bold">
                  {p.sessionId}
                </text>
              )}
              <text x="12" y="105" fill="#9ca3af" fontSize="11" className="font-mono">
                In:<tspan fill="#3b82f6"><AnimatedNumber value={p.in} compact as="tspan" disableAnimation={!isExpanded && i >= 4} /></tspan>|
                CW:<tspan fill="#ec4899"><AnimatedNumber value={p.cacheWrite} compact as="tspan" disableAnimation={!isExpanded && i >= 4} /></tspan>|
                CR:<tspan fill="#8b5cf6"><AnimatedNumber value={p.cacheRead} compact as="tspan" disableAnimation={!isExpanded && i >= 4} /></tspan>|
                Out:<tspan fill="#f59e0b"><AnimatedNumber value={p.out} compact as="tspan" disableAnimation={!isExpanded && i >= 4} /></tspan>
              </text>

              {onPromptClick && (
                <rect 
                  width="320" height={nodeHeight} rx="8" 
                  fill="transparent" 
                  className="cursor-pointer hover:fill-white/5 transition-colors"
                  onClick={() => onPromptClick(p.sessionId)}
                />
              )}
            </g>
          );
        })}
      </svg>
    </div>
  );
};

export const SessionFlowchart: React.FC<{
  session: MetricsData['sessions'][string];
  sessionId: string;
}> = ({ session, sessionId }) => {
  const flowchartPrompts: FlowchartPrompt[] = session.prompts.map(p => ({
    ...p,
    sessionId,
    turnCount: session.turnCount
  }));

  const totals = {
    in: session.in,
    cw: session.cacheWrite,
    cr: session.cacheRead,
    out: session.out
  };

  return <Flowchart prompts={flowchartPrompts} totals={totals} title="TOTAL SESSION BUDGET" />;
};

