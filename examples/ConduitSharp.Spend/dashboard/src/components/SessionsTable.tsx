import React, { useState, useEffect } from 'react';
import type { MetricsData } from '../utils/parser';
import { SessionFlowchart } from './SessionFlowchart';
import { SportShoe, Wrench } from 'lucide-react';

const formatCompact = (num: number) => Intl.NumberFormat('en-US', { notation: 'compact', maximumSignificantDigits: 3 }).format(num);

export const SessionsTable = React.memo(({ metrics, focusedSession, onSessionClear }: { metrics: MetricsData; focusedSession?: string | null; onSessionClear?: () => void }) => {
  const [searchTerm, setSearchTerm] = useState('');
  const [expandedSessionId, setExpandedSessionId] = useState<string | null>(null);
  type SortColumn = 'name' | 'lastActive' | 'read' | 'written' | 'total';
  const [sortColumn, setSortColumn] = useState<SortColumn>('total');
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('desc');

  const handleSort = (column: SortColumn) => {
    if (sortColumn === column) {
      setSortDirection(prev => prev === 'asc' ? 'desc' : 'asc');
    } else {
      setSortColumn(column);
      setSortDirection('desc');
    }
  };

  const toggleExpand = (id: string) => {
    setExpandedSessionId(prev => prev === id ? null : id);
  };

  const getSortIcon = (col: SortColumn) => {
    if (sortColumn !== col) return <span className="text-primary ml-1">-</span>;
    return sortDirection === 'asc' ? <span className="text-primary ml-1">↑</span> : <span className="text-primary ml-1">↓</span>;
  };

  useEffect(() => {
    if (focusedSession) {
      setExpandedSessionId(focusedSession);
      setTimeout(() => {
        const row = document.getElementById(`session-row-${focusedSession}`);
        if (row) {
          row.scrollIntoView({ behavior: 'smooth', block: 'center' });
          row.classList.add('bg-white/20');
          row.style.transition = 'background-color 0.5s ease-out';
          setTimeout(() => {
            row.classList.remove('bg-white/20');
          }, 1500);
        }
      }, 100);
      onSessionClear?.();
    }
  }, [focusedSession, onSessionClear]);

  // Format sessions data
  const sessions = Object.entries(metrics.sessions).map(([sessionId, session]) => {
    const lastActive = Math.max(...session.prompts.map(p => new Date(p.ts || 0).getTime()), 0);

    return {
      id: sessionId,
      name: session.sessionName || sessionId,
      read: session.cacheRead + session.in,
      written: session.out,
      rawIn: session.in,
      rawOut: session.out,
      rawCacheRead: session.cacheRead,
      rawCacheWrite: session.cacheWrite,
      turns: session.turnCount,
      lastActive,
      session
    };
  });

  const filteredSessions = sessions
    .filter(s => s.name.toLowerCase().includes(searchTerm.toLowerCase()) || s.id.toLowerCase().includes(searchTerm.toLowerCase()))
    .sort((a, b) => {
      let valA, valB;
      if (sortColumn === 'name') {
        valA = a.name.toLowerCase();
        valB = b.name.toLowerCase();
      } else if (sortColumn === 'lastActive') {
        valA = a.lastActive;
        valB = b.lastActive;
      } else if (sortColumn === 'read') {
        valA = a.read;
        valB = b.read;
      } else if (sortColumn === 'written') {
        valA = a.written;
        valB = b.written;
      } else {
        valA = a.read + a.written;
        valB = b.read + b.written;
      }

      if (valA < valB) return sortDirection === 'asc' ? -1 : 1;
      if (valA > valB) return sortDirection === 'asc' ? 1 : -1;
      return 0;
    });

  return (
    <div className="space-y-6 mt-8">
      <div className="flex flex-col md:flex-row justify-between items-center gap-4">
        <h2 className="text-2xl font-bold glow-text">Sessions</h2>
        <input
          type="text"
          placeholder="Search sessions..."
          value={searchTerm}
          onChange={(e) => setSearchTerm(e.target.value)}
          className="bg-gray-800/50 border border-gray-700 rounded px-4 py-2 text-sm text-gray-200 focus:outline-none focus:border-primary w-full md:w-64"
        />
      </div>

      <div className="glass-panel overflow-hidden animate-slide-up">
        <div className="overflow-x-auto">
          <table className="w-full text-left border-collapse">
            <thead>
              <tr className="bg-white/5 border-b border-white/10 select-none">
                <th className="p-4 text-sm font-semibold text-gray-400 cursor-pointer hover:text-white transition-colors whitespace-nowrap" onClick={() => handleSort('name')}>
                  Session Name / ID {getSortIcon('name')}
                </th>
                <th className="p-4 text-sm font-semibold text-gray-400 cursor-pointer hover:text-white transition-colors whitespace-nowrap" onClick={() => handleSort('lastActive')}>
                  Last Active {getSortIcon('lastActive')}
                </th>
                <th className="p-4 text-sm font-semibold text-gray-400 text-right cursor-pointer hover:text-white transition-colors whitespace-nowrap" onClick={() => handleSort('read')}>
                  Input {getSortIcon('read')}
                </th>
                <th className="p-4 text-sm font-semibold text-gray-400 text-right cursor-pointer hover:text-white transition-colors whitespace-nowrap" onClick={() => handleSort('written')}>
                  Output {getSortIcon('written')}
                </th>
                <th className="p-4 text-sm font-semibold text-gray-400 text-right cursor-pointer hover:text-white transition-colors whitespace-nowrap" onClick={() => handleSort('total')}>
                  Tokens {getSortIcon('total')}
                </th>
              </tr>
            </thead>
            <tbody>
              {filteredSessions.length > 0 ? (
                filteredSessions.map((s) => {
                  const isMarathon = s.name === s.id && s.turns > 15;
                  const isTool = s.session.isToolHeavy;
                  const nameColor = isTool ? 'text-success font-bold' : (isMarathon ? 'text-primary font-bold' : 'text-white');
                  const subColor = s.turns > 15 ? 'text-primary font-bold' : 'text-gray-400';

                  return (
                  <React.Fragment key={s.id}>
                    <tr 
                      id={`session-row-${s.id}`}
                      className={`border-b border-white/5 hover:bg-white/5 transition-colors cursor-pointer ${expandedSessionId === s.id ? 'bg-white/5' : ''}`}
                      onClick={() => toggleExpand(s.id)}
                    >
                      <td className="p-4 max-w-[200px] sm:max-w-[300px]">
                        <div className={`flex items-center gap-1.5 font-medium truncate ${nameColor}`}>
                          {isTool && <Wrench size={14} className="text-emerald-500 shrink-0" />}
                          <span className="truncate">{s.name}</span>
                          {isMarathon && <SportShoe size={14} className="text-primary shrink-0" />}
                        </div>
                        {s.name !== s.id && (
                          <div className={`flex items-center gap-1.5 text-xs font-mono mt-1 truncate ${subColor}`}>
                            <span className="truncate">{s.id}</span>
                            {s.turns > 15 && <SportShoe size={12} className="text-primary shrink-0" />}
                          </div>
                        )}
                      </td>
                      <td className="p-4 text-gray-400 text-xs">
                        {s.lastActive > 0 ? new Date(s.lastActive).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }) : 'Unknown'}
                      </td>
                      <td className="p-4 text-right">
                        <div className="font-mono text-gray-300">{formatCompact(s.read)}</div>
                        <div className="text-[10px] text-gray-500 font-mono mt-1 flex justify-end gap-1">
                          <span>in:<span className="text-blue-500">{formatCompact(s.rawIn)}</span></span>
                          <span className="text-gray-600">|</span>
                          <span>cw:<span className="text-pink-500">{formatCompact(s.rawCacheWrite)}</span></span>
                          <span className="text-gray-600">|</span>
                          <span>cr:<span className="text-purple-500">{formatCompact(s.rawCacheRead)}</span></span>
                        </div>
                      </td>
                      <td className="p-4 text-right">
                        <div className="font-mono text-gray-300">{formatCompact(s.written)}</div>
                        <div className="text-[10px] text-gray-500 font-mono mt-1 flex justify-end">
                          <span>out:<span className="text-amber-500">{formatCompact(s.rawOut)}</span></span>
                        </div>
                      </td>
                      <td className="p-4 text-right font-mono text-secondary font-bold">{formatCompact(s.read + s.written)}</td>
                    </tr>
                    {expandedSessionId === s.id && (
                      <tr className="bg-black/20 border-b border-white/5">
                        <td colSpan={5} className="p-0">
                          <SessionFlowchart session={metrics.sessions[s.id]} sessionId={s.id} />
                        </td>
                      </tr>
                    )}
                  </React.Fragment>
                  );
                })
              ) : (
                <tr>
                  <td colSpan={5} className="p-6 text-center text-gray-500">
                    No sessions match your search.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
});
