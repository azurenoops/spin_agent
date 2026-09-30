import { expect, test, type Page } from '@playwright/test';
import type { PolicyReference, PolicySource } from '../../src/api/policyWorkspace';

const route = '/workspaces/organizations/org-a/systems/system-a/legal';
async function fixture(page: Page, baseURL: string) {
  const sources: PolicySource[] = [{ id: 'policy-a', name: 'Access control policy', description: 'Original access policy content.',
    subType: 'Access', status: 'Active', owner: 'Organization policy team', revision: 'revision-a',
    versionLabel: 'Updated Sep 25, 2026', modifiedAt: '2026-09-25T00:00:00Z', alreadyLinked: false, relatedControls: ['AC-2'] }];
  const references: Array<PolicyReference & { snapshot: PolicySource }> = [];
  await page.route(/^https?:\/\/[^/]+\/api\//, async intercepted => {
    const request = intercepted.request(), url = new URL(request.url()), path = decodeURIComponent(url.pathname);
    const ok = (data: unknown) => intercepted.fulfill({ json: { status: 'success', data } });
    if (path === '/api/auth/login-config') return ok({
      branding: { deploymentName: 'SPIN policy fixture', logoUrl: null, supportEmail: null },
      defaultMethod: 'Entra', enabledMethods: [], cloud: 'AzurePublic', idleTimeoutMinutes: 30, rememberTenantCookieDays: 0, simulation: null,
      msal: { clientId: '11111111-1111-1111-1111-111111111111', authority: 'https://login.microsoftonline.com/common',
        redirectUri: `${baseURL}/login/callback`, postLogoutRedirectUri: `${baseURL}/` },
    });
    if (path === '/api/auth/me') return ok({
      oid: 'policy-user', directoryTenantId: 'directory-a', displayName: 'Policy manager', persona: 'ISSM',
      homeTenant: null, effectiveTenant: { id: 'org-a', displayName: 'SPIN Demo Organization', status: 'Active' },
      isImpersonating: false, impersonation: null, isCspAdmin: false, isSocAnalyst: false, pimRoles: [],
      tenantMemberships: [], availableWorkspaces: [], availableWorkspacesTotal: 0,
      workspace: { kind: 'organization', tenantId: 'org-a', mode: 'ordinary', displayName: 'SPIN Demo Organization',
        personId: 'person-a', roles: ['ISSM'], permissions: { canAccessCsp: false, canManageMemberships: false,
          canManageOrganization: true, canCreateSystem: false } },
    });
    if (path.endsWith('/workspace-access')) return ok({ systemId: 'system-a', roles: ['ISSM'], permissions: {
      canRead: true, canEditProfile: true, canManageSystem: true, canAuthorNarratives: true, canReviewNarratives: true,
      canManageEvidence: true, canRunAssessments: false, canManageRemediation: false, canDecideAuthorization: false,
    } });
    const root = '/api/dashboard/systems/system-a/policy-workspace';
    if (path === root) {
      const search = (url.searchParams.get('search') ?? '').toLowerCase();
      const items = references.filter(r => `${r.name} ${r.rationale}`.toLowerCase().includes(search));
      return intercepted.fulfill({ json: { systemId: 'system-a', systemName: 'SPIN Demo System', items,
        totalCount: items.length, unfilteredTotal: references.length, page: 1, pageSize: 25,
        permissions: { canAssign: true, assignReason: null, canCreateLibrary: true, createReason: null } } });
    }
    if (path === `${root}/library` && request.method() === 'POST') {
      const body = request.postDataJSON();
      const source: PolicySource = { ...sources[0]!, id: `created-${sources.length}`, name: body.name, description: body.description,
        subType: body.subType, revision: `revision-${sources.length}`, versionLabel: 'Created Sep 28, 2026',
        owner: null, modifiedAt: null, relatedControls: [], alreadyLinked: false };
      sources.push(source); return intercepted.fulfill({ status: 201, json: source });
    }
    if (path === `${root}/library`) {
      const search = (url.searchParams.get('search') ?? '').toLowerCase();
      const items = sources.filter(s => `${s.name} ${s.subType} ${s.description}`.toLowerCase().includes(search))
        .map(s => ({ ...s, alreadyLinked: references.some(r => r.policyId === s.id) }));
      return intercepted.fulfill({ json: { systemId: 'system-a', items, totalCount: items.length, page: 1, pageSize: 25 } });
    }
    if (path.startsWith(`${root}/sources/`)) return intercepted.fulfill({ json: sources.find(s => path.endsWith(s.id)) });
    if (path === `${root}/references` && request.method() === 'POST') {
      const body = request.postDataJSON(), source = sources.find(s => s.id === body.policyId);
      if (!source || source.revision !== body.expectedSourceRevision) return intercepted.fulfill({ status: 409, json: { error: 'Source changed.' } });
      if (references.some(r => r.policyId === body.policyId)) return intercepted.fulfill({ status: 409, json: { error: 'Already linked.' } });
      const reference = { id: `ref-${source.id}`, policyId: source.id, name: source.name, rationale: body.rationale,
        retainedVersionLabel: source.versionLabel, sourceStatus: source.status, sourceChanged: false, retention: 'Retained' as const,
        revision: 1, canEdit: true, canRemove: true, actionReason: null, snapshot: { ...source } };
      references.push(reference); return intercepted.fulfill({ status: 201, json: reference });
    }
    if (path.startsWith(`${root}/references/`)) {
      const reference = references.find(r => path.endsWith(r.id));
      if (!reference) return intercepted.fulfill({ status: 404, json: { error: 'Reference not found.' } });
      if (request.method() === 'DELETE') { references.splice(references.indexOf(reference), 1); return intercepted.fulfill({ status: 204 }); }
      if (request.method() === 'PATCH') {
        const body = request.postDataJSON();
        if (body.expectedRevision !== reference.revision) return intercepted.fulfill({ status: 409, json: { error: 'Reference changed.' } });
        reference.rationale = body.rationale; reference.revision++;
        return intercepted.fulfill({ json: reference });
      }
      const source = sources.find(s => s.id === reference.policyId)!;
      return intercepted.fulfill({ json: { systemId: 'system-a', systemName: 'SPIN Demo System',
        reference: { ...reference, sourceChanged: source.revision !== reference.snapshot.revision }, retainedSource: reference.snapshot,
        currentSource: source, relatedControls: source.relatedControls, reviewMessage: 'Applicability review is not implemented.',
        history: [{ id: 'event-a', action: 'Reference added', actor: 'Policy manager', at: '2026-09-28T10:00:00Z', description: 'Source and rationale retained.' }],
        removalImpact: ['Only this system reference will be unlinked.', 'The organization policy and other assignments will remain.'] } });
    }
    if (path.endsWith('/profile/completeness')) return intercepted.fulfill({ json: {
      systemId: 'system-a', totalSections: 0, statusCounts: {}, approvedPercentage: 0, isProfileComplete: false,
      incompleteSections: [], missionOwnerAssigned: false, missionOwnerName: null, daysSinceRegistration: 1,
    } });
    if (path.endsWith('/todos')) return intercepted.fulfill({ json: { items: [] } });
    if (path === '/api/dashboard/systems/system-a') return intercepted.fulfill({ json: {
      systemId: 'system-a', name: 'SPIN Demo System', acronym: 'SPIN', systemType: 'Application', missionCriticality: 'MissionSupport',
      hostingEnvironment: 'Cloud', impactLevel: 'IL4', baselineLevel: 'Moderate', currentRmfPhase: 'Prepare', rmfPhaseProgress: [], keyMetrics: {}, recentActivity: [], categorization: null,
    } });
    return intercepted.fulfill({ status: 404, json: { error: `No fixture for ${path}` } });
  });
  return { sources, references };
}

