import { describe, expect, it } from 'vitest';
import { packagePurposeFromSearch, packageSourceHref, packageReturnHref } from '../../features/systems/packageReadinessNavigation';

describe('Package readiness navigation', () => {
  it('preserves the existing Legacy default and explicit purpose without accepting unknown values', () => {
    // Arrange / Act / Assert
    expect(packagePurposeFromSearch('')).toBe('Legacy');
    expect(packagePurposeFromSearch('?purpose=InitialSubmission')).toBe('InitialSubmission');
    expect(packagePurposeFromSearch('?purpose=AuthorizedBaselineArchive')).toBe('AuthorizedBaselineArchive');
    expect(packagePurposeFromSearch('?purpose=ChangeSubmission')).toBe('ChangeSubmission');
    expect(packagePurposeFromSearch('?purpose=invalid')).toBeNull();
  });
  it('preserves the selected purpose, run and drawer when opening and returning from a same-system source', () => {
    // Arrange
    const search = '?purpose=Legacy&run=run-a&check=inventory&checks=blocking';
    // Act
    const href = packageSourceHref('system-a', 'security-capabilities/inventory', search);
    // Assert
    expect(href).toContain('/systems/system-a/security-capabilities/inventory?');
    const sourceSearch = href!.slice(href!.indexOf('?'));
    expect(packageReturnHref('system-a', sourceSearch)).toBe(`/systems/system-a/documents${search}`);
    expect(packageReturnHref('system-b', sourceSearch)).toBeNull();
  });
  it.each(['https://outside.invalid', '//outside.invalid', '../other', '/systems/b/evidence', 'evidence/../authorize'])(
    'rejects an unsafe source path %s', path => {
      // Arrange / Act / Assert
      expect(packageSourceHref('system-a', path, '?purpose=InitialSubmission')).toBeNull();
    });
  it.each(['https://outside.invalid', '//outside.invalid', '/systems/b/documents', '/systems/a/authorize', '/systems/a/documents/../authorize'])(
    'rejects an unsafe return target %s', target => {
      // Arrange / Act / Assert
      expect(packageReturnHref('a', `?readinessReturn=${encodeURIComponent(target)}`)).toBeNull();
    });
  it('preserves source query and anchor while adding a readiness return path', () => {
    // Arrange / Act
    const href = packageSourceHref('a', 'assessments?tab=plan#schedule', '?purpose=InitialSubmission');
    // Assert
    expect(href).toContain('/systems/a/assessments?tab=plan&readinessReturn=');
    expect(href).toMatch(/#schedule$/);
  });
});
