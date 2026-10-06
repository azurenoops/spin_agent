import { expect, test, type Page } from '@playwright/test';

const route = '/workspaces/organizations/org-a/systems/system-a/narratives';
const statement = {
  state: 'Missing', hasContent: false, hasApprovedContent: false,
  proposalId: null, proposalStatus: null, isStale: false,
};
const item = {
  id: 'implementation-a', controlId: 'AC-2', controlTitle: 'Account Management', family: 'AC',
  implementationStatus: 'Planned', currentVersion: 3, approvalStatus: 'Draft',
  policy: statement, technical: { ...statement, state: 'Draft', hasContent: true },
  nextAction: 'AuthorPolicy', nextActionLabel: 'Add policy statement', nextActionReason: null,
};

async function installFixture(page: Page, baseURL: string) {
  await page.route(/^https?:\/\/[^/]+\/api\//, async intercepted => {
    const request = intercepted.request();
    const path = new URL(request.url()).pathname;
    const success = (data: unknown) => intercepted.fulfill({ json: { status: 'success', data } });
    if (path === '/api/auth/login-config') return success({
      branding: { deploymentName: 'SPIN narrative fixture', logoUrl: null, supportEmail: null },
      defaultMethod: 'Entra', enabledMethods: [], cloud: 'AzurePublic', idleTimeoutMinutes: 30,
      rememberTenantCookieDays: 0, simulation: null,
      msal: { clientId: '11111111-1111-1111-1111-111111111111', authority: 'https://login.microsoftonline.com/common',
        redirectUri: `${baseURL}/login/callback`, postLogoutRedirectUri: `${baseURL}/` },
    });
    if (path === '/api/auth/me') return success({
      oid: 'narrative-user', directoryTenantId: 'directory-a', displayName: 'Narrative reviewer', persona: 'ISSM',
      homeTenant: null, effectiveTenant: { id: 'org-a', displayName: 'Organization A', status: 'Active' },
      isImpersonating: false, impersonation: null, isCspAdmin: false, isSocAnalyst: false, pimRoles: [],
      tenantMemberships: [], availableWorkspaces: [], availableWorkspacesTotal: 0,
      workspace: { kind: 'organization', tenantId: 'org-a', mode: 'ordinary', displayName: 'Organization A',
        personId: 'person-a', roles: ['ISSM'], permissions: { canAccessCsp: false, canManageMemberships: false,
          canManageOrganization: false, canCreateSystem: false } },
    });
    if (path.endsWith('/workspace-access')) return success({
      systemId: 'system-a', roles: ['ISSM'], permissions: {
        canRead: true, canEditProfile: false, canManageSystem: false, canAuthorNarratives: true,
        canReviewNarratives: true, canManageEvidence: true, canRunAssessments: false,
        canManageRemediation: false, canDecideAuthorization: false,
      },
    });
    if (path === '/api/systems/system-a/requirement-coverage/AC-2') return intercepted.fulfill({ json: {
      systemId: 'system-a', controlId: 'AC-2', framework: null, catalogVersion: null, sourceUri: null,
      baselineRevision: 0, narrativeVersion: 3, parent: null, enhancements: [], requirements: [],
      parameters: [], parameterValues: {}, gaps: ['Catalog source needs reconciliation.'], proposals: [],
      canAuthor: true, canReview: false, canBind: false,
    } });
    if (path === '/api/dashboard/systems/system-a/narrative-workspace') return intercepted.fulfill({ json: {
      systemId: 'system-a', counts: { needsAttention: 1, allControls: 1, approvedStatements: 0, proposedUpdates: 0 },
      items: [item], permissions: { canAuthor: true, canReview: true, canManageEvidence: true },
    } });
    if (path === '/api/dashboard/systems/system-a/narrative-workspace/AC-2') return intercepted.fulfill({ json: {
      systemId: 'system-a', id: item.id, controlId: item.controlId, controlTitle: item.controlTitle,
      family: item.family, implementationStatus: item.implementationStatus,
      approvalStatus: item.approvalStatus, currentVersion: item.currentVersion,
      statements: {
        policy: { currentContent: '', approvedContent: '', state: 'Missing' },
        technical: { currentContent: 'Entra ID enforces conditional access.', approvedContent: '', state: 'Draft' },
      },
      proposals: [], responsibilities: [],
      history: [{ versionNumber: 3, status: 'Draft', authoredBy: 'Narrative author',
        authoredAt: '2026-09-01T00:00:00Z', changeReason: 'Updated identity flow', reviews: [] }],
      permissions: { canAuthor: true, authorReason: null, canReview: true, reviewReason: null,
        canManageEvidence: true, evidenceReason: null },
    } });
    if (path === '/api/dashboard/systems/system-a/narrative-library') return intercepted.fulfill({ json: [] });
    if (path === '/api/dashboard/systems/system-a/narrative-library/proposals') return intercepted.fulfill({ json: [] });
    if (path === '/api/dashboard/systems/system-a/narrative-library/access') return intercepted.fulfill({ json: {
      tenantId: 'org-a', systemName: 'Synthetic system', canAuthor: true, canPublishShared: false,
      canGenerate: true, capabilities: [],
    } });
    if (path === '/api/dashboard/systems/system-a/profile/completeness') return intercepted.fulfill({ json: {
      systemId: 'system-a', totalSections: 0, statusCounts: {}, approvedPercentage: 0, isProfileComplete: false,
      incompleteSections: [], missionOwnerAssigned: false, missionOwnerName: null, daysSinceRegistration: 1,
    } });
    if (path === '/api/dashboard/systems/system-a/todos') return intercepted.fulfill({ json: { items: [] } });
    if (path === '/api/dashboard/systems/system-a') return intercepted.fulfill({ json: {
      systemId: 'system-a', name: 'Synthetic system', acronym: 'SYN', systemType: 'Application',
      missionCriticality: 'MissionSupport', hostingEnvironment: 'Cloud', impactLevel: 'IL4',
      baselineLevel: 'Moderate', currentRmfPhase: 'Implement', rmfPhaseProgress: [], keyMetrics: {},
      recentActivity: [], categorization: null,
    } });
    return intercepted.fulfill({ status: 404, json: { title: `No fixture for ${path}` } });
  });
}

