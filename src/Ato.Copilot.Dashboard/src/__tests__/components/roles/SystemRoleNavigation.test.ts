import { describe, expect, it } from 'vitest';
import { SYSTEM_NAV_GROUPS } from '../../../components/layout/SystemLayout';

describe('system role navigation', () => {
  it('exposes System team in the mock-defined Team & permissions group', () => {
    // Arrange
    const items = SYSTEM_NAV_GROUPS.flatMap((group) => group.items);

    // Act
    const roleLinks = items.filter((item) => item.path === 'roles');

    // Assert
    expect(roleLinks).toEqual([
      expect.objectContaining({ label: 'System team' }),
    ]);
  });

  it('preserves eight task groups and adds System design to the thirty existing screens', () => {
    // Arrange
    const labels = SYSTEM_NAV_GROUPS.map(group => group.label);
    // Act
    const items = SYSTEM_NAV_GROUPS.flatMap(group => group.items);
    // Assert
    expect(labels).toEqual([
      'Overview', 'System definition', 'Controls & evidence', 'Assessment & risk',
      'ATO Readiness', 'Continuous monitoring', 'Team & permissions', 'Activity & history',
    ]);
    expect(items).toHaveLength(31);
    expect(new Set(items.map(item => item.path)).size).toBe(31);
    expect(items).toContainEqual(expect.objectContaining({ path: 'profile/SystemDesign', label: 'System design' }));
    expect(items).toContainEqual(expect.objectContaining({ path: 'inheritance/subscriptions', label: 'Responsibilities' }));
  });
});