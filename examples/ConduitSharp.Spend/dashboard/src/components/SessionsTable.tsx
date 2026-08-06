import React, { useState } from 'react';
import type { MetricsData } from '../utils/parser';
import { SessionFlowchart } from './SessionFlowchart';

export const SessionsTable: React.FC<{ metrics: MetricsData }> = ({ metrics }) => {
  const [searchTerm, setSearchTerm] = useState('');
  const [expandedSessionId, setExpandedSessionId] = useState<string | null>(null);
  type SortColumn = 'name' | 'read' | 'written' | 'total';
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

  // Format sessions data
  const sessions = Object.entries(metrics.sessions).map(([id, data]) => ({
    id,
    name: data.sessionName || id,
    read: data.in + data.cacheWrite + data.cacheRead,
    written: data.out,
    turns: data.turnCount,
    rawIn: data.in,
    rawCacheRead: data.cacheRead,
    rawCacheWrite: data.cacheWrite,
    rawOut: data.out
  }));

  const filteredSessions = sessions
    .filter(s => s.name.toLowerCase().includes(searchTerm.toLowerCase()) || s.id.toLowerCase().includes(searchTerm.toLowerCase()))
    .sort((a, b) => {
      let valA, valB;
      if (sortColumn === 'name') {
        valA = a.name.toLowerCase();
        valB = b.name.toLowerCase();
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
                filteredSessions.map((s) => (
                  <React.Fragment key={s.id}>
                    <tr 
                      className={`border-b border-white/5 hover:bg-white/5 transition-colors cursor-pointer ${expandedSessionId === s.id ? 'bg-white/5' : ''}`}
                      onClick={() => toggleExpand(s.id)}
                    >
                      <td className="p-4 max-w-[200px] sm:max-w-[300px]">
                        <div className={`font-medium truncate ${s.name === s.id && s.turns > 15 ? 'text-primary font-bold' : 'text-gray-200'}`}>{s.name}{s.name === s.id && s.turns > 15 ? ' 🏃‍♂️' : ''}</div>
                        {s.name !== s.id && <div className={`text-xs font-mono mt-1 truncate ${s.turns > 15 ? 'text-primary font-bold' : 'text-gray-500'}`}>{s.id}{s.turns > 15 ? ' 🏃‍♂️' : ''}</div>}
                      </td>
                      <td className="p-4 text-right">
                        <div className="font-mono text-gray-300">{s.read.toLocaleString(undefined, { maximumFractionDigits: 0 })}</div>
                        <div className="text-[10px] text-gray-500 font-mono mt-1 flex justify-end gap-1">
                          <span>in:{s.rawIn.toLocaleString(undefined, { maximumFractionDigits: 0 })}</span>
                          <span className="text-gray-600">|</span>
                          <span>cw:{s.rawCacheWrite.toLocaleString(undefined, { maximumFractionDigits: 0 })}</span>
                          <span className="text-gray-600">|</span>
                          <span>cr:{s.rawCacheRead.toLocaleString(undefined, { maximumFractionDigits: 0 })}</span>
                        </div>
                      </td>
                      <td className="p-4 text-right">
                        <div className="font-mono text-gray-300">{s.written.toLocaleString(undefined, { maximumFractionDigits: 0 })}</div>
                        <div className="text-[10px] text-gray-500 font-mono mt-1 flex justify-end">
                          <span>out:{s.rawOut.toLocaleString(undefined, { maximumFractionDigits: 0 })}</span>
                        </div>
                      </td>
                      <td className="p-4 text-right font-mono text-secondary font-bold">{(s.read + s.written).toLocaleString(undefined, { maximumFractionDigits: 0 })}</td>
                    </tr>
                    {expandedSessionId === s.id && (
                      <tr className="bg-black/20 border-b border-white/5">
                        <td colSpan={4} className="p-0">
                          <SessionFlowchart session={metrics.sessions[s.id]} sessionId={s.id} />
                        </td>
                      </tr>
                    )}
                  </React.Fragment>
                ))
              ) : (
                <tr>
                  <td colSpan={4} className="p-6 text-center text-gray-500">
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
};
