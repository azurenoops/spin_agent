import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { systemDetail } from '../../src/__tests__/fixtures/assessmentEnvironment';

for (const width of [1440, 390]) {
  test(`mission evidence and provider summaries remain distinct at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: synthetic responses exercise the actual SPA, not live backend sharing.
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!);
    await context.route('**/api/dashboard/systems/system-a', route => route.fulfill({ json: {
      ...systemDetail, systemId: 'system-a', name: 'Synthetic Mission System', currentRmfPhase: 'Prepare',
    } }));
    await context.route('**/api/dashboard/systems/system-a/evidence?*', route => route.fulfill({ json: {
      items: [{ id: 'evidence-a', source: 'Manual', fileName: 'Mission access review.pdf', contentType: 'application/pdf',
        fileSizeBytes: 2048, artifactCategory: 'PolicyDocument', narrativeType: 'Policy', controlId: 'AC-2',
        controlImplementationId: 'implementation-a', securityCapabilityId: null, description: 'Mission-specific review.',
        uploadedBy: 'Mission reviewer', uploadedAt: '2026-09-26T12:00:00Z', contentHash: 'mission-hash' }],
      totalCount: 1, page: 1, pageSize: 50,
    } }));
    await context.route('**/api/dashboard/systems/system-a/evidence/summary', route => route.fulfill({ json: {
      totalCount: 1, manualCount: 1, automatedCount: 0, controlsWithEvidence: 1, totalControls: 3, coveragePercentage: 33.3,
    } }));
    let state: 'available' | 'unavailable' | 'empty' = 'available';
    await context.route('**/api/dashboard/systems/system-a/provider-evidence?*', route => {
      expect(route.request().headers()['x-workspace-tenant-id']).toBe('org-a');
      if (state === 'unavailable') return route.fulfill({ status: 503, json: {
        error: { message: 'Sharing service is unavailable.' },
      } });
      return route.fulfill({ json: { status: 'success', data: {
        items: state === 'empty' ? [] : [{
          shareId: 'summary-a', providerId: 'provider-a', offeringId: 'offering-a', evidenceId: 'source-a',
          assignmentId: 'assignment-a', targetTenantId: 'org-a', systemId: 'system-a', version: 2,
          previousVersionId: null, summary: 'Reviewed service assessment summary, not the private attachment.',
          sourceSha256: 'source-hash', contentHash: 'summary-hash', approvedBy: 'Provider reviewer',
          approvedAt: '2026-09-26T12:00:00Z', revision: 2, revokedAt: null,
          permission: 'ApprovedSummaryOnly', privateAttachmentAccess: false,
        }], total: state === 'empty' ? 0 : 1, page: 1, pageSize: 25,
      } } });
    });
    const mutations: string[] = [];
    const errors: string[] = [];
    page.on('request', request => {
      if (request.url().includes('/api/') && ['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method())) mutations.push(request.url());
    });
    page.on('pageerror', error => errors.push(error.message));

    // Act / Assert: both sections are directly visible; access and counts are never merged.
    await page.goto('/workspaces/organizations/org-a/systems/system-a/evidence');
    const provider = page.getByRole('region', { name: 'Provider-approved evidence' });
    const mission = page.getByRole('region', { name: 'Mission evidence', exact: true });
    const summaries = provider.getByRole('table', { name: 'Provider-approved summaries' });
    await expect(summaries).toBeVisible();
    await expect(mission.getByRole('table', { name: 'Mission evidence records' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Upload Evidence', exact: true })).toBeDisabled();
    await expect(summaries.getByText('Private attachment restricted')).toBeVisible();
    await expect(summaries.getByText('Approved summary only', { exact: true })).toBeVisible();
    await summaries.getByText('Preview approved summary', { exact: true }).click();
    await expect(summaries.getByText('Reviewed service assessment summary, not the private attachment.')).toBeVisible();
    await expect(provider.getByRole('button', { name: 'Approve summary access' })).toHaveCount(0);
    const geometry = await page.evaluate(() => ({ width: innerWidth, documentWidth: document.documentElement.scrollWidth }));
    expect(geometry.documentWidth).toBeLessThanOrEqual(geometry.width);
    await page.screenshot({ path: info.outputPath(`provider-summary-table-${width}.png`), fullPage: true });

    await mission.getByText('Mission evidence summary', { exact: true }).click();
    await expect(mission.getByText('Mission records', { exact: true }).locator('..')).toHaveText('Mission records1');
    await expect(mission.getByRole('button', { name: 'Review evidence Mission access review.pdf' })).toBeVisible();
    await mission.scrollIntoViewIfNeeded();
    await page.screenshot({ path: info.outputPath(`mission-evidence-table-${width}.png`), fullPage: true });

    // A failed refresh is unavailable, never a fabricated empty/available result.
    state = 'unavailable';
    await provider.getByRole('button', { name: 'Refresh evidence access' }).click();
    await expect(provider.getByRole('alert')).toContainText('Provider evidence is unavailable. Sharing service is unavailable.');
    await expect(provider.getByRole('button', { name: 'Download approved summary' })).toHaveCount(0);
    await expect(provider.getByText(/No approved provider evidence is available/)).toHaveCount(0);
    await expect(mission.getByRole('button', { name: 'Review evidence Mission access review.pdf' })).toBeVisible();
    await provider.scrollIntoViewIfNeeded();
    await page.screenshot({ path: info.outputPath(`provider-unavailable-${width}.png`), fullPage: true });
    state = 'empty';
    await provider.getByRole('button', { name: 'Refresh evidence access' }).click();
    await expect(provider.getByText(/No approved provider evidence is available/)).toBeVisible();
    expect(mutations).toEqual([]);
    expect(errors).toEqual([]);
  });
}
