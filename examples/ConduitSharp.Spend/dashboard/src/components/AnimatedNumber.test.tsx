import { render, screen } from '@testing-library/react';
import { AnimatedNumber } from './AnimatedNumber';
import { describe, it, expect, vi } from 'vitest';

describe('AnimatedNumber Component', () => {
  it('renders standard formatted number as a span', () => {
    const { container } = render(<AnimatedNumber value={1234} disableAnimation />);
    expect(screen.getByText('1,234')).toBeInTheDocument();
    expect(container.querySelector('span')).toBeInTheDocument();
  });

  it('renders compact notation when compact is true', () => {
    render(<AnimatedNumber value={1500000} compact disableAnimation />);
    expect(screen.getByText('1.5M')).toBeInTheDocument();
  });

  it('renders as tspan element inside svg', () => {
    const { container } = render(
      <svg>
        <text>
          <AnimatedNumber value={42} as="tspan" disableAnimation />
        </text>
      </svg>
    );
    expect(container.querySelector('tspan')).toBeInTheDocument();
    expect(screen.getByText('42')).toBeInTheDocument();
  });

  it('animates value changes on update using requestAnimationFrame', () => {
    const rafSpy = vi.spyOn(window, 'requestAnimationFrame');
    const { rerender } = render(<AnimatedNumber value={100} durationMs={200} />);
    rerender(<AnimatedNumber value={200} durationMs={200} />);
    expect(rafSpy).toHaveBeenCalled();
    rafSpy.mockRestore();
  });
});
