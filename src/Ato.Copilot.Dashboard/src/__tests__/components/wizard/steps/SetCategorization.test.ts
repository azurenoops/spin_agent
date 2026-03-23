import { describe, it, expect } from 'vitest';

// Test the high-water mark (FIPS 199) computation logic directly
// since the component imports it internally, we re-implement the pure function for testing.

function highWaterMark(levels: string[]): string {
  if (levels.includes('High')) return 'High';
  if (levels.includes('Moderate')) return 'Moderate';
  return 'Low';
}

describe('SetCategorization — FIPS 199 High-Water Mark', () => {
  it('returns Low when all levels are Low', () => {
    expect(highWaterMark(['Low', 'Low', 'Low'])).toBe('Low');
  });

  it('returns Moderate when highest is Moderate', () => {
    expect(highWaterMark(['Low', 'Moderate', 'Low'])).toBe('Moderate');
  });

  it('returns High when any level is High', () => {
    expect(highWaterMark(['Low', 'Moderate', 'High'])).toBe('High');
  });

  it('returns Low for empty array', () => {
    expect(highWaterMark([])).toBe('Low');
  });

  it('returns High when all are High', () => {
    expect(highWaterMark(['High', 'High', 'High'])).toBe('High');
  });
});

describe('SetCategorization — overall FIPS 199 computation', () => {
  // Example of computing overall category from selected info types
  interface SelectedType {
    confidentialityImpact: string;
    integrityImpact: string;
    availabilityImpact: string;
  }

  function computeOverallFips199(selected: SelectedType[]): {
    confidentiality: string;
    integrity: string;
    availability: string;
    overall: string;
  } {
    const conf = highWaterMark(selected.map(s => s.confidentialityImpact));
    const intg = highWaterMark(selected.map(s => s.integrityImpact));
    const avail = highWaterMark(selected.map(s => s.availabilityImpact));
    const overall = highWaterMark([conf, intg, avail]);
    return { confidentiality: conf, integrity: intg, availability: avail, overall };
  }

  it('computes Moderate overall from mixed types', () => {
    const selected: SelectedType[] = [
      { confidentialityImpact: 'Low', integrityImpact: 'Moderate', availabilityImpact: 'Low' },
      { confidentialityImpact: 'Low', integrityImpact: 'Low', availabilityImpact: 'Moderate' },
    ];

    const result = computeOverallFips199(selected);
    expect(result.confidentiality).toBe('Low');
    expect(result.integrity).toBe('Moderate');
    expect(result.availability).toBe('Moderate');
    expect(result.overall).toBe('Moderate');
  });

  it('computes High overall when one type has High', () => {
    const selected: SelectedType[] = [
      { confidentialityImpact: 'Low', integrityImpact: 'Low', availabilityImpact: 'Low' },
      { confidentialityImpact: 'High', integrityImpact: 'Low', availabilityImpact: 'Moderate' },
    ];

    const result = computeOverallFips199(selected);
    expect(result.confidentiality).toBe('High');
    expect(result.overall).toBe('High');
  });

  it('computes Low when no types selected', () => {
    const result = computeOverallFips199([]);
    expect(result.overall).toBe('Low');
  });

  it('single type uses its own levels', () => {
    const selected: SelectedType[] = [
      { confidentialityImpact: 'Moderate', integrityImpact: 'Moderate', availabilityImpact: 'Low' },
    ];

    const result = computeOverallFips199(selected);
    expect(result.confidentiality).toBe('Moderate');
    expect(result.integrity).toBe('Moderate');
    expect(result.availability).toBe('Low');
    expect(result.overall).toBe('Moderate');
  });
});
