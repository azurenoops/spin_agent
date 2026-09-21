import { test, expect } from '@playwright/test';
import { systemId, systemDetail, profileCompleteness } from '../../src/__tests__/fixtures/assessmentEnvironment';

test('import mappings, preserve active text and review a proposal', async ({ context, page }, testInfo) => {
  // Arrange
  const origin = 'http://localhost:4179';
  const blocked: string[] = [];
  const pageErrors: string[] = [];
  page.on('pageerror', error => pageErrors.push(error.message));
  const root = `/api/systems/${systemId}/narrative-library`;
  const dashboard = `/api/dashboard/systems/${systemId}`;
  const reference = { id: 'reference-1', referenceKey: 'key-1', title: 'Access policy', scope: 'System', scopeId: systemId,
    sourceName: 'policy.txt', sourceSha256: 'synthetic-sha256', version: 1, revision: 1, isPublished: false,
    createdAt: '2026-01-01T00:00:00Z', createdBy: 'synthetic-author', publishedAt: null as string | null, publishedBy: null as string | null,
    passages: [{ controlId: null as string | null, narrativeType: null as string | null, content: 'Review access quarterly.' }] };
  const narrative = { id: 'implementation-1', controlId: 'AC-2', family: 'AC', narrative: 'Existing technical',
    policyNarrative: 'Existing policy', technicalNarrative: 'Existing technical', migratedFromLegacy: false,
    implementationStatus: 'Planned', approvalStatus: 'Approved', authoredBy: 'synthetic-author', authoredAt: '2026-01-01T00:00:00Z',
    version: 7, isAutoPopulated: false, aiSuggested: false };
  const proposal = { id: 'proposal-1', controlId: 'AC-2', narrativeType: 'Technical', baseVersion: 7, beforeContent: 'Existing technical',
    proposedContent: 'Federated accounts. Access review evidence is missing.', stateHash: 'synthetic-state-hash',
    provenance: { referenceClaims: [], observedEvidence: [] }, conflicts: ['Imported federation claim is unverified.'],
    missingEvidence: ['Access review evidence is missing.'], status: 'Draft', revision: 1, createdAt: '2026-01-02T00:00:00Z',
    createdBy: 'synthetic-author', reviewedAt: null as string | null, reviewedBy: null as string | null, reviewNote: null as string | null,
    acceptedVersion: null as number | null, isStale: false, canReview: true };
  let imported = false;
  let generated = false;
  const responses: Record<string, unknown> = {
    '/api/auth/login-config': { status: 'success', data: {
      branding: { deploymentName: 'Synthetic dashboard', logoUrl: null, supportEmail: null }, defaultMethod: 'Simulation',
      enabledMethods: [{ id: 'Simulation', displayName: 'Simulation' }], cloud: 'AzurePublic', idleTimeoutMinutes: 30,
      rememberTenantCookieDays: 7, simulation: { identities: [] }, msal: { clientId: '00000000-0000-4000-8000-000000000001',
        authority: 'https://login.microsoftonline.com/common', redirectUri: `${origin}/login/callback`, postLogoutRedirectUri: `${origin}/login` },
    } },
    '/api/auth/me': { oid: 'synthetic-reviewer', displayName: 'Synthetic Reviewer', persona: 'ISSM',
      homeTenant: { id: 'synthetic-tenant', displayName: 'Synthetic Tenant', status: 'Active' },
      effectiveTenant: { id: 'synthetic-tenant', displayName: 'Synthetic Tenant', status: 'Active' },
      isImpersonating: false, impersonation: null, pimRoles: [], isCspAdmin: false, isSocAnalyst: false, tenantMemberships: [] },
    '/api/onboarding/organization-context': { ok: true, data: null },
    '/api/onboarding/state': { ok: true, data: { steps: [{ step: 'OrganizationContext', status: 'Completed' }, { step: 'Roles', status: 'Completed' }] } },
    '/api/onboarding/tenant/state': { status: 'success', data: { tenantId: 'synthetic-tenant', currentStep: 'Submitted',
      completedSteps: ['Tenant.LegalEntity', 'Tenant.HqAddress', 'Tenant.Classification', 'Tenant.Ao', 'Tenant.PrimaryPoc', 'Org.Profile', 'Submitted'],
      onboardingState: 'Active', firstOrganizationId: 'synthetic-organization' } },
    '/api/deployment/mode': { mode: 'SingleTenant' },
    '/api/dashboard/notifications': { items: [] }, '/api/dashboard/notifications/summary': { unreadCount: 0, totalCount: 0 },
    [dashboard]: systemDetail, [dashboard + '/todos']: { items: [] }, [dashboard + '/profile/completeness']: profileCompleteness,
    [dashboard + '/business-context/flagged-controls']: [],
    [dashboard + '/business-context/AC-2']: null,
    [root + '/access']: { tenantId: 'synthetic-tenant', systemName: systemDetail.name, canAuthor: true, canPublishShared: false, capabilities: [] },
  };
  await context.route('**/*', async route => {
    const url = new URL(route.request().url());
    if (url.origin !== origin) { blocked.push(url.href); return route.abort('blockedbyclient'); }
    if (!url.pathname.startsWith('/api/') && !url.pathname.startsWith('/hubs/')) return route.continue();
    const request = route.request();
    const endpoint = url.pathname;
    const reply = (json: unknown, status = 200) => route.fulfill({ status, json });
    if (request.method() === 'GET') {
      if (endpoint === root) return reply(imported ? [reference] : []);
      if (endpoint === root + '/proposals') return reply(generated ? [proposal] : []);
      if (endpoint === dashboard + '/narratives') return reply([narrative]);
      if (endpoint === '/api/csp/onboarding/state') return reply({}, 404);
      if (endpoint === '/api/tenants') return reply([], 403);
      if (endpoint in responses) return reply(responses[endpoint]);
      if (endpoint === dashboard + '/controls/AC-2/evidence') return reply({ direct: [], inherited: [], automated: [] });
      if (endpoint === dashboard + '/controls/AC-2/validation') return reply({ links: [] });
    }
    if (request.method() === 'POST') {
      if (endpoint === root + '/imports') {
        expect(request.headers()['content-type']).toContain('multipart/form-data');
        expect(request.postData()).toContain('Review access quarterly.');
        imported = true; return reply(reference, 201);
      }
      if (endpoint === root + '/reference-1/publish') {
        expect(request.postDataJSON()).toEqual({ expectedRevision: 1, reviewed: true,
          passages: [{ controlId: 'AC-2', narrativeType: 'Policy', content: 'Review access quarterly.' }] });
        reference.passages = request.postDataJSON().passages; reference.isPublished = true;
        reference.publishedAt = '2026-01-01T00:00:00Z'; reference.publishedBy = 'synthetic-reviewer'; reference.revision++;
        return reply(reference);
      }
      if (endpoint === root + '/proposals') {
        expect(request.postDataJSON()).toEqual({ controlId: 'AC-2', narrativeType: 'Technical', expectedVersion: 7 });
        generated = true; return reply(proposal);
      }
      if (endpoint === root + '/proposals/proposal-1/review') {
        expect(request.postDataJSON()).toEqual({ expectedRevision: 1, decision: 'Approve', note: 'Reviewed with explicit evidence gap.' });
        proposal.status = 'Approved'; proposal.reviewedBy = 'synthetic-reviewer'; proposal.acceptedVersion = 8; proposal.canReview = false;
        narrative.technicalNarrative = proposal.proposedContent; narrative.narrative = proposal.proposedContent; narrative.version = 8;
        return reply(proposal);
      }
    }
    blocked.push(`${request.method()} ${endpoint}`); return route.abort('blockedbyclient');
  });
  await context.routeWebSocket('**/*', socket => socket.close());
  await context.addInitScript(() => localStorage.setItem('ato-dashboard-settings', JSON.stringify({ role: 'ISSM', autoRefreshInterval: 0 })));

  // Act
  await page.goto(`/systems/${systemId}/narratives/library`);
  await expect(page.getByRole('heading', { name: 'Narrative Library', exact: true })).toBeVisible();
  await expect(page.locator('.narrative-workspace')).toHaveCSS('color', 'rgb(17, 24, 39)');
  await expect(page.locator('.narrative-workspace')).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
  await expect(page.getByRole('button', { name: 'Upload narratives' })).toHaveCSS('background-color', 'rgb(79, 70, 229)');
  await page.getByRole('button', { name: 'Upload narratives' }).click();
  await page.getByLabel('Reference title').fill('Access policy');
  await expect(page.getByLabel('Reference title')).toHaveCSS('background-color', 'rgb(255, 255, 255)');
  await page.getByLabel('Upload file').setInputFiles({ name: 'policy.txt', mimeType: 'text/plain', buffer: Buffer.from('Review access quarterly.') });
  await page.getByRole('button', { name: 'Extract passages' }).click();
  await expect(page.getByRole('button', { name: 'Publish references' })).toBeDisabled();
  await page.getByLabel('Control 1', { exact: true }).fill('AC-2');
  await page.getByLabel('Narrative type 1').selectOption('Policy');
  await page.getByLabel('I reviewed these reference claims and mappings').check();
  await page.screenshot({ path: testInfo.outputPath('import.png'), fullPage: true });
  await page.getByRole('button', { name: 'Publish references' }).click();
  await expect(page.getByText('Published v1')).toBeVisible();
  await expect(page.locator('.nw-reference')).toHaveCSS('background-color', 'rgb(255, 255, 255)');
  await page.screenshot({ path: testInfo.outputPath('library.png'), fullPage: true });
  await page.getByRole('link', { name: '02 · Narratives', exact: true }).click();
  await expect(page.getByText('0 of 1 implemented')).toBeVisible();
  await page.getByTitle('Expand', { exact: true }).click();
  await expect(page.getByLabel('Technical narrative for AC-2')).toHaveValue('Existing technical');
  await expect(page.getByLabel('Technical narrative for AC-2')).toHaveAttribute('readonly', '');
  if (testInfo.project.name === 'mobile') {
    const bounds = await page.getByLabel('Technical narrative for AC-2').boundingBox();
    expect(bounds).not.toBeNull();
    expect(bounds!.x).toBeGreaterThanOrEqual(0);
    expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(390);
  }
  await page.screenshot({ path: testInfo.outputPath('narratives.png'), fullPage: true });
  await page.getByRole('button', { name: 'Regenerate as draft', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Review change', exact: true })).toBeVisible();
  await expect(page.locator('.nw-diff').first()).toHaveCSS('background-color', 'rgb(255, 255, 255)');
  await expect(page.getByText('Existing technical', { exact: true })).toBeVisible();
  await page.getByLabel('Review note').fill('Reviewed with explicit evidence gap.');
  await page.screenshot({ path: testInfo.outputPath('review.png'), fullPage: true });
  await page.getByRole('button', { name: 'Approve v8', exact: true }).click();
  await expect(page.getByText(/Approved by synthetic-reviewer/)).toBeVisible();

  // Assert
  expect(narrative.policyNarrative).toBe('Existing policy');
  expect(narrative.implementationStatus).toBe('Planned');
  expect(blocked).toEqual([]);
  expect(pageErrors).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});