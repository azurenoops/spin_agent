import { expect, test, type Page } from '@playwright/test';
import { planWorkspace, resultsWorkspace, resultDetail } from '../../src/__tests__/fixtures/assessmentWorkspace';
import type { AssessmentReport, AssessmentResultDetail, RetainedAssessmentPlan } from '../../src/api/assessmentWorkspace';

const route = '/workspaces/organizations/org-a/systems/system-a/assessments';
async function fixture(page: Page, baseURL: string) {
  const plans = [structuredClone(planWorkspace.plan!)];
  const results: AssessmentResultDetail[] = [];
  const reports: AssessmentReport[] = [];
  const savedContent = (plan: RetainedAssessmentPlan) => `# ${plan.title}\n\n${plan.assessmentApproach ?? ''}\n\nLead: ${plan.assessmentLead ?? 'Not recorded'}\n\nIncluded controls: ${plan.controls.filter(c => c.included).map(c => c.controlId).join(', ')}\n\n${plan.controls.filter(c => !c.included).map(c => `Excluded ${c.controlId}: ${c.exclusionRationale}`).join('\n')}`;
  const state = { plans, results, reports, createdPlans: 0, reviews: 0, uploads: [] as string[],
    azureReady: false, runRequests: [] as string[], failFirstRun: false, reconciledHash: '' };
  const projectPlan = (plan: RetainedAssessmentPlan) => ({
    ...planWorkspace, plan,
    plans: plans.map(p => ({ id: p.id, title: p.title, status: p.status, revision: p.revision, generatedAt: p.generatedAt, finalizedAt: p.finalizedAt })),
    tasks: planWorkspace.tasks.map(t => ({ ...t, complete: t.key === 'lead' ? !!plan.assessmentLeadId : t.key === 'approach' ? !!plan.assessmentApproach
      : t.key === 'scope' ? plan.scopeCount > 0 : t.key === 'team' ? plan.teamMembers.length > 0 : !!plan.scheduleStart && !!plan.scheduleEnd })),
    permissions: { ...planWorkspace.permissions, canEditPlan: plan.status === 'Draft', canFinalizePlan: plan.status === 'Draft' },
  });
  const pinResult = (plan: RetainedAssessmentPlan | undefined, id: string, name: string, source: string) => {
    const value = structuredClone(resultDetail);
    value.item = { ...value.item, id, recordId: id.split(':')[1]!, name, source, method: source === 'Azure' ? 'Examine' : 'Imported scan',
      collectionStatus: source === 'Azure' ? 'Partial' : 'Imported', reviewStatus: 'Not reviewed', canReview: true,
      planId: plan?.id ?? null, planRevision: plan?.revision ?? null, planTitle: plan?.title ?? null,
      planStatusAtCollection: plan?.status ?? null, scopeControlCount: plan?.scopeCount ?? null, requiresReconciliation: false };
    value.originalScope = plan?.controls.filter(c => c.included).map(c => c.controlId) ?? [];
    value.selectedScope = [...value.originalScope]; value.observedControls = ['AC-1'];
    value.missingControls = value.selectedScope.filter(c => !value.observedControls.includes(c));
    value.permissions.canRemediate = false;
    results.push(value); return value;
  };
  await page.route(/\/hubs\//, request => request.fulfill({ status: 503 }));
  await page.route(/^https?:\/\/[^/]+\/api\//, async intercepted => {
    const request = intercepted.request(), url = new URL(request.url()), path = decodeURIComponent(url.pathname);
    const ok = (data: unknown) => intercepted.fulfill({ json: { status: 'success', data } });
    if (path === '/api/auth/login-config') return ok({
      branding: { deploymentName: 'Assessment fixture', logoUrl: null, supportEmail: null }, defaultMethod: 'Entra',
      enabledMethods: [], cloud: 'AzurePublic', idleTimeoutMinutes: 30, rememberTenantCookieDays: 0, simulation: null,
      msal: { clientId: '11111111-1111-1111-1111-111111111111', authority: 'https://login.microsoftonline.com/common',
        redirectUri: `${baseURL}/login/callback`, postLogoutRedirectUri: `${baseURL}/` },
    });
    if (path === '/api/auth/me') return ok({
      oid: 'assessor', directoryTenantId: 'directory-a', displayName: 'Alex Assessor', persona: 'SCA', homeTenant: null,
      effectiveTenant: { id: 'org-a', displayName: 'SPIN Demo Organization', status: 'Active' }, isImpersonating: false,
      impersonation: null, isCspAdmin: false, isSocAnalyst: false, pimRoles: [], tenantMemberships: [], availableWorkspaces: [], availableWorkspacesTotal: 0,
      workspace: { kind: 'organization', tenantId: 'org-a', mode: 'ordinary', displayName: 'SPIN Demo Organization', personId: 'person-a',
        roles: ['Sca', 'ISSM'], permissions: { canAccessCsp: false, canManageMemberships: false, canManageOrganization: false, canCreateSystem: false } },
    });
    if (path.endsWith('/workspace-access')) return ok({ systemId: 'system-a', roles: ['Sca', 'ISSM'], permissions: {
      canRead: true, canEditProfile: true, canManageSystem: false, canAuthorNarratives: false, canReviewNarratives: false,
      canManageEvidence: false, canRunAssessments: true, canManageRemediation: false, canDecideAuthorization: false,
    } });
    if (path === '/api/dashboard/notifications/capabilities') return intercepted.fulfill({ json: {
      recipientId: 'assessor', rest: { available: true, reasonCode: null },
      realtime: { available: false, authentication: 'bearer', cookieSessionSupported: false,
        reasonCode: 'REALTIME_BEARER_REQUIRED', hubPaths: ['/hubs/import-progress'] },
      fallback: { transport: 'rest-polling', pollIntervalSeconds: 1 },
    } });
    const root = '/api/dashboard/systems/system-a/assessment-workspace';
    const chosen = () => plans.find(p => p.id === url.searchParams.get('planId')) ?? plans.at(-1)!;
    if (path === `${root}/plan`) return intercepted.fulfill({ json: projectPlan(chosen()) });
    if (path === `${root}/plans` && request.method() === 'POST') {
      let draft = plans.find(p => p.status === 'Draft');
      if (!draft) {
        draft = { ...structuredClone(plans.at(-1)!), id: 'sap-b', status: 'Draft', revision: 1, contentHash: 'sap-b-hash-1', finalizedAt: null };
        plans.push(draft); state.createdPlans++;
      }
      return intercepted.fulfill({ json: projectPlan(draft) });
    }
    if (path.startsWith(`${root}/plans/`)) {
      const id = path.slice(`${root}/plans/`.length).split('/')[0], plan = plans.find(p => p.id === id);
      if (!plan) return intercepted.fulfill({ status: 404 });
      if (path.endsWith('/preview')) return intercepted.fulfill({ json: { systemId: 'system-a', sapId: id, revision: plan.revision, contentHash: plan.contentHash, content: savedContent(plan) } });
      if (path.endsWith('/export')) return intercepted.fulfill({ body: Buffer.from('Synthetic export bytes'), contentType: 'application/octet-stream' });
      const body = request.postDataJSON();
      if (body.expectedContentHash !== plan.contentHash || body.expectedRevision !== plan.revision || plan.status !== 'Draft')
        return intercepted.fulfill({ status: 409, json: { error: 'The saved plan changed.' } });
      if (path.endsWith('/finalize')) { plan.status = 'Finalized'; plan.finalizedAt = '2026-09-28T16:00:00Z'; }
      else {
        if (body.task === 'approach') { plan.assessmentApproach = body.assessmentApproach; plan.rulesOfEngagement = body.rulesOfEngagement; }
        if (body.task === 'lead') { plan.assessmentLeadId = body.assessmentLeadId; plan.assessmentLead = 'Alex Assessor'; }
        if (body.task === 'scope') {
          plan.scopeNotes = body.scopeNotes;
          plan.controls = plan.controls.map(c => ({ ...c, included: body.includedControlIds.includes(c.controlId), exclusionRationale: body.exclusionReasons[c.controlId] ?? null }));
          plan.scopeCount = plan.controls.filter(c => c.included).length;
        }
        plan.revision++; plan.contentHash = `${plan.id}-hash-${plan.revision}`; plan.updatedAt = '2026-09-28T15:00:00Z';
      }
      return intercepted.fulfill({ json: projectPlan(plan) });
    }
    if (path === '/api/dashboard/systems/system-a/scans/import' && request.method() === 'POST') {
      const body = request.postData() ?? ''; state.uploads.push(body);
      const planId = /name="planId"\r\n\r\n([^\r]+)/.exec(body)?.[1];
      const plan = plans.find(p => p.id === planId);
      if (!results.some(r => r.item.id === 'import:scan-a')) pinResult(plan, 'import:scan-a', 'Imported CKL results', 'CKL');
      return intercepted.fulfill({ status: 202, json: { importJobId: 'scan-a', statusUrl: '/api/dashboard/systems/system-a/scans/import/scan-a/status',
        detectedFileType: 'CKL', fileName: 'assessment.ckl', fileSizeBytes: 100 } });
    }
    if (path.endsWith('/scans/import/scan-a/status')) return intercepted.fulfill({ json: { id: 'scan-a', status: 'Completed',
      processedCount: 1, totalCount: 1, errorMessage: null, cancelRequested: false, resultId: 'import:scan-a', warnings: [] } });
    if (path === `${root}/collect`) {
      const body = request.postDataJSON(); state.runRequests.push(body.requestId);
      if (!results.some(r => r.item.id === 'assessment:azure-a')) {
        const record = pinResult(plans.find(p => p.id === body.planId), 'assessment:azure-a', 'Azure configuration checks', 'Azure');
        record.errors = ['One configured scope was unavailable.'];
      }
      if (state.failFirstRun && state.runRequests.length === 1) return intercepted.fulfill({ status: 503, json: { error: 'Connection interrupted. Retained work may exist.' } });
      return intercepted.fulfill({ json: { status: 'Partial', message: 'One scope failed; completed observations were retained.', resultIds: ['assessment:azure-a'] } });
    }
    if (path === `${root}/results`) {
      const plan = chosen(), ids = (url.searchParams.get('selectedResultIds') ?? '').split(',').filter(Boolean);
      const selected = results.filter(r => ids.includes(r.item.id));
      const items = results.filter(r => r.item.name.toLowerCase().includes((url.searchParams.get('search') ?? '').toLowerCase())).map(r => ({
        ...r.item, requiresReconciliation: r.item.planId !== plan.id && state.reconciledHash !== plan.contentHash,
      }));
      return intercepted.fulfill({ json: {
        ...resultsWorkspace, items, totalCount: items.length, selectedResults: selected.map(r => ({ id: r.item.id, name: r.item.name, revision: r.item.revision })),
        collection: { ...resultsWorkspace.collection, canRunAzure: state.azureReady, runReason: state.azureReady ? null : resultsWorkspace.collection.runReason,
          azure: { ...resultsWorkspace.collection.azure, state: state.azureReady ? 'Ready' : 'Denied', message: state.azureReady ? 'Configured subscription scope verified.' : 'Azure assessment access required.',
            scopeDescription: ['Configured subscription scope'], subscriptions: [{ id: 'subscription-a', name: 'Assessment subscription' }], checkedAt: state.azureReady ? '2026-09-28T12:00:00Z' : null } },
        sarReadiness: { canPrepareDraft: !!selected.length, blockers: selected.length ? [] : ['Select results for the draft report.'],
          warnings: selected.some(r => !r.item.reviewedControlCount) ? ['Collected results have not yet been reviewed. Draft generation remains available.'] : [],
          scopeCount: plan.scopeCount, observedControlCount: selected.length ? 1 : 0, reviewedControlCount: selected.some(r => r.item.reviewedControlCount > 0) ? 1 : 0,
          missingControlIds: plan.controls.filter(c => c.included && c.controlId !== 'AC-1').map(c => c.controlId), selectedResultIds: ids },
        permissions: { ...resultsWorkspace.permissions, canReview: true, reviewReason: null },
        reports: reports.map(r => ({ id: r.id, title: r.title, status: r.status, createdAt: r.createdAt })),
      } });
    }
    if (path.startsWith(`${root}/results/`)) {
      const key = path.slice(`${root}/results/`.length).split('/')[0]!, value = results.find(r => r.item.id === key);
      if (!value) return intercepted.fulfill({ status: 404 });
      if (request.method() === 'POST') {
        const body = request.postDataJSON();
        if (body.expectedResultRevision !== value.item.revision) return intercepted.fulfill({ status: 409, json: { error: 'Result changed.' } });
        if (path.endsWith('/review')) {
          state.reviews++; value.item.reviewedControlCount = 1; value.item.reviewStatus = 'Reviewed';
          value.history.push({ action: 'Control reviewed', actor: 'Alex Assessor', at: '2026-09-28T17:00:00Z', description: body.notes });
        } else if (path.endsWith('/reconcile')) state.reconciledHash = body.expectedPlanHash;
        value.item.revision = `result-revision-${state.reviews}-${state.reconciledHash}`;
      }
      const plan = chosen();
      return intercepted.fulfill({ json: { ...value, selectedScope: plan.controls.filter(c => c.included).map(c => c.controlId),
        missingControls: plan.controls.filter(c => c.included && !value.observedControls.includes(c.controlId)).map(c => c.controlId),
        item: { ...value.item, requiresReconciliation: value.item.planId !== plan.id && state.reconciledHash !== plan.contentHash } } });
    }
    if (path === `${root}/reports` && request.method() === 'POST') {
      const body = request.postDataJSON(), plan = plans.find(p => p.id === body.planId)!;
      const report: AssessmentReport = { id: 'sar-a', title: body.title, status: 'Draft', createdAt: '2026-09-28T18:00:00Z',
        downloadUrl: '/api/v1/systems/system-a/sar/sar-a/export', sourceResultIds: body.resultIds, warnings: ['Draft report; review and authorization remain separate.'],
        sections: [{ title: 'Selected assessment sources', content: `Plan ${plan.id}, revision ${plan.revision}. ${plan.assessmentApproach}\n\n${state.reviews} explicit control review recorded. Findings remain subject to disposition.` }] };
      if (!reports.length) reports.push(report);
      return intercepted.fulfill({ json: reports[0] });
    }
    if (path === `${root}/reports/sar-a`) return intercepted.fulfill({ json: reports[0] });
    if (path.endsWith('/profile/completeness')) return intercepted.fulfill({ json: {
      systemId: 'system-a', totalSections: 0, statusCounts: {}, approvedPercentage: 0, isProfileComplete: false,
      incompleteSections: [], missionOwnerAssigned: false, missionOwnerName: null, daysSinceRegistration: 1,
    } });
    if (path.endsWith('/todos')) return intercepted.fulfill({ json: { items: [] } });
    if (path === '/api/dashboard/systems/system-a') return intercepted.fulfill({ json: {
      systemId: 'system-a', name: 'SPIN Demo System', acronym: 'SPIN', systemType: 'Application', missionCriticality: 'MissionSupport',
      hostingEnvironment: 'Cloud', impactLevel: 'IL4', baselineLevel: 'Moderate', currentRmfPhase: 'Assess', rmfPhaseProgress: [], keyMetrics: {}, recentActivity: [], categorization: null,
    } });
    return intercepted.fulfill({ status: 404, json: { error: `No fixture for ${path}` } });
  });
  return state;
}

