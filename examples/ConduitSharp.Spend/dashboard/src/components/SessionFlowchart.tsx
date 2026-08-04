import React from 'react';
import type { MetricsData } from '../utils/parser';

export const SessionFlowchart: React.FC<{
  session: MetricsData['sessions'][string];
  sessionId: string;
}> = ({ session, sessionId }) => {
  const totalTokens = session.in + session.out + session.cacheRead + session.cacheWrite;
  if (totalTokens === 0 || session.prompts.length === 0) return (
    <div className="p-8 text-center text-gray-500">No token data available for this session.</div>
  );

  const nodeHeight = 145;
  const gap = 25;
  const H = Math.max(300, session.prompts.length * (nodeHeight + gap) + gap);
  const W = 1000;

  const leftX = 20;
  const leftW = 220;
  const leftY = H / 2 - nodeHeight / 2;

  const rightX = 660;
  const rightW = 320;

  const startX = leftX + leftW;
  const startY = H / 2;

  const getThickness = (tokens: number) => {
    if (tokens === 0) return 0;
    return Math.max(2, (tokens / totalTokens) * 100); // max 100px thick
  };

  const createPath = (x1: number, y1: number, x2: number, y2: number) => {
    const dx = Math.abs(x2 - x1) * 0.5;
    return `M ${x1} ${y1} C ${x1 + dx} ${y1}, ${x2 - dx} ${y2}, ${x2} ${y2}`;
  };

  return (
    <div className="w-full overflow-hidden bg-gray-900/40 rounded-xl border border-white/5 animate-fade-in p-2 my-2 shadow-inner">
      <svg viewBox={`0 0 ${W} ${H}`} className="w-full h-auto drop-shadow-lg font-sans">
        
        {/* Draw Paths First (so they are under nodes) */}
        {session.prompts.map((p, i) => {
          const y = gap + i * (nodeHeight + gap);
          
          const inThick = getThickness(p.in);
          const cacheRThick = getThickness(p.cacheRead);
          const cacheWThick = getThickness(p.cacheWrite);
          const outThick = getThickness(p.out);

          return (
            <g key={`paths-${i}`} style={{ mixBlendMode: 'screen' }}>
              {p.in > 0 && (
                <path 
                  d={createPath(startX, startY, rightX, y + 25)} 
                  fill="none" 
                  stroke="#3b82f6" 
                  strokeWidth={inThick} 
                  strokeOpacity="0.5"
                  className="transition-all duration-500 hover:stroke-opacity-100"
                />
              )}
              {p.cacheRead > 0 && (
                <path 
                  d={createPath(startX, startY, rightX, y + 42)} 
                  fill="none" 
                  stroke="#8b5cf6" 
                  strokeWidth={cacheRThick} 
                  strokeOpacity="0.5"
                  className="transition-all duration-500 hover:stroke-opacity-100"
                />
              )}
              {p.cacheWrite > 0 && (
                <path 
                  d={createPath(startX, startY, rightX, y + 55)} 
                  fill="none" 
                  stroke="#ec4899" 
                  strokeWidth={cacheWThick} 
                  strokeOpacity="0.5"
                  className="transition-all duration-500 hover:stroke-opacity-100"
                />
              )}
              {p.out > 0 && (
                <path 
                  d={createPath(startX, startY, rightX, y + 70)} 
                  fill="none" 
                  stroke="#f59e0b" 
                  strokeWidth={outThick} 
                  strokeOpacity="0.5"
                  className="transition-all duration-500 hover:stroke-opacity-100"
                />
              )}
            </g>
          );
        })}

        {/* Left Node */}
        <g transform={`translate(${leftX}, ${leftY})`}>
          <rect width={leftW} height={nodeHeight} rx="12" fill="#1f2937" stroke="#4b5563" strokeWidth="2" className="shadow-lg" />
          <text x={leftW/2} y={nodeHeight/2 - 10} fill="#9ca3af" fontSize="14" textAnchor="middle" fontWeight="600" letterSpacing="1">TOTAL SESSION BUDGET</text>
          <text x={leftW/2} y={nodeHeight/2 + 20} fill="#f3f4f6" fontSize="28" textAnchor="middle" fontWeight="bold">{totalTokens.toLocaleString()}</text>
        </g>

        {/* Right Nodes (Prompts) */}
        {session.prompts.map((p, i) => {
          const y = gap + i * (nodeHeight + gap);
          const rawPrompt = p.prompt || 'Empty Prompt';
          const promptText = rawPrompt.length > 25 ? rawPrompt.substring(0, 25) + '...' : rawPrompt;
          
          let timeStr = '';
          if (p.ts) {
            const d = new Date(p.ts);
            timeStr = d.toISOString().replace('T', ' ').substring(0, 19) + ' UTC';
          }

          return (
            <g key={`node-${i}`} transform={`translate(${rightX}, ${y})`}>
              <rect width={rightW} height={nodeHeight} rx="8" fill="#1f2937" stroke="#374151" strokeWidth="1" />
              <text x="12" y="25" fill="#f3f4f6" fontSize="14" fontWeight="600">
                {timeStr}
              </text>
              <text x={rightW - 12} y="25" fill="#f3f4f6" fontSize="15" textAnchor="end" fontWeight="bold">
                {p.total.toLocaleString()}
              </text>
              <text x="12" y="45" fill="#f3f4f6" fontSize="14">
                {promptText}
              </text>
              <text x="12" y="65" fill="#9ca3af" fontSize="12" className="font-mono">
                {p.model || 'Unknown'}
              </text>
              <text x="12" y="85" fill="#6b7280" fontSize="10" className="font-mono">
                {sessionId}
              </text>
              <text x="12" y="105" fill="#9ca3af" fontSize="11" className="font-mono">
                In:<tspan fill="#3b82f6">{p.in.toLocaleString()}</tspan>|
                CR:<tspan fill="#8b5cf6">{p.cacheRead.toLocaleString()}</tspan>|
                CW:<tspan fill="#ec4899">{p.cacheWrite.toLocaleString()}</tspan>|
                Out:<tspan fill="#f59e0b">{p.out.toLocaleString()}</tspan>
              </text>
              {p.tools > 0 && (
                <text x="12" y="125" fill="#10b981" fontSize="12" className="font-mono">
                  Toolcalls {p.tools}
                </text>
              )}
            </g>
          );
        })}
      </svg>
    </div>
  );
};
