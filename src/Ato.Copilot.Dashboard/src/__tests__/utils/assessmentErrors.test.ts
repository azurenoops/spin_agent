import { describe, expect, it } from 'vitest';
import { assessmentError } from '../../utils/assessmentErrors';

describe('assessment error normalization', () => {
  it('preserves the normalized backend error and corrective suggestion', () => {
    // Arrange
    const error = { error: 'Select an organization.', errorCode: 'ASSESSMENT_AZURE_ORGANIZATION_REQUIRED', suggestion: 'Use the organization selector.' };

    // Act
    const result = assessmentError(error, 'Fallback');

    // Assert
    expect(result).toEqual({ message: error.error, errorCode: error.errorCode, suggestion: error.suggestion });
  });

  it('provides writer guidance when authorization middleware returns a bodyless 403', () => {
    // Arrange
    const error = Object.assign(new Error('Request failed with status code 403'), {
      isAxiosError: true, response: { status: 403 },
    });

    // Act
    const result = assessmentError(error, 'Unable to configure environment.');

    // Assert
    expect(result.errorCode).toBe('ASSESSMENT_PERMISSION_REQUIRED');
    expect(result.message).toMatch(/permission/i);
    expect(result.suggestion).toMatch(/authorized compliance writer/i);
  });

  it('retains network error messages without presenting them as permission failures', () => {
    // Arrange
    const error = new Error('Network connection interrupted.');

    // Act
    const result = assessmentError(error, 'Fallback');

    // Assert
    expect(result).toEqual({ message: error.message, suggestion: null, errorCode: null });
  });

  it.each([undefined, null, 'failure', {}, new Error(''), { error: '', suggestion: 42, errorCode: false }])('provides a visible fallback for malformed or empty errors: %s', (error) => {
    // Arrange
    const fallback = 'Unable to configure environment.';

    // Act
    const result = assessmentError(error, fallback);

    // Assert
    expect(result).toEqual({ message: fallback, suggestion: null, errorCode: null });
  });
});
