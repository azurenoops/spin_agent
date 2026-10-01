import { expect, test, type BrowserContext, type Page, type TestInfo } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import type { OrganizationOnboardingDraft, OrganizationSetupSummary } from '../../src/features/workspace-operations/organizationOnboardingApi';
import type { ProvisioningResult } from '../../src/features/workspace-operations/types';
import type { TenantOnboardingProgress } from '../../src/features/onboarding/TenantWizard/api';

const tenantId = '11111111-1111-1111-1111-111111111111';
const operationId = '22222222-2222-2222-2222-222222222222';
const directoryId = '33333333-3333-3333-3333-333333333333';
const objectId = '44444444-4444-4444-4444-444444444444';
const personId = '55555555-5555-5555-5555-555555555555';
const initial: ProvisioningResult = { tenantId, operationId, tenantState: 'Completed',
  administratorState: 'Pending', membershipState: 'Pending', personState: 'NotRequested',
  initialAdministrator: null, idempotencyKey: 'stable-create-key', lastError: null, canEditAdministrator: true, revision: 0 };

async function capture(page: Page, info: TestInfo, state: string) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: info.outputPath(`${state}.png`), fullPage: true });
}

async function organizationFixture(context: BrowserContext, baseURL: string) {
  await installWorkspaceFixture(context, baseURL, { providerOnly: true });
  const state = {
    drafts: new Map<string, OrganizationOnboardingDraft>(), creates: 0, grants: 0, loseSaveResponse: false,
    liveAdmin: false, forbidSummary: false, conflictNextSave: false, operation: { ...initial },
  };
  await context.route('**/api/csp/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const success = (data: unknown, status = 200) => route.fulfill({ status, json: { status: 'success', data } });
    if (path === '/api/csp/onboarding/setup') return success({
      providerId: 'provider-a', profile: { cspProfileId: 'provider-a', onboardingState: 'Active', currentStep: 'Complete',
        identity: { legalEntityName: 'Synthetic operator', displayName: 'Synthetic Provider', logoUrl: null } },
      profileRevision: 1, draft: null, access: { state: 'Authorized', scope: 'Provider', actor: { displayName: 'Synthetic Owner' } },
      handling: { state: 'Unknown', uploadsPermitted: false, analysisPermitted: false }, uploadIntents: [], facts: [],
    });
    if (path === '/api/csp/directory/connections') return success([]);
    const draftRoot = '/api/csp/organization-onboarding/drafts';
    if (path === draftRoot) return success({ items: [...state.drafts.values()], page: 1, pageSize: 25, total: state.drafts.size });
    if (path.startsWith(`${draftRoot}/`)) {
      const id = path.slice(draftRoot.length + 1).split('/')[0]!;
      if (request.method() === 'PUT') {
        const body = request.postDataJSON();
        const old = state.drafts.get(id);
        expect(body.expectedRevision).toBe(old?.revision ?? 0);
        if (state.conflictNextSave && old) {
          state.conflictNextSave = false;
          state.drafts.set(id, { ...old, revision: old.revision + 1,
            values: { ...old.values, displayName: 'Other tab organization', primaryPocName: 'Other tab contact' } });
          return route.fulfill({ status: 409, json: { error: { code: 'STALE_REVISION', message: 'Another tab saved this draft.' } } });
        }
        const draft: OrganizationOnboardingDraft = { draftId: id, revision: (old?.revision ?? 0) + 1,
          schemaVersion: 1, state: 'Draft', savedAt: '2026-09-30T13:00:00Z',
          displayName: body.values.displayName || 'Organization draft', currentStep: body.currentStep, values: body.values,
          creationKey: 'stable-create-key', tenantId: null, operationId: null, resumeUrl: `/organizations/new?draft=${id}` };
        state.drafts.set(id, draft);
        if (state.loseSaveResponse) { state.loseSaveResponse = false; return route.abort('failed'); }
        return success(draft, old ? 200 : 201);
      }
      const saved = state.drafts.get(id);
      if (!saved) return route.fulfill({ status: 404, json: { error: { code: 'NOT_FOUND', message: 'Draft not found' } } });
      if (path.endsWith('/confirm')) {
        expect(request.postDataJSON()).toEqual({ expectedRevision: saved.revision, confirmed: true });
        if (saved.state !== 'Confirmed') state.creates++;
        const result = { ...saved, state: 'Confirmed' as const, tenantId, operationId,
          resumeUrl: `/organizations/${tenantId}/provisioning?key=stable-create-key&operationId=${operationId}` };
        state.drafts.set(id, result);
        return success(result);
      }
      return success(saved);
    }
    if (path === `/api/csp/organizations/${tenantId}`) return success({
      id: tenantId, displayName: 'Synthetic mission organization', lifecycle: 'Active', onboarding: 'Pending',
      systems: [], subscriptions: [], activity: [], setupState: 'Pending', memberCount: state.liveAdmin ? 1 : 0,
    });
    if (path.endsWith('/setup-summary')) {
      if (state.forbidSummary) return route.fulfill({ status: 403, json: { error: { code: 'FORBIDDEN', message: 'Provider authority is required.' } } });
      const data: OrganizationSetupSummary = {
        tenant: { id: tenantId, displayName: 'Synthetic mission organization', lifecycle: 'Active', onboardingState: 'Pending' },
        observedAt: '2026-09-30T13:00:00Z',
        liveAccess: { state: state.liveAdmin ? 'Available' : 'Missing', activeMemberCount: state.liveAdmin ? 1 : 0,
          administrators: { items: state.liveAdmin ? [{ personId, displayName: 'Recorded administrator',
            membershipId: 'membership-record', directoryTenantId: directoryId, objectId, assignmentId: 'scoped-role-record' }] : [],
          page: 1, pageSize: 25, total: state.liveAdmin ? 1 : 0 } },
        requestedOperation: state.operation, reconciliation: state.liveAdmin ? 'DifferentIdentity' : 'Unbound',
        actorActions: { canManageMemberships: true, canResumeEnrollment: !state.liveAdmin, canEnterOrganization: false },
      };
      return success(data);
    }
    if (path.includes(`/organizations/${tenantId}/provisioning`)) {
      if (request.method() === 'PATCH') {
        state.grants++;
        return route.fulfill({ status: 409, json: { error: { code: 'CONFLICT', message: 'No implicit enrollment allowed by this fixture.' } } });
      }
      return success(state.operation);
    }
    return route.fallback();
  });
  return state;
}

