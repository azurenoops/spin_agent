import { expect, test } from '@playwright/test';
import { installSystemCapabilityFixture } from '../fixtures/system-capabilities';

const root = '/workspaces/organizations/org-a/systems/system-a';
for (const width of [1440, 390]) {
  test(`Mission Owner completion advances their queue while ISSM receives review work at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: role and persisted workflow state are controlled by server fixtures.
    await page.setViewportSize({ width, height: 1000 });
    await installSystemCapabilityFixture(context, baseURL!);
    let role = 'MissionOwner';
    let status = 'Draft';
    const writes: string[] = [];
    await context.route('**/api/dashboard/systems/system-a/workspace-access', route => route.fulfill({ json: {
      status: 'success', data: { systemId: 'system-a', roles: [role], permissions: {
        canRead: true, canEditProfile: true, canManageSystem: true, canAuthorNarratives: false,
        canReviewNarratives: role === 'Issm', canManageEvidence: false, canRunAssessments: false,
        canManageRemediation: false, canDecideAuthorization: false,
      } },
    } }));
    await context.route('**/api/dashboard/systems/system-a/next-actions', route => {
      const review = role === 'Issm' && status === 'UnderReview';
      const author = role === 'MissionOwner' && status === 'Draft';
      const items = review || author ? [{
        id: review ? 'review-mission' : 'submit-mission',
        title: review ? 'Review submitted mission profile' : 'Submit mission profile for review',
        description: review ? 'A saved submission is awaiting your review.' : 'Your saved draft is ready for the submission step.',
        path: 'profile/MissionAndPurpose', actionLabel: 'Open', responsibleRole: role,
      }] : role === 'MissionOwner' ? [{
        id: 'complete-data', title: 'Complete data profile', description: 'Record mission information types.',
        path: 'profile/DataTypes', actionLabel: 'Open', responsibleRole: role,
      }] : [];
      return route.fulfill({ json: { systemId: 'system-a', checkedAt: '2026-09-28T18:00:00Z',
        effectiveRoles: [role], items, waitingOnOtherRoles: role === 'MissionOwner' && status === 'UnderReview' ? [{ role: 'Issm', count: 1 }] : [] } });
    });
    await context.route('**/api/dashboard/systems/system-a/profile/MissionAndPurpose', route => route.fulfill({ json: {
      id: 'profile-a', sectionType: 'MissionAndPurpose', canEditProfile: true, governanceStatus: status,
      draftContent: '{"missionStatement":"Recorded mission","businessPurpose":"Recorded purpose"}',
      approvedContent: status === 'Approved' ? '{"missionStatement":"Recorded mission","businessPurpose":"Recorded purpose"}' : null,
      completionPercentage: 100, userCategories: [], dataTypeEntries: [], ppsEntries: [], leveragedAuthorizations: [],
    } }));
    await context.route('**/api/dashboard/systems/system-a/profile/submit', route => {
      expect(role).toBe('MissionOwner');
      expect(route.request().postDataJSON()).toEqual({ action: 'submit', sectionTypes: ['MissionAndPurpose'] });
      writes.push('submit'); status = 'UnderReview';
      return route.fulfill({ json: { submittedSections: ['MissionAndPurpose'], skippedSections: [] } });
    });
    await context.route('**/api/dashboard/systems/system-a/profile/MissionAndPurpose/review', route => {
      expect(role).toBe('Issm');
      expect(route.request().postDataJSON().decision).toBe('approve');
      writes.push('approve'); status = 'Approved';
      return route.fulfill({ json: { sectionType: 'MissionAndPurpose', newStatus: status } });
    });
    await context.route('**/api/v1/systems/system-a/packages/validate*', route => route.fulfill({ json: {
      purpose: 'InitialSubmission', isValid: false, errorCount: 1, warningCount: 0, validatedAt: 'now',
      findings: [{ severity: 'error', category: 'profile-approval', artifactType: 'ssp',
        description: 'ISSM approval is still required.', remediation: 'Review the submission.' }],
    } }));
    // Act: the owner completes submission, not ISSM approval.
    await page.goto(root);
    await expect(page.getByRole('heading', { name: 'Submit mission profile for review', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Review submitted mission profile', exact: true })).toHaveCount(0);
    await page.getByRole('link', { name: 'Open: Submit mission profile for review', exact: true }).click();
    await page.getByRole('button', { name: 'Submit for Review', exact: true }).click();
    await expect(page.getByText('Section submitted for review.', { exact: true })).toBeVisible();
    await page.goBack();
    // Assert: the completed owner task is replaced; the reviewer is only a waiting dependency.
    await expect(page.getByRole('heading', { name: 'Complete data profile', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Submit mission profile for review', exact: true })).toHaveCount(0);
    await expect(page.getByRole('heading', { name: 'Review submitted mission profile', exact: true })).toHaveCount(0);
    await expect(page.getByRole('region', { name: 'Waiting on other roles' })).toContainText('ISSM: 1 pending');
    await page.getByRole('button', { name: 'Check readiness', exact: true }).click();
    await expect(page.getByText('1 blocking requirement remains', { exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Continue preparation', exact: true })).toHaveAttribute('href', `${root}/profile/DataTypes`);
    await expect(page.getByRole('region', { name: 'Next actions for your system roles' }).getByRole('heading', { name: 'ISSM approval is still required.', exact: true })).toHaveCount(0);
    await expect(page.getByRole('region', { name: 'Team readiness findings' }).getByRole('heading', { name: 'ISSM approval is still required.', exact: true })).toBeVisible();
    await page.screenshot({ path: info.outputPath(`mission-owner-next-actions-${width}.png`) });
    // Act: load the reviewer's effective server role.
    role = 'Issm'; await page.reload();
    await expect(page.getByRole('heading', { name: 'Review submitted mission profile', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Complete data profile', exact: true })).toHaveCount(0);
    await page.getByRole('link', { name: 'Open: Review submitted mission profile', exact: true }).click();
    await page.getByRole('button', { name: 'Approve', exact: true }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Approve section', exact: true }).click();
    await expect(page.getByText('Section approved.', { exact: true })).toBeVisible();
    await page.goBack();
    // Assert: approved work disappears from the ISSM queue too.
    await expect(page.getByRole('heading', { name: 'No actions assigned to you', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Review submitted mission profile', exact: true })).toHaveCount(0);
    expect(writes).toEqual(['submit', 'approve']);
  });
}
