import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { WeightsControl } from './WeightsControl';

describe('WeightsControl Component', () => {
  it('expands when button is clicked', () => {
    const setWeightsConfig = vi.fn();
    const setUseWeights = vi.fn();
    render(<WeightsControl models={['modelA']} weightsConfig={{}} setWeightsConfig={setWeightsConfig} useWeights={false} setUseWeights={setUseWeights} />);
    
    const btn = screen.getByRole('button', { name: /Token Weights/i });
    expect(screen.queryByText('Apply to')).not.toBeInTheDocument();
    
    fireEvent.click(btn);
    expect(screen.getByText(/Apply to/)).toBeInTheDocument();
  });

  it('calls setUseWeights when toggle is clicked', () => {
    const setWeightsConfig = vi.fn();
    const setUseWeights = vi.fn();
    render(<WeightsControl models={['modelA']} weightsConfig={{}} setWeightsConfig={setWeightsConfig} useWeights={false} setUseWeights={setUseWeights} />);
    
    // expand
    fireEvent.click(screen.getByRole('button', { name: /Token Weights/i }));
    
    const toggle = screen.getByRole('checkbox');
    fireEvent.click(toggle);
    expect(setUseWeights).toHaveBeenCalledWith(true);
  });
});
