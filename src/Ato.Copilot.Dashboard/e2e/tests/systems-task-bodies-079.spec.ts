import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { systemDetail } from '../../src/__tests__/fixtures/assessmentEnvironment';

const root = '/workspaces/organizations/org-a/systems/system-a';
const categorization = {
  confidentiality: 'Low', integrity: 'Low', availability: 'Low', overall: 'Low',
  formalNotation: 'SC = {(confidentiality, Low), (integrity, Low), (availability, Low)}',
  dodImpactLevel: 'IL2', isNationalSecuritySystem: false,
  informationTypes: [{ name: 'Mission support information', confidentiality: 'Low', integrity: 'Low', availability: 'Low' }],
};

for (const width of [1440, 390]) {
  test(`record-based Systems tasks at ${width}px`, async ({ context, page, baseURL }, info) => {
    test.setTimeout(90000);
    // Arrange: fixture responses exercise the actual SPA, not production persistence.
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!);
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    await context.route('**/api/dashboard/systems/system-a', route => route.fulfill({ json: {
      ...systemDetail, systemId: 'system-a', name: 'Synthetic Mission System', currentRmfPhase: 'Prepare', categorization,
    } }));
    await context.route('**/api/v1/systems/system-a/packages/validate?purpose=InitialSubmission', route => route.fulfill({ json: {
      purpose: 'InitialSubmission', isValid: false, errorCount: 1, warningCount: 0, validatedAt: '2026-09-26T12:00:00Z',
      findings: [{ severity: 'error', category: 'boundary', artifactType: null, description: 'Review the mission boundary.', remediation: 'Confirm included resources.' }],
    } }));
    await context.route('**/api/dashboard/systems/system-a/next-actions', route => route.fulfill({ json: {
      systemId: 'system-a', checkedAt: '2026-09-28T18:00:00Z', effectiveRoles: ['MissionOwner'],
      items: [{ id: 'submit-data', title: 'Submit data profile for review.', description: 'Submit your saved information types.',
        path: 'profile/DataTypes', actionLabel: 'Open', responsibleRole: 'MissionOwner' }], waitingOnOtherRoles: [],
    } }));
    await context.route('**/api/roles/effective', route => route.fulfill({ json: { status: 'success', data: { effectiveRole: null, isTenantAdministrator: false } } }));
    await context.route('**/api/roles/system/system-a', route => route.fulfill({ json: { status: 'success', data: {
      systemId: 'system-a', roles: ['AuthorizingOfficial', 'Issm', 'Isso', 'Sca', 'SystemOwner', 'MissionOwner', 'Administrator'].map(role => ({
        role, person: role === 'Issm' ? { id: 'reviewer-a', displayName: 'Reviewer A' } : null,
        source: role === 'Issm' ? 'org-fallback' : 'not-assigned',
      })),
    } } }));
    await context.route('**/api/dashboard/systems/system-a/profile/*', async route => {
      const sectionType = new URL(route.request().url()).pathname.split('/').at(-1);
      if (sectionType === 'completeness') return route.fallback();
      const value = {
        id: 'section-a', sectionType, governanceStatus: 'Draft', canEditProfile: true,
        draftContent: JSON.stringify({ missionStatement: 'Coordinate mission support.', businessPurpose: 'Maintain operational records.',
          accessOverview: 'Mission staff use approved access.', dataOverview: 'Operational records.', ppsOverview: 'Approved application interfaces.' }),
        userCategories: [{ id: 'users-a', categoryName: 'Application Users', description: 'Mission staff', approximateCount: 12, accessMethod: 'CAC/PIV', dataSensitivityLevel: 'CUI', sortOrder: 0 }],
        dataTypeEntries: [{ id: 'data-a', dataTypeName: 'Operational Data', description: 'Mission records', sensitivityClassification: 'CUI', source: 'Internal System', destination: 'Internal Database', applicableRegulations: 'FISMA', sortOrder: 0 }],
        ppsEntries: [{ id: 'port-a', portOrRange: '443 (HTTPS)', protocol: 'TCP', serviceName: 'Web Application', direction: 'Inbound', justification: 'Approved user access', sortOrder: 0 }],
        leveragedAuthorizations: [],
      };
      if (route.request().method() === 'PUT') value.draftContent = route.request().postDataJSON().content;
      return route.fulfill({ json: value });
    });
    await context.route('**/api/dashboard/systems/system-a/boundary-definitions', route => route.fulfill({ json: { items: [{
      id: 'boundary-a', name: 'Production boundary', description: 'Mission application and storage.', boundaryType: 'Logical',
      registeredSystemId: 'system-a', isPrimary: true, componentCount: 2, resourceCount: 2, coveragePercent: 50, createdAt: '2026-09-01T00:00:00Z',
    }] } }));
    const boundaryComponents = ['Mission application', 'Mission storage'].map((name, index) => ({
      assignmentId: `assignment-${index}`, componentId: `component-${index}`, componentName: name, componentType: 'Thing',
      source: 'System', isInScope: true, exclusionRationale: null, inheritanceProvider: null, subType: 'Service',
      azureResourceId: null, azureResourceType: null, azureResourceGroup: null, azureLocation: null,
      createdAt: '2026-09-01T00:00:00Z', createdBy: 'Reviewer A',
    }));
    await context.route('**/api/dashboard/systems/system-a/boundary-definitions/boundary-a/components{,?*}', route => route.fulfill({
      json: { items: boundaryComponents, totalCount: 2, page: 1, pageSize: Number(new URL(route.request().url()).searchParams.get('pageSize') ?? 25) },
    }));
    await context.route('**/api/dashboard/boundary-definitions/boundary-a/components', route => route.fulfill({ json: {
      items: boundaryComponents.map(item => ({ id: item.componentId, name: item.componentName, componentType: item.componentType,
        status: 'Active', createdAt: item.createdAt, capabilityLinks: [], systemAssignments: [] })), totalCount: 2,
    } }));
    await context.route('**/api/dashboard/boundary-definitions/boundary-a/resources', route => route.fulfill({ json: { items: [], totalCount: 0 } }));
    await context.route('**/api/dashboard/systems/system-a/boundary-definitions/boundary-a/lock', route => route.fulfill({ json: {
      locked: false, lockedBy: null, lockedAt: null, expiresAt: null,
    } }));
    await context.route('**/api/dashboard/systems/system-a/baseline', route => route.fulfill({ json: {
      baselineId: 'baseline-a', baselineLevel: 'Low', totalControls: 1, inheritedControls: 0, sharedControls: 0, customerControls: 1,
      overlayApplied: null, tailoredInControls: 0, tailoredOutControls: 0, familyBreakdown: [{ family: 'AC', count: 1 }],
      tailorings: [], controlIds: ['AC-1'], createdAt: '2026-09-01T00:00:00Z', createdBy: 'Reviewer A', modifiedAt: null,
    } }));
    const policy = { id: 'policy-a', name: 'Access management policy', componentType: 'Policy', status: 'Active',
      scopeLevel: 'System', linkedCapabilities: [], description: 'Recorded account access policy.', subType: 'Organization policy' };
    await context.route('**/api/dashboard/components?*', route => route.fulfill({ json: { items: [policy], totalCount: 1, page: 1, pageSize: 200 } }));
    await context.route('**/api/dashboard/systems/system-a/components?*', route => route.fulfill({ json: { systemId: 'system-a', items: [policy], totalCount: 1 } }));
    await context.route('**/api/dashboard/systems/system-a/evidence?*', route => route.fulfill({ json: {
      items: [{ id: 'evidence-a', source: 'Manual', fileName: 'Access review.pdf', contentType: 'application/pdf', fileSizeBytes: 2048,
        artifactCategory: 'PolicyDocument', narrativeType: 'Policy', controlId: 'AC-2', controlImplementationId: 'implementation-a',
        securityCapabilityId: null, description: 'Recorded access review.', uploadedBy: 'Reviewer A',
        uploadedAt: '2026-09-26T12:00:00Z', contentHash: 'fixture-hash' }],
      totalCount: 1, page: 1, pageSize: 50,
    } }));
    await context.route('**/api/dashboard/systems/system-a/evidence/summary', route => route.fulfill({ json: {
      totalCount: 1, manualCount: 1, automatedCount: 0, controlsWithEvidence: 1, totalControls: 3, coveragePercentage: 33.3,
    } }));
    await context.route('**/api/dashboard/systems/system-a/history?*', route => route.fulfill({ json: {
      systemId: 'system-a', source: 'DashboardActivity', page: 1, pageSize: 25, totalCount: 1,
      items: [{ id: 'event-a', eventType: 'ProfileReviewed', timestamp: '2026-09-26T12:00:00Z', actor: 'Reviewer A',
        summary: 'Mission profile reviewed', relatedEntityType: 'SystemProfile', relatedEntityId: 'section-a' }],
    } }));
    await context.route('**/api/dashboard/systems/system-a/documents', route => route.fulfill({ json: {
      systemId: 'system-a', interconnections: [{
        interconnectionId: 'connection-a', targetSystem: 'Partner system', direction: 'Outbound', status: 'Proposed',
        hasAgreement: false, agreementType: null, agreementStatus: null,
      }],
    } }));
    await context.route('**/api/dashboard/systems/system-a/interconnections?*', route => route.fulfill({ json: {
      items: [{
        id: 'connection-a', interconnectionId: 'connection-a', systemId: 'system-a',
        targetSystemName: 'Partner system', targetSystemOwner: '', targetSystemAcronym: '',
        interconnectionType: 'Api', dataFlowDirection: 'Outbound', dataClassification: 'CUI',
        dataDescription: '', protocolsUsed: ['HTTPS'], portsUsed: ['443'], securityMeasures: [], authenticationMethod: '',
        status: 'Proposed', statusReason: null, authorizationToConnect: false, hasAgreement: false, agreements: [],
        createdBy: 'fixture-user', createdAt: '2026-09-28T17:00:00Z', modifiedAt: null, canManageInterconnections: false,
      }], total: 1, page: 1, pageSize: 50, canManageInterconnections: false,
    } }));
    await context.route('**/api/systems/system-a/narrative-library**', route => {
      const path = new URL(route.request().url()).pathname;
      if (path.endsWith('/access')) return route.fulfill({ json: { tenantId: 'org-a', systemName: 'Synthetic Mission System', canAuthor: false, canPublishShared: false, canGenerate: false, capabilities: [] } });
      if (path.endsWith('/proposals')) return route.fulfill({ json: [{
        id: 'proposal-a', controlId: 'AC-2', narrativeType: 'Technical', baseVersion: 2, beforeContent: 'The owner approves accounts.',
        proposedContent: 'The owner approves accounts and retains quarterly review evidence.', stateHash: 'fixture-state', provenance: {},
        conflicts: [], missingEvidence: [], status: 'Draft', revision: 1, createdAt: '2026-09-26T12:00:00Z', createdBy: 'Reviewer A',
        reviewedAt: null, reviewedBy: null, reviewNote: null, acceptedVersion: null, isStale: false, canReview: false,
      }] });
      if (path.endsWith('/impact-receipts')) return route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 50 } });
      return route.fulfill({ json: [] });
    });

    const routes = [
      ['', 'A clear path to your ATO package'],
      ['profile/MissionAndPurpose', 'Mission & purpose'],
      ['profile/UsersAndAccess', 'Users & access'],
      ['profile/DataTypes', 'Data types & sensitivity'],
      ['profile/PortsProtocolsAndServices', 'Ports & interconnections'],
      ['boundaries', 'Inventory & system boundary'],
      ['baseline', 'Categorization & control baseline'],
      ['legal', 'Applicable policies & references'],
      ['evidence', 'Evidence for control assessment'],
      ['roles', 'Team & permissions'],
      ['history', 'Activity & decision history'],
      ['narratives', 'Control implementation narratives'],
    ];
    for (const [path, title] of routes) {
      // Act
      await page.goto(`${root}${path ? `/${path}` : ''}`);
      // Assert
      await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();
      if (path === 'profile/PortsProtocolsAndServices') await expect(page.getByRole('button', { name: 'Add connection', exact: true })).toBeVisible();
      else if (path.startsWith('profile/')) await expect(page.getByRole('button', { name: 'Save Draft', exact: true })).toBeVisible();
      if (path === 'profile/MissionAndPurpose') {
        await page.getByLabel('Mission Statement', { exact: false }).fill('Updated mission support purpose.');
        const saved = page.waitForRequest(request => request.method() === 'PUT'
          && request.url().endsWith('/api/dashboard/systems/system-a/profile/MissionAndPurpose'));
        await page.getByRole('button', { name: 'Save Draft', exact: true }).click();
        expect(JSON.parse((await saved).postDataJSON().content).missionStatement).toBe('Updated mission support purpose.');
        await expect(page.getByText('Section saved as Draft.', { exact: true })).toBeVisible();
      }
      if (path === 'profile/PortsProtocolsAndServices') await expect(page.getByText('Partner system', { exact: true })).toBeVisible();
      if (path === '') {
        await page.screenshot({ path: info.outputPath(`readiness-initial-${width}.png`), fullPage: true });
        await expect(page.getByText('Initial submission · Not checked', { exact: true })).toBeVisible();
        await page.getByRole('button', { name: 'Check readiness', exact: true }).click();
        await expect(page.getByText('Initial submission · 1 blocking requirement remains', { exact: true })).toBeVisible();
        await expect(page.getByRole('heading', { name: 'Submit data profile for review.', exact: true })).toBeVisible();
      }
      if (path === 'narratives') await expect(page.getByRole('heading', { name: 'Previous v2' })).toBeVisible();
      if (path === 'evidence') await expect(page.getByRole('button', { name: 'Review evidence Access review.pdf' })).toBeVisible();
      if (path === 'boundaries') {
        await expect(page.getByRole('cell', { name: 'Production boundary', exact: true })).toBeVisible();
        await page.getByRole('button', { name: 'Review boundary', exact: true }).click();
        await expect(page.getByRole('dialog', { name: 'Production boundary — Details' })).toBeVisible();
        await expect(page.getByRole('region', { name: 'Placement Mission application', exact: true })).toBeVisible();
        await page.keyboard.press('Escape');
        await expect(page.getByRole('button', { name: 'Review boundary', exact: true })).toBeFocused();
      }
      if (path === 'history') {
        await page.getByRole('button', { name: 'View record: Mission profile reviewed' }).click();
        await expect(page.getByRole('dialog', { name: 'Retained activity record' })).toBeVisible();
        await page.keyboard.press('Escape');
      }
      const geometry = await page.evaluate(() => ({
        viewport: innerWidth, width: document.documentElement.scrollWidth,
        outside: [...document.querySelectorAll('body *')].filter(element => element.getBoundingClientRect().right > innerWidth)
          .slice(0, 15).map(element => ({ tag: element.tagName, className: element.className,
            right: element.getBoundingClientRect().right, overflow: getComputedStyle(element).overflowX })),
      }));
      expect(geometry.width, `${path}: ${JSON.stringify(geometry)}`).toBeLessThanOrEqual(geometry.viewport);
      await expect(page.locator('main')).toHaveCount(1);
      await page.screenshot({ path: info.outputPath(`${path?.replaceAll('/', '-') || 'readiness'}-${width}.png`), fullPage: true });
    }
    expect(errors).toEqual([]);
  });
}
