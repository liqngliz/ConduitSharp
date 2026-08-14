import React, { useEffect, useRef } from 'react';

const formatValue = (val: number, compact: boolean) => {
  return compact
    ? Intl.NumberFormat('en-US', { notation: 'compact', maximumSignificantDigits: 3 }).format(val)
    : val.toLocaleString(undefined, { maximumFractionDigits: 0 });
};

export const AnimatedNumber: React.FC<{ value: number; durationMs?: number; compact?: boolean; as?: 'span' | 'tspan', disableAnimation?: boolean }> = ({ value, durationMs = 400, compact = false, as = 'span', disableAnimation = false }) => {
  const nodeRef = useRef<HTMLSpanElement | SVGTSpanElement>(null);
  const currentDisplay = useRef(value);

  useEffect(() => {
    if (disableAnimation) {
      currentDisplay.current = value;
      return;
    }

    let startTimestamp: number | null = null;
    const startValue = currentDisplay.current;
    const endValue = value;
    const frameRef = { current: 0 };
    
    if (startValue === endValue) {
      if (nodeRef.current) {
        nodeRef.current.textContent = formatValue(endValue, compact);
      }
      return;
    }

    const step = (timestamp: number) => {
      if (!startTimestamp) startTimestamp = timestamp;
      const progress = Math.min((timestamp - startTimestamp) / durationMs, 1);
      
      // easeOutExpo for smooth deceleration
      const easeProgress = progress === 1 ? 1 : 1 - Math.pow(2, -10 * progress);
      
      const current = Math.floor(startValue + (endValue - startValue) * easeProgress);
      currentDisplay.current = current;

      if (nodeRef.current) {
        nodeRef.current.textContent = formatValue(current, compact);
      }

      if (progress < 1) {
        frameRef.current = window.requestAnimationFrame(step);
      } else {
        currentDisplay.current = endValue;
        if (nodeRef.current) {
          nodeRef.current.textContent = formatValue(endValue, compact);
        }
      }
    };

    frameRef.current = window.requestAnimationFrame(step);
    return () => window.cancelAnimationFrame(frameRef.current);
  }, [value, durationMs, compact, disableAnimation]);

  const displayVal = disableAnimation ? value : currentDisplay.current;

  if (as === 'tspan') {
    return <tspan ref={nodeRef as React.RefObject<SVGTSpanElement>} className="tabular-nums">{formatValue(displayVal, compact)}</tspan>;
  }
  return <span ref={nodeRef as React.RefObject<HTMLSpanElement>} className="tabular-nums">{formatValue(displayVal, compact)}</span>;
};
