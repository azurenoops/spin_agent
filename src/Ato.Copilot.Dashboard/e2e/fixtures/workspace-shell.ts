import { expect, type BrowserContext, type Page } from '@playwright/test';

const orgA = { kind: 'organization', tenantId: 'org-a', displayName: 'Organization A', status: 'Active', onboardingState: 'Active' };
const orgB = { ...orgA, tenantId: 'org-b', displayName: 'Organization B' };
const provider = { ...orgA, kind: 'csp', tenantId: null, displayName: 'Synthetic Provider' };
const permissions = { canManageMemberships: false, canManageOrganization: false, canAccessCsp: false };
export const workspaceFixtureActor = 'synthetic-owner';

export interface WorkspaceFixtureOptions {
  providerOnly?: boolean;
  multiple?: boolean;
  mismatch?: boolean;
  support?: boolean;
}

/** Synthetic API responses exercise the real SPA; they do not establish backend authorization. */
export async function installWorkspaceFixture(context: BrowserContext, baseURL: string, options: WorkspaceFixtureOptions = {}) {
  const requests: { path: string; kind?: string; tenant?: string; mode?: string; method: string }[] = [];
  const choices = options.providerOnly ? [provider] : options.multiple ? [orgA, orgB] : [orgA];
  let supportStarted = false;
  await context.route(/^https?:\/\/[^/]+\/api\//, async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const headers = request.headers();
    const tenantId = options.mismatch ? 'org-a' : headers['x-workspace-tenant-id'] ?? 'org-a';
    const tenant = { id: tenantId, displayName: tenantId === 'org-b' ? 'Organization B' : 'Organization A', status: 'Active' };
    const isProvider = headers['x-workspace-kind'] === 'csp';
    const inSupport = headers['x-workspace-mode'] === 'support' && supportStarted;
    requests.push({ path, kind: headers['x-workspace-kind'], tenant: headers['x-workspace-tenant-id'], mode: headers['x-workspace-mode'], method: request.method() });
    const json = (data: unknown) => route.fulfill({ json: data });
    const success = (data: unknown) => json({ status: 'success', data });
    if (path === '/api/auth/login-config') return success({
      branding: { deploymentName: 'Synthetic workspace tests', logoUrl: null, supportEmail: null },
      defaultMethod: 'Entra', enabledMethods: [], cloud: 'AzurePublic', idleTimeoutMinutes: 30,
      rememberTenantCookieDays: 0, simulation: null,
      msal: { clientId: '11111111-1111-1111-1111-111111111111', authority: 'https://login.microsoftonline.com/common',
        redirectUri: `${baseURL}/login/callback`, postLogoutRedirectUri: `${baseURL}/` },
    });
    if (path === '/api/auth/me') return success({
      oid: workspaceFixtureActor, directoryTenantId: 'synthetic-directory', displayName: 'Synthetic Owner', persona: 'MissionOwner',
      homeTenant: tenant, effectiveTenant: isProvider ? null : tenant,
      isImpersonating: inSupport, impersonation: inSupport ? {
        impersonatedTenant: tenant, startedAt: new Date().toISOString(), expiresAt: new Date(Date.now() + 3600000).toISOString(),
      } : null, isCspAdmin: options.providerOnly ?? false, isSocAnalyst: false, pimRoles: [],
      tenantMemberships: [tenant], availableWorkspaces: choices, availableWorkspacesTotal: choices.length,
      permissions: isProvider ? { ...permissions, canAccessCsp: true, canManageMemberships: true } : permissions,
      workspace: !headers['x-workspace-kind'] ? null : {
        kind: isProvider ? 'csp' : 'organization', tenantId: isProvider ? null : tenantId,
        displayName: isProvider ? provider.displayName : tenant.displayName, mode: inSupport ? 'support' : 'ordinary',
        personId: isProvider ? null : 'person-a', roles: isProvider ? ['CSPAdmin'] : ['MissionOwner', 'Reader'],
        permissions: isProvider ? { ...permissions, canAccessCsp: true, canManageMemberships: true } : permissions,
      },
    });
    if (path === '/api/tenants/org-a/impersonate' && request.method() === 'POST') {
      supportStarted = true;
      return success({ impersonatedTenantId: 'org-a', expiresAt: new Date(Date.now() + 3600000).toISOString() });
    }
    if (path === '/api/tenants/impersonation' && request.method() === 'DELETE') {
      supportStarted = false;
      return route.fulfill({ status: 204 });
    }
    if (path === '/api/auth/workspaces') return success({ items: choices, total: choices.length });
    if (path.endsWith('/workspace-access')) return success({
      systemId: 'system-a', roles: ['MissionOwner', 'Reader'],
      permissions: { canRead: true, canEditProfile: true, canManageSystem: false, canAuthorNarratives: false,
        canReviewNarratives: false, canManageEvidence: false, canRunAssessments: false,
        canManageRemediation: false, canDecideAuthorization: false },
    });
    if (path === '/api/csp/onboarding/state') return success({
      cspProfileId: 'synthetic-provider', onboardingState: 'Active', currentStep: 'Complete',
      identity: { displayName: 'Synthetic Provider', logoUrl: null },
    });
    if (path === '/api/csp/dashboard/summary') return success({
      tenantCounts: { active: 1, suspended: 0, disabled: 0, total: 1 }, disabledTenantCount: 0,
      organizationCount: 1, systemCount: 0, atoStatusCounts: { authorized: 0, inProcess: 0, denied: 0 },
      openFindingsBySeverity: { critical: 0, high: 0, moderate: 0, low: 0 },
      openPoamCount: 0, openDeviationCount: 0, generatedAt: '2026-09-21T00:00:00Z',
    });
    if (path === '/api/csp/dashboard/tenants') return success({
      items: options.support ? [{
        tenantId: 'org-a', displayName: 'Organization A', status: 'Active', onboardingState: 'Active',
        organizationCount: 1, systemCount: 0, atoStatusCounts: { authorized: 0, inProcess: 0, denied: 0 },
        openFindingCount: 0, openPoamCount: 0, openDeviationCount: 0, lastActivityTimestamp: null,
      }] : [], totalCount: options.support ? 1 : 0, page: 1, pageSize: 25,
    });
    if (path.endsWith('/organization-context')) return json({
      ok: true, data: { organizationName: 'Untrusted profile organization', subOrganization: 'Not the tenant identity' },
    });
    if (path === '/api/dashboard/portfolio' || path === '/api/dashboard/systems') return json({ items: [], totalCount: 0 });
    if (path.endsWith('/profile/completeness')) return json({ statusCounts: {}, totalSections: 6, approvedPercentage: 0, incompleteSections: [] });
    if (path.endsWith('/todos')) return json({ items: [], currentPhase: 'Prepare', nextPhase: 'Categorize' });
    if (path === '/api/dashboard/systems/system-a') return json({
      systemId: 'system-a', name: 'Synthetic Mission System', acronym: 'SYN', currentRmfStep: 'Prepare', rmfPhase: 'Prepare',
      hostingEnvironment: 'Synthetic cloud', systemType: 'MajorApplication', missionCriticality: 'MissionEssential',
      activeAssessments: [], roleAssignments: [], boundaryResources: [],
    });
    if (path.endsWith('/profile/MissionAndPurpose')) return json({
      id: 'synthetic-profile', sectionType: 'MissionAndPurpose', canEditProfile: true, governanceStatus: 'Draft',
      draftContent: JSON.stringify({ missionStatement: 'Authorized synthetic mission' }),
      userCategories: [], dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [],
    });
    return route.fulfill({ status: 404, json: { error: 'Outside the synthetic shell contract' } });
  });
  return requests;
}

export async function switchWorkspace(page: Page, name: string) {
  await page.getByRole('button', { name: 'Switch workspace', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Switch workspace?' })).toBeVisible();
  await page.getByRole('button', { name: 'Discard and choose workspace' }).click();
  await page.getByRole('button', { name: new RegExp(name) }).click();
}
