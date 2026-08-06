import React from 'react';
import {
  BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip as RechartsTooltip, Legend, ResponsiveContainer, PieChart, Pie, Cell
} from 'recharts';
import type { MetricsData } from '../utils/parser';

const COLORS = ['#8b5cf6', '#3b82f6', '#10b981', '#f59e0b', '#ef4444', '#ec4899', '#6366f1'];

export const Charts: React.FC<{ metrics: MetricsData }> = ({ metrics }) => {
  // Format daily usage data
  const dailyData = Object.entries(metrics.dailyUsage)
    .map(([date, counts]) => ({
      date,
      In: counts.in,
      CW: counts.cacheWrite,
      CR: counts.cacheRead,
      Out: counts.out,
    }))
    .sort((a, b) => a.date.localeCompare(b.date));

  // Format model breakdown data
  const modelData = Object.entries(metrics.modelBreakdown)
    .map(([model, counts]) => ({
      model,
      In: counts.in,
      CW: counts.cacheWrite,
      CR: counts.cacheRead,
      Out: counts.out,
      Total: counts.in + counts.cacheWrite + counts.cacheRead + counts.out
    }))
    .filter(d => d.Total > 0)
    .sort((a, b) => b.Total - a.Total);

  return (
    <div className="space-y-6 mt-8">
      <h2 className="text-2xl font-bold glow-text">Usage Charts</h2>
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        
        {/* Daily Usage Chart */}
        <div className="glass-panel p-6 animate-slide-up" data-testid="chart-daily">
          <h3 className="text-xl font-semibold mb-4 text-center">Tokens per Day</h3>
          <div className="w-full h-72">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={dailyData} margin={{ top: 20, right: 30, left: 20, bottom: 5 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="#374151" />
                <XAxis dataKey="date" stroke="#9ca3af" />
                <YAxis stroke="#9ca3af" />
                <RechartsTooltip
                  contentStyle={{ backgroundColor: '#1f2937', borderColor: '#374151', color: '#fff' }}
                  itemStyle={{ color: '#e5e7eb' }}
                  formatter={(value: any) => Number(value).toLocaleString(undefined, { maximumFractionDigits: 0 })}
                />
                <Legend />
                <Bar dataKey="In" stackId="a" fill="#3b82f6" name="In" />
                <Bar dataKey="CW" stackId="a" fill="#ec4899" name="Cache Write" />
                <Bar dataKey="CR" stackId="a" fill="#8b5cf6" name="Cache Read" />
                <Bar dataKey="Out" stackId="a" fill="#f59e0b" name="Out" />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </div>

        {/* Model Breakdown Chart */}
        <div className="glass-panel p-6 animate-slide-up" style={{ animationDelay: '100ms' }} data-testid="chart-model">
          <h3 className="text-xl font-semibold mb-4 text-center">Tokens by Model</h3>
          <div className="w-full h-72">
            <ResponsiveContainer width="100%" height="100%">
              <PieChart margin={{ top: 20, right: 30, left: 20, bottom: 5 }}>
                <Pie
                  data={modelData}
                  dataKey="Total"
                  nameKey="model"
                  cx="50%"
                  cy="50%"
                  outerRadius={100}
                  label={({ percent }) => `${percent !== undefined ? (percent * 100).toFixed(0) : 0}%`}
                >
                  {modelData.map((_, index) => (
                    <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
                  ))}
                </Pie>
                <RechartsTooltip
                  contentStyle={{ backgroundColor: '#1f2937', borderColor: '#374151', color: '#fff' }}
                  itemStyle={{ color: '#e5e7eb' }}
                  formatter={(value: any) => Number(value).toLocaleString(undefined, { maximumFractionDigits: 0 })}
                />
                <Legend />
              </PieChart>
            </ResponsiveContainer>
          </div>
        </div>

      </div>
    </div>
  );
};