for (const width of [1440, 390]) {
  test(`AI requirement first pass resolves readable values and saves only on acceptance at ${width}px`, async ({ page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    await installFixture(page, baseURL!);
    let saved = false;
    const requests: string[] = [];
    const statementText = 'Determine {{ insert: param, authority }} for recorded processing.';
    await page.route('**/api/systems/system-a/requirement-coverage/AC-2{,/**}', async intercepted => {
      const request = intercepted.request();
      const path = new URL(request.url()).pathname;
      if (path.endsWith('/first-pass')) {
        requests.push('generate');
        return intercepted.fulfill({ json: {
          systemId: 'system-a', controlId: 'AC-2', kind: 'Policy', expectedVersion: 3, contextHash: 'synthetic-context',
          token: 'synthetic-protected-proof', generatedAt: '2026-10-05T18:00:00Z',
          sources: [{ id: 'policy:a', kind: 'RetainedPolicy', title: 'Recorded system policy', version: '1',
            contentHash: 'synthetic-hash', reviewState: 'Retained reference; review needed' }],
          responses: [{ statementId: 'requirement-a', response: 'First pass based on recorded system information.',
            sourceIds: ['policy:a'], explanation: 'Uses the recorded system policy.' }],
          parameters: [{ parameterId: 'authority', value: 'Synthetic recorded authority', sourceIds: ['policy:a'], explanation: 'Copied from the synthetic retained policy.' }],
          questions: ['Confirm this recorded authority applies to the system.'], conflicts: [],
        } });
      }
      if (request.method() === 'PUT') {
        requests.push('save');
        const input = request.postDataJSON();
        expect(input.firstPassToken).toBe('synthetic-protected-proof');
        expect(input.parameters.authority).toBe('Synthetic recorded authority');
        expect(input.responses[0].evidence).toEqual([]);
        saved = true;
      }
      return intercepted.fulfill({ json: {
        systemId: 'system-a', controlId: 'AC-2', framework: 'SYNTHETIC', catalogVersion: '1', sourceUri: 'https://example.invalid/catalog',
        baselineRevision: 1, narrativeVersion: saved ? 4 : 3, parent: null, enhancements: [],
        requirements: [{ id: 'requirement-a', label: 'a', text: statementText,
          responses: saved ? [{ statementId: 'requirement-a', kind: 'Policy', response: 'First pass based on recorded system information.', evidence: [] }] : [],
          responseState: saved ? 'Draft' : 'Missing', reviewed: false, evidenceGap: true }],
        parameters: [{ id: 'authority', definition: '{"label":"legal authority"}' }],
        parameterValues: saved ? { authority: 'Synthetic recorded authority' } : {}, gaps: ['Supporting evidence is still needed.'],
        proposals: [], canAuthor: true, canReview: false, canBind: false,
      } });
    });
    // Act
    await page.goto(`${route}?view=all&control=AC-2&statement=policy`);
    const drawer = page.getByRole('dialog', { name: 'AC-2 Account Management' });
    // Assert
    await expect(drawer.getByText('Determine [Legal authority — not recorded] for recorded processing.')).toBeVisible();
    await expect(drawer.getByText('First pass based on recorded system information.', { exact: true })).toBeVisible();
    await expect(drawer.getByRole('textbox', { name: 'Policy response for a' })).toHaveValue('');
    expect(requests).toEqual(['generate']);
    // Act
    await drawer.getByRole('button', { name: 'Use first pass in empty fields' }).click();
    // Assert
    await expect(drawer.getByText('Determine Synthetic recorded authority for recorded processing.')).toBeVisible();
    await expect(drawer.getByRole('textbox', { name: 'Policy response for a' })).toHaveValue('First pass based on recorded system information.');
    expect(saved).toBe(false);
    // Act
    await drawer.getByRole('button', { name: 'Save requirement responses' }).click();
    // Assert
    await expect(drawer.getByText('Draft response — review needed')).toBeVisible();
    expect(requests).toEqual(['generate', 'save']);
    expect(saved).toBe(true);
  });
}

for (const author of [false, true]) {
  for (const width of [1440, 390]) {
    test(`requirement relationships and ${author ? 'author' : 'viewer'} coverage at ${width}px`, async ({ page, baseURL }) => {
      // Arrange
      await page.setViewportSize({ width, height: 1000 });
      await installFixture(page, baseURL!);
      let saved = false;
      const controls = [
        { ...item, id: 'parent', controlId: 'AC-11', controlTitle: 'Synthetic device lock', parentControlId: null },
        { ...item, id: 'child', controlId: 'AC-11(1)', controlTitle: 'Synthetic concealment', parentControlId: 'AC-11' },
      ];
      await page.route('**/api/dashboard/systems/system-a/narrative-workspace?*', intercepted =>
        intercepted.fulfill({ json: { systemId: 'system-a', counts: { needsAttention: 2, allControls: 2, approvedStatements: 0, proposedUpdates: 0 },
          items: controls, permissions: { canAuthor: author, canReview: false, canManageEvidence: author } } }));
      await page.route('**/api/dashboard/systems/system-a/narrative-workspace/*', intercepted => {
        const id = decodeURIComponent(new URL(intercepted.request().url()).pathname.split('/').pop()!);
        const selected = controls.find(control => control.controlId === id)!;
        return intercepted.fulfill({ json: {
          systemId: 'system-a', ...selected,
          statements: { policy: { currentContent: '', approvedContent: '', state: 'Missing' },
            technical: { currentContent: 'Existing synthetic technical narrative', approvedContent: '', state: 'Draft' } },
          proposals: [], responsibilities: [], history: [],
          permissions: { canAuthor: author, authorReason: author ? null : 'View only', canReview: false,
            reviewReason: 'Separate reviewer required', canManageEvidence: author, evidenceReason: null },
        } });
      });
      await page.route('**/api/systems/system-a/requirement-coverage/**', async intercepted => {
        const request = intercepted.request();
        const path = decodeURIComponent(new URL(request.url()).pathname);
        if (path.endsWith('/first-pass')) {
          const input = request.postDataJSON();
          return intercepted.fulfill({ json: { systemId: 'system-a', controlId: path.includes('AC-11(1)') ? 'AC-11(1)' : 'AC-11',
            kind: input.kind, expectedVersion: input.expectedVersion, contextHash: 'synthetic-context', token: 'synthetic-generation-proof',
            generatedAt: '2026-10-05T18:00:00Z', sources: [], responses: [], parameters: [],
            questions: ['No additional first-pass source facts are present in this synthetic relationship fixture.'], conflicts: [] } });
        }
        if (request.method() === 'PUT') {
          expect(author).toBe(true);
          expect(request.postDataJSON().expectedVersion).toBe(3);
          expect(request.postDataJSON().responses[0].response).toBe('Synthetic policy response');
          expect(request.headers()['x-workspace-tenant-id']).toBe('org-a');
          saved = true;
        }

        const enhancement = path.includes('AC-11(1)');
        return intercepted.fulfill({ json: {
          systemId: 'system-a', controlId: enhancement ? 'AC-11(1)' : 'AC-11',
          framework: 'SYNTHETIC', catalogVersion: 'test-1', sourceUri: 'https://example.invalid/catalog',
          baselineRevision: 1, narrativeVersion: saved ? 4 : 3,
          parent: enhancement ? { controlId: 'AC-11', title: 'Synthetic device lock', selected: true, hasNarrative: true } : null,
          enhancements: enhancement ? [] : [{ controlId: 'AC-11(1)', title: 'Synthetic concealment', selected: true, hasNarrative: true }],
          requirements: [{ id: enhancement ? 'synthetic-enhancement-statement' : 'synthetic-parent-statement',
            label: enhancement ? null : 'a.', text: enhancement ? 'Synthetic concealment requirement' : 'Synthetic lock requirement',
            responses: saved && !enhancement ? [{ statementId: 'synthetic-parent-statement', kind: 'Policy',
              response: 'Synthetic policy response', evidence: [] }] : [], responseState: saved ? 'Draft' : 'Missing',
            reviewed: false, evidenceGap: true }],
          parameters: [], parameterValues: {}, gaps: ['Supporting evidence is needed.'], proposals: [],
          canAuthor: author, canReview: false, canBind: false,
        } });
      });

      // Act
      await page.goto(`${route}?view=all&search=AC-11&page=1&control=AC-11&statement=policy`);
      const parent = page.getByRole('dialog', { name: 'AC-11 Synthetic device lock' });

      // Assert
      await expect(parent.getByText('Synthetic lock requirement')).toBeVisible();
      await expect(parent.getByText(/Supporting evidence is needed/)).toBeVisible();
      await expect(page.getByText('Enhancement of AC-11')).toBeVisible();
      if (author) {
        await parent.getByRole('textbox', { name: 'Policy response for a.' }).fill('Synthetic policy response');
        await parent.getByRole('button', { name: 'Save requirement responses' }).click();
        await expect(parent.getByText(/Draft response/)).toBeVisible();
        expect(saved).toBe(true);
      } else {
        await expect(parent.getByRole('button', { name: 'Save requirement responses' })).toHaveCount(0);
      }
      await parent.getByRole('button', { name: 'AC-11(1) · Synthetic concealment' }).click();
      const child = page.getByRole('dialog', { name: 'AC-11(1) Synthetic concealment' });
      await expect(child.getByText('Synthetic concealment requirement')).toBeVisible();
      await child.getByRole('button', { name: 'Parent control: AC-11 · Synthetic device lock' }).click();
      await expect(parent).toBeVisible();
      expect(new URL(page.url()).searchParams.get('search')).toBe('AC-11');
      expect(new URL(page.url()).searchParams.get('page')).toBe('1');
      expect(new URL(page.url()).pathname).toBe(route);
      await expect(page.getByText('Satisfied', { exact: true })).toHaveCount(0);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await page.keyboard.press('Escape');
      await expect(parent).toHaveCount(0);
      const parentRow = page.getByRole('row').filter({ hasText: 'Synthetic device lock' });
      const childRow = page.getByRole('row').filter({ hasText: 'Synthetic concealment' });
      await expect(childRow).toHaveClass(/cnw-enhancement-row/);
      await expect(parentRow.getByText('1 enhancement shown')).toBeVisible();
      const parentLink = parentRow.locator('.cnw-control-link');
      const childLink = childRow.locator('.cnw-control-link');
      const parentBox = await parentLink.boundingBox();
      const childBox = await childLink.boundingBox();
      expect(childBox!.x - parentBox!.x).toBeGreaterThanOrEqual(20);
      expect(childBox!.y).toBeGreaterThan(parentBox!.y);
      await expect(childLink.locator(':scope > span')).toHaveCSS('border-left-style', 'solid');
    });
  }
}

for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`preserves narrative list state and provides an accessible drawer at ${viewport.width}px`, async ({ page, baseURL }) => {
    // Arrange
    await page.setViewportSize(viewport);
    await installFixture(page, baseURL!);

    // Act
    await page.goto(`${route}?view=all&search=account`);

    // Assert
    await expect(page.getByRole('heading', { name: 'Document how your controls work' })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'All controls (1)' })).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByText('Account Management')).toBeVisible();
    await page.locator('html').evaluate(element => element.classList.add('dark'));
    await expect(page.locator('.cnw-page')).toHaveCSS('color', 'rgb(232, 235, 243)');

    // Act
    await page.getByRole('button', { name: /Account Management AC-2/ }).click();

    // Assert
    const drawer = page.getByRole('dialog', { name: 'AC-2 Account Management' });
    await expect(drawer).toBeVisible();
    await page.reload();
    await expect(drawer).toBeVisible();
    await drawer.getByRole('tab', { name: 'Technical statement' }).click();
    await expect(drawer.getByText('Entra ID enforces conditional access.')).toBeVisible();
    await drawer.getByRole('tab', { name: 'History' }).click();
    await expect(drawer.getByText('Updated identity flow')).toBeVisible();
    if (viewport.width < 700) {
      const box = await drawer.boundingBox();
      expect(box?.width).toBeGreaterThanOrEqual(viewport.width - 1);
    }

    // Act
    await page.goBack();

    // Assert
    await expect(drawer).toBeHidden();
    await expect(page.getByLabel('Search controls')).toHaveValue('account');
    expect(new URL(page.url()).searchParams.get('view')).toBe('all');

    // Act / Assert
    await page.getByRole('button', { name: /Account Management AC-2/ }).click();
    await expect(drawer).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(drawer).toBeHidden();
  });
}