for (const width of [1440, 390]) for (const theme of ['light', 'dark']) {
  test(`connected plan import review and SAR ${width}px ${theme}`, async ({ page, baseURL }) => {
    test.setTimeout(60_000);
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const state = await fixture(page, baseURL!);
    await page.goto(`${route}?tab=plan&plan=sap-a`);
    await expect(page.getByRole('heading', { name: 'Prepare your assessment plan' })).toBeVisible();
    await page.locator('html').evaluate((el, dark) => el.classList.toggle('dark', dark), theme === 'dark');
    await page.getByText('Plan details & history', { exact: true }).click();
    const planActions = page.getByRole('group', { name: 'Saved plan actions' });
    const editTitle = planActions.getByRole('button', { name: 'Edit title' });
    await expect(editTitle).toHaveCSS('border-radius', '7px');
    await expect(editTitle).toHaveCSS('font-size', '14px');
    await editTitle.focus();
    await page.keyboard.press('Tab');
    await expect(planActions.getByRole('button', { name: 'Edit team' })).toBeFocused();
    const refreshBox = await planActions.getByRole('button', { name: 'Refresh saved plan' }).boundingBox();
    const titleBox = await editTitle.boundingBox();
    expect(refreshBox!.height).toBeGreaterThanOrEqual(42);
    if (width > 700) expect(Math.abs(refreshBox!.y - titleBox!.y)).toBeLessThan(2);
    else {
      expect(refreshBox!.width).toBeGreaterThan(280);
      expect(refreshBox!.x + refreshBox!.width).toBeLessThanOrEqual(width);
    }
    await planActions.screenshot({ path: `test-results/assessment-plan-actions-${width}-${theme}.png` });
    await page.getByText('Plan details & history', { exact: true }).click();
    await page.screenshot({ path: `test-results/assessment-plan-${width}-${theme}.png`, fullPage: true });
    // Act: edit the existing retained plan.
    await page.getByRole('button', { name: 'Add approach' }).click();
    let editor = page.getByRole('dialog', { name: 'Assessment approach' });
    await editor.getByRole('textbox', { name: 'Approach and procedures' }).fill('Unsaved planning work to resume.');
    await page.goBack();
    await expect(editor).toBeHidden();
    await page.getByRole('button', { name: 'Resume approach' }).click();
    await expect(editor.getByRole('textbox', { name: 'Approach and procedures' })).toHaveValue('Unsaved planning work to resume.');
    await editor.getByRole('button', { name: 'Close dialog' }).focus();
    await page.keyboard.press('Shift+Tab');
    await expect(editor.getByRole('button', { name: 'Save draft' })).toBeFocused();
    await editor.getByRole('textbox', { name: 'Approach and procedures' }).fill('Examine retained records and interview the system team.');
    await page.screenshot({ path: `test-results/assessment-approach-${width}-${theme}.png`, fullPage: true });
    await editor.getByRole('button', { name: 'Save draft' }).click();
    await expect(editor).toBeHidden();
    await page.getByRole('button', { name: 'Choose lead' }).click();
    await page.getByRole('combobox', { name: 'Assessment lead', exact: true }).selectOption('person-a');
    await page.getByRole('button', { name: 'Save draft', exact: true }).click();
    await page.getByRole('button', { name: 'Define scope' }).click();
    await page.getByRole('checkbox', { name: 'Include AC-2 Account management' }).uncheck();
    await page.getByRole('textbox', { name: 'Exclusion reason for AC-2' }).fill('Outside this assessment cycle.');
    await page.getByRole('button', { name: 'Save draft', exact: true }).click();
    await expect(page.getByText('1 control in assessment scope')).toBeVisible();
    await page.getByRole('button', { name: 'Preview saved draft' }).click();
    const preview = page.getByRole('dialog', { name: 'Saved assessment plan' });
    await expect(preview).toContainText('Examine retained records and interview the system team.');
    await expect(preview).toContainText('Outside this assessment cycle.');
    await preview.getByRole('button', { name: 'Close preview' }).click();
    expect(state.createdPlans).toBe(0);
    const originalRevision = state.plans[0]!.revision;
    // Act: preliminary import, not a second SAP generation flow.
    await page.getByRole('navigation', { name: 'Assessment workflow' }).getByRole('link', { name: /Collect results/ }).click();
    await expect(page.getByRole('button', { name: 'Run Azure checks' })).toBeDisabled();
    await expect(page.getByText('No results yet')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Finalize plan' })).toBeHidden();
    await page.screenshot({ path: `test-results/assessment-results-empty-${width}-${theme}.png`, fullPage: true });
    await page.getByRole('button', { name: 'View import options' }).click();
    const importer = page.getByRole('dialog', { name: /Import Scan Results/i });
    await importer.locator('input[type=file]').setInputFiles({ name: 'assessment.ckl', mimeType: 'application/xml', buffer: Buffer.from('<CHECKLIST><STIGS/></CHECKLIST>') });
    await importer.getByRole('button', { name: /Upload|Import/i }).last().click();
    await expect(page.getByRole('button', { name: 'Open result Imported CKL results' })).toBeVisible({ timeout: 20_000 });
    expect(state.uploads[0]).toContain('sap-a');
    expect(state.results[0]!.item.planRevision).toBe(originalRevision);
    expect(state.reviews).toBe(0);
    await page.getByRole('checkbox', { name: 'Include Imported CKL results in SAR' }).click();
    await expect(page.getByRole('checkbox', { name: 'Include Imported CKL results in SAR' })).toBeChecked();
    await expect(page.getByRole('button', { name: 'Prepare draft SAR' })).toBeEnabled();
    await page.getByRole('button', { name: 'Open result Imported CKL results' }).click();
    const result = page.getByRole('dialog', { name: 'Result details' });
    await expect(result.getByText(`Collected against revision ${originalRevision}`)).toBeVisible();
    await result.getByRole('button', { name: 'Review a control' }).click();
    await result.getByRole('combobox', { name: 'Control to review' }).selectOption('AC-1');
    await result.getByRole('combobox', { name: 'Determination', exact: true }).selectOption('Satisfied');
    await result.getByRole('textbox', { name: 'Assessor notes' }).fill('Reviewed retained records.');
    await result.getByRole('button', { name: 'Save control review' }).click();
    await expect(result.getByText('Reviewed', { exact: true })).toBeVisible();
    await result.getByRole('button', { name: 'Close result' }).click();
    // Act: finalize, then revise without mutating the historical result.
    await page.getByRole('navigation', { name: 'Assessment workflow' }).getByRole('link', { name: /Plan$/ }).click();
    await page.getByRole('button', { name: 'Finalize plan', exact: true }).click();
    await page.getByRole('button', { name: 'Finalize saved plan' }).click();
    await page.getByRole('button', { name: 'Start a new draft revision' }).click();
    await page.getByRole('button', { name: 'Create draft', exact: true }).click();
    await expect(page).toHaveURL(/plan=sap-b/);
    expect(state.plans[0]!.status).toBe('Finalized');
    expect(state.plans).toHaveLength(2);
    await page.getByRole('navigation', { name: 'Assessment workflow' }).getByRole('link', { name: /Review$/ }).click();
    await page.getByRole('button', { name: 'Open result Imported CKL results' }).click();
    await expect(result.getByText(`Collected against revision ${originalRevision}`)).toBeVisible();
    await result.getByRole('button', { name: 'Reconcile with selected plan' }).click();
    await result.getByRole('button', { name: 'Confirm reconciliation' }).click();
    await expect(result.getByRole('button', { name: 'Reconcile with selected plan' })).toBeHidden();
    await result.getByRole('button', { name: 'Close result' }).click();
    await page.screenshot({ path: `test-results/assessment-results-reviewed-${width}-${theme}.png`, fullPage: true });
    await page.getByRole('button', { name: 'Prepare draft SAR' }).click();
    await page.getByRole('button', { name: 'Prepare selected-source SAR' }).click();
    const sar = page.getByRole('dialog', { name: 'Retained assessment report' });
    await expect(sar).toContainText('1 explicit control review recorded.');
    expect(state.reports[0]!.sourceResultIds).toEqual(['import:scan-a']);
    await page.reload();
    await expect(sar).toBeVisible();
    expect(state.reports).toHaveLength(1);
    await page.keyboard.press('Escape');
    await expect(sar).toBeHidden();
  });
}

test('Azure collection retries keep the request identity and partial work', async ({ page, baseURL }) => {
  // Arrange
  const state = await fixture(page, baseURL!); state.azureReady = true; state.failFirstRun = true;
  await page.goto(`${route}?tab=results&plan=sap-a`);
  // Act
  await page.getByRole('button', { name: 'Run Azure checks' }).click();
  await page.getByRole('button', { name: 'Start scoped checks' }).click();
  await expect(page.getByRole('dialog', { name: 'Collect Azure results' }).getByRole('alert')).toContainText('Connection interrupted');
  await page.getByRole('button', { name: 'Start scoped checks' }).click();
  // Assert
  await expect(page.getByRole('alert')).toContainText('One scope failed');
  expect(state.runRequests).toHaveLength(2);
  expect(state.runRequests[0]).toBe(state.runRequests[1]);
  expect(state.results).toHaveLength(1);
  expect(state.reviews).toBe(0);
});
