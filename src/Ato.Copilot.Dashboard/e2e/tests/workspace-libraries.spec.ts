import { expect, test } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';

for (const width of [1440, 390]) {
  for (const kind of ['provider', 'organization'] as const) {
    test(`${kind} library uses its real workspace route at ${width}px`, async ({ page, context, baseURL }) => {
      // Arrange
      await page.setViewportSize({ width, height: 1000 });
      await installWorkspaceFixture(context, baseURL!, { providerOnly: kind === 'provider' });
      const prefix = kind === 'provider' ? '/workspaces/csp' : '/workspaces/organizations/org-a';
      const api = kind === 'provider' ? '/api/csp/narrative-library' : '/api/narrative-library';
      const scopeId = kind === 'provider' ? 'provider-a' : 'org-a';
      const calls: string[] = [];
      let imported = false;
      const reference = {
        id: 'reference-a', referenceKey: 'key-a', title: 'Synthetic reference',
        scope: kind === 'provider' ? 'Provider' : 'Organization', scopeId, sourceName: 'pasted-reference.txt',
        sourceSha256: 'synthetic-hash', version: 1, revision: 1, isPublished: false,
        createdAt: '2026-01-01T00:00:00Z', createdBy: 'synthetic-owner',
        publishedAt: null as string | null, publishedBy: null as string | null,
        passages: [{ controlId: 'AC-2', narrativeType: 'Policy', content: 'Synthetic reference claim.' }],
      };
      await context.route(`**${api}{,/**}`, async route => {
        const request = route.request();
        const path = new URL(request.url()).pathname;
        calls.push(path);
        expect(request.headers()['x-workspace-kind']).toBe(kind === 'provider' ? 'csp' : 'organization');
        expect(request.headers()['x-workspace-mode']).toBe('ordinary');
        expect(request.headers()['x-workspace-tenant-id']).toBe(kind === 'provider' ? undefined : 'org-a');
        if (path.endsWith('/access')) return route.fulfill({ json: kind === 'provider'
          ? { cspProfileId: scopeId, displayName: 'Synthetic Provider', canPublish: true, capabilities: [] }
          : { tenantId: scopeId, canPublishShared: false, capabilities: [] } });
        if (request.method() === 'GET' && path === api) return route.fulfill({ json: imported ? [reference] : [] });
        if (path.endsWith('/imports')) {
          expect(request.postData()).toContain(scopeId);
          imported = true;
          return route.fulfill({ status: 201, json: reference });
        }
        if (path.endsWith('/publish')) {
          expect(request.postDataJSON()).toEqual({ expectedRevision: 1, reviewed: true, passages: reference.passages });
          Object.assign(reference, { isPublished: true, revision: 2, publishedAt: '2026-01-02T00:00:00Z', publishedBy: 'synthetic-owner' });
          return route.fulfill({ json: reference });
        }
        return route.fulfill({ status: 404, json: { error: 'Outside the synthetic library contract' } });
      });

      // Act
      await page.goto(`${prefix}/narrative-library`);

      // Assert
      await expect(page.getByRole('heading', { name: 'Narrative Library', exact: true })).toBeVisible();
      await expect(page.getByRole('link', { name: `${kind === 'provider' ? 'Provider' : 'Organization'} Narrative Library`, exact: true }))
        .toHaveAttribute('href', `${prefix}/narrative-library`);
      if (kind === 'organization') {
        await expect(page.getByRole('button', { name: 'Upload narratives' })).toBeDisabled();
      } else {
        await page.getByRole('button', { name: 'Upload narratives' }).click();
        await page.getByLabel('Reference title').fill(reference.title);
        await page.getByLabel('Paste narratives').fill('Synthetic reference claim.');
        await page.getByRole('button', { name: 'Extract passages' }).click();
        await expect(page.getByRole('heading', { name: 'Review before publishing' })).toBeVisible();
        await page.getByLabel('I reviewed these reference claims and mappings').check();
        await page.getByRole('button', { name: 'Publish references' }).click();
        await expect(page.getByText('Published v1', { exact: true })).toBeVisible();
      }
      expect(calls).toContain(`${api}/access`);
      expect(calls).toContain(api);
      expect(calls.every(path => !path.includes('/systems/'))).toBe(true);
    });
  }
}
