import React, { useState, useEffect } from 'react';
import { Weight, X } from 'lucide-react';
import { type TokenWeights, DEFAULT_WEIGHTS } from '../utils/parser';

interface Props {
  models: string[];
  weightsConfig: Record<string, TokenWeights>;
  setWeightsConfig: React.Dispatch<React.SetStateAction<Record<string, TokenWeights>>>;
  useWeights: boolean;
  setUseWeights: React.Dispatch<React.SetStateAction<boolean>>;
}

export const WeightsControl: React.FC<Props> = ({ models, weightsConfig, setWeightsConfig, useWeights, setUseWeights }) => {
  const [expanded, setExpanded] = useState(false);
  const [selectedModel, setSelectedModel] = useState<string>(models[0] || '');
  
  // Local state for the inputs before hitting apply
  const [localWeights, setLocalWeights] = useState<TokenWeights>(DEFAULT_WEIGHTS);

  // When selected model changes, load its existing config into the inputs, or reset to defaults
  useEffect(() => {
    if (selectedModel && weightsConfig[selectedModel]) {
      setLocalWeights(weightsConfig[selectedModel]);
    } else {
      setLocalWeights(DEFAULT_WEIGHTS);
    }
  }, [selectedModel, weightsConfig]);

  // If models list loads asynchronously and selectedModel is empty, pick the first one
  useEffect(() => {
    if (!selectedModel && models.length > 0) {
      setSelectedModel(models[0]);
    }
  }, [models, selectedModel]);

  if (!expanded) {
    return (
      <button 
        onClick={() => setExpanded(true)}
        className="fixed bottom-6 right-6 flex items-center gap-2 bg-surface/80 backdrop-blur-md border border-white/10 rounded-full px-5 py-3 shadow-lg transition-all hover:scale-105 hover:shadow-[0_0_20px_rgba(6,182,212,0.3)] hover:border-cyan-500/50 hover:bg-surface z-50 group"
      >
        <Weight size={20} className="text-gray-400 group-hover:text-cyan-400 transition-colors" />
        <span className="font-semibold text-sm text-gray-200 group-hover:text-white transition-colors">Token Weights</span>
      </button>
    );
  }

  const handleApply = () => {
    if (!selectedModel) return;
    setWeightsConfig(prev => ({
      ...prev,
      [selectedModel]: { ...localWeights }
    }));
  };

  const handleUpdate = (field: keyof TokenWeights, val: string) => {
    const num = parseFloat(val);
    setLocalWeights(prev => ({
      ...prev,
      [field]: isNaN(num) ? prev[field] : num
    }));
  };

  return (
    <div className="fixed bottom-6 right-6 w-80 bg-gray-800 border border-gray-700 rounded-xl shadow-2xl z-50 overflow-hidden animate-fade-in flex flex-col">
      <div className="bg-gray-900/50 p-4 border-b border-gray-700 flex justify-between items-center">
        <div className="flex items-center gap-2 text-gray-200 font-semibold">
          <Weight size={18} className="text-blue-400" /> Token Weights
        </div>
        <button onClick={() => setExpanded(false)} className="text-gray-400 hover:text-white transition-colors">
          <X size={18} />
        </button>
      </div>

      <div className="p-4 flex flex-col gap-4">
        {/* Master Toggle */}
        <div className="flex items-center justify-between">
          <span className="text-sm font-medium text-gray-300">Enable Weights</span>
          <label className="relative inline-flex items-center cursor-pointer">
            <input 
              type="checkbox" 
              className="sr-only peer" 
              checked={useWeights}
              onChange={(e) => setUseWeights(e.target.checked)}
            />
            <div className="w-11 h-6 bg-gray-700 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-gray-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-blue-500"></div>
          </label>
        </div>

        <hr className="border-gray-700" />

        {/* Model Selection */}
        <div className="flex flex-col gap-1">
          <label className="text-xs font-semibold text-gray-400">Model</label>
          <select 
            value={selectedModel}
            onChange={(e) => setSelectedModel(e.target.value)}
            className="bg-gray-900 border border-gray-600 rounded-lg p-2 text-sm text-gray-200 focus:outline-none focus:border-blue-500"
          >
            {models.map(m => <option key={m} value={m}>{m}</option>)}
          </select>
        </div>

        {/* Inputs */}
        <div className="grid grid-cols-2 gap-3">
          <div className="flex flex-col gap-1">
            <label className="text-xs font-semibold text-gray-400">In</label>
            <input type="number" step="0.1" value={localWeights.in} onChange={e => handleUpdate('in', e.target.value)} className="bg-gray-900 border border-gray-600 rounded-lg p-2 text-sm text-gray-200 focus:outline-none focus:border-blue-500" />
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-xs font-semibold text-gray-400">CW</label>
            <input type="number" step="0.1" value={localWeights.cw} onChange={e => handleUpdate('cw', e.target.value)} className="bg-gray-900 border border-gray-600 rounded-lg p-2 text-sm text-gray-200 focus:outline-none focus:border-blue-500" />
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-xs font-semibold text-gray-400">CR</label>
            <input type="number" step="0.1" value={localWeights.cr} onChange={e => handleUpdate('cr', e.target.value)} className="bg-gray-900 border border-gray-600 rounded-lg p-2 text-sm text-gray-200 focus:outline-none focus:border-blue-500" />
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-xs font-semibold text-gray-400">Out</label>
            <input type="number" step="0.1" value={localWeights.out} onChange={e => handleUpdate('out', e.target.value)} className="bg-gray-900 border border-gray-600 rounded-lg p-2 text-sm text-gray-200 focus:outline-none focus:border-blue-500" />
          </div>
        </div>

        <button 
          onClick={handleApply}
          className="mt-2 bg-blue-600 hover:bg-blue-500 text-white font-medium text-sm py-2 px-4 rounded-lg transition-colors w-full"
        >
          Apply to {selectedModel || 'Model'}
        </button>
      </div>
    </div>
  );
};
