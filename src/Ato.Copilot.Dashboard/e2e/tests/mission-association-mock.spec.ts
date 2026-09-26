import { expect, test, type BrowserContext } from '@playwright/test';
import axe from 'axe-core';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { allocation, allocationResponse, capability, adoption, relationship } from '../../src/__tests__/provider-relationships/fixtures';

async function installEnvironment(context: BrowserContext) {
  let draftContent = JSON.stringify({ hostingModel: 'CSP-hosted', additionalDetails: 'Mission application in an allocated environment.',
    networkZones: '["DMZ"]', rtoRpo: 'RTO < 1hr / RPO < 15min', retainedKey: 'historical value' });
  const saved: Record<string, string>[] = [];
  await context.route('**/api/dashboard/systems/system-a/profile/EnvironmentAndDeployment', route => {
    if (route.request().method() === 'PUT') {
      draftContent = route.request().postDataJSON().content;
      saved.push(JSON.parse(draftContent));
    }
    return route.fulfill({ json: { id: 'environment-a', sectionType: 'EnvironmentAndDeployment', canEditProfile: true,
      governanceStatus: 'Draft', draftContent, userCategories: [], dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [] } });
  });
  await context.route('**/api/dashboard/systems/system-a/provider-relationships?*', route => route.fulfill({ json: {
    status: 'success', data: { items: [{ ...allocationResponse, relationshipId: 'relationship-a' }], page: 1, pageSize: 25, total: 1 },
  } }));
  await context.route('**/api/workspaces/organizations/org-a/systems/system-a/security-capabilities?*', route => route.fulfill({ json: { data: {
    items: [], page: 1, pageSize: 10, total: 0, scope: 'applied', grouping: 'capability', boundaries: [],
    permissions: { canRead: true, canManage: true, canReviewResponsibilities: false, canManageEvidence: false,
      canAuthorNarratives: false, canReviewNarratives: false },
  } } }));
  return saved;
}

