import { describe, expect, it } from 'vitest';
import { assessmentConfigurationUrl } from '../../utils/assessmentConfiguration';

describe('Azure assessment configuration location', () => {
  it.each(['system-a', 'system with spaces'])('keeps system %s in the Assessments configuration route', systemId => {
    // Arrange
    const expected = `/systems/${encodeURIComponent(systemId)}/assessments/environment#azure-assessment-environment`;
    // Act
    const url = assessmentConfigurationUrl(systemId);
    // Assert
    expect(url).toBe(expected);
  });
});
