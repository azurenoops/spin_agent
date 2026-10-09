import { expect, test } from '@playwright/test';
import axe from 'axe-core';
import { installSystemCapabilityFixture } from '../fixtures/system-capabilities';
import { designFixture } from '../../src/__tests__/system-design/fixtures';
import type { SaveSystemDesignRequest } from '../../src/api/systemDesign';

for (const width of [1440, 390]) {
  test(`full-width statuses precede the introduction and populated register can create at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange: writes are fulfilled only against synthetic records.
    await page.setViewportSize({ width, height: 1100 });
    await installSystemCapabilityFixture(context, baseURL!);
    const graph = designFixture();
    await context.route('**/api/dashboard/systems/system-a/design', route => {
      expect(route.request().method()).toBe('GET');
      return route.fulfill({ json: graph });
    });
    const definitions = ['mission-app', 'mission-api'].map((name, index) => ({
      id: `boundary-${index}`, name, boundaryType: 'Logical', isPrimary: index === 0,
      registeredSystemId: 'system-a', componentCount: 0, resourceCount: 0, coveragePercent: 0,
    }));
    const writes: unknown[] = [];
    let reject = true;
    await context.route('**/api/dashboard/systems/system-a/boundary-definitions', route => {
      if (route.request().method() === 'POST') {
        const body = route.request().postDataJSON();
        writes.push(body);
        if (reject) return route.fulfill({ status: 400, json: { error: 'Synthetic create rejected' } });
        const created = { ...definitions[0], ...body, id: 'new-boundary', isPrimary: false };
        definitions.push(created);
        return route.fulfill({ json: created });
      }
      return route.fulfill({ json: { items: definitions } });
    });
    const pageErrors: string[] = [];
    page.on('pageerror', error => pageErrors.push(error.message));
    // Act
    await page.goto('/workspaces/organizations/org-a/systems/system-a/boundaries');
    // Assert: measure both actual ribbons against the whole workspace, not a column.
    const workspace = page.getByRole('region', { name: 'Governed components & system scope' });
    const navigation = page.getByRole('navigation', { name: 'System task views' });
    const canonical = page.getByRole('status', { name: 'Boundary record status' });
    const governed = page.getByRole('status', { name: 'Inventory review status' });
    const intro = page.getByRole('heading', { name: 'What belongs to this system?', exact: true });
    await expect(canonical).toHaveCount(1);
    await expect(governed).toHaveCount(1);
    await expect(governed).toContainText('Draft · Working revision 2');
    const [container, nav, first, second, introduction] = await Promise.all(
      [workspace, navigation, canonical, governed, intro].map(locator => locator.boundingBox()));
    expect(nav!.y + nav!.height).toBeLessThan(first!.y);
    expect(first!.y + first!.height).toBeLessThan(second!.y);
    expect(second!.y + second!.height).toBeLessThan(introduction!.y);
    for (const box of [first!, second!]) {
      expect(Math.abs(box.x - container!.x)).toBeLessThan(2);
      expect(Math.abs(box.width - container!.width)).toBeLessThan(2);
    }
    await expect(page.getByText('SSP · Boundary description and inventory', { exact: true })).toHaveCount(0);
    await expect(page.getByText('SSP · Reviewed system definition', { exact: true })).toBeVisible();
    await expect(page.getByRole('complementary', { name: 'Document contribution and next tasks' })).toHaveCount(1);
    await expect(page.getByRole('button', { name: 'Review boundary', exact: true })).toHaveCount(0);
    const register = page.getByRole('region', { name: 'Recorded boundary inventory' });
    const support = page.getByRole('complementary', { name: 'Document contribution and next tasks' });
    const main = support.locator('..').locator(':scope > div').first();
    const componentTable = page.getByRole('table', { name: 'Components & system scope' });
    await expect(main.getByRole('heading', { name: 'What belongs to this system?', exact: true })).toHaveCount(1);
    await expect(main.getByRole('region', { name: 'Recorded boundary inventory' })).toHaveCount(1);
    await expect(main.getByRole('table', { name: 'Components & system scope' })).toHaveCount(1);
    const [mainBox, supportBox, registerBox, componentScrollBox, definitionsTableBox, componentTableBox] = await Promise.all(
      [main, support, register, componentTable.locator('..'), register.getByRole('table'), componentTable]
        .map(locator => locator.boundingBox()));
    expect(Math.abs(registerBox!.x - mainBox!.x)).toBeLessThan(2);
    expect(Math.abs(registerBox!.width - mainBox!.width)).toBeLessThan(2);
    expect(Math.abs(componentScrollBox!.x - mainBox!.x)).toBeLessThan(2);
    expect(Math.abs(componentScrollBox!.width - mainBox!.width)).toBeLessThan(2);
    expect(registerBox!.y + registerBox!.height).toBeLessThan(componentTableBox!.y);
    if (width === 1440) {
      expect(Math.abs(supportBox!.y - mainBox!.y)).toBeLessThan(2);
      expect(second!.y + second!.height).toBeLessThan(supportBox!.y);
      expect(mainBox!.x + mainBox!.width).toBeLessThan(supportBox!.x);
      expect(supportBox!.y).toBeLessThan(definitionsTableBox!.y);
      expect(supportBox!.y).toBeLessThan(componentTableBox!.y);
      expect(supportBox!.y + supportBox!.height).toBeGreaterThanOrEqual(componentTableBox!.y + componentTableBox!.height);
    } else {
      expect(supportBox!.y).toBeGreaterThanOrEqual(mainBox!.y + mainBox!.height);
      expect(Math.abs(supportBox!.x - mainBox!.x)).toBeLessThan(2);
      for (const table of [register.getByRole('table'), componentTable]) {
        const scroll = table.locator('..');
        expect(await scroll.evaluate(element => getComputedStyle(element).overflowX)).toBe('auto');
        expect(await scroll.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
        await scroll.evaluate(element => { element.scrollLeft = 40; });
        expect(await scroll.evaluate(element => element.scrollLeft)).toBeGreaterThan(0);
        await scroll.evaluate(element => { element.scrollLeft = 0; });
      }
    }
    const create = register.getByRole('button', { name: 'Add System Boundary', exact: true });
    await expect(register.getByRole('button', { name: /^Open boundary / })).toHaveCount(2);
    await expect(register).toContainText('Canonical definition and placement changes take effect immediately');
    await expect(page.getByRole('button', { name: 'Save scope draft', exact: true })).toBeDisabled();
    // Act / Assert: keyboard create, focus trap, Escape/cancel and failed-create retention.
    await create.focus();
    await page.keyboard.press('Enter');
    const dialog = page.getByRole('dialog', { name: 'Create Boundary', exact: true });
    await expect(dialog).toBeVisible();
    await dialog.getByRole('textbox', { name: 'Name *', exact: true }).fill('Cancelled boundary');
    await dialog.getByRole('button', { name: 'Create Boundary', exact: true }).focus();
    await page.keyboard.press('Tab');
    await expect(dialog.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(dialog.getByRole('button', { name: 'Close dialog', exact: true })).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(create).toBeFocused();
    expect(writes).toEqual([]);
    await create.click();
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
    await expect(create).toBeFocused();
    expect(writes).toEqual([]);
    await create.click();
    await dialog.getByRole('textbox', { name: 'Name *', exact: true }).fill('Recorded additional boundary');
    await dialog.getByRole('button', { name: 'Create Boundary', exact: true }).click();
    await expect(dialog.getByRole('alert')).toContainText('Synthetic create rejected');
    await expect(dialog.getByRole('textbox', { name: 'Name *', exact: true })).toHaveValue('Recorded additional boundary');
    reject = false;
    await dialog.getByRole('button', { name: 'Create Boundary', exact: true }).click();
    await expect(dialog).toHaveCount(0);
    await expect(create).toBeFocused();
    await expect(register.getByRole('cell', { name: 'Recorded additional boundary', exact: true })).toBeVisible();
    await expect(canonical).toContainText('3 boundaries defined');
    await expect(governed).toContainText('Draft · Working revision 2');
    await expect(page.getByRole('button', { name: 'Save scope draft', exact: true })).toBeDisabled();
    expect(writes).toEqual([
      { name: 'Recorded additional boundary', boundaryType: 'Logical' },
      { name: 'Recorded additional boundary', boundaryType: 'Logical' },
    ]);
    expect(pageErrors).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
  test(`components share system scope, retain unknown decisions and save errors with keyboard access at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    await installSystemCapabilityFixture(context, baseURL!);
    let graph = designFixture();
    graph.nodes[1] = { ...graph.nodes[1]!, boundaryDisposition: 'LegacyUnknown' };
    graph.nodes.push({ ...graph.nodes[1]!, id: 'api', label: 'Recorded API', boundaryDisposition: 'Undetermined' },
      { ...graph.nodes[1]!, id: 'hosted-app', label: 'Provider-hosted app', kind: 'ProviderReference',
        boundaryDisposition: 'Undetermined', boundaryRelationship: 'SystemManaged',
        source: { ...graph.nodes[1]!.source!, type: 'CspInheritedComponent', provenance: 'CSP reference', version: 'provider-version' } },
      { ...graph.nodes[1]!, id: 'external', label: 'Consumed identity service', kind: 'ProviderReference',
        boundaryDisposition: 'OutOfBoundary', boundaryRelationship: 'SharedService', boundaryRationale: 'External responsibility split' },
      { ...graph.nodes[1]!, id: 'unused', label: 'Unused excluded host', boundaryDisposition: 'OutOfBoundary',
        boundaryRationale: 'Not used by this system' });
    graph.availableNodes = ['mission-app', 'mission-api'].map(id => ({
      ...graph.nodes[1]!, id, kind: 'BoundaryDefinition', label: id, diagramRole: 'SourceRecord',
      properties: { BoundaryType: 'Logical' },
      source: { ...graph.nodes[1]!.source!, type: 'BoundaryDefinition', id },
    }));
    graph.edges.push({ ...graph.edges[0]!, id: 'consumption', sourceNodeId: 'api', targetNodeId: 'external',
      relationshipType: 'UsesService', purpose: 'Recorded identity use' });
    const originalSources = graph.nodes.map(node => node.source);
    const originalGroups = structuredClone(graph.groups);
    const attempts: SaveSystemDesignRequest[] = [];
    let reject = true;
    await context.route('**/api/dashboard/systems/system-a/design', async route => {
      if (route.request().method() === 'PUT') {
        const body: SaveSystemDesignRequest = route.request().postDataJSON();
        attempts.push(body);
        if (reject) return route.fulfill({ status: 409, json: { error: 'Recorded revision changed' } });
        expect(body.expectedRevision).toBe(2);
        graph = { ...graph, nodes: body.nodes, edges: body.edges, groups: body.groups, revision: 3 };
      }
      await route.fulfill({ json: graph });
    });
    await context.route('**/api/dashboard/systems/system-a/boundary-definitions',
      route => route.fulfill({ json: { items: [] } }));
    // Act
    await page.goto('/workspaces/organizations/org-a/systems/system-a/boundaries');
    const table = page.getByRole('table', { name: 'Components & system scope' });
    await expect(table.getByText('Unknown recorded decision: LegacyUnknown', { exact: true })).toBeVisible();
    const services = page.getByRole('region', { name: 'External systems & shared services' });
    await expect(services).toContainText('Recorded service consumption');
    await expect(services.getByText('Unused excluded host', { exact: true })).toHaveCount(0);
    await expect(table.getByRole('row', { name: /Unused excluded host/ })).toContainText('Excluded component; use not recorded');
    for (const label of ['Mission records', 'Recorded API', 'Provider-hosted app']) {
      const trigger = table.getByRole('button', { name: `Open ${label}`, exact: true });
      await trigger.focus();
      await page.keyboard.press('Enter');
      const dialog = page.getByRole('dialog', { name: 'Edit inventory component', exact: true });
      const decision = dialog.getByLabel('Does this component belong to this system?');
      if (label === 'Mission records') await expect(decision).toHaveValue('LegacyUnknown');
      await decision.selectOption('InBoundary');
      await dialog.getByLabel('Who operates or manages this component?').fill('Recorded system operator');
      await dialog.getByLabel('Inclusion / exclusion rationale', { exact: true }).fill('Same recorded mission system scope');
      await dialog.getByText('Advanced scope details', { exact: true }).click();
      await expect(dialog.getByLabel('Named boundary scope')).toHaveValue('');
      await expect(dialog.getByLabel('Named boundary scope').getByRole('option')).toHaveCount(3);
      await dialog.getByText('Advanced scope details', { exact: true }).click();
      await dialog.getByRole('button', { name: 'Apply to draft', exact: true }).click();
      await expect(trigger).toBeFocused();
    }
    const save = page.getByRole('button', { name: 'Save scope draft', exact: true });
    await save.click();
    await page.getByLabel('Reason for change').fill('Review shared app and API scope');
    await page.getByRole('button', { name: 'Confirm save', exact: true }).click();
    await expect(page.getByRole('alert')).toContainText('Your local edits are retained');
    await expect(page.getByLabel('Reason for change')).toHaveValue('Review shared app and API scope');
    await expect(page.getByText('Scope draft saved. Reviewed baseline unchanged.', { exact: true })).toHaveCount(0);
    reject = false;
    await page.getByRole('button', { name: 'Confirm save', exact: true }).click();
    await expect(page.getByText('Scope draft saved. Reviewed baseline unchanged.', { exact: true })).toBeVisible();
    // Assert
    expect(attempts).toHaveLength(2);
    expect(attempts[1]!.nodes.map(node => node.source)).toEqual(originalSources);
    expect(attempts[1]!.groups).toEqual(originalGroups);
    for (const id of ['storage', 'api', 'hosted-app']) {
      expect(attempts[1]!.nodes.find(node => node.id === id)).toMatchObject({
        boundaryDisposition: 'InBoundary', boundaryRationale: 'Same recorded mission system scope',
        deploymentOwner: 'Recorded system operator',
      });
      expect(attempts[1]!.nodes.find(node => node.id === id)?.boundaryDefinitionId).toBeUndefined();
    }
    expect(graph.approvedRevision).toBe(1);
    await page.reload();
    await expect(table.getByText('Included in this system', { exact: true })).toHaveCount(3);
    await expect(save).toBeDisabled();
    const trigger = table.getByRole('button', { name: 'Open Mission records', exact: true });
    await trigger.focus();
    await page.keyboard.press('Enter');
    const dialog = page.getByRole('dialog', { name: 'Edit inventory component', exact: true });
    await dialog.getByText('Advanced scope details', { exact: true }).click();
    await page.keyboard.press('Escape');
    await expect(dialog).not.toBeVisible();
    await expect(trigger).toBeFocused();
    await page.addScriptTag({ content: axe.source });
    const violations = await page.evaluate(async () => (await (window as Window & { axe: typeof import('axe-core') }).axe.run(
      document.querySelector('main')!, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa'] } })).violations
      .map(item => ({ id: item.id, impact: item.impact, nodes: item.nodes.map(node => ({
        target: node.target, html: node.html, failureSummary: node.failureSummary,
      })) })));
    expect(violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
  test(`inventory boundary top draft save retains scope owner source and review at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    await installSystemCapabilityFixture(context, baseURL!);
    let graph = designFixture();
    graph.nodes[1]!.properties = { ComponentType: 'Thing', SubType: 'Application service' };
    const scope = { ...graph.nodes[1]!, id: 'scope-node', label: 'Recorded boundary', kind: 'BoundaryDefinition',
      diagramRole: 'SourceRecord' as const, source: { ...graph.nodes[1]!.source!, type: 'BoundaryDefinition', id: 'boundary-a' } };
    graph.availableNodes = [scope];
    graph.nodes.push({ ...graph.nodes[1]!, id: 'provider-a', label: 'Recorded provider service', kind: 'ProviderReference',
      boundaryRelationship: 'SharedService', boundaryDisposition: 'OutOfBoundary',
      source: { ...graph.nodes[1]!.source!, provenance: 'CSP reference', type: 'BoundaryComponentAssignment' } });
    const writes: SaveSystemDesignRequest[] = [];
    await context.route('**/api/dashboard/systems/system-a/design', async route => {
      if (route.request().method() === 'PUT') {
        const body: SaveSystemDesignRequest = route.request().postDataJSON();
        expect(body.expectedRevision).toBe(graph.revision);
        writes.push(body);
        graph = { ...graph, nodes: body.nodes, edges: body.edges, groups: body.groups, revision: graph.revision + 1 };
      }
      await route.fulfill({ json: graph });
    });
    await context.route('**/api/dashboard/systems/system-a/boundary-definitions',
      route => route.fulfill({ json: { items: [{ id: 'boundary-a', name: 'Recorded boundary', boundaryType: 'Logical',
        isPrimary: true, registeredSystemId: 'system-a', componentCount: 0, resourceCount: 0 }] } }));
    // Act
    await page.goto('/workspaces/organizations/org-a/systems/system-a/boundaries');
    const table = page.getByRole('table', { name: 'Components & system scope' });
    await expect(table).toBeVisible();
    const definitions = page.getByRole('region', { name: 'Recorded boundary inventory' });
    await expect(definitions).toBeVisible();
    expect(await definitions.evaluate(element => element.closest('details') === null)).toBe(true);
    expect((await definitions.boundingBox())!.y).toBeLessThan((await table.boundingBox())!.y);
    const intro = page.getByRole('heading', { name: 'What belongs to this system?', exact: true });
    expect(await intro.evaluate(element =>
      element.closest('header')?.nextElementSibling?.querySelector('h2')?.textContent))
      .toBe('Recorded boundary definitions');
    await expect(page.getByRole('region', { name: 'Recorded system scope' })).toHaveCount(0);
    const save = page.getByRole('button', { name: 'Save scope draft', exact: true });
    expect((await save.boundingBox())!.y).toBeLessThan((await table.boundingBox())!.y);
    await expect(table.getByText('Outside this system', { exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Open Mission records', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Edit inventory component', exact: true });
    await dialog.getByLabel('Environment', { exact: true }).fill('Production');
    await dialog.getByLabel('Who operates or manages this component?').fill('Recorded application team');
    await dialog.getByRole('combobox', { name: 'Does this component belong to this system?' }).selectOption('InBoundary');
    await dialog.getByText('Advanced scope details', { exact: true }).click();
    await dialog.getByRole('combobox', { name: /^Named boundary scope/ }).selectOption('boundary-a');
    await dialog.getByLabel('Inclusion / exclusion rationale', { exact: true }).fill('Recorded workload within mission scope');
    await dialog.getByRole('button', { name: 'Apply to draft', exact: true }).click();
    expect(writes).toEqual([]);
    await save.click();
    await page.getByLabel('Reason for change').fill('Document recorded component responsibility and scope');
    await page.getByRole('button', { name: 'Confirm save', exact: true }).click();
    await expect(page.getByText('Scope draft saved. Reviewed baseline unchanged.', { exact: true })).toBeVisible();
    // Assert
    expect(writes).toHaveLength(1);
    expect(writes[0]!.nodes.find(n => n.id === 'storage')).toMatchObject({
      environment: 'Production', deploymentOwner: 'Recorded application team', boundaryDisposition: 'InBoundary',
      boundaryRationale: 'Recorded workload within mission scope', source: designFixture().nodes[1]!.source,
      boundaryDefinitionId: 'boundary-a',
    });
    expect(writes[0]!.nodes.find(n => n.id === 'scope-node')).toEqual(scope);
    expect(graph.governanceStatus).toBe('Draft'); expect(graph.approvedRevision).toBe(1);
    await page.reload();
    await expect(table.getByText('Recorded application team', { exact: true })).toBeVisible();
    await expect(table.getByText('Production', { exact: true })).toBeVisible();
    await expect(table.getByText('Outside this system', { exact: true })).toBeVisible();
    await expect(save).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Open boundary Recorded boundary', exact: true })).toBeVisible();
    await expect(page.getByText('Manage boundary definitions and source placements', { exact: true })).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    expect(writes).toHaveLength(1);
  });
}