for (const width of [1440, 390]) for (const theme of ['light', 'dark']) {
  test(`policy add, retain, edit and unlink ${width}px ${theme}`, async ({ page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const state = await fixture(page, baseURL!);
    await page.goto(route);
    await expect(page.getByText('No policies linked yet')).toBeVisible();
    await page.locator('html').evaluate((el, dark) => el.classList.toggle('dark', dark), theme === 'dark');
    await expect(page.getByRole('button', { name: 'Add policy', exact: true })).toHaveCount(1);
    await expect(page.getByRole('table')).toBeHidden();
    await expect(page.getByRole('region', { name: 'Policies for this system' }).getByRole('textbox')).toBeHidden();
    await page.screenshot({ path: `test-results/policies-empty-${width}-${theme}.png`, fullPage: true });
    // Act: select existing library source without any mutation.
    await page.getByRole('button', { name: 'Add policy', exact: true }).click();
    const drawer = page.getByRole('dialog', { name: 'Add policy', exact: true });
    await drawer.getByRole('radio', { name: /Access control policy/ }).check();
    await drawer.getByRole('button', { name: 'View source' }).click();
    await expect(drawer.getByRole('region', { name: 'Current library source' })).toContainText('Original access policy content.');
    await drawer.getByRole('button', { name: 'Close source preview' }).click();
    await page.screenshot({ path: `test-results/policies-choose-${width}-${theme}.png`, fullPage: true });
    if (width < 700) expect((await drawer.boundingBox())?.width).toBe(width);
    await drawer.getByRole('button', { name: 'Continue' }).click();
    // Assert: only one step, real character count and no premature writes.
    await expect(drawer.getByRole('textbox', { name: 'Find a policy by name or topic' })).toBeHidden();
    const rationale = 'Defines access responsibilities for the system team.';
    await drawer.getByRole('textbox', { name: 'Why does this apply to this system?' }).fill(rationale);
    await expect(drawer.getByText(`${rationale.length}/500`)).toBeVisible();
    expect(state.references).toHaveLength(0);
    await page.screenshot({ path: `test-results/policies-explain-${width}-${theme}.png`, fullPage: true });
    await drawer.getByRole('button', { name: 'Add system reference' }).click();
    const details = page.getByRole('dialog', { name: 'Policy reference details' });
    await expect(details.getByRole('region', { name: 'Retained source' })).toContainText('Original access policy content.');
    expect(state.references[0]?.rationale).toBe(rationale);
    // Act: source changes must not replace retained text.
    state.sources[0]!.revision = 'revision-b'; state.sources[0]!.description = 'Updated library content.';
    await page.reload();
    await expect(details.getByText('The library source has changed. Your retained reference is unchanged.')).toBeVisible();
    await expect(details.getByRole('region', { name: 'Retained source' })).toContainText('Original access policy content.');
    await details.getByRole('button', { name: 'Edit rationale' }).click();
    await details.getByRole('textbox', { name: 'Why does this apply to this system?' }).fill('Updated system rationale.');
    await details.getByRole('button', { name: 'Save rationale' }).click();
    await expect(details.getByText('Updated system rationale.')).toBeVisible();
    await page.goBack();
    await expect(details).toBeHidden();
    await expect(page.getByRole('table', { name: 'System policy references' })).toBeVisible();
    await page.getByRole('textbox', { name: 'Search policy references' }).fill('Access');
    await expect(page.getByRole('textbox', { name: 'Search policy references' })).toBeFocused();
    await page.getByRole('button', { name: 'View reference Access control policy' }).click();
    await expect(details.getByRole('button', { name: 'Close policy drawer' })).toBeFocused();
    await page.keyboard.press('Shift+Tab');
    await expect(details.getByRole('button', { name: 'Close', exact: true })).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(details).toBeHidden();
    await expect(page.getByRole('button', { name: 'View reference Access control policy' })).toBeFocused();
    await expect(page.getByRole('textbox', { name: 'Search policy references' })).toHaveValue('Access');
    await page.screenshot({ path: `test-results/policies-list-${width}-${theme}.png`, fullPage: true });
    await page.getByRole('button', { name: 'View reference Access control policy' }).click();
    await details.getByRole('button', { name: 'Unlink from system' }).click();
    await expect(details.getByText('The organization policy and other assignments will remain.')).toBeVisible();
    await details.getByRole('button', { name: 'Confirm unlink' }).click();
    await expect(page.getByText('No policies linked yet')).toBeVisible();
    expect(state.sources).toHaveLength(1);
    // Act: explicit library creation returns to selection without assignment.
    await page.getByRole('button', { name: 'Add policy', exact: true }).click();
    await drawer.getByRole('button', { name: 'Create a library policy' }).click();
    await drawer.getByRole('textbox', { name: 'Policy name' }).fill('Incident response policy');
    await drawer.getByRole('textbox', { name: 'Source description' }).fill('Retained incident response guidance.');
    await drawer.getByRole('button', { name: 'Save library policy' }).click();
    await expect(drawer.getByRole('radio', { name: /Incident response policy/ })).toBeChecked();
    expect(state.references).toHaveLength(0);
    await drawer.getByRole('button', { name: 'Cancel' }).click();
    await expect(drawer).toBeHidden();
    expect(state.sources).toHaveLength(2);
  });
}
