import { describe, expect, it } from 'vitest';
import { displayWorkspaceRoles } from '../../features/workspaces/workspaceRoles';

describe('effective workspace role display', () => {
  it('retains every effective role and normalizes display aliases', () => {
    // Arrange
    const roles = ['MissionOwner', 'Sca', 'Issm', 'AuthorizingOfficial', 'ISSM'];

    // Act
    const displayed = displayWorkspaceRoles(roles, 'Engineer');

    // Assert
    expect(displayed).toEqual(['MissionOwner', 'SCA', 'ISSM', 'AO']);
  });

  it('does not fill an empty scoped assignment set from browser preferences', () => {
    // Arrange
    const roles: string[] = [];

    // Act
    const displayed = displayWorkspaceRoles(roles, 'AO');

    // Assert
    expect(displayed).toEqual([]);
  });

  it('preserves the legacy display preference only outside a workspace', () => {
    // Arrange
    const roles = undefined;

    // Act
    const displayed = displayWorkspaceRoles(roles, 'ISSO');

    // Assert
    expect(displayed).toEqual(['ISSO']);
  });
});
