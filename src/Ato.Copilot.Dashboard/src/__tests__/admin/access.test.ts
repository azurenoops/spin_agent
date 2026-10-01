import { describe, expect, it } from 'vitest';
import {
  canAccessAction,
  resolveRememberedDestination,
  type EffectiveAccessDestination,
} from '../../features/admin/access';

const destination = (
  id: string,
  actions: string[],
): EffectiveAccessDestination => ({
  id,
  workspace: 'Administration',
  scopeKind: 'Organization',
  scopeId: id,
  displayName: id,
  actions,
  badges: [],
  availability: 'Available',
});

describe('role-aware administration access helpers', () => {
  it('rejects a remembered destination that is no longer authorized', () => {
    const available = [destination('organization:one', ['organization.profile.view'])];

    expect(resolveRememberedDestination('organization:revoked', available)).toBeNull();
  });

  it('accepts a remembered destination only when it is in the server result', () => {
    const available = [destination('organization:one', ['organization.profile.view'])];

    expect(resolveRememberedDestination('organization:one', available)?.id)
      .toBe('organization:one');
  });

  it('does not infer an action from a role badge', () => {
    const adminBadgeOnly: EffectiveAccessDestination = {
      ...destination('organization:one', []),
      badges: [{ label: 'Administrator', source: 'test' }],
    };

    expect(canAccessAction(adminBadgeOnly, 'organization.profile.edit')).toBe(false);
  });
});
