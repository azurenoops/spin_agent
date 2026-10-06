import { describe, expect, it } from 'vitest';
import { dataDocumentation, ciaSummary, privacySummary } from '../../features/systems/dataTypePresentation';

describe('Information handling documentation', () => {
  it('counts declared handling fields without treating unknown CIA or pending privacy review as complete', () => {
    // Arrange
    const row = { id: 'data', dataTypeName: 'Recorded data', description: 'Actual information type', sensitivityClassification: 'CUI',
      cuiCategory: '', confidentialityImpact: 'Moderate', integrityImpact: 'Moderate', availabilityImpact: 'Low',
      privacyApplicability: 'ReviewRequired', retentionRule: '', disposalMethod: '', categorizationReference: '' };
    // Act
    const status = dataDocumentation([row]);
    // Assert
    expect(status).toEqual({ recorded: 6, total: 11, missing: 5, types: 1, firstIncompleteId: 'data', firstIncompleteName: 'Recorded data' });
    expect(ciaSummary(row)).toBe('M / M / L · declared');
    expect(privacySummary(row)).toBe('Privacy review required · Retention not recorded');
  });
  it('does not require a CUI category for explicit public data or invent mock readiness for empty records', () => {
    // Arrange / Act
    const status = dataDocumentation([{ _tempId: 'local', dataTypeName: 'Public record', sensitivityClassification: 'Public',
      confidentialityImpact: 'Undetermined', integrityImpact: '', availabilityImpact: '', privacyApplicability: 'NoPii' }]);
    // Assert
    expect(status.total).toBe(10);
    expect(status.recorded).toBe(3);
    expect(status.firstIncompleteId).toBe('local');
    expect(ciaSummary({})).toBe('? / ? / ? · declared');
    expect(dataDocumentation([]).total).toBe(0);
  });
});
