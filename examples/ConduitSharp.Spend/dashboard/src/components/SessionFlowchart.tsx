import React, { useState } from 'react';
import type { MetricsData } from '../utils/parser';

const formatCompact = (num: number) => Intl.NumberFormat('en-US', { notation: 'compact', maximumSignificantDigits: 3 }).format(num);

type SortOrder = 'latest' | 'oldest' | 'total' | 'in' | 'cw' | 'cr' | 'out';

export interface FlowchartPrompt {
  ts: string;
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
  onPromptClick?: (sessionId: string) => void;
}> = ({ prompts, totals, title, onPromptClick }) => {
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

  const nodeHeight = 145;
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
    <div className="w-full bg-gray-900/40 rounded-xl border border-white/5 animate-fade-in p-4 my-2 shadow-inner">
      <div className="flex justify-end items-center mb-2">
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

          return (
            <g key={`paths-left-${p.ts}-${p.total}`} style={{ mixBlendMode: 'screen' }}>
              {p.in > 0 && (
                <path 
                  d={createPath(340, y + 25, 440, midYIn + hIn / 2)} 
                  fill="none" stroke="#3b82f6" strokeWidth={inThick} strokeOpacity="0.5"
                  style={{ transition: 'd 0.8s ease-in-out, stroke-opacity 0.8s' }} className="hover:stroke-opacity-100"
                />
              )}
              {p.cacheWrite > 0 && (
                <path 
                  d={createPath(340, y + 42, 440, midYCw + hCw / 2)} 
                  fill="none" stroke="#ec4899" strokeWidth={cwThick} strokeOpacity="0.5"
                  style={{ transition: 'd 0.8s ease-in-out, stroke-opacity 0.8s' }} className="hover:stroke-opacity-100"
                />
              )}
              {p.cacheRead > 0 && (
                <path 
                  d={createPath(340, y + 55, 440, midYCr + hCr / 2)} 
                  fill="none" stroke="#8b5cf6" strokeWidth={crThick} strokeOpacity="0.5"
                  style={{ transition: 'd 0.8s ease-in-out, stroke-opacity 0.8s' }} className="hover:stroke-opacity-100"
                />
              )}
              {p.out > 0 && (
                <path 
                  d={createPath(340, y + 70, 440, midYOut + hOut / 2)} 
                  fill="none" stroke="#f59e0b" strokeWidth={outThick} strokeOpacity="0.5"
                  style={{ transition: 'd 0.8s ease-in-out, stroke-opacity 0.8s' }} className="hover:stroke-opacity-100"
                />
              )}
            </g>
          );
        })}

        {/* Paths from Middle to Right */}
        <g style={{ mixBlendMode: 'screen' }}>
          {hIn > 0 && (
            <path d={createPath(560, midYIn + hIn / 2, 760, totalY + totalHeight * 0.2)} fill="none" stroke="#3b82f6" strokeWidth={getThickness(totals.in)} strokeOpacity="0.5" style={{ transition: 'd 0.8s ease-in-out' }} />
          )}
          {hCw > 0 && (
            <path d={createPath(560, midYCw + hCw / 2, 760, totalY + totalHeight * 0.4)} fill="none" stroke="#ec4899" strokeWidth={getThickness(totals.cw)} strokeOpacity="0.5" style={{ transition: 'd 0.8s ease-in-out' }} />
          )}
          {hCr > 0 && (
            <path d={createPath(560, midYCr + hCr / 2, 760, totalY + totalHeight * 0.6)} fill="none" stroke="#8b5cf6" strokeWidth={getThickness(totals.cr)} strokeOpacity="0.5" style={{ transition: 'd 0.8s ease-in-out' }} />
          )}
          {hOut > 0 && (
            <path d={createPath(560, midYOut + hOut / 2, 760, totalY + totalHeight * 0.8)} fill="none" stroke="#f59e0b" strokeWidth={getThickness(totals.out)} strokeOpacity="0.5" style={{ transition: 'd 0.8s ease-in-out' }} />
          )}
        </g>

        {/* Middle Nodes (IN, CW, CR, OUT) */}
        {hIn > 0 && (
          <g transform={`translate(440, ${midYIn})`} style={{ transition: 'transform 0.8s ease-in-out' }}>
            <rect width="120" height={hIn} rx="6" fill="#1f2937" stroke="#3b82f6" strokeWidth="1" style={{ transition: 'height 0.8s ease-in-out' }} />
            <text x="60" y={hIn / 2 - 4} fill="#9ca3af" fontSize="10" textAnchor="middle" fontWeight="bold">IN</text>
            <text x="60" y={hIn / 2 + 12} fill="#f3f4f6" fontSize="14" textAnchor="middle" fontWeight="bold">{formatCompact(totals.in)}</text>
          </g>
        )}
        {hCw > 0 && (
          <g transform={`translate(440, ${midYCw})`} style={{ transition: 'transform 0.8s ease-in-out' }}>
            <rect width="120" height={hCw} rx="6" fill="#1f2937" stroke="#ec4899" strokeWidth="1" style={{ transition: 'height 0.8s ease-in-out' }} />
            <text x="60" y={hCw / 2 - 4} fill="#9ca3af" fontSize="10" textAnchor="middle" fontWeight="bold">CW</text>
            <text x="60" y={hCw / 2 + 12} fill="#f3f4f6" fontSize="14" textAnchor="middle" fontWeight="bold">{formatCompact(totals.cw)}</text>
          </g>
        )}
        {hCr > 0 && (
          <g transform={`translate(440, ${midYCr})`} style={{ transition: 'transform 0.8s ease-in-out' }}>
            <rect width="120" height={hCr} rx="6" fill="#1f2937" stroke="#8b5cf6" strokeWidth="1" style={{ transition: 'height 0.8s ease-in-out' }} />
            <text x="60" y={hCr / 2 - 4} fill="#9ca3af" fontSize="10" textAnchor="middle" fontWeight="bold">CR</text>
            <text x="60" y={hCr / 2 + 12} fill="#f3f4f6" fontSize="14" textAnchor="middle" fontWeight="bold">{formatCompact(totals.cr)}</text>
          </g>
        )}
        {hOut > 0 && (
          <g transform={`translate(440, ${midYOut})`} style={{ transition: 'transform 0.8s ease-in-out' }}>
            <rect width="120" height={hOut} rx="6" fill="#1f2937" stroke="#f59e0b" strokeWidth="1" style={{ transition: 'height 0.8s ease-in-out' }} />
            <text x="60" y={hOut / 2 - 4} fill="#9ca3af" fontSize="10" textAnchor="middle" fontWeight="bold">OUT</text>
            <text x="60" y={hOut / 2 + 12} fill="#f3f4f6" fontSize="14" textAnchor="middle" fontWeight="bold">{formatCompact(totals.out)}</text>
          </g>
        )}

        {/* Right Node (Total) */}
        <g transform={`translate(760, ${totalY})`} style={{ transition: 'transform 0.8s ease-in-out' }}>
          <rect width="220" height={totalHeight} rx="12" fill="#1f2937" stroke="#06B6D4" strokeWidth="2" className="shadow-lg" style={{ transition: 'height 0.8s ease-in-out' }} />
          <text x="110" y={totalHeight/2 - 10} fill="#9ca3af" fontSize="14" textAnchor="middle" fontWeight="600" letterSpacing="1">{title}</text>
          <text x="110" y={totalHeight/2 + 20} fill="#f3f4f6" fontSize="28" textAnchor="middle" fontWeight="bold">
            {formatCompact(totalTokens)}
          </text>
        </g>

        {/* Left Nodes (Prompts) */}
        {sortedPrompts.map((p, i) => {
          const y = gap + i * (nodeHeight + gap);
          const rawPrompt = p.prompt || 'Empty Prompt';
          const totalIn = p.in + p.cacheRead + p.cacheWrite;
          const isVague = rawPrompt.length < 30 && totalIn > 1000;
          const isInputHeavy = totalIn > 5000 && p.out < 100;
          
          let prefix = '';
          let shortenLength = 25;
          let promptColor = '#f3f4f6';
          
          if (p.hasToolCall) {
            prefix += '🔧 ';
            shortenLength -= 3;
            promptColor = '#10B981';
          }
          if (isInputHeavy) {
            prefix += '🏋️‍♂️ ';
            shortenLength -= 3;
            promptColor = '#06B6D4'; // overrides color
          }
          if (isVague) {
            prefix += '❓ ';
            shortenLength -= 3;
            promptColor = '#F43F5E'; // overrides color
          }
          
          const truncated = rawPrompt.length > shortenLength ? rawPrompt.substring(0, shortenLength) + '...' : rawPrompt;
          const promptText = prefix + truncated;
          
          let timeStr = '';
          if (p.ts) {
            const d = new Date(p.ts);
            timeStr = d.toISOString().replace('T', ' ').substring(0, 19) + ' UTC';
          }

          return (
            <g 
              key={`node-${p.ts}-${p.total}`} 
              transform={`translate(20, ${y})`}
              style={{ transition: 'transform 0.8s ease-in-out' }}
            >
              <rect width="320" height={nodeHeight} rx="8" fill="#1f2937" stroke="#374151" strokeWidth="1" />
              <text x="12" y="25" fill="#f3f4f6" fontSize="14" fontWeight="600">
                {timeStr}
              </text>
              <text x={320 - 12} y="25" fill="#f3f4f6" fontSize="15" textAnchor="end" fontWeight="bold">
                {formatCompact(p.total)}
              </text>
              <text x="12" y="45" fill={promptColor} fontSize="14">
                {promptText}
              </text>
              <text x="12" y="65" fill="#9ca3af" fontSize="12" className="font-mono">
                {p.model || 'Unknown'}
              </text>
              <text x="12" y="85" fill={p.turnCount > 15 ? '#6D28D9' : '#6b7280'} fontSize="10" className="font-mono font-bold">
                {p.sessionId}{p.turnCount > 15 ? ' 🏃‍♂️' : ''}
              </text>
              <text x="12" y="105" fill="#9ca3af" fontSize="11" className="font-mono">
                In:<tspan fill="#3b82f6">{formatCompact(p.in)}</tspan>|
                CW:<tspan fill="#ec4899">{formatCompact(p.cacheWrite)}</tspan>|
                CR:<tspan fill="#8b5cf6">{formatCompact(p.cacheRead)}</tspan>|
                Out:<tspan fill="#f59e0b">{formatCompact(p.out)}</tspan>
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

