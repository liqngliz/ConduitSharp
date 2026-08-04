import { render, screen } from '@testing-library/react';
import { Insights } from './Insights';
import type { InsightsData } from '../utils/parser';
import { describe, it, expect } from 'vitest';

const mockInsights: InsightsData = {
  vaguePrompts: 5,
  marathonSessions: 2,
  inputHeavy: 10,
  toolHeavy: 1,
  routeDominance: { route: 'Claude', percent: 65 },
};

describe('Insights Component', () => {
  it('renders correctly', () => {
    render(<Insights insights={mockInsights} />);
    expect(screen.getByText('5')).toBeInTheDocument(); // Vague
    expect(screen.getByText('2')).toBeInTheDocument(); // Marathon
    expect(screen.getByText('10')).toBeInTheDocument(); // Input Heavy
    expect(screen.getByText('1')).toBeInTheDocument(); // Tool Heavy
  });

  it('renders route dominance', () => {
    render(<Insights insights={mockInsights} />);
    expect(screen.getByText(/Route Dominance Detected:/i)).toBeInTheDocument();
    expect(screen.getByText('Claude')).toBeInTheDocument();
  });
});
