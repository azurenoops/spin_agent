import { describe, expect, it, vi } from 'vitest';
import {
  buildWorkspaceUrl,
  canonicalizeSystemRoute,
  parseWorkspaceUrl,
  systemIdFromRoute,
  type WorkspaceTarget,
} from '../../features/workspaces/workspaceRoutes';

const organization: WorkspaceTarget = { kind: 'organization', tenantId: 'org-alpha' };
const provider: WorkspaceTarget = { kind: 'csp' };

describe('workspace routes', () => {
  it.each([
    [provider, '/', '/workspaces/csp'],
    [provider, '/systems?sort=name#overview', '/workspaces/csp/systems?sort=name#overview'],
    [organization, '/', '/workspaces/organizations/org-alpha'],
    [organization, '/systems/system-a/narratives?kind=policy#AC-2',
      '/workspaces/organizations/org-alpha/systems/system-a/narratives?kind=policy#AC-2'],
  ] as const)('builds and round-trips %j with %s', (workspace, route, expected) => {
    // Arrange
    const target = workspace;

    // Act
    const url = buildWorkspaceUrl(target, route);
    const parsed = parseWorkspaceUrl(url);

    // Assert
    expect(url).toBe(expected);
    expect(parsed).toEqual({ workspace, route });
  });

  it('defaults to the workspace landing route', () => {
    // Arrange
    const target = organization;

    // Act
    const url = buildWorkspaceUrl(target);

    // Assert
    expect(url).toBe('/workspaces/organizations/org-alpha');
  });

  it('keeps audited support intent separate from ordinary workspace URLs', () => {
    // Arrange
    const url = '/workspaces/support/organizations/org-alpha/systems/a?view=review#control';

    // Act
    const parsed = parseWorkspaceUrl(url);

    // Assert
    expect(parsed).toEqual({
      workspace: { kind: 'organization', tenantId: 'org-alpha', mode: 'support' },
      route: '/systems/a?view=review#control',
    });
    expect(parsed && buildWorkspaceUrl(parsed.workspace, parsed.route)).toBe(url);
  });

  it('preserves a home-page search and fragment', () => {
    // Arrange
    const route = '/?view=review#pending';

    // Act
    const url = buildWorkspaceUrl(organization, route);

    // Assert
    expect(url).toBe('/workspaces/organizations/org-alpha?view=review#pending');
    expect(parseWorkspaceUrl(url)).toEqual({ workspace: organization, route });
  });

  it.each(['/', '/portfolio', '/systems/new', '/systems/a/narratives?q=x#AC-2', '/login/callback'])(
    'distinguishes the legacy URL %s from an invalid workspace',
    (url) => {
      // Arrange
      const legacyUrl = url;

      // Act
      const parsed = parseWorkspaceUrl(legacyUrl);

      // Assert
      expect(parsed).toBeNull();
    },
  );

  it('normalizes a trailing slash on a workspace home', () => {
    // Arrange
    const url = '/workspaces/organizations/org-alpha/';

    // Act
    const parsed = parseWorkspaceUrl(url);

    // Assert
    expect(parsed).toEqual({ workspace: organization, route: '/' });
  });

  it('does not share a selected organization between independently parsed locations', () => {
    // Arrange
    const first = '/workspaces/organizations/org-alpha/systems/a';
    const second = '/workspaces/organizations/org-beta/systems/b';

    // Act
    const firstScope = parseWorkspaceUrl(first);
    const secondScope = parseWorkspaceUrl(second);

    // Assert
    expect(firstScope?.workspace).toEqual(organization);
    expect(secondScope?.workspace).toEqual({ kind: 'organization', tenantId: 'org-beta' });
    expect(parseWorkspaceUrl(first)).toEqual(firstScope);
  });

  it.each([
    '/workspaces',
    '/workspaces/',
    '/workspaces/unknown',
    '/workspaces/organizations',
    '/workspaces/organizations/',
    '/workspaces/organizations//systems',
    '/workspaces/organizations/../systems',
    '/workspaces/organizations/%2e%2e/systems',
    '/workspaces/organizations/org%2Falpha/systems',
    '/workspaces/organizations/org%5Calpha/systems',
    '/workspaces/organizations/org%252Falpha/systems',
    '/workspaces/organizations/%/systems',
    '/workspaces/csp/../systems',
    '/workspaces/csp/%2e%2e/systems',
  ])('rejects malformed workspace URL %s instead of falling back', (url) => {
    // Arrange
    const invalidUrl = url;

    // Act
    const parse = () => parseWorkspaceUrl(invalidUrl);

    // Assert
    expect(parse).toThrow('Invalid workspace URL');
  });

  it.each([
    'https://example.invalid/systems',
    '//example.invalid/systems',
    '\\\\example.invalid\\systems',
    '/systems\\a',
    '/systems/a\n',
    'systems/a',
  ])('rejects unsafe local URL %s', (route) => {
    // Arrange
    const invalidRoute = route;

    // Act
    const build = () => buildWorkspaceUrl(organization, invalidRoute);
    const parse = () => parseWorkspaceUrl(invalidRoute);

    // Assert
    expect(build).toThrow('Invalid workspace URL');
    expect(parse).toThrow('Invalid workspace URL');
  });

  it.each(['', '.', '..', 'org/a', 'org\\a', 'org?other', 'org#other', 'org%2Fa', 'org\n'])(
    'rejects invalid organization identifier %s',
    (tenantId) => {
      // Arrange
      const target: WorkspaceTarget = { kind: 'organization', tenantId };

      // Act
      const build = () => buildWorkspaceUrl(target);

      // Assert
      expect(build).toThrow('Invalid workspace URL');
    },
  );

  it('rejects double-prefixing an already-scoped URL', () => {
    // Arrange
    const route = '/workspaces/organizations/org-beta/systems';

    // Act
    const build = () => buildWorkspaceUrl(organization, route);

    // Assert
    expect(build).toThrow('Invalid workspace URL');
  });

  it('does not mask an unexpected decoder failure as invalid user input', () => {
    // Arrange
    const error = new Error('Decoder failure');
    const decoder = vi.spyOn(globalThis, 'decodeURIComponent').mockImplementationOnce(() => {
      throw error;
    });

    // Act
    const parse = () => parseWorkspaceUrl('/workspaces/csp');

    // Assert
    try {
      expect(parse).toThrow(error);
    } finally {
      decoder.mockRestore();
    }
  });
});

