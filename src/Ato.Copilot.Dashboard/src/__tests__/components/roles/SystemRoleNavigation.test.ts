import { describe, expect, it } from 'vitest';
import { SYSTEM_NAV_GROUPS } from '../../../components/layout/SystemLayout';

describe('system role navigation', () => {
  it('exposes the Roles & Permissions route in the system sidebar', () => {
    // Arrange
    const items = SYSTEM_NAV_GROUPS.flatMap((group) => group.items);

    // Act
    const roleLinks = items.filter((item) => item.path === 'roles');

    // Assert
    expect(roleLinks).toEqual([
      expect.objectContaining({ label: 'Roles & Permissions' }),
    ]);
  });
});