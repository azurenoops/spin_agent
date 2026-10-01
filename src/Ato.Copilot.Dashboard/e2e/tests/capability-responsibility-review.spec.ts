import { expect, test, type Page } from '@playwright/test';
import type { CapabilityResponsibilityResponse } from '../../src/api/capabilityResponsibilities';
import type { NarrativeProposal } from '../../src/api/narrativeLibrary';
import axe from 'axe-core';
import type { ResponsibilityDraft, ResponsibilityDraftContext, ResponsibilityValue } from '../../src/api/responsibilityDrafts';

declare global {
  interface Window { axe: typeof axe; }
}

const systemRoot = '/workspaces/organizations/org-a/systems/system-a';
const apiRoot = '/api/dashboard/systems/system-a/capability-subscriptions';
const proposalId = '44444444-4444-4444-4444-444444444444';
const sourceSnapshot = (controls = ['AC-1', 'AC-2', 'AC-3', 'AC-4']) => JSON.stringify({
  Id: '11111111-1111-1111-1111-111111111111', Name: 'Published access capability', Description: 'Current provider review description.',
  Status: 0, Controls: controls, Component: {
    CspInheritedComponentId: '22222222-2222-2222-2222-222222222222', CspProfileId: '33333333-3333-3333-3333-333333333333',
    Name: 'Published provider component', Description: 'Approved provider component description.', Status: 1, SourceArtifactReference: '[redacted]',
  },
});
const allocation = (state = 'MissingAllocation', controlId = 'AC-1') => ({
  subscriptionId: 'subscription-a', capabilityId: '11111111-1111-1111-1111-111111111111',
  componentId: '22222222-2222-2222-2222-222222222222', cspProfileId: '33333333-3333-3333-3333-333333333333',
  providerName: 'Synthetic Provider', controlId, sourceRevision: 'source-1', reviewRevision: 'review-1', state, reviewedSourceRevision: null,
  confirmedBy: null, confirmedAt: null, allocation: null, effectiveInheritanceType: null, designationSource: null,
  sourceAvailable: true, sourceSnapshotJson: sourceSnapshot(), reviewedSourceSnapshotJson: null,
});

async function installFixture(page: Page, baseURL: string, options: {
  baseline?: boolean; canConfirm?: boolean; conflict?: boolean;
  source?: 'available' | 'unavailable' | 'removed' | 'malformed'; sourceRevision?: string;
  reviewed?: boolean;
  failSave?: boolean;
  drafts?: boolean;
  failGeneration?: boolean;
} = {}) {
  const writes: { path: string; method: string; body: unknown; tenant: string | undefined; mode: string | undefined }[] = [];
  const reads: { path: string; tenant: string | undefined; mode: string | undefined }[] = [];
  let conflicts = options.conflict ? 1 : 0;
  let data: CapabilityResponsibilityResponse = {
    systemId: 'system-a',
    baselineId: options.baseline === false ? null : 'baseline-a',
    baselineName: options.baseline === false ? null : 'Moderate baseline',
    canConfirm: options.canConfirm !== false,
    items: [allocation(options.baseline === false ? 'MissingBaseline' : 'MissingAllocation'), {
      ...allocation('PreservedOverride', 'AC-2'), effectiveInheritanceType: 'Customer', designationSource: 'Manual',
    }, allocation('OutsideBaseline', 'AC-3'), allocation('Inactive', 'AC-4')],
    pendingImpacts: ['AC-1', 'AC-2'].map((controlId, index) => ({
      id: index === 0 ? 'impact-a' : 'impact-b', baselineId: 'baseline-a', controlId, stateHash: `state-${index}`,
      reason: 'SourceReconciled', sourcesJson: '[{"SubscriptionId":"subscription-a","IsActive":false}]', createdAt: '2026-09-21T00:00:00Z',
    })),
  };
  data.items = data.items.map(item => ({
    ...item, sourceRevision: options.sourceRevision ?? item.sourceRevision,
    sourceAvailable: options.source !== 'unavailable',
    sourceSnapshotJson: options.source === 'unavailable' ? null : options.source === 'malformed' ? '{"Controls":'
      : options.source === 'removed' ? sourceSnapshot(['AC-2', 'AC-3', 'AC-4']) : item.sourceSnapshotJson,
    state: item.controlId === 'AC-1' && (options.source === 'unavailable' || options.source === 'removed') ? 'PendingReview' : item.state,
    reviewedSourceSnapshotJson: options.reviewed && item.controlId === 'AC-1'
      ? JSON.stringify({ ...JSON.parse(sourceSnapshot()), Name: 'Actual previously reviewed public capability', Description: 'Persisted public review description.' })
      : item.reviewedSourceSnapshotJson,
    reviewedSourceRevision: options.reviewed && item.controlId === 'AC-1' ? 'REVIEWED-OPAQUE-PIN' : item.reviewedSourceRevision,
  }));
  if (options.drafts) {
    data.supportsResponsibilityDrafts = true;
    data.baselineControlIds = ['AU-11'];
    data.systemAllocations = [];
  }
  const sourceValue = (value: string): ResponsibilityValue => ({
    value, origin: 'From system records', sourceIds: ['fixture-system'], explanation: 'Synthetic fixture record.',
    userEdited: false, sourceHash: 'fixture-source-1',
  });
  let draft: ResponsibilityDraft | null = null;
  const draftContext = (scopeId: string | null): ResponsibilityDraftContext => ({
    systemId: 'system-a', controlId: 'AU-11', baselineId: 'baseline-a', scopeId, canPrepare: options.canConfirm !== false,
    sourceHash: 'fixture-source-1', scopes: [], sources: [{
      id: 'fixture-system', title: 'Synthetic system record', origin: 'From system records', version: 'fixture-source-1',
      content: 'Synthetic system record for isolated tests only.', href: `${systemRoot.replace('/workspaces/organizations/org-a', '')}/evidence`,
    }],
    sourceValues: {
      allocation: sourceValue('NeedsConfirmation'), provider: sourceValue(''), providerDuties: sourceValue(''),
      customer: sourceValue('Recorded local operational duty.'), scope: sourceValue(''), exclusions: sourceValue(''),
      source: sourceValue(''), basis: sourceValue('Recorded system ownership context.'), information: sourceValue('Confirm responsibility.'),
    },
    questions: ['Synthetic demonstration only.'], conflicts: [], draft,
  });
  let proposal: NarrativeProposal = {
    id: proposalId, controlId: 'AC-1', narrativeType: 'Technical', baseVersion: 7, beforeContent: 'Preserved approved narrative',
    proposedContent: '', stateHash: 'opaque-narrative-state', provenance: { changeOrigin: { SubscriptionId: 'subscription-a' } },
    conflicts: [], missingEvidence: [], status: 'PendingGeneration', revision: 1, createdAt: '2026-09-21T00:00:00Z',
    createdBy: 'provider-actor', reviewedAt: null, reviewedBy: null, reviewNote: null, acceptedVersion: null,
    isStale: false, canReview: false, generationErrorCode: null, changeSourceKind: 'CspCapability',
    changeSourceId: '11111111-1111-1111-1111-111111111111',
  };
  await page.route(/^https?:\/\/[^/]+\/api\//, async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const json = (value: unknown) => route.fulfill({ json: value });
    const success = (value: unknown) => json({ status: 'success', data: value });
    if (request.method() !== 'GET') writes.push({
      path, method: request.method(), body: request.postDataJSON(), tenant: request.headers()['x-workspace-tenant-id'],
      mode: request.headers()['x-workspace-mode'],
    });
    else reads.push({ path, tenant: request.headers()['x-workspace-tenant-id'], mode: request.headers()['x-workspace-mode'] });
    if (path === '/api/auth/login-config') return success({
      branding: { deploymentName: 'Synthetic responsibility review', logoUrl: null, supportEmail: null },
      defaultMethod: 'Entra', enabledMethods: [], cloud: 'AzurePublic', idleTimeoutMinutes: 30,
      rememberTenantCookieDays: 0, simulation: null,
      msal: { clientId: '11111111-1111-1111-1111-111111111111', authority: 'https://login.microsoftonline.com/common',
        redirectUri: `${baseURL}/login/callback`, postLogoutRedirectUri: `${baseURL}/` },
    });
    if (path === '/api/auth/me') return success({
      oid: 'synthetic-reviewer', directoryTenantId: 'synthetic-directory', displayName: 'Synthetic reviewer', persona: 'ISSO',
      homeTenant: null, effectiveTenant: { id: 'org-a', displayName: 'Organization A', status: 'Active' },
      isImpersonating: false, impersonation: null, isCspAdmin: false, isSocAnalyst: false, pimRoles: [],
      tenantMemberships: [], availableWorkspaces: [], availableWorkspacesTotal: 0,
      workspace: { kind: 'organization', tenantId: 'org-a', mode: 'ordinary', displayName: 'Organization A', personId: 'person-a',
        roles: ['ISSO'], permissions: { canAccessCsp: false, canManageMemberships: false, canManageOrganization: false, canCreateSystem: false } },
    });
    if (path.endsWith('/workspace-access')) return success({
      systemId: 'system-a', roles: ['ISSO'], permissions: { canRead: true, canEditProfile: false, canManageSystem: false,
        canAuthorNarratives: false, canReviewNarratives: false, canManageEvidence: false, canRunAssessments: false,
        canManageRemediation: false, canDecideAuthorization: false },
    });
    if (path === `${apiRoot}/responsibilities`) return json(data);
    if (options.drafts && path.includes(`${apiRoot}/drafts/`)) {
      const scopeId = new URL(request.url()).searchParams.get('scopeId');
      if (request.method() === 'GET') return json(draftContext(scopeId));
      const body = request.postDataJSON();
      if (path.endsWith('/prepare')) {
        const values = draftContext(body.scopeId).sourceValues;
        const suggestion = { ...values, allocation: { ...sourceValue('Customer'), origin: 'AI proposed' as const },
          customer: { ...sourceValue(draft ? 'Refreshed proposed duty.' : 'AI proposed local duty.'), origin: 'AI proposed' as const } };
        draft = {
          id: '55555555-5555-5555-5555-555555555555', revision: (draft?.revision ?? 0) + 1,
          status: draft ? 'ComparisonRequired' : 'Proposed', sourceHash: 'fixture-source-1', isStale: false,
          generationState: options.failGeneration ? 'Failed' : body.generate ? 'Prepared' : 'NotRequested',
          generationError: options.failGeneration ? 'Synthetic generation unavailable.' : null,
          preparedAt: '2026-10-01T00:00:00Z', generatedAt: body.generate ? '2026-10-01T00:00:00Z' : null,
          preparedBy: 'Synthetic reviewer', reviewedAt: null, reviewedBy: null,
          values: draft?.values ?? (body.generate && !options.failGeneration ? suggestion : values),
          suggestion: { values: suggestion, questions: [], conflicts: [] },
          sources: draftContext(body.scopeId).sources, history: [],
        };
        return options.failGeneration
          ? route.fulfill({ status: 503, json: { status: 503, title: 'Synthetic generation unavailable.', draftContext: draftContext(body.scopeId) } })
          : json(draftContext(body.scopeId));
      }
      if (request.method() === 'PUT' && draft) {
        const savedDraft = draft;
        draft = { ...draft, revision: draft.revision + 1, status: body.applySuggestion ? 'Proposed' : draft.status,
          values: Object.fromEntries(Object.entries(body.values).map(([key, value]) =>
            [key, { ...savedDraft.values[key as keyof typeof savedDraft.values], value: String(value), userEdited: true }])) as ResponsibilityDraft['values'] };
        return json(draft);
      }
      if (path.endsWith('/confirm') && draft) {
        draft = { ...draft, revision: draft.revision + 1, status: 'Accepted', reviewedBy: 'Synthetic reviewer', reviewedAt: '2026-10-01T01:00:00Z' };
        data.systemAllocations = [{ controlId: 'AU-11', inheritanceType: 'Customer', provider: null, customerResponsibility: draft.values.customer.value }];
        return json(draft);
      }
      return route.fulfill({ status: 400, json: { status: 400, title: 'Unexpected synthetic draft operation' } });
    }
    if (path.endsWith('/responsibilities') && request.method() === 'PUT') {
      if (options.failSave) return route.fulfill({ status: 500, json: { status: 500, title: 'Synthetic save failure' } });
      if (conflicts-- > 0) {
        data = { ...data, items: data.items.map(item => ({ ...item, sourceRevision: 'source-2', reviewRevision: 'review-2' })) };
        return route.fulfill({ status: 409, json: { status: 409, title: 'Provider changed', errorCode: 'RESPONSIBILITY_REVIEW_CONFLICT' } });
      }
      data = { ...data, items: data.items.map(item => item.controlId === 'AC-1' ? {
        ...item, state: 'Applied', reviewRevision: 'review-confirmed', allocation: {
          controlId: 'AC-1', inheritanceType: 'Shared', provider: 'Synthetic reviewed provider', customerResponsibility: 'Customer reviews access.',
        }, reviewedSourceRevision: item.sourceRevision, reviewedSourceSnapshotJson: item.sourceSnapshotJson,
        confirmedBy: 'synthetic-reviewer', confirmedAt: '2026-09-21T00:00:00Z',
        providerCoverageVerified: true, customerDutiesReviewed: true,
        reviewNotes: request.postDataJSON().reviewNotes,
        effectiveInheritanceType: 'Shared', designationSource: 'CspSubscription',
      } : { ...item, reviewRevision: 'review-confirmed' }) };
      return json(data);
    }
    if (path === `${apiRoot}/reconcile`) return json(data);
    if (path === `${apiRoot}/review-impacts/dispatch`) {
      data = { ...data, pendingImpacts: data.pendingImpacts.filter(impact => impact.id === 'impact-b') };
      return json({ delivered: 1, pending: 1, proposalIds: [proposalId],
        deferred: [{ impactId: 'impact-b', controlId: 'AC-2', reason: 'MissingNarrative' }] });
    }
    if (path === '/api/systems/system-a/narrative-library') return json([]);
    if (path === '/api/systems/system-a/narrative-library/proposals') return json([proposal]);
    if (path.startsWith('/api/systems/system-a/narrative-library/proposals/') && path.endsWith('/impact-receipts'))
      return json({ items: [], totalCount: 0, page: 1, pageSize: 50 });
    if (path === '/api/systems/system-a/narrative-library/access') return json({
      tenantId: 'org-a', systemName: 'Synthetic responsibility system', canAuthor: false, canPublishShared: false, capabilities: [],
    });
    if (path === '/api/dashboard/systems/system-a/narratives') return json([{
      id: 'implementation-a', controlId: 'AC-1', family: 'AC', narrative: null, policyNarrative: null,
      technicalNarrative: 'Preserved approved narrative', migratedFromLegacy: false, implementationStatus: 'Implemented',
      approvalStatus: 'Approved', authoredBy: 'customer-author', authoredAt: '2026-09-21T00:00:00Z',
      version: 7, isAutoPopulated: false, aiSuggested: false,
    }]);
    if (path.endsWith('/organization-context')) return json({ ok: true, data: null });
    if (path.endsWith('/profile/completeness')) return json({ statusCounts: {}, totalSections: 0, approvedPercentage: 0, incompleteSections: [] });
    if (path.endsWith('/todos')) return json({ items: [], currentPhase: 'Prepare', nextPhase: 'Categorize' });
    if (path === '/api/dashboard/systems/system-a') return json({
      systemId: 'system-a', name: 'Synthetic responsibility system', acronym: 'SYN', currentRmfStep: 'Prepare', rmfPhase: 'Prepare',
      hostingEnvironment: 'Synthetic cloud', systemType: 'MajorApplication', missionCriticality: 'MissionEssential',
      activeAssessments: [], roleAssignments: [], boundaryResources: [],
    });
    if (path === '/api/dashboard/systems/system-a/inheritance') return json({
      items: [], totalItems: 0, page: 1, pageSize: 50, summary: null,
    });
    if (path === '/api/dashboard/systems/system-a/capability-coverage') return json({
      capabilities: [], summary: { totalCapabilities: 0, totalMappedControls: 0, totalNarrativesPopulated: 0,
        totalNarrativesCustom: 0, totalNarrativesEmpty: 0, coveragePercent: 0 },
    });
    return route.fulfill({ status: 404, json: { error: 'Outside the synthetic responsibility fixture' } });
  });
  return {
    writes, reads,
    setProposalStatus: (status: 'PendingGeneration' | 'GenerationFailed' | 'Draft') => {
      proposal = { ...proposal, status, revision: proposal.revision + 1,
        generationErrorCode: status === 'GenerationFailed' ? 'GENERATION_TIMEOUT' : null,
        proposedContent: status === 'Draft' ? 'Generated draft awaiting independent human review' : '' };
    },
  };
}