for (const width of [1440, 900, 390]) {
  test(`organization mock states and explicit server draft survive reload at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    const fixture = await organizationFixture(context, baseURL!);
    await page.setViewportSize({ width, height: 1000 });
    await page.goto('/workspaces/csp/organizations/new');
    // Act
    await page.getByLabel('Organization name', { exact: true }).fill('Synthetic mission organization');
    await page.getByLabel('Primary contact name (optional)').fill('Synthetic contact only');
    await capture(page, info, 'o-details');
    await page.getByRole('button', { name: 'Save & finish later' }).click();
    await expect(page).toHaveURL(/\/workspaces\/csp\/setup\/resume$/);
    expect(fixture.creates).toBe(0);
    expect(fixture.grants).toBe(0);
    const id = [...fixture.drafts.keys()][0]!;
    await page.goto(`/workspaces/csp/organizations/new?draft=${id}`);
    await page.reload();
    await expect(page.getByLabel('Organization name', { exact: true })).toHaveValue('Synthetic mission organization');
    await expect(page.getByLabel('Primary contact name (optional)')).toHaveValue('Synthetic contact only');
    await page.getByRole('button', { name: 'Continue', exact: true }).click();
    await capture(page, info, 'o-admin');
    await page.getByRole('button', { name: 'Directory lookup is unavailable', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Directory lookup is unavailable', level: 1 })).toBeVisible();
    await capture(page, info, 'directory-offline');
    await page.getByRole('button', { name: 'Complete enrollment later', exact: true }).click();
    await page.getByRole('button', { name: 'Review setup', exact: true }).click();
    await expect(page.getByText('Enrollment deferred', { exact: true })).toBeVisible();
    await capture(page, info, 'o-review');
    await page.getByRole('button', { name: 'Create organization', exact: true }).click();
    // Assert
    await expect(page.getByRole('heading', { name: 'Organization setup status', level: 1 })).toBeVisible();
    await expect(page.getByText('Administrator: Pending', { exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Choose authorized organization workspace' })).toHaveCount(0);
    await capture(page, info, 'o-ready');
    await page.reload();
    await expect(page.getByText('Administrator: Pending', { exact: true })).toBeVisible();
    expect(fixture.creates).toBe(1);
    expect(fixture.grants).toBe(0);
  });

  test(`organization repair shows current access without granting the different request at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    const fixture = await organizationFixture(context, baseURL!);
    fixture.liveAdmin = true;
    fixture.operation = { ...initial, initialAdministrator: { directoryTenantId: directoryId,
      objectId: '66666666-6666-6666-6666-666666666666', personId }, lastError: 'Historical response interrupted' };
    await page.setViewportSize({ width, height: 1000 });
    // Act
    await page.goto(`/workspaces/csp/organizations/${tenantId}/provisioning?key=stable-create-key&operationId=${operationId}`);
    // Assert
    await expect(page.getByRole('heading', { name: 'Review administrator setup status', level: 1 })).toBeVisible();
    await expect(page.getByText('Recorded administrator', { exact: true })).toBeVisible();
    await expect(page.getByText('Administrator: Pending', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Retry incomplete enrollment' })).toBeDisabled();
    await capture(page, info, 'o-repair');
    await page.getByRole('button', { name: 'Refresh setup status' }).click();
    expect(fixture.grants).toBe(0);
    expect(fixture.creates).toBe(0);
  });
}

test('uncertain draft save reconciles the same retained request without creation', async ({ page, context, baseURL }, info) => {
  // Arrange
  const fixture = await organizationFixture(context, baseURL!);
  fixture.loseSaveResponse = true;
  await page.goto('/workspaces/csp/organizations/new');
  await page.getByLabel('Organization name', { exact: true }).fill('Retained uncertain draft');
  // Act
  await page.getByRole('button', { name: 'Save & finish later' }).click();
  await expect(page.getByRole('button', { name: 'Check saved draft' })).toBeVisible();
  await capture(page, info, 'draft-uncertain');
  await page.getByRole('button', { name: 'Check saved draft' }).click();
  await expect(page.getByRole('button', { name: 'Check saved draft' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Save & finish later' }).click();
  // Assert
  await expect(page).toHaveURL(/\/setup\/resume$/);
  expect(fixture.drafts.size).toBe(1);
  expect([...fixture.drafts.values()][0]?.revision).toBe(2);
  expect(fixture.creates).toBe(0);
  expect(fixture.grants).toBe(0);
});

test('forbidden live-access summary cannot trigger enrollment or customer handoff', async ({ page, context, baseURL }, info) => {
  // Arrange
  const fixture = await organizationFixture(context, baseURL!);
  fixture.forbidSummary = true;
  await page.goto(`/workspaces/csp/organizations/${tenantId}/provisioning?key=stable-create-key`);
  // Assert
  await expect(page.getByText('Provider authority is required.', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Resume incomplete enrollment' })).toBeDisabled();
  await expect(page.getByRole('link', { name: 'Choose authorized organization workspace' })).toHaveCount(0);
  await capture(page, info, 'forbidden-access');
  expect(fixture.grants).toBe(0);
});

test('stale draft conflict requires adopting server fields before its revision can be reused', async ({ page, context, baseURL }, info) => {
  // Arrange
  const fixture = await organizationFixture(context, baseURL!);
  const id = '77777777-7777-7777-7777-777777777777';
  fixture.drafts.set(id, {
    draftId: id, revision: 1, schemaVersion: 1, state: 'Draft', currentStep: 'details',
    savedAt: '2026-09-30T13:00:00Z', displayName: 'Original organization', creationKey: 'stable-key',
    tenantId: null, operationId: null, resumeUrl: `/organizations/new?draft=${id}`,
    values: { organizationChoice: 'create', displayName: 'Original organization', administratorChoice: 'deferred' },
  });
  await page.goto(`/workspaces/csp/organizations/new?draft=${id}`);
  await page.getByLabel('Organization name', { exact: true }).fill('Stale local organization');
  fixture.conflictNextSave = true;
  // Act
  await page.getByRole('button', { name: 'Save & finish later' }).click();
  await page.getByRole('button', { name: 'Check saved draft' }).click();
  // Assert
  await expect(page.getByRole('button', { name: 'Use saved server version' })).toBeVisible();
  await expect(page.getByLabel('Organization name', { exact: true })).toHaveValue('Stale local organization');
  await page.getByRole('button', { name: 'Save & finish later' }).click();
  expect(fixture.drafts.get(id)?.revision).toBe(2);
  await capture(page, info, 'explicit-conflict-reconciliation');
  // Act
  await page.getByRole('button', { name: 'Use saved server version' }).click();
  await expect(page.getByLabel('Organization name', { exact: true })).toHaveValue('Other tab organization');
  await expect(page.getByLabel('Primary contact name (optional)')).toHaveValue('Other tab contact');
  await page.getByRole('button', { name: 'Save & finish later' }).click();
  // Assert
  await expect(page).toHaveURL(/\/setup\/resume$/);
  expect(fixture.drafts.get(id)?.revision).toBe(3);
  expect(fixture.drafts.get(id)?.values.displayName).toBe('Other tab organization');
  expect(fixture.creates).toBe(0);
});

for (const width of [1440, 390]) {
  test(`tenant applied values and unapplied draft remain distinct across navigation and reload at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    await installWorkspaceFixture(context, baseURL!);
    const org = { id: 'org-a', displayName: 'Synthetic organization', status: 'Active' };
    await context.route('**/api/auth/me', route => route.fulfill({ json: { status: 'success', data: {
      oid: 'synthetic-owner', directoryTenantId: 'synthetic-directory', displayName: 'Synthetic Administrator', persona: 'Administrator',
      homeTenant: org, effectiveTenant: org, isImpersonating: false, impersonation: null, isCspAdmin: false, isSocAnalyst: false, pimRoles: [],
      tenantMemberships: [org], availableWorkspaces: [{ kind: 'organization', tenantId: org.id, displayName: org.displayName, status: 'Active', onboardingState: 'Pending' }],
      availableWorkspacesTotal: 1, permissions: { canManageMemberships: true, canManageOrganization: true, canAccessCsp: false },
      workspace: { kind: 'organization', tenantId: org.id, displayName: org.displayName, mode: 'ordinary', personId,
        roles: ['Administrator'], permissions: { canManageMemberships: true, canManageOrganization: true, canAccessCsp: false } },
    } } }));
    let progress: TenantOnboardingProgress = {
      tenantId: org.id, currentStep: 'Tenant.LegalEntity', completedSteps: [], onboardingState: 'Pending',
      firstOrganizationId: 'subgroup-a', draftRevision: 0, draft: null,
      submittedValues: {
        legalEntity: { legalEntityName: 'Applied legal entity', doDComponent: 'NAVY', timeZone: 'America/New_York' },
        hqAddress: { hqAddressLine1: '10 Test Way', hqAddressLine2: '', hqCity: 'Norfolk', hqStateOrProvince: 'VA', hqPostalCode: '23501', hqCountry: 'USA' },
        classification: { defaultClassificationLevel: 'CUI' },
        ao: { authorizingOfficialName: 'Recorded AO', authorizingOfficialEmail: 'ao@example.invalid' },
        primaryPoc: { primaryPocName: 'Recorded contact', primaryPocEmail: 'poc@example.invalid', primaryPocPhone: '' },
        orgProfile: { name: 'Recorded subgroup', description: 'Retained description', firstOrganizationId: 'subgroup-a' },
      },
      missingRequiredFields: [],
    };
    let saves = 0;
    let applications = 0;
    await context.route('**/api/onboarding/tenant/**', route => {
      const path = new URL(route.request().url()).pathname;
      if (route.request().method() === 'PUT' && path.endsWith('/draft')) {
        const body = route.request().postDataJSON();
        expect(body.expectedRevision).toBe(progress.draftRevision);
        saves++;
        progress = { ...progress, draftRevision: progress.draftRevision! + 1,
          draft: { schemaVersion: 1, revision: progress.draftRevision! + 1, currentStep: body.currentStep,
            values: body.values, savedAt: '2026-09-30T13:00:00Z' } };
      } else if (route.request().method() !== 'GET') applications++;
      return route.fulfill({ json: { status: 'success', data: progress } });
    });
    await context.route('**/api/workspaces/organizations/org-a/systems/setup-access', route => route.fulfill({ json: {
      status: 'success', data: { canCreateSystem: false },
    } }));
    await context.route('**/api/workspaces/organizations/org-a/systems/setup-drafts?*', route => route.fulfill({ json: {
      status: 'success', data: { items: [], nextCursor: null },
    } }));
    await page.setViewportSize({ width, height: 1000 });
    await page.goto('/workspaces/organizations/org-a/onboarding/tenant');
    // Act
    await expect(page.getByLabel('Legal entity name', { exact: false })).toHaveValue('Applied legal entity');
    await page.getByLabel('Legal entity name', { exact: false }).fill('Unapplied saved draft');
    if (width === 1440) {
      await page.getByRole('button', { name: /Headquarters address/i }).click();
      await expect(page.getByLabel(/^City/)).toHaveValue('Norfolk');
      await page.getByRole('button', { name: /Default classification/i }).click();
      await expect(page.getByRole('radio', { name: /^CUI/ })).toBeChecked();
      await page.getByRole('button', { name: /Legal entity/i }).click();
    }
    await expect(page.getByLabel('Legal entity name', { exact: false })).toHaveValue('Unapplied saved draft');
    await capture(page, info, 'tenant-hydrated-dirty');
    await page.getByRole('button', { name: 'Save & finish later' }).click();
    await expect(page).toHaveURL(/\/setup\/resume$/);
    await page.goto('/workspaces/organizations/org-a/onboarding/tenant');
    await page.reload();
    // Assert
    await expect(page.getByLabel('Legal entity name', { exact: false })).toHaveValue('Unapplied saved draft');
    expect(progress.submittedValues!.legalEntity.legalEntityName).toBe('Applied legal entity');
    expect(progress.onboardingState).toBe('Pending');
    expect(saves).toBe(1);
    expect(applications).toBe(0);
    await capture(page, info, 'tenant-restored-draft');
  });
}
