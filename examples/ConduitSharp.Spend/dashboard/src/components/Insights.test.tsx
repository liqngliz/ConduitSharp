import { render, screen } from '@testing-library/react';
import { Insights } from './Insights';
import type { InsightsData } from '../utils/parser';
import { describe, it, expect } from 'vitest';

const mockInsights: InsightsData = {
  vaguePrompts: 5,
  marathonSessions: 2,
  inputHeavy: 10,
  toolHeavy: 1,
  globalToolPrompts: 15,
  globalChatPrompts: 50,
  avgToolTokens: 100,
  avgChatTokens: 50,
  avgVagueTokens: 200,
  avgNonVagueTokens: 50,
  avgMarathonPromptTokens: 300,
  avgNonMarathonPromptTokens: 100,
  avgInputHeavyTokens: 400,
  avgNonInputHeavyTokens: 20,
  modelDominance: { model: 'Claude', percent: 65 },
};

const mockTopPrompts = [
  { prompt: 'Hello world', in: 500, cacheRead: 0, cacheWrite: 0, out: 1000, totalTokens: 1500, session: 'sess1', turn: 1, model: 'claude-3-opus' },
];

const mockSessions = {
  sess1: { turnCount: 1, in: 1000, cacheRead: 0, cacheWrite: 0, out: 500, sessionName: 'Test', route: 'claude', models: new Set(['claude']), tools: 0, prompts: [] }
};

describe('Insights Component', () => {
  it('renders correctly', () => {
    render(<Insights insights={mockInsights} topPrompts={mockTopPrompts} sessions={mockSessions} />);
    expect(screen.getByText('5 ❓')).toBeInTheDocument(); // Vague
    expect(screen.getByText('2 🏃‍♂️')).toBeInTheDocument(); // Marathon
    expect(screen.getByText('10 🏋️‍♂️')).toBeInTheDocument(); // Input Heavy
    expect(screen.getByText('1 🔧')).toBeInTheDocument(); // Tool Heavy
  });

  it('renders model dominance', () => {
    render(<Insights insights={mockInsights} topPrompts={mockTopPrompts} sessions={mockSessions} />);
    expect(screen.getByText(/Model Dominance Detected:/i)).toBeInTheDocument();
    expect(screen.getByText('Claude')).toBeInTheDocument();
  });

  it('renders top prompts', () => {
    render(<Insights insights={mockInsights} topPrompts={mockTopPrompts} sessions={mockSessions} />);
    expect(screen.getByText('Hello world')).toBeInTheDocument();
    expect(screen.getByText(/Tot:/)).toBeInTheDocument();
    expect(screen.getAllByText('1.5K').length).toBeGreaterThan(0);
  });
});
