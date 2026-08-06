import React, { useEffect, useState, useRef } from 'react';

export const AnimatedNumber: React.FC<{ value: number; durationMs?: number; compact?: boolean; as?: 'span' | 'tspan' }> = ({ value, durationMs = 800, compact = false, as = 'span' }) => {
  const [displayValue, setDisplayValue] = useState(value);
  const currentDisplay = useRef(displayValue);

  useEffect(() => {
    let startTimestamp: number | null = null;
    const startValue = currentDisplay.current;
    const endValue = value;
    
    if (startValue === endValue) return;

    const step = (timestamp: number) => {
      if (!startTimestamp) startTimestamp = timestamp;
      const progress = Math.min((timestamp - startTimestamp) / durationMs, 1);
      
      // easeOutExpo for smooth deceleration
      const easeProgress = progress === 1 ? 1 : 1 - Math.pow(2, -10 * progress);
      
      const current = Math.floor(startValue + (endValue - startValue) * easeProgress);
      setDisplayValue(current);
      currentDisplay.current = current;

      if (progress < 1) {
        window.requestAnimationFrame(step);
      } else {
        setDisplayValue(endValue);
        currentDisplay.current = endValue;
      }
    };

    const frameId = window.requestAnimationFrame(step);
    return () => window.cancelAnimationFrame(frameId);
  }, [value, durationMs]);

  const content = compact
    ? Intl.NumberFormat('en-US', { notation: 'compact', maximumSignificantDigits: 3 }).format(displayValue)
    : displayValue.toLocaleString(undefined, { maximumFractionDigits: 0 });

  if (as === 'tspan') {
    return <tspan className="tabular-nums">{content}</tspan>;
  }
  return <span className="tabular-nums">{content}</span>;
};
