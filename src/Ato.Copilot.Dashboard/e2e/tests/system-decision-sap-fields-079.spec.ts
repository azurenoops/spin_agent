import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
const root = '/workspaces/organizations/org-a/systems/system-a';
const hash = 'a'.repeat(64);
for (const width of [1440, 390]) {
  test(`external decision recording and manual SAP fields at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: production components, synthetic service records; backend persistence is tested separately.
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!);
    await context.route('**/api/dashboard/systems/system-a/workspace-access', route => route.fulfill({ json: {
      status: 'success', data: { systemId: 'system-a', roles: ['AuthorizingOfficial', 'SCA'], permissions: {
        canRead: true, canEditProfile: false, canManageSystem: false, canAuthorNarratives: false, canReviewNarratives: false,
        canManageEvidence: false, canRunAssessments: false, canManageRemediation: false, canDecideAuthorization: true,
      } },
    } }));
    const writes: { path: string; body: Record<string, unknown> }[] = [];
    const records: Record<string, unknown>[] = [];
    await context.route('**/api/dashboard/systems/system-a/authorization/record-context?*', route => route.fulfill({ json: {
      systemId: 'system-a', canRecord: true, activeDecisionId: null, page: 1, pageSize: 50, sourceTotal: 1, packageTotal: 1, recordTotal: records.length,
      sourceEvidence: [{ id: 'source-a', fileName: 'Retained AO decision.pdf', contentHash: hash }],
      completedPackages: [{ id: 'package-a', contentHash: hash, generatedAt: '2026-09-01', purpose: 1 }], records,
    } }));
    await context.route('**/api/dashboard/systems/system-a/authorization/records', route => {
      const body = route.request().postDataJSON();
      writes.push({ path: new URL(route.request().url()).pathname, body });
      records.push({ id: 'decision-a', ...body, externalIssuingAuthority: body.issuingAuthority,
        sourceEvidenceHash: body.expectedSourceHash, baselinePackageHash: body.expectedPackageHash,
        isActive: body.makeCurrent, recordedBy: 'Recording AO', recordedAt: '2026-09-26T12:00:00Z' });
      return route.fulfill({ status: 201, json: { id: 'decision-a' } });
    });
    await context.route('**/api/dashboard/systems/system-a/authorization', route => route.fulfill({ status: 404 }));
    await context.route('**/api/dashboard/systems/system-a/risk-acceptances', route => route.fulfill({ json: [] }));
    let plan = { sapId: 'sap-a', systemId: 'system-a', title: 'Initial assessment', assessmentLead: 'Lead A',
      scopeNotes: 'Reviewed boundary', assessmentApproach: 'Examine', draftHash: hash, status: 'Draft', canEdit: true, canCreate: true };
    await context.route('**/api/v1/systems/system-a/sap/draft', route => route.fulfill({ json: plan }));
    await context.route('**/api/v1/systems/system-a/sap/sap-a/draft', route => {
      const body = route.request().postDataJSON();
      writes.push({ path: new URL(route.request().url()).pathname, body });
      plan = { ...plan, ...body, draftHash: 'b'.repeat(64) };
      return route.fulfill({ json: plan });
    });
    await context.route('**/api/dashboard/assessments', route => route.fulfill({ json: [] }));
    await context.route('**/api/v1/systems/system-a/sap', route => route.fulfill({ json: {
      sapId: 'sap-a', systemId: 'system-a', title: plan.title, status: 'Draft', format: 'markdown', baselineLevel: 'Low',
      totalControls: 1, customerControls: 1, inheritedControls: 0, sharedControls: 0, stigBenchmarkCount: 0,
      controlsWithObjectives: 1, evidenceGaps: 0, familySummaries: [], generatedAt: '2026-09-26T12:00:00Z', warnings: [],
    } }));
    await context.route('**/api/v1/systems/system-a/sar', route => route.fulfill({ status: 404 }));

    // Act: external record is distinct from Issue Authorization.
    await page.goto(`${root}/authorize`);
    await page.getByRole('button', { name: 'Record external decision', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Record external authorization decision' });
    await dialog.getByLabel('Decision source').selectOption('source-a');
    await dialog.getByLabel('Issuing authority as recorded').fill('External AO Office');
    await dialog.getByLabel('Decision date', { exact: true }).fill('2026-01-01');
    await dialog.getByLabel('Expiration date', { exact: true }).fill('2027-01-01');
    await dialog.getByLabel('Applicable retained package').selectOption('package-a');
    await dialog.getByLabel('I reviewed the source and exact retained package.').check();
    await dialog.getByRole('button', { name: 'Save recorded decision' }).click();
    // Assert
    await expect(page.getByText(/External decision decision-a recorded/)).toBeVisible();
    expect(writes[0]?.body).toMatchObject({ sourceEvidenceId: 'source-a', expectedSourceHash: hash,
      baselinePackageId: 'package-a', expectedPackageHash: hash, makeCurrent: false, issuingAuthority: 'External AO Office' });
    expect(writes[0]?.body).not.toHaveProperty('issuedBy');
    await page.reload();
    await expect(page.getByRole('heading', { name: 'ATO · External AO Office' })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`external-decision-${width}.png`), fullPage: true });

    // Act: save and reload the same SAP draft, not a second plan engine.
    await page.goto(`${root}/assessments?tab=plan`);
    await expect(page.getByLabel('Assessment title', { exact: true })).toHaveCount(0);
    await page.getByRole('button', { name: 'Edit assessment draft', exact: true }).click();
    const assessmentDialog = page.getByRole('dialog', { name: 'Edit assessment draft' });
    await expect(assessmentDialog).toBeVisible();
    await page.getByLabel('Assessment title', { exact: true }).fill('Manual mission assessment');
    await page.getByLabel('Assessment lead', { exact: true }).fill('Assigned SCA');
    await page.getByLabel('Assessment scope', { exact: true }).fill('Reviewed boundary and selected baseline.');
    await page.getByLabel('Assessment approach', { exact: true }).fill('Examine documents, interview staff and test controls.');
    await page.getByRole('button', { name: 'Save assessment draft' }).click();
    // Assert
    await expect(page.getByText('Assessment draft saved. Review and finalization remain separate.')).toBeVisible();
    expect(writes[1]?.body).toMatchObject({ title: 'Manual mission assessment', assessmentLead: 'Assigned SCA', expectedContentHash: hash });
    await expect(assessmentDialog).toHaveCount(0);
    await page.reload();
    await expect(page.getByText('Examine documents, interview staff and test controls.', { exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Edit assessment draft', exact: true }).click();
    await expect(page.getByLabel('Assessment title', { exact: true })).toHaveValue('Manual mission assessment');
    await expect(page.getByLabel('Assessment approach', { exact: true })).toHaveValue('Examine documents, interview staff and test controls.');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`manual-sap-${width}.png`), fullPage: true });
    await page.keyboard.press('Escape');
    await expect(assessmentDialog).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Edit assessment draft', exact: true })).toBeFocused();
  });
}