describe('system aliases', () => {
  it.each([
    ['/systems/new', null],
    ['/systems/%6eew', null],
    ['/systems/system-a/narratives?kind=policy', 'system-a'],
    ['/settings/org', null],
  ])('extracts the active system from %s without interpreting intake as an ID', (route, expected) => {
    // Arrange
    const url = route;

    // Act
    const systemId = systemIdFromRoute(url);

    // Assert
    expect(systemId).toBe(expected);
  });

  it.each([
    ['/systems/system-a/categorization/?view=all#section', '/systems/system-a/baseline?view=all#section'],
    ['/systems/system-a/%63ategorization', '/systems/system-a/baseline'],
    ['/SYSTEMS/system-a/CATEGORIZATION', '/systems/system-a/baseline'],
    ['/systems/%6Eew/categorization', '/systems/%6Eew/categorization'],
  ])('matches router-normalized alias URL %s', (route, expected) => {
    // Arrange
    const url = route;

    // Act
    const canonical = canonicalizeSystemRoute(url);

    // Assert
    expect(canonical).toBe(expected);
  });

  it.each([
    ['control-inheritance', 'inheritance'],
    ['categorization', 'baseline'],
    ['capabilities', 'security-capabilities'],
    ['capability-coverage', 'security-capabilities'],
    ['mission-purpose', 'profile/MissionAndPurpose'],
    ['users-access', 'profile/UsersAndAccess'],
    ['environment', 'profile/EnvironmentAndDeployment'],
    ['data-types', 'profile/DataTypes'],
    ['ports-protocols', 'profile/PortsProtocolsAndServices'],
    ['leveraged-auth', 'profile/LeveragedAuthorizations'],
    ['legal-regulatory', 'legal'],
  ])('maps %s to %s while preserving query and fragment', (alias, canonical) => {
    // Arrange
    const route = `/systems/system-a/${alias}?view=all#section`;

    // Act
    const actual = canonicalizeSystemRoute(route);

    // Assert
    expect(actual).toBe(`/systems/system-a/${canonical}?view=all#section`);
  });

  it.each(['/systems/new', '/systems/a/narratives', '/systems/a/constructor', '/systems/a/profile/MissionAndPurpose',
    '/systems/a/categorization/nested', '/systems/a', '/controls'])(
    'leaves non-alias route %s intact',
    (route) => {
      // Arrange
      const original = route;

      // Act
      const canonical = canonicalizeSystemRoute(original);

      // Assert
      expect(canonical).toBe(original);
    },
  );

  it('does not interpret the new-system action as a system identifier', () => {
    // Arrange
    const route = '/systems/new/categorization';

    // Act
    const canonical = canonicalizeSystemRoute(route);

    // Assert
    expect(canonical).toBe(route);
  });
});
