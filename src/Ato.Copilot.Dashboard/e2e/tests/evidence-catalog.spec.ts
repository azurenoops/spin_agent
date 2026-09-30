import { expect, test, type Page } from '@playwright/test';

const route = '/workspaces/organizations/org-a/systems/system-a/evidence';
const records = [
  { id: 'artifact:review', recordId: 'review', source: 'Manual', name: 'Access review report',
    sourceLabel: 'System upload', recordedAt: '2026-09-25T10:00:00Z', category: 'Other', linksKnown: true,
    controls: [{ controlId: 'AC-2', title: 'Account management', kind: 'direct' }] },
  { id: 'automated:configuration', recordId: 'configuration', source: 'Automated', name: 'Configuration assessment',
    sourceLabel: 'Automated collection', recordedAt: '2026-09-24T10:00:00Z', category: 'Configuration', linksKnown: true,
    controls: [{ controlId: 'CM-6', title: 'Configuration settings', kind: 'automated' },
      { controlId: 'CM-2', title: 'Baseline configuration', kind: 'validation' },
      { controlId: 'CM-3', title: 'Configuration change control', kind: 'validation' }] },
  { id: 'artifact:exercise', recordId: 'exercise', source: 'Manual', name: 'Incident response exercise',
    sourceLabel: 'System upload', recordedAt: '2026-09-23T10:00:00Z', category: 'TestResult', linksKnown: true, controls: [] },
];
async function fixture(page: Page, baseURL: string) {
  await page.route(/^https?:\/\/[^/]+\/api\//, async intercepted => {
    const url = new URL(intercepted.request().url()), path = decodeURIComponent(url.pathname);
    const ok = (data: unknown) => intercepted.fulfill({ json: { status: 'success', data } });
    if (path === '/api/auth/login-config') return ok({
      branding: { deploymentName: 'SPIN test fixture', logoUrl: null, supportEmail: null },
      defaultMethod: 'Entra', enabledMethods: [], cloud: 'AzurePublic', idleTimeoutMinutes: 30,
      rememberTenantCookieDays: 0, simulation: null,
      msal: { clientId: '11111111-1111-1111-1111-111111111111', authority: 'https://login.microsoftonline.com/common',
        redirectUri: `${baseURL}/login/callback`, postLogoutRedirectUri: `${baseURL}/` },
    });
    if (path === '/api/auth/me') return ok({
      oid: 'evidence-user', directoryTenantId: 'directory-a', displayName: 'Evidence reader', persona: 'MissionOwner',
      homeTenant: null, effectiveTenant: { id: 'org-a', displayName: 'SPIN Demo Organization', status: 'Active' },
      isImpersonating: false, impersonation: null, isCspAdmin: false, isSocAnalyst: false, pimRoles: [],
      tenantMemberships: [], availableWorkspaces: [], availableWorkspacesTotal: 0,
      workspace: { kind: 'organization', tenantId: 'org-a', mode: 'ordinary', displayName: 'SPIN Demo Organization',
        personId: 'person-a', roles: ['MissionOwner'], permissions: { canAccessCsp: false, canManageMemberships: false,
          canManageOrganization: false, canCreateSystem: false } },
    });
    if (path.endsWith('/workspace-access')) return ok({
      systemId: 'system-a', roles: ['MissionOwner'], permissions: {
        canRead: true, canEditProfile: false, canManageSystem: false, canAuthorNarratives: false,
        canReviewNarratives: false, canManageEvidence: false, canRunAssessments: false,
        canManageRemediation: false, canDecideAuthorization: false,
      },
    });
    if (path === '/api/dashboard/systems/system-a/evidence-catalog') {
      if (['dateFrom', 'dateTo'].some(key => url.searchParams.has(key) && url.searchParams.get(key) === ''))
        return intercepted.fulfill({ status: 400, json: { error: 'Omit unset optional dates.' } });
      const filtered = records.filter(item => `${item.name} ${item.controls.map(c => c.controlId).join(' ')}`.toLowerCase().includes((url.searchParams.get('search') ?? '').toLowerCase()));
      return intercepted.fulfill({ json: {
        systemId: 'system-a', items: url.searchParams.get('view') === 'provider' ? [] : filtered,
        totalCount: url.searchParams.get('view') === 'provider' ? 0 : filtered.length,
        availableCount: url.searchParams.get('view') === 'provider' ? 0 : filtered.length, page: 1, pageSize: 25,
        counts: { all: filtered.length, system: filtered.length, provider: 0, missingLinks: filtered.filter(i => !i.controls.length).length },
        sources: [{ source: 'system', state: 'available', message: null }, { source: 'provider', state: 'available', message: null }],
        permissions: { canUpload: false, uploadReason: 'Upload access required' },
      } });
    }
    if (path.startsWith('/api/dashboard/systems/system-a/evidence-catalog/')) {
      const item = records.find(r => path.endsWith(r.id));
      if (!item) return intercepted.fulfill({ status: 404, json: { error: 'Record not accessible.' } });
      return intercepted.fulfill({ json: {
        systemId: 'system-a', item, description: 'Retained record for human review.', owner: null,
        recordedBy: 'System security team', version: '1', contentHash: 'retained-hash', contentType: 'application/pdf',
        fileSizeBytes: 1200, availability: 'FileAvailable', availabilityReason: null, summary: null,
        review: null, currency: null, relevance: null,
        provenance: [{ label: 'Collection method', value: 'Manual' }],
        history: [{ id: 'v1', label: 'Record uploaded', at: item.recordedAt, actor: 'System security team', fileName: null, downloadUrl: null }],
        permissions: { canDownload: true, downloadUrl: '/api/dashboard/systems/system-a/evidence/review/download',
          canReplace: false, canDelete: false, canCollect: false, canLink: false,
          manageReason: 'View only', linkReason: 'Your applicable assignments do not authorize control linking.' },
      } });
    }
    if (path.endsWith('/evidence/review/download')) return intercepted.fulfill({ status: 403, json: { status: 'error', error: { message: 'Access was revoked.' } } });
    if (path.endsWith('/profile/completeness')) return intercepted.fulfill({ json: {
      systemId: 'system-a', totalSections: 0, statusCounts: {}, approvedPercentage: 0, isProfileComplete: false,
      incompleteSections: [], missionOwnerAssigned: false, missionOwnerName: null, daysSinceRegistration: 1,
    } });
    if (path.endsWith('/todos')) return intercepted.fulfill({ json: { items: [] } });
    if (path === '/api/dashboard/systems/system-a') return intercepted.fulfill({ json: {
      systemId: 'system-a', name: 'SPIN Demo System', acronym: 'SPIN', systemType: 'Application',
      missionCriticality: 'MissionSupport', hostingEnvironment: 'Cloud', impactLevel: 'IL4',
      baselineLevel: 'Moderate', currentRmfPhase: 'Implement', rmfPhaseProgress: [], keyMetrics: {},
      recentActivity: [], categorization: null,
    } });
    return intercepted.fulfill({ status: 404, json: { error: `No fixture for ${path}` } });
  });
}

