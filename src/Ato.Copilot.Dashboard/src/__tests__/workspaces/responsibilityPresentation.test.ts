import { describe, expect, it } from 'vitest';
import { responsibilityLabel, responsibilityNeedsReview } from '../../features/workspace-operations/system-capabilities/responsibilityPresentation';

describe('task-oriented responsibility status', () => {
  it('keeps unknown states explicit instead of showing internal values as completion', () => {
    // Arrange
    const state = 'UnknownInternalState';
    // Act
    const label = responsibilityLabel(state);
    // Assert
    expect(label).toBe('Review state unavailable');
    expect(responsibilityNeedsReview(state)).toBe(true);
  });
  it.each(['Applied', 'Ready', 'Persisted', 'Inactive', 'OutsideBaseline'])('does not count resolved or out-of-scope %s as unresolved', state => {
    // Arrange / Act
    const pending = responsibilityNeedsReview(state);
    // Assert
    expect(pending).toBe(false);
    expect(responsibilityLabel(state)).not.toBe('Review state unavailable');
  });
});
