import { describe, expect, it } from 'vitest';
import { workspaceStorageKey } from '../../features/workspaces/workspaceStorage';

const scope = {
  directoryTenantId: 'directory-a', objectId: 'actor-a', kind: 'organization' as const,
  tenantId: 'org-alpha', mode: 'ordinary' as const, systemId: 'system-a',
};

describe('workspace chat storage identity', () => {
  it.each([
    { directoryTenantId: 'directory-b' },
    { objectId: 'actor-b' },
    { tenantId: 'org-beta' },
    { mode: 'support' as const },
    { systemId: 'system-b' },
    { kind: 'csp' as const, tenantId: null },
  ])('isolates a changed scope dimension %j', change => {
    // Arrange
    const original = workspaceStorageKey('chat', scope);

    // Act
    const changed = workspaceStorageKey('chat', { ...scope, ...change });

    // Assert
    expect(changed).not.toBe(original);
    expect(changed).not.toBeNull();
  });

  it('does not read the legacy cache or invent a namespace without an issuer', () => {
    // Arrange
    const incomplete = { ...scope, directoryTenantId: undefined };

    // Act
    const key = workspaceStorageKey('ato-chat-conversations', incomplete);

    // Assert
    expect(key).toBeNull();
  });

  it('preserves the legacy key only outside an explicit workspace', () => {
    // Arrange
    const legacy = null;

    // Act
    const key = workspaceStorageKey('ato-chat-conversations', legacy);

    // Assert
    expect(key).toBe('ato-chat-conversations');
  });

  it('treats GUID casing as the same qualified identity', () => {
    // Arrange
    const original = workspaceStorageKey('chat', scope);

    // Act
    const repeated = workspaceStorageKey('chat', { ...scope,
      directoryTenantId: 'DIRECTORY-A', objectId: 'ACTOR-A', tenantId: 'ORG-ALPHA', systemId: 'SYSTEM-A' });

    // Assert
    expect(repeated).toBe(original);
  });
});
