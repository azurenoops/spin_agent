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

  it('uses all eight task groups and all thirty mock screen labels', () => {
    // Arrange
    const labels = SYSTEM_NAV_GROUPS.map(group => group.label);
    // Act
    const items = SYSTEM_NAV_GROUPS.flatMap(group => group.items);
    // Assert
    expect(labels).toEqual([
      'Overview', 'System definition', 'Controls & evidence', 'Assessment & risk',
      'ATO package & eMASS', 'Continuous monitoring', 'Team & permissions', 'Activity & history',
    ]);
    expect(items).toHaveLength(30);
    expect(new Set(items.map(item => item.label)).size).toBe(30);
    expect(items).toContainEqual(expect.objectContaining({ path: 'inheritance/subscriptions', label: 'Responsibilities' }));
  });
});