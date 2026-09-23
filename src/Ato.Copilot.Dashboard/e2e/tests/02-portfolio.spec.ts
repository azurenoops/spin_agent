import { test, expect } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import type { PortfolioSystemSummary } from '../../src/types/dashboard';

const system: PortfolioSystemSummary = {
  systemId: 'system-a', name: 'Mission system', acronym: 'MS', systemType: 'MajorApplication',
  missionCriticality: 'High', hostingEnvironment: 'Cloud', description: null,
  impactLevel: 'Moderate', currentRmfPhase: 'Implement', complianceScore: 100, complianceScoreDelta: 0,
  atoExpirationDate: null, atoStatus: 'None', atoDaysRemaining: null, atoSeverity: 'green',
  openPoamCount: 0, overduePoamCount: 0, catICounts: 0, catIICounts: 0, catIIICounts: 0,
  isSetupComplete: false, hasBoundary: false, hasRoles: true, hasCategorization: false,
};

for (const provider of [false, true]) {
  for (const width of [1440, 390]) {
    for (const zero of [false, true]) {
      test(`${provider ? 'CSP' : 'organization'} portfolio graphs at ${width}px with ${zero ? 'zero' : 'recorded'} counts`, async ({ page, context, baseURL }, testInfo) => {
        // Arrange
        await page.setViewportSize({ width, height: 1100 });
        await page.emulateMedia({ colorScheme: 'dark' });
        await installWorkspaceFixture(context, baseURL!, { providerOnly: provider });
        await context.route('**/api/csp/dashboard/summary', route => route.fulfill({ json: { status: 'success', data: {
          tenantCounts: { active: 5, suspended: 0, disabled: 0, total: 5 }, disabledTenantCount: 0,
          organizationCount: 5, systemCount: 8,
          atoStatusCounts: { authorized: zero ? 0 : 2, inProcess: 0, denied: zero ? 0 : 1 },
          openFindingsBySeverity: { critical: zero ? 0 : 1, high: zero ? 0 : 2, moderate: zero ? 0 : 1, low: 0 },
          openPoamCount: zero ? 0 : 5, openDeviationCount: zero ? 0 : 1, generatedAt: '2026-09-23T12:00:00Z',
        } } }));
        const systems = zero ? [system] : [
          { ...system, atoStatus: 'Active', catICounts: 1, catIICounts: 2, catIIICounts: 3, openPoamCount: 5 },
          { ...system, systemId: 'system-b', atoStatus: 'Expired' },
          { ...system, systemId: 'system-c' },
        ];
        await context.route('**/api/dashboard/portfolio?*', route =>
          route.fulfill({ json: { items: systems, totalCount: systems.length, nextCursor: null } }));
        await context.route('**/api/dashboard/capabilities/coverage?*', route =>
          route.fulfill({ json: { orgWide: { coveragePercent: 75 }, perSystem: [] } }));

        // Act
        const root = provider ? '/workspaces/csp' : '/workspaces/organizations/org-a';
        await page.goto(root);
        const ato = page.getByRole('region', { name: provider ? 'ATO status across organizations' : 'ATO status across systems' });
        const findings = page.getByRole('region', { name: 'Open findings by severity' });
        await expect(ato).toBeVisible();
        await expect(findings).toBeVisible();

        // Assert
        await expect(page.locator('main .recharts-wrapper > svg.recharts-surface')).toHaveCount(2);
        await expect(page.getByRole('table')).toHaveCount(0);
        for (const name of ['Compliance by System', 'Open POA&Ms by System', 'System Risk Summary']) {
          await expect(page.getByRole('heading', { name, exact: true })).toHaveCount(0);
        }
        await expect(ato.getByRole('img', { name: /^ATO status/ })).toHaveAttribute('aria-label', provider
          ? new RegExp(`Authorized: ${zero ? 0 : 2}.*In Process: 0.*Denied: ${zero ? 0 : 1}`)
          : new RegExp(`Active: ${zero ? 0 : 1}.*Expired: ${zero ? 0 : 1}.*Not recorded: 1`));
        await expect(findings.getByRole('img', { name: /^Open findings by severity/ })).toHaveAttribute('aria-label', provider
          ? new RegExp(`Critical: ${zero ? 0 : 1}.*High: ${zero ? 0 : 2}.*Moderate: ${zero ? 0 : 1}.*Low: 0`)
          : new RegExp(`CAT I: ${zero ? 0 : 1}.*CAT II: ${zero ? 0 : 2}.*CAT III: ${zero ? 0 : 3}`));
        await expect(ato.locator('.recharts-bar-rectangle path')).toHaveCount(provider ? (zero ? 0 : 2) : (zero ? 1 : 3));
        if (zero) {
          await expect(findings.getByText('No open findings', { exact: true })).toBeVisible();
          await expect(findings.locator('.recharts-bar-rectangle path')).toHaveCount(0);
        } else {
          await expect(findings.locator('.recharts-bar-rectangle path')).toHaveCount(3);
          await expect(findings.locator('.recharts-bar-rectangle path').first()).toBeVisible();
        }
        const followUp = page.getByRole('region', { name: provider ? 'Portfolio follow-up' : 'Needs attention' });
        await expect(followUp).toBeVisible();
        const [atoBox, findingsBox, followUpBox] = await Promise.all([ato.boundingBox(), findings.boundingBox(), followUp.boundingBox()]);
        expect(atoBox).not.toBeNull();
        expect(findingsBox).not.toBeNull();
        expect(followUpBox!.y).toBeGreaterThanOrEqual(findingsBox!.y + findingsBox!.height);
        if (width >= 1024) expect(Math.abs(atoBox!.y - findingsBox!.y)).toBeLessThan(2);
        else expect(findingsBox!.y).toBeGreaterThanOrEqual(atoBox!.y + atoBox!.height);
        expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
        await expect(page.locator('html')).not.toHaveClass(/dark/);
        await expect(ato).toHaveCSS('background-color', 'rgb(255, 255, 255)');
        await expect(page.getByRole('navigation', { name: 'Portfolio workspaces' }).getByRole('link', { name: /Systems/ }))
          .toHaveAttribute('href', `${root}/systems`);
        await page.screenshot({ path: testInfo.outputPath('portfolio-graphs.png'), fullPage: true });
        await ato.screenshot({ path: testInfo.outputPath('ato-graph.png') });
        await findings.screenshot({ path: testInfo.outputPath('findings-graph.png') });
      });
    }
  }
}