test('Environment exposes hosting while old association bookmarks remain compatible', async ({ page, context, baseURL }) => {
  // Arrange
  const requests = await installWorkspaceFixture(context, baseURL!);
  await installEnvironment(context);
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto('/workspaces/organizations/org-a/systems/system-a/provider-relationships/setup');
  // Act
  await expect(page.getByRole('link', { name: 'Provider relationships', exact: true })).toHaveCount(0);
  await page.getByRole('link', { name: 'Environment', exact: true }).click();
  await expect(page.getByRole('combobox', { name: 'Hosting model' })).toBeVisible();
  const hosting = page.getByRole('region', { name: 'Associated hosting & security capabilities' });
  await expect(hosting.getByRole('link', { name: 'Associate hosting & capabilities' })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 1000 });
  await hosting.getByRole('link', { name: 'Associate hosting & capabilities' }).click();
  await expect(page).toHaveURL('/workspaces/organizations/org-a/systems/system-a/profile/EnvironmentAndDeployment/hosting');
  await expect(page.getByRole('heading', { name: 'Choose provider', exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Choose a different system' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Back to Environment' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Active workspace' })).toContainText('Synthetic Mission System');
  await page.goto('/workspaces/organizations/org-a/systems/system-a/provider-relationships');
  // Assert
  await expect(page).toHaveURL('/workspaces/organizations/org-a/systems/system-a/provider-relationships');
  const entry = page.getByRole('link', { name: 'Start guided association' });
  await expect(entry).toHaveAttribute('href', '/workspaces/organizations/org-a/systems/system-a/provider-relationships/setup');
  await expect(entry).toBeVisible();
  await page.setViewportSize({ width: 390, height: 1000 });
  await expect(entry).toBeVisible();
  // Act
  await entry.click();
  // Assert
  await expect(page).toHaveURL('/workspaces/organizations/org-a/systems/system-a/provider-relationships/setup');
  await expect(page.getByRole('button', { name: 'Choose hosting scope' })).toBeEnabled();
  expect(requests.filter(item => item.method !== 'GET')).toEqual([]);
});

for (const width of [1440, 390]) {
  test(`Environment leads with its form, preserves advanced data and separates assessments at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const requests = await installWorkspaceFixture(context, baseURL!);
    const saved = await installEnvironment(context);
    await context.route('**/api/dashboard/systems/system-a/assessment-environment', route => route.fulfill({ status: 403, body: '' }));
    await context.route('**/api/dashboard/systems/system-a/assessment-readiness', route => route.fulfill({ status: 403, body: '' }));
    // Act
    await page.goto('/workspaces/organizations/org-a/systems/system-a/profile/EnvironmentAndDeployment');
    if (width === 390) await page.evaluate(() => document.documentElement.classList.add('dark'));
    await expect(page.getByRole('combobox', { name: 'Hosting model' })).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Environment description' })).toBeVisible();
    await expect(page.getByLabel('Availability Tier')).not.toBeVisible();
    await expect(page.getByText('Profile Completeness', { exact: true })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Collapse panel', exact: true })).toHaveCount(0);
    expect(requests.filter(item => /assessment-environment|assessment-readiness/.test(item.path))).toEqual([]);
    await page.getByRole('button', { name: 'Use these hosting details' }).click();
    const dialog = page.getByRole('dialog', { name: 'Review hosting details' });
    // Assert
    await expect(dialog.getByRole('button', { name: 'Use in draft' })).toBeDisabled();
    expect(saved).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.addScriptTag({ content: axe.source });
    expect(await page.evaluate(async () => {
      const browserAxe = (window as unknown as { axe: typeof import('axe-core') }).axe;
      return (await browserAxe.run('dialog')).violations;
    })).toEqual([]);
    // Act
    await dialog.getByRole('checkbox').check();
    await dialog.getByRole('button', { name: 'Use in draft' }).click();
    await expect(page.getByRole('textbox', { name: 'Environment description' })).toHaveValue(/Harbor hosting/);
    expect(saved).toEqual([]);
    await page.getByRole('button', { name: 'Save Draft' }).click();
    // Assert
    await expect.poll(() => saved.length).toBe(1);
    expect(saved[0]).toMatchObject({ networkZones: '["DMZ"]', rtoRpo: 'RTO < 1hr / RPO < 15min', retainedKey: 'historical value' });
    expect(requests.filter(item => /applicable-provider-capabilities|provider-capability-adoptions/.test(item.path))).toEqual([]);
    await page.getByRole('link', { name: 'Assessments: configure Azure assessment' }).click();
    await expect(page.getByRole('heading', { name: 'Azure assessment configuration', exact: true })).toBeVisible();
    await expect(page.getByText('You do not have permission to configure or run Azure assessments for this system.')).toBeVisible();
    await expect(page.getByRole('button', { name: /Retry/ })).toHaveCount(0);
    await page.goto('/workspaces/organizations/org-a/systems/system-a/profile/EnvironmentAndDeployment#azure-assessment-environment');
    await expect(page).toHaveURL(/\/assessments\/environment#azure-assessment-environment$/);
  });

  test(`Mission Owner confirms existing hosting and retries partial adoption at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const requests = await installWorkspaceFixture(context, baseURL!);
    await context.route('**/api/dashboard/systems?*', route => route.fulfill({ json: {
      items: [{ systemId: 'system-a', name: 'Vanguard', acronym: 'VAN' }], nextCursor: null, totalCount: 1,
    } }));
    await context.route('**/api/dashboard/systems/system-a', route => route.fulfill({ json: {
      systemId: 'system-a', name: 'Vanguard', acronym: 'VAN', currentRmfStep: 'Prepare', rmfPhase: 'Prepare',
      activeAssessments: [], roleAssignments: [], boundaryResources: [],
    } }));
    await context.route('**/api/dashboard/systems/system-a/provider-relationships?*', route => route.fulfill({ json: {
      status: 'success', data: { items: [allocationResponse], page: 1, pageSize: 25, total: 1 },
    } }));
    let associated = false;
    const capabilityReads: URL[] = [];
    await context.route('**/api/dashboard/systems/system-a/applicable-provider-capabilities?*', route => {
      capabilityReads.push(new URL(route.request().url()));
      return route.fulfill({ json: {
        status: 'success', data: { items: [{ ...capability, canConfirmResponsibilities: true,
          canProposeAdoption: associated,
          applicabilityPreviewHash: associated ? 'after-association-preview' : capability.applicabilityPreviewHash,
          outstandingDecisions: associated ? capability.outstandingDecisions
            : [...capability.outstandingDecisions, 'MissionAssociationRequired'],
        }], page: 1, pageSize: 25, total: 1 },
      } });
    });
    const writes: { path: string; key: string; body: unknown }[] = [];
    await context.route('**/api/dashboard/systems/system-a/provider-relationships', route => {
      writes.push({ path: 'relationship', key: route.request().headers()['idempotency-key']!, body: route.request().postDataJSON() });
      associated = true;
      return route.fulfill({ json: { status: 'success', data: relationship } });
    });
    await context.route('**/api/dashboard/systems/system-a/provider-capability-adoptions', route => {
      writes.push({ path: 'adoption', key: route.request().headers()['idempotency-key']!, body: route.request().postDataJSON() });
      return writes.filter(item => item.path === 'adoption').length === 1
        ? route.fulfill({ status: 503, json: { status: 'error', error: { code: 'SERVICE_UNAVAILABLE', message: 'Adoption service unavailable.' } } })
        : route.fulfill({ json: { status: 'success', data: adoption } });
    });

    // Act
    if (width === 1440) {
      await page.goto('/workspaces/organizations/org-a/systems/system-a/profile/EnvironmentAndDeployment/hosting');
      await page.getByRole('radio', { name: 'Harbor provider' }).check();
      await page.getByRole('button', { name: 'Choose hosting scope' }).click();
    } else {
      await page.goto('/workspaces/organizations/org-a/provider-relationships/setup');
      await page.getByRole('combobox', { name: 'System', exact: true }).selectOption('system-a');
      await page.getByRole('button', { name: 'Choose hosting scope' }).click();
      await expect(page).toHaveURL(/\/workspaces\/organizations\/org-a\/systems\/system-a\/provider-relationships\/setup$/);
    }
    await page.getByRole('radio', { name: /Harbor hosting/ }).check();
    await page.getByRole('button', { name: 'Choose capabilities' }).click();
    await page.getByRole('checkbox', { name: /Security monitoring/ }).check();
    await page.getByRole('button', { name: 'Review responsibilities' }).click();

    // Assert
    await expect(page.getByText('Customer: investigate alerts')).toBeVisible();
    await expect(page.getByText(/Harbor SSP/)).toBeVisible();
    expect(writes).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.addScriptTag({ content: axe.source });
    const violations = await page.evaluate(async () => {
      const browserAxe = (window as unknown as { axe: typeof import('axe-core') }).axe;
      return (await browserAxe.run('[aria-label="Mission association task"]')).violations;
    });
    expect(violations).toEqual([]);

    // Act
    await page.getByRole('button', { name: 'Continue to confirmation' }).click();
    await expect(page.getByRole('button', { name: 'Associate this allocation' })).toBeDisabled();
    await page.getByRole('checkbox', { name: /I confirm these associations/ }).check();
    await page.getByRole('button', { name: 'Associate this allocation' }).click();
    await expect(page.getByRole('heading', { name: 'Review refreshed capability subscriptions' })).toBeVisible();
    expect(writes.map(item => item.path)).toEqual(['relationship']);
    await expect(page.getByText('Customer: investigate alerts')).toBeVisible();
    const refreshedViolations = await page.evaluate(async () => {
      const browserAxe = (window as unknown as { axe: typeof import('axe-core') }).axe;
      return (await browserAxe.run('[aria-label="Mission association task"]')).violations;
    });
    expect(refreshedViolations).toEqual([]);
    await expect(page.getByRole('button', { name: 'Confirm subscriptions' })).toBeDisabled();
    await page.getByRole('checkbox', { name: /I confirm these associations/ }).check();
    await page.getByRole('button', { name: 'Confirm subscriptions' }).click();

    // Assert
    await expect(page.getByRole('alert')).toContainText('Adoption service unavailable.');
    await expect(page.getByRole('button', { name: 'Review current choices' })).toHaveCount(0);
    await expect(page.getByText('Hosting relationship recorded.')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Associations recorded' })).toHaveCount(0);

    // Act
    await page.getByRole('button', { name: 'Retry incomplete operations' }).click();

    // Assert
    await expect(page.getByRole('heading', { name: 'Associations recorded' })).toBeVisible();
    expect(writes.map(item => item.path)).toEqual(['relationship', 'adoption', 'adoption']);
    expect(writes[1]!.key).toBe(writes[2]!.key);
    expect(writes[1]!.body).toEqual(writes[2]!.body);
    expect(writes[1]!.body).toEqual(expect.objectContaining({
      contextSnapshotHash: capability.applicability.snapshotHash, applicabilityPreviewHash: 'after-association-preview',
    }));
    expect(capabilityReads).toHaveLength(2);
    expect(capabilityReads[1]!.searchParams.get('capabilityId')).toBe(capability.capabilityId);
    expect(capabilityReads[1]!.searchParams.get('releaseId')).toBe(capability.releaseId);
    expect(writes[0]!.body).toEqual({ assignmentId: allocation.assignmentId, expectedAssignmentRevision: allocation.revision });
    if (width === 1440) {
      await expect(page.getByRole('link', { name: 'Confirm responsibilities: Security monitoring' })).toHaveAttribute(
        'href', '/workspaces/organizations/org-a/systems/system-a/security-capabilities/provider/capability-a?tab=coverage');
    } else {
      await expect(page.getByRole('link', { name: 'Review subscription responsibilities' })).toHaveAttribute(
        'href', '/workspaces/organizations/org-a/systems/system-a/inheritance/subscriptions');
    }
    expect(requests.filter(item => item.path.endsWith('/workspace-access'))).toEqual([
      expect.objectContaining({ tenant: 'org-a', kind: 'organization', method: 'GET' }),
    ]);
    expect(requests.filter(item => item.method !== 'GET')).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}

for (const rejectStale of [false, true]) {
test(`existing mission relationship skips association and handles stale=${rejectStale}`, async ({ page, context, baseURL }) => {
  // Arrange
  const requests = await installWorkspaceFixture(context, baseURL!);
  await context.route('**/api/dashboard/systems/system-a/provider-relationships?*', route => route.fulfill({ json: {
    status: 'success', data: { items: [{ ...allocationResponse, relationshipId: 'existing-relationship', canAssociate: false }],
      page: 1, pageSize: 25, total: 1 },
  } }));
  let subscriptions = 0;
  const keys: string[] = [];
  await context.route('**/api/dashboard/systems/system-a/applicable-provider-capabilities?*', route => route.fulfill({ json: {
    status: 'success', data: { items: [{ ...capability,
      applicabilityPreviewHash: rejectStale && subscriptions > 0 ? 'changed-preview' : capability.applicabilityPreviewHash,
    }], page: 1, pageSize: 25, total: 1 },
  } }));
  await context.route('**/api/dashboard/systems/system-a/provider-capability-adoptions', route => {
    subscriptions += 1;
    keys.push(route.request().headers()['idempotency-key']!);
    if (rejectStale && subscriptions === 1) {
      return route.fulfill({ status: 409, json: {
        status: 'error', error: { errorCode: 'AUTHORIZATION_CONTEXT_STALE', message: 'Exact capability changed.' },
      } });
    }
    return route.fulfill({ json: { status: 'success', data: adoption } });
  });
  // Act
  await page.goto('/workspaces/organizations/org-a/systems/system-a/provider-relationships/setup');
  await page.getByRole('button', { name: 'Choose hosting scope' }).click();
  await page.getByRole('radio', { name: /Harbor hosting/ }).check();
  await page.getByRole('button', { name: 'Choose capabilities' }).click();
  await page.getByRole('checkbox', { name: /Security monitoring/ }).check();
  await page.getByRole('button', { name: 'Review responsibilities' }).click();
  await page.getByRole('button', { name: 'Continue to confirmation' }).click();
  await page.getByRole('checkbox', { name: /I confirm these associations/ }).check();
  await page.getByRole('button', { name: 'Confirm subscriptions' }).click();
  if (rejectStale) {
    await expect(page.getByRole('alert')).toContainText('Exact capability changed.');
    await page.getByRole('button', { name: 'Review current choices' }).click();
    await page.getByRole('checkbox', { name: /Security monitoring/ }).uncheck();
    await page.getByRole('checkbox', { name: /Security monitoring/ }).check();
    await page.getByRole('button', { name: 'Review responsibilities' }).click();
    await page.getByRole('button', { name: 'Continue to confirmation' }).click();
    await expect(page.getByRole('button', { name: 'Confirm subscriptions' })).toBeDisabled();
    expect(subscriptions).toBe(1);
    await page.getByRole('checkbox', { name: /I confirm these associations/ }).check();
    await page.getByRole('button', { name: 'Confirm subscriptions' }).click();
  }
  // Assert
  await expect(page.getByRole('heading', { name: 'Associations recorded' })).toBeVisible();
  await expect(page.getByText('Existing hosting relationship retained.')).toBeVisible();
  expect(subscriptions).toBe(rejectStale ? 2 : 1);
  if (rejectStale) expect(keys[1]).not.toBe(keys[0]);
  expect(requests.filter(item => item.method !== 'GET')).toEqual([]);
});
}

test('Mission Owner can explicitly save hosting only when subscription permission is unavailable', async ({ page, context, baseURL }) => {
  // Arrange
  const requests = await installWorkspaceFixture(context, baseURL!);
  await context.route('**/api/dashboard/systems/system-a/provider-relationships?*', route => route.fulfill({ json: {
    status: 'success', data: { items: [allocationResponse], page: 1, pageSize: 25, total: 1 },
  } }));
  await context.route('**/api/dashboard/systems/system-a/applicable-provider-capabilities?*', route => route.fulfill({ json: {
    status: 'success', data: { items: [{ ...capability, canProposeAdoption: false, canConfirmResponsibilities: false }],
      page: 1, pageSize: 25, total: 1 },
  } }));
  let associations = 0;
  await context.route('**/api/dashboard/systems/system-a/provider-relationships', route => {
    associations += 1;
    return route.fulfill({ json: { status: 'success', data: relationship } });
  });
  // Act
  await page.goto('/workspaces/organizations/org-a/systems/system-a/provider-relationships/setup');
  await page.getByRole('button', { name: 'Choose hosting scope' }).click();
  await page.getByRole('radio', { name: /Harbor hosting/ }).check();
  await page.getByRole('button', { name: 'Choose capabilities' }).click();
  await expect(page.getByRole('checkbox', { name: /Security monitoring/ })).toBeDisabled();
  await page.getByRole('button', { name: 'Continue with hosting association only' }).click();
  await page.getByRole('button', { name: 'Continue to confirmation' }).click();
  expect(associations).toBe(0);
  await page.getByRole('checkbox', { name: /I confirm these associations/ }).check();
  await page.getByRole('button', { name: 'Associate this allocation' }).click();
  // Assert
  await expect(page.getByRole('heading', { name: 'Hosting association recorded' })).toBeVisible();
  await expect(page.getByText(/No capability subscription or pending request was created/)).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Published capabilities and duties — read-only' })).toBeVisible();
  await expect(page.getByText('Customer: investigate alerts')).toBeVisible();
  expect(associations).toBe(1);
  expect(requests.filter(item => item.method !== 'GET')).toEqual([]);
});

test('Mission Owner sees a retryable unavailable read, not an empty hosting list', async ({ page, context, baseURL }) => {
  // Arrange
  await installWorkspaceFixture(context, baseURL!);
  await context.route('**/api/dashboard/systems/system-a/provider-relationships?*', route =>
    route.fulfill({ status: 503, json: { status: 'error', error: { code: 'HOSTING_UNAVAILABLE', message: 'Hosting read unavailable.' } } }));
  // Act
  await page.goto('/workspaces/organizations/org-a/systems/system-a/provider-relationships/setup');
  await page.getByRole('button', { name: 'Choose hosting scope' }).click();
  // Assert
  await expect(page.getByRole('alert')).toContainText('Hosting read unavailable.');
  await expect(page.getByRole('button', { name: 'Retry hosting scopes' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Choose capabilities' })).toBeDisabled();
  await expect(page.getByText(/No existing hosting allocations/)).toHaveCount(0);
});
