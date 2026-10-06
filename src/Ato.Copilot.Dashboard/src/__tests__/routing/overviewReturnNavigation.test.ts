import { describe, expect, it } from 'vitest';
import { packageReturnHref, packageSourceHref } from '../../features/systems/packageReadinessNavigation';

describe('Overview workflow return navigation', () => {
  it('keeps overview phase, assignment filter and expanded group when following a real workflow', () => {
    // Arrange
    const state = '?overview=readiness&phase=Implement&owner=mine&expanded=ac1';
    // Act
    const href = packageSourceHref('a', 'narratives?control=AC-1', state, 'overview');
    // Assert
    const returnHref = new URL(href!, 'https://example.invalid').searchParams.get('readinessReturn');
    expect(returnHref).toBe(`/systems/a${state}`);
    expect(packageReturnHref('a', new URL(href!, 'https://example.invalid').search)).toBe(returnHref);
  });
  it.each(['/systems/b?overview=readiness', '/systems/a/authorize', '//example.org', '/systems/a?phase=Invented'])(
    'rejects unsafe or wrong-scope return path %s', target => {
      // Arrange
      const query = new URLSearchParams({ readinessReturn: target }).toString();
      // Act / Assert
      expect(packageReturnHref('a', `?${query}`)).toBeNull();
    });
});
