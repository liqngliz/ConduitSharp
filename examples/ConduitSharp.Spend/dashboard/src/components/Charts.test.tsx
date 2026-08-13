import { describe, it, expect } from 'vitest';
import { calculateBrushStartIndex, calculateBrushStartRatio } from './Charts';

describe('Charts Brush Arithmetic', () => {
  describe('calculateBrushStartIndex', () => {
    it('handles empty or non-positive data length', () => {
      expect(calculateBrushStartIndex(0, 0.75)).toBe(0);
      expect(calculateBrushStartIndex(-5, 0.75)).toBe(0);
    });

    it('returns 0 for single item list', () => {
      expect(calculateBrushStartIndex(1, 0.75)).toBe(0);
      expect(calculateBrushStartIndex(1, 0)).toBe(0);
      expect(calculateBrushStartIndex(1, 1)).toBe(0);
    });

    it('calculates 75% start index correctly for 100 items (latest 25%)', () => {
      // maxIdx = 99; 99 * 0.75 = 74.25 -> 74
      expect(calculateBrushStartIndex(100, 0.75)).toBe(74);
    });

    it('clamps ratios strictly within [0, maxIdx]', () => {
      expect(calculateBrushStartIndex(100, 0)).toBe(0);
      expect(calculateBrushStartIndex(100, 1.0)).toBe(99);
      expect(calculateBrushStartIndex(100, -0.5)).toBe(0);
      expect(calculateBrushStartIndex(100, 1.5)).toBe(99);
    });
  });

  describe('calculateBrushStartRatio', () => {
    it('returns 0 for data length <= 1', () => {
      expect(calculateBrushStartRatio(0, 0)).toBe(0);
      expect(calculateBrushStartRatio(5, 1)).toBe(0);
    });

    it('calculates ratio accurately', () => {
      expect(calculateBrushStartRatio(0, 100)).toBe(0);
      expect(calculateBrushStartRatio(99, 100)).toBe(1);
      expect(calculateBrushStartRatio(74, 100)).toBeCloseTo(74 / 99, 4);
    });

    it('clamps output within [0, 1]', () => {
      expect(calculateBrushStartRatio(-10, 100)).toBe(0);
      expect(calculateBrushStartRatio(200, 100)).toBe(1);
    });

    it('roundtrips start index to ratio within sub-bucket precision', () => {
      const initialRatio = 0.75;
      const dataLen = 100;
      const index = calculateBrushStartIndex(dataLen, initialRatio);
      const recoveredRatio = calculateBrushStartRatio(index, dataLen);
      expect(recoveredRatio).toBeCloseTo(initialRatio, 2);
    });
  });
});