for (const width of [1440, 390]) {
  for (const theme of ['light', 'dark']) {
    test(`Evidence catalog drawer, navigation and alternate empty state ${width}px ${theme}`, async ({ page, baseURL }) => {
      // Arrange
      await page.setViewportSize({ width, height: 1000 });
      await fixture(page, baseURL!);
      await page.goto(route);
      await page.getByRole('heading', { name: 'Evidence', exact: true }).waitFor();
      await page.locator('html').evaluate((element, dark) => element.classList.toggle('dark', dark), theme === 'dark');
      // Act / Assert
      await expect(page.getByText('1 record needs a control link')).toBeVisible();
      await expect(page.getByRole('button', { name: 'Upload evidence' })).toBeDisabled();
      await expect(page.getByText('No provider evidence shared yet.')).toBeHidden();
      await page.screenshot({ path: `test-results/evidence-catalog-${width}-${theme}.png`, fullPage: true });
      await page.getByRole('button', { name: 'View evidence Access review report' }).click();
      const drawer = page.getByRole('dialog', { name: 'Evidence details' });
      await expect(drawer).toBeVisible();
      await expect(drawer.getByText('File available')).toBeVisible();
      await expect(drawer.getByText('Owner not recorded')).toBeVisible();
      await page.screenshot({ path: `test-results/evidence-drawer-${width}-${theme}.png`, fullPage: true });
      if (width < 650) expect((await drawer.boundingBox())?.width).toBe(width);
      await page.keyboard.press('Shift+Tab');
      await expect(drawer.getByRole('button', { name: 'Close', exact: true })).toBeFocused();
      await page.keyboard.press('Tab');
      await expect(drawer.getByRole('button', { name: 'Close evidence details' })).toBeFocused();
      await drawer.getByRole('tab', { name: 'History' }).click();
      await expect(drawer.getByText('Record uploaded')).toBeVisible();
      await page.reload();
      await expect(drawer.getByText('Record uploaded')).toBeVisible();
      await page.goBack();
      await expect(drawer).toBeHidden();
      await page.getByRole('tab', { name: 'Provider shared (0)' }).click();
      await expect(page.getByText('No provider evidence shared yet.')).toBeVisible();
      await expect(page.getByRole('table')).toBeHidden();
      await expect(page.getByRole('navigation', { name: 'Evidence pages' })).toBeHidden();
      await page.getByRole('button', { name: 'Sharing guidance' }).click();
      await expect(page.getByRole('region', { name: 'Sharing guidance' })).toBeVisible();
      await page.getByRole('tab', { name: 'All evidence (3)' }).click();
      await expect(page.getByRole('tab', { name: 'All evidence (3)' })).toHaveAttribute('aria-selected', 'true');
      await page.getByRole('textbox', { name: 'Search evidence or control' }).fill('Access');
      await expect(page.getByRole('tab', { name: 'All evidence (1)' })).toBeVisible();
      await page.getByRole('button', { name: 'View evidence Access review report' }).click();
      await expect(drawer.getByText('File available')).toBeVisible();
      await page.keyboard.press('Escape');
      await expect(drawer).toBeHidden();
      await expect(page.getByRole('textbox', { name: 'Search evidence or control' })).toHaveValue('Access');
      await expect(page.getByRole('button', { name: 'View evidence Access review report' })).toBeFocused();
      await page.getByRole('button', { name: 'View evidence Access review report' }).click();
      await drawer.getByRole('button', { name: 'Open file' }).click();
      await expect(drawer.getByRole('alert')).toContainText('Access was revoked.');
      await expect(drawer.getByText('Retained record for human review.')).toBeHidden();
    });
  }
}