async function chooseShared(page: Page) {
  if (!await page.getByRole('dialog').count())
    await page.getByRole('button', { name: 'Open AC-1 responsibility' }).click();
  await page.getByRole('radio', { name: /Provider and my team/ }).check();
  await page.getByRole('textbox', { name: 'Provider for AC-1' }).fill('Synthetic reviewed provider');
  await page.getByRole('textbox', { name: 'Customer duties' }).fill('Customer reviews access.');
  await page.getByRole('textbox', { name: 'Provider duties' }).fill('Operate scoped access service.');
  await page.getByRole('textbox', { name: 'Basis for this allocation' }).fill('Reviewed system source and evidence.');
  await page.getByRole('button', { name: 'Review allocation', exact: true }).click();
  await page.getByRole('checkbox').check();
}

for (const width of [1440, 390]) {
  test(`prepared AU-11 draft, correction, comparison and authorized confirmation at ${width}px`, async ({ page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 900 });
    const { writes } = await installFixture(page, baseURL!, { drafts: true });
    await page.goto(`${systemRoot}/inheritance/subscriptions`);
    await page.getByLabel('Review any baseline control, including without a provider').selectOption('AU-11');
    await page.getByRole('button', { name: 'Review system control' }).click();
    const panel = page.getByRole('dialog', { name: 'Review responsibility AU-11' });
    // Act
    await panel.getByRole('button', { name: 'Prepare first pass', exact: true }).click();
    // Assert
    await expect(panel.getByRole('textbox', { name: 'Customer duties', exact: true })).toHaveValue('AI proposed local duty.');
    expect(writes.some(write => write.path.endsWith('/confirm'))).toBe(false);
    // Act
    await panel.getByRole('textbox', { name: 'Customer duties', exact: true }).fill('Human corrected local duty.');
    await panel.getByRole('button', { name: 'Save proposed draft' }).click();
    await expect(panel.getByText(/draft revision 2/)).toBeVisible();
    await panel.getByRole('button', { name: 'Refresh suggestion' }).click();
    await expect(panel.getByRole('region', { name: 'Compare refreshed suggestion' })).toBeVisible();
    // Assert
    await expect(panel.getByRole('textbox', { name: 'Customer duties', exact: true })).toHaveValue('Human corrected local duty.');
    // Act
    await panel.getByRole('button', { name: 'Keep my edits after comparison' }).click();
    await expect(panel.getByRole('region', { name: 'Compare refreshed suggestion' })).toHaveCount(0);
    await panel.getByRole('button', { name: 'Review allocation', exact: true }).click();
    await panel.getByRole('checkbox').check();
    await panel.getByRole('button', { name: 'Confirm responsibility' }).click();
    // Assert
    await expect(panel).toHaveCount(0);
    await expect(page.getByRole('row').filter({ hasText: 'AU-11' })).toContainText('Human corrected local duty.');
    expect(writes.filter(write => write.path.endsWith('/confirm'))).toHaveLength(1);
  });

  test(`generation failure is explicit and permits manual correction at ${width}px`, async ({ page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 900 });
    const { writes } = await installFixture(page, baseURL!, { drafts: true, failGeneration: true });
    await page.goto(`${systemRoot}/inheritance/subscriptions`);
    await page.getByLabel('Review any baseline control, including without a provider').selectOption('AU-11');
    await page.getByRole('button', { name: 'Review system control' }).click();
    const panel = page.getByRole('dialog');
    // Act
    await panel.getByRole('button', { name: 'Prepare first pass', exact: true }).click();
    // Assert
    await expect(panel.getByRole('alert')).toHaveCount(1);
    await expect(panel.getByRole('alert')).toContainText('Synthetic generation unavailable');
    await panel.getByRole('radio', { name: /My team implements/ }).check();
    await expect(panel.getByRole('textbox', { name: 'Customer duties', exact: true })).toBeEditable();
    expect(writes.some(write => write.path.endsWith('/confirm'))).toBe(false);
  });

  test(`task choices, keyboard, disclosure and fixed actions at ${width}px`, async ({ page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 800 });
    const { writes } = await installFixture(page, baseURL!);
    await page.goto(`${systemRoot}/inheritance/subscriptions`);
    await page.getByRole('button', { name: 'Open AC-1 responsibility' }).click();
    const panel = page.getByRole('dialog');
    // Act
    await panel.getByRole('radio', { name: /I need more information/ }).focus();
    await page.keyboard.press('ArrowUp');
    // Assert
    await expect(panel.getByRole('radio', { name: /My team implements/ })).toBeChecked();
    // Act
    await panel.getByRole('textbox', { name: 'Customer duties', exact: true }).fill('Maintain local duties.');
    await panel.getByRole('textbox', { name: 'Basis for this allocation' }).fill('Reviewed customer ownership.');
    await panel.getByRole('radio', { name: /Provider covers the control/ }).check();
    await panel.getByRole('textbox', { name: 'Provider duties' }).fill('Scoped archive service.');
    await panel.getByRole('textbox', { name: 'Applicable scope' }).fill('Enrolled resources.');
    await panel.getByRole('textbox', { name: 'Exclusions' }).fill('None verified.');
    await panel.getByRole('textbox', { name: 'Supporting source' }).fill('Synthetic evidence revision 1.');
    await panel.getByRole('button', { name: 'Review allocation', exact: true }).click();
    // Assert
    await expect(panel.getByRole('heading', { name: 'Review before confirming' })).toBeFocused();
    await expect(panel.getByText('Maintain local duties.', { exact: true })).toBeVisible();
    expect(writes).toHaveLength(0);
    const button = await panel.getByRole('button', { name: 'Confirm responsibility' }).boundingBox();
    expect(button).not.toBeNull();
    expect(button!.y + button!.height).toBeLessThanOrEqual(800);
    expect(await panel.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    // Act
    await panel.getByRole('button', { name: 'Back to edit' }).click();
    // Assert
    await expect(panel.getByRole('button', { name: 'Review allocation', exact: true })).toBeFocused();
    await expect(panel.getByRole('textbox', { name: 'Local operational duties (optional)' })).toHaveValue('Maintain local duties.');
    // Act
    await page.addScriptTag({ content: axe.source });
    const findings = await page.evaluate(async () => {
      return (await window.axe.run(document.querySelector('dialog')!, {
        runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
      })).violations.map(item => `${item.id}: ${item.description}`);
    });
    // Assert
    expect(findings).toEqual([]);
    await panel.getByRole('button', { name: 'Close dialog' }).focus();
    await page.keyboard.press('Shift+Tab');
    await expect(panel.getByRole('button', { name: 'Review allocation', exact: true })).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('button', { name: 'Open AC-1 responsibility' })).toBeFocused();
  });

  test(`save failure stays local and preserves the draft at ${width}px`, async ({ page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 800 });
    const { writes } = await installFixture(page, baseURL!, { failSave: true });
    await page.goto(`${systemRoot}/inheritance/subscriptions`);
    await chooseShared(page);
    // Act
    await page.getByRole('button', { name: 'Confirm responsibility' }).click();
    // Assert
    await expect(page.getByRole('alert')).toHaveCount(1);
    await expect(page.getByRole('alert')).toContainText('Confirmation failed.');
    await expect(page.getByRole('button', { name: 'Confirm responsibility' })).toBeDisabled();
    expect(writes).toHaveLength(1);
    // Act
    await page.getByRole('button', { name: 'Refresh saved state' }).click();
    // Assert
    await expect(page.getByRole('textbox', { name: 'Customer duties' })).toHaveValue('Customer reviews access.');
    await expect(page.getByText('Last verified saved allocation: Not confirmed')).toBeVisible();
    expect(writes).toHaveLength(1);
  });

  test(`explicit scoped responsibility confirmation and separate mark-only delivery at ${width}px`, async ({ page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const sourceRevision = 'OPAQUE:server-source-revision:unchanged';
    const { writes } = await installFixture(page, baseURL!, { sourceRevision });
    // Act
    await page.goto(`${systemRoot}/inheritance`);
    await page.getByRole('link', { name: 'Review subscription responsibilities' }).click();
    // Assert
    await expect(page).toHaveURL(`${baseURL}${systemRoot}/inheritance/subscriptions`);
    await expect(page.getByRole('heading', { name: 'Control responsibilities' })).toBeVisible();
    await expect(page.getByText('Preserved override', { exact: true })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'Customer', exact: true })).toBeVisible();
    await expect(page.getByRole('combobox', { name: 'Allocation for AC-3' })).toHaveCount(0);
    await page.getByRole('button', { name: 'Open AC-1 responsibility' }).click();
    const snapshot = page.getByRole('region', { name: 'Provider contribution' });
    await expect(snapshot.getByText('Synthetic Provider · Published access capability', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Review information gap' })).toBeDisabled();
    expect(writes).toEqual([]);
    await chooseShared(page);
    await page.getByRole('button', { name: 'Confirm responsibility' }).click();
    await expect(page.getByRole('cell', { name: 'Shared', exact: true })).toBeVisible();
    expect(writes[0]).toMatchObject({
      method: 'PUT', tenant: 'org-a', mode: 'ordinary',
      body: { baselineId: 'baseline-a', sourceRevision, reviewRevision: 'review-1',
        allocations: [{ controlId: 'AC-1', inheritanceType: 'Shared', provider: 'Synthetic reviewed provider', customerResponsibility: 'Customer reviews access.' }] },
    });
    expect(writes).toHaveLength(1);
    await page.getByRole('button', { name: 'Deliver pending review impacts' }).click();
    await expect(page.getByText(/1 delivered, 1 pending/)).toBeVisible();
    await expect(page.getByText(/AC-2: missing narrative/)).toBeVisible();
    await expect(page.getByRole('link', { name: `Review queued proposal ${proposalId}` }))
      .toHaveAttribute('href', `${systemRoot}/narratives/review?proposal=${proposalId}`);
    expect(writes.map(write => write.path)).toEqual([`${apiRoot}/11111111-1111-1111-1111-111111111111/responsibilities`, `${apiRoot}/review-impacts/dispatch`]);
    await page.reload();
    await expect(page.getByRole('cell', { name: 'Shared', exact: true })).toBeVisible();
    await page.getByRole('navigation', { name: 'System task views' })
      .getByRole('link', { name: 'Applied capabilities', exact: true }).click();
    await page.getByRole('navigation', { name: 'System task views' })
      .getByRole('link', { name: 'Responsibilities', exact: true }).click();
    await expect(page).toHaveURL(`${baseURL}${systemRoot}/inheritance/subscriptions`);
    await expect(page.getByRole('cell', { name: 'Shared', exact: true })).toBeVisible();
  });

  test(`provider concurrency conflict requires new explicit review at ${width}px`, async ({ page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const { writes } = await installFixture(page, baseURL!, { conflict: true });
    await page.goto(`${systemRoot}/inheritance/subscriptions`);
    await chooseShared(page);
    // Act
    await page.getByRole('button', { name: 'Confirm responsibility' }).click();
    // Assert
    await expect(page.getByRole('alert')).toContainText('review again');
    await expect(page.getByRole('button', { name: 'Confirm responsibility' })).toBeDisabled();
    await page.getByRole('button', { name: 'Refresh saved state' }).click();
    await expect(page.getByRole('textbox', { name: 'Customer duties' })).toHaveValue('Customer reviews access.');
    expect(writes).toHaveLength(1);
    await chooseShared(page);
    await page.getByRole('button', { name: 'Confirm responsibility' }).click();
    await expect(page.getByRole('cell', { name: 'Shared', exact: true })).toBeVisible();
    expect(writes[1]?.body).toMatchObject({ sourceRevision: 'source-2', reviewRevision: 'review-2' });
  });

  test(`server permission denies confirmation despite an ISSO display role at ${width}px`, async ({ page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const { writes } = await installFixture(page, baseURL!, { canConfirm: false });
    // Act
    await page.goto(`${systemRoot}/inheritance/subscriptions`);
    // Assert
    await expect(page.getByText(/Read-only: an effective assigned ISSM or ISSO/)).toBeVisible();
    await expect(page.getByRole('combobox', { name: /Allocation for/ })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Reconcile current baseline' })).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Deliver pending review impacts' })).toBeDisabled();
    expect(writes).toEqual([]);
  });

  test(`missing baseline blocks allocations and preserves baseline navigation scope at ${width}px`, async ({ page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const { writes } = await installFixture(page, baseURL!, { baseline: false });
    // Act
    await page.goto(`${systemRoot}/inheritance/subscriptions`);
    // Assert
    await expect(page.getByText('Select a baseline before confirming allocations.')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Select or review baseline' })).toHaveAttribute('href', `${systemRoot}/baseline`);
    await expect(page.getByRole('combobox', { name: /Allocation for/ })).toHaveCount(0);
    expect(writes).toEqual([]);
  });

  test(`unavailable and removed provider mappings cannot be confirmed at ${width}px`, async ({ page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const { writes } = await installFixture(page, baseURL!, { source: 'unavailable' });
    // Act
    await page.goto(`${systemRoot}/inheritance/subscriptions`);
    await page.getByRole('button', { name: 'Open AC-1 responsibility' }).click();
    // Assert
    await expect(page.getByText(/Provider source unavailable/)).toBeVisible();
    await expect(page.getByRole('region', { name: 'Current provider snapshot' })).toHaveCount(0);
    await expect(page.getByRole('combobox', { name: /Allocation for/ })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Confirm selected allocations' })).toHaveCount(0);
    expect(writes).toEqual([]);
  });

  test(`removed historical mapping retains provenance without an allocation input at ${width}px`, async ({ page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const { writes } = await installFixture(page, baseURL!, { source: 'removed' });
    // Act
    await page.goto(`${systemRoot}/inheritance/subscriptions`);
    await page.getByRole('button', { name: 'Open AC-1 responsibility' }).click();
    // Assert
    await expect(page.getByText(/AC-1 is no longer mapped/)).toBeVisible();
    await expect(page.getByRole('combobox', { name: 'Allocation for AC-1' })).toHaveCount(0);
    await page.getByRole('radio', { name: /Provider and my team/ }).check();
    await expect(page.getByRole('button', { name: 'Review allocation', exact: true })).toBeDisabled();
    await page.getByText('Review provider scope & evidence', { exact: true }).click();
    await expect(page.getByText('Mappings: AC-2, AC-3, AC-4.', { exact: false })).toBeVisible();
    expect(writes).toEqual([]);
  });

  test(`malformed required provider snapshot blocks the preview at ${width}px`, async ({ page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const { writes } = await installFixture(page, baseURL!, { source: 'malformed' });
    // Act
    await page.goto(`${systemRoot}/inheritance/subscriptions`);
    // Assert
    await expect(page.getByRole('alert')).toContainText('provider snapshot is missing or malformed');
    await expect(page.getByRole('button', { name: 'Retry preview' })).toBeVisible();
    await expect(page.getByRole('combobox', { name: /Allocation for/ })).toHaveCount(0);
    expect(writes).toEqual([]);
  });

  test(`dispatched queue ID opens the same pending work and refreshes failed/generated status at ${width}px`, async ({ page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const fixture = await installFixture(page, baseURL!);
    await page.goto(`${systemRoot}/inheritance/subscriptions`);
    // Act
    await page.getByRole('button', { name: 'Deliver pending review impacts' }).click();
    await page.getByRole('link', { name: `Review queued proposal ${proposalId}` }).click();
    // Assert
    await expect(page).toHaveURL(`${baseURL}${systemRoot}/narratives/review?proposal=${proposalId}`);
    await expect(page.getByText(/Generation pending/)).toBeVisible();
    await expect(page.getByLabel('Proposed change')).toHaveValue(proposalId);
    await expect(page.getByRole('button', { name: /Approve/ })).toHaveCount(0);
    expect(fixture.reads).toContainEqual({ path: '/api/systems/system-a/narrative-library/proposals', tenant: 'org-a', mode: 'ordinary' });

    // Act
    fixture.setProposalStatus('GenerationFailed');
    await page.getByRole('button', { name: 'Refresh proposal status' }).click();
    // Assert
    await expect(page.getByRole('alert')).toContainText('GENERATION_TIMEOUT');
    await expect(page.getByText('Preserved approved narrative', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: /Approve/ })).toHaveCount(0);

    // Act
    fixture.setProposalStatus('Draft');
    await page.getByRole('button', { name: 'Refresh proposal status' }).click();
    // Assert
    await expect(page.getByRole('button', { name: 'Approve v8' })).toBeDisabled();
    await expect(page.getByLabel('Proposed change')).toHaveValue(proposalId);
    expect(fixture.writes.map(write => write.path)).toEqual([`${apiRoot}/review-impacts/dispatch`]);
  });

  test(`reviewed public snapshot remains visible when current provider content is withheld at ${width}px`, async ({ page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const fixture = await installFixture(page, baseURL!, { source: 'unavailable', reviewed: true });
    // Act
    await page.goto(`${systemRoot}/inheritance/subscriptions`);
    await page.getByRole('button', { name: 'Open AC-1 responsibility' }).click();
    // Assert
    const reviewed = page.getByRole('region', { name: 'Provider contribution', exact: true });
    await expect(reviewed.getByText('Synthetic Provider · Actual previously reviewed public capability', { exact: true })).toBeVisible();
    await reviewed.getByText('Review provider scope & evidence', { exact: true }).click();
    await reviewed.getByText('Technical source details', { exact: true }).click();
    await expect(reviewed.locator('pre').filter({ hasText: '[redacted]' })).toBeVisible();
    await expect(page.getByText(/Provider source unavailable. Historical review remains visible/)).toBeVisible();
    await expect(page.getByRole('region', { name: 'Current provider snapshot', exact: true })).toHaveCount(0);
    await expect(page.getByRole('combobox', { name: /Allocation for/ })).toHaveCount(0);
    expect(fixture.writes).toEqual([]);
  });
}
