import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';

const systemRoot = '/workspaces/organizations/org-a/systems/system-a';

for (const width of [1440, 390]) {
  test(`categorization and baseline actions match their labels at ${width}px`, async ({ context, page, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    await installWorkspaceFixture(context, baseURL!, { canManageSystem: true });
    await page.route('**/api/dashboard/systems/system-a/profile/DataTypes', route => route.fulfill({
      json: {
        id: 'profile-data',
        sectionType: 'DataTypes',
        governanceStatus: 'UnderReview',
        draftContent: null,
        approvedContent: null,
        completionPercentage: 100,
        lastEditedBy: 'System Owner',
        lastEditedAt: '2026-09-02T00:00:00Z',
        submittedBy: 'System Owner',
        submittedAt: '2026-09-02T00:00:00Z',
        reviewedBy: null,
        reviewedAt: null,
        reviewerComments: null,
        userCategories: [],
        dataTypeEntries: [{
          id: 'data-a',
          dataTypeName: 'Mission support records',
          description: 'Operational mission support data.',
          sensitivityClassification: 'CUI',
          source: 'Mission owner inventory',
          destination: null,
          applicableRegulations: null,
          sortOrder: 0,
        }],
        ppsEntries: [],
        leveragedAuthorizations: [],
      },
    }));
    await page.route('**/api/dashboard/systems/system-a/baseline', route => route.fulfill({
      json: {
        baselineId: 'baseline-a',
        baselineLevel: 'Moderate',
        totalControls: 325,
        overlayApplied: 'CNSSI 1253 IL4',
        inheritedControls: 100,
        sharedControls: 25,
        customerControls: 175,
        tailoredInControls: 2,
        tailoredOutControls: 1,
        createdAt: '2026-09-01T00:00:00Z',
        createdBy: 'ISSM User',
        modifiedAt: null,
        familyBreakdown: [{ family: 'Access Control', count: 25 }],
        tailorings: [],
        controlIds: ['AC-1'],
      },
    }));
    await page.route('**/api/dashboard/systems/system-a', route => route.fulfill({
      json: {
        systemId: 'system-a',
        name: 'Synthetic Mission System',
        acronym: 'SYN',
        systemType: 'MajorApplication',
        missionCriticality: 'MissionEssential',
        hostingEnvironment: 'AzureGovernment',
        impactLevel: 'IL4',
        baselineLevel: 'Moderate',
        currentRmfPhase: 'Categorize',
        rmfPhaseProgress: [],
        keyMetrics: {},
        recentActivity: [],
        categorization: {
          confidentiality: 'Moderate',
          integrity: 'Moderate',
          availability: 'Low',
          overall: 'Moderate',
          formalNotation: 'SC = {(confidentiality, Moderate), (integrity, Moderate), (availability, Low)}',
          dodImpactLevel: 'IL4',
          isNationalSecuritySystem: false,
          informationTypes: [{
            name: 'Mission support records',
            confidentiality: 'Moderate',
            integrity: 'Moderate',
            availability: 'Low',
          }],
        },
      },
    }));

    // Act
    await page.goto(`${systemRoot}/baseline`);

    // Assert
    await expect(page.getByRole('heading', { name: 'Categorization & control baseline' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Applied capabilities' })).toHaveAttribute(
      'href',
      `${systemRoot}/security-capabilities`,
    );
    await expect(page.getByRole('link', { name: 'Responsibilities' })).toHaveAttribute(
      'href',
      `${systemRoot}/inheritance/subscriptions`,
    );
    await expect(page.getByRole('link', { name: 'Preview contribution' })).toHaveAttribute(
      'href',
      `${systemRoot}/documents#ssp-sections`,
    );
    await expect(page.getByRole('link', { name: 'View package readiness' })).toHaveAttribute('href', systemRoot);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
    await page.getByRole('button', { name: 'Review categorization', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Re-categorize System' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Re-categorize System' }).locator('..')).toBeInViewport();
  });
}
