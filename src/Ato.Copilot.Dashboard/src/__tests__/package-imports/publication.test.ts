import { describe, expect, it } from 'vitest';
import { packagePublicationKey } from '../../features/package-imports/publication';
import { preview } from './fixtures';

describe('exact-preview publication identity', () => {
  it('reconstructs the same bounded key from the same persisted preview after refresh', () => {
    // Arrange
    const beforeRefresh = preview({ previewId: 'bf46e60a-e47d-47e9-82a6-d76a1198f839' });
    const restoredFromServer = { ...beforeRefresh, candidates: [...beforeRefresh.candidates] };
    // Act
    const originalKey = packagePublicationKey(beforeRefresh);
    const restoredKey = packagePublicationKey(restoredFromServer);
    // Assert
    expect(restoredKey).toBe(originalKey);
    expect(restoredKey.length).toBeLessThanOrEqual(100);
  });

  it('does not reuse a publication key for another server preview', () => {
    // Arrange
    const first = preview({ previewId: 'first-exact-preview' });
    const second = preview({ previewId: 'second-exact-preview' });
    // Act
    const firstKey = packagePublicationKey(first);
    const secondKey = packagePublicationKey(second);
    // Assert
    expect(firstKey).not.toBe(secondKey);
  });
});
