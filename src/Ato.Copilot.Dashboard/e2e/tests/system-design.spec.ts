import { expect, test, type BrowserContext } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';
import { designFixture, designLayoutFixture } from '../../src/__tests__/system-design/fixtures';
import type { DesignLayout, SystemDesignGraph } from '../../src/api/systemDesign';

const root = '/workspaces/organizations/org-a/systems/system-a';
const endpoint = '/api/dashboard/systems/system-a/design';
async function installDesign(context: BrowserContext, baseURL: string, graph = designFixture()) {
  await installWorkspaceFixture(context, baseURL);
  let saved = structuredClone(graph);
  const layouts = new Map<string, DesignLayout>();
  const writes: { path: string; body: Record<string, unknown> }[] = [];
  await context.route('**/api/dashboard/systems/system-a/documents/ssp/preview', route => route.fulfill({ json: {
    systemId: 'system-a', format: 'json', contentType: 'application/json', content: '{"system-security-plan":{"description":"Exact synthetic working design narrative"}}',
    contentHash: 'working-ssp-hash', generatedAt: '2026-09-30T12:00:00Z', sourceGaps: [], isPreview: true, sourceState: 'CurrentWorkingData',
    canGenerate: false, sourceManifest: { scope: 'WorkingProfilePreview', profiles: [], providerSources: [], narratives: [], otherSources: 'synthetic', previewOnly: true },
  } }));
  await context.route('**/api/dashboard/systems/system-a/documents/ssp/preview?source=approved', route => route.fulfill({ json: {
    systemId: 'system-a', format: 'json', contentType: 'application/json', content: '{"system-security-plan":{"description":"Exact synthetic approved design narrative"}}',
    contentHash: 'approved-ssp-hash', generatedAt: '2026-09-30T12:00:00Z', sourceGaps: [], isPreview: true, sourceState: 'ApprovedSources',
    canGenerate: true, sourceManifest: { scope: 'ApprovedSources', profiles: [], providerSources: [], narratives: [], otherSources: 'synthetic',
      design: { kind: 'SystemDesign', recordId: 'system-a', versionId: '1', contentHash: 'synthetic-approved-hash' } },
  } }));
  await context.route(`**${endpoint}{,/**}`, async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() !== 'GET') writes.push({ path, body: request.postDataJSON() });
    if (path.includes('/layout/')) {
      const view = path.split('/').at(-1)!;
      return route.fulfill({ json: layouts.get(view) ?? designLayoutFixture(view) });
    }
    if (path.endsWith('/layout')) {
      const body = request.postDataJSON();
      if (body.layout.collapsedGroups.some((id: string) => !saved.groups.some(group => group.id === id)))
        return route.fulfill({ status: 400, json: { error: 'Collapsed canonical groups must use graph group IDs.' } });
      const result = { ...body.layout, version: body.expectedVersion + 1 };
      layouts.set(result.view, result);
      return route.fulfill({ json: result });
    }
    if (path.endsWith('/approved')) return route.fulfill({ json: {
      graph, revision: 1, approvedBy: 'Synthetic Reviewer', approvedAt: '2026-09-29T12:00:00Z',
      snapshotHash: 'synthetic-approved-hash', sourceFingerprint: 'source-v0', sourcesStale: false,
    } });
    if (path.endsWith('/history')) return route.fulfill({ json: [
      { revision: 1, action: 'approve', actor: 'Synthetic Reviewer', at: '2026-09-29T12:00:00Z', reason: 'Reviewed synthetic baseline', governanceStatus: 'Approved', sourceFingerprint: 'source-v0' },
    ] });
    if (path.endsWith('/build')) {
      const body = request.postDataJSON();
      expect(body.expectedRevision).toBe(saved.revision);
      saved = { ...saved, revision: saved.revision + 1, governanceStatus: 'Draft', sourcesStale: false };
    } else if (path === endpoint && request.method() === 'PUT') {
      const body = request.postDataJSON();
      saved = { ...saved, nodes: body.nodes, edges: body.edges, groups: body.groups, revision: saved.revision + 1 };
    } else if (path.endsWith('/decision')) {
      const body = request.postDataJSON();
      const states: Record<string, string> = { accept: 'Accepted', edit_accept: 'Accepted', reject: 'Rejected', defer: 'Deferred', recover: 'Pending' };
      saved = { ...saved, revision: saved.revision + 1, proposals: saved.proposals.map(proposal => ({
        ...proposal, state: states[body.action]!, actor: 'Synthetic Owner', reason: body.reason, decidedAt: '2026-09-30T12:00:00Z',
      })) };
    } else if (path.endsWith('/review')) {
      const body = request.postDataJSON();
      const status: Record<string, SystemDesignGraph['governanceStatus']> = { submit: 'UnderReview', withdraw: 'Draft', approve: 'Approved', request_revision: 'NeedsRevision', derive_draft: 'Draft' };
      saved = { ...saved, governanceStatus: status[body.action]!, revision: saved.revision + 1 };
    } else if (path !== endpoint && !path.endsWith('/reconcile')) return route.fulfill({ status: 404, json: { error: 'Unknown synthetic design path' } });
    return route.fulfill({ json: saved });
  });
  return { writes, layouts };
}

for (const width of [1440, 390]) {
  test(`recorded architecture is assembled without source-row boxes or false data flows at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange: exact server-projected facts, not client-side inference.
    await page.setViewportSize({ width, height: 1050 });
    const graph = designFixture();
    const system = graph.nodes[0]!;
    const storage = graph.nodes[1]!;
    graph.nodes.push(
      { ...storage, id: 'actor', label: 'Mission users', kind: 'ActorGroup', diagramRole: 'Architecture',
        source: { ...storage.source!, type: 'ActorGroup', id: 'actor-a' }, properties: { AccessMethod: 'Browser' } },
      { ...storage, id: 'provider', label: 'Recorded provider service', kind: 'ProviderReference', diagramRole: 'Architecture',
        source: { ...storage.source!, type: 'ProviderHostingAssignment', id: 'provider-a' } },
      { ...storage, id: 'profile', label: 'UsersAndAccess', kind: 'ProfileSection', diagramRole: 'SourceRecord' },
    );
    const template = graph.edges[0]!;
    graph.edges = [
      { ...template, id: 'membership', sourceNodeId: storage.id, targetNodeId: system.id, relationshipType: 'Membership', source: { ...storage.source!, type: 'RecordedRelationship' },
        port: null, protocol: null, protection: null },
      { ...template, id: 'actor-access', sourceNodeId: 'actor', targetNodeId: system.id, relationshipType: 'Access',
        source: { ...graph.nodes.find(node => node.id === 'actor')!.source!, type: 'RecordedRelationship' }, port: null, protocol: null, protection: null },
      { ...template, id: 'provider-use', sourceNodeId: system.id, targetNodeId: 'provider', relationshipType: 'UsesService',
        source: { ...graph.nodes.find(node => node.id === 'provider')!.source!, type: 'RecordedRelationship' }, port: null, protocol: null, protection: null },
    ];
    const { writes } = await installDesign(context, baseURL!, graph);
    await page.goto(`${root}/profile/SystemDesign`);
    const canvas = page.getByTestId('design-canvas');
    // Assert: known associations visible on entry, original source rows still inspectable.
    await expect(canvas.locator('.react-flow__node')).toHaveCount(4);
    await expect(canvas.locator('[data-id="profile"]')).toHaveCount(0);
    await expect(canvas.locator('.react-flow__edge')).toHaveCount(3);
    await expect(canvas).toContainText('Uses provider service');
    await expect(canvas).not.toContainText('PPS not recorded');
    await page.getByRole('button', { name: 'Source records (1)', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Open UsersAndAccess', exact: true })).toBeVisible();
    // Act: one explicit server build, no source mutation or review.
    await page.getByRole('button', { name: 'Build from recorded information', exact: true }).click();
    await page.getByRole('button', { name: 'Confirm build from recorded information', exact: true }).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(writes).toHaveLength(1);
    expect(writes[0]!.path).toBe(`${endpoint}/build`);
    await page.getByRole('button', { name: 'Data flows', exact: true }).click();
    // Assert: membership/provider use is not packet flow.
    await expect(page.getByText(/No documented data flows are recorded/)).toBeVisible();
    await expect(canvas.locator('.react-flow__edge')).toHaveCount(0);
    await page.getByRole('button', { name: 'System context', exact: true }).click();
    await expect(canvas.locator('.react-flow__edge')).toHaveCount(3);
    await page.reload();
    await canvas.scrollIntoViewIfNeeded();
    await page.getByRole('button', { name: 'Fit to view', exact: true }).click();
    await expect(canvas).toHaveAttribute('data-relationship-count', '3');
    await expect(canvas).toHaveAttribute('data-measured-node-count', '4');
    await expect(canvas).toHaveAttribute('aria-busy', 'false');
    await expect(canvas.locator('.react-flow__edge')).toHaveCount(3);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`automatic-architecture-${width}.png`), fullPage: true });
    await canvas.screenshot({ path: info.outputPath(`automatic-architecture-canvas-${width}.png`) });
  });
  test(`selected element summary and styled review actions remain compact at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1050 });
    const graph = designFixture();
    graph.nodes[1]!.source!.version = 'abcdef0123456789'.repeat(4);
    graph.nodes[1]!.properties = { retainedSourceDetails: 'Detailed canonical context that belongs in the full record, not the compact summary.' };
    await installDesign(context, baseURL!, graph);
    if (width === 390) await context.addInitScript(() => localStorage.setItem('ato-dashboard-settings', JSON.stringify({ theme: 'dark' })));
    await page.goto(`${root}/profile/SystemDesign`);
    await page.getByRole('button', { name: 'Open Mission records', exact: true }).click();
    const inspector = page.getByRole('complementary', { name: 'Selected element inspector' });
    // Assert
    await expect(inspector).toContainText('System Component');
    await expect(inspector).not.toContainText(graph.nodes[1]!.source!.version);
    await expect(inspector).not.toContainText('Not recorded');
    await expect(inspector.getByRole('button', { name: 'Edit selected record' })).toHaveText('Edit');
    expect(await inspector.evaluate(element => element.scrollHeight <= element.clientHeight + 1)).toBe(true);
    expect(await inspector.evaluate(element => element.getBoundingClientRect().height)).toBeLessThan(650);
    await expect(page.getByRole('link', { name: 'Review gaps', exact: true })).toHaveCSS('text-decoration-line', 'none');
    const contribution = page.getByRole('region', { name: 'System definition contributions' }).getByRole('link').first();
    await expect(contribution).toHaveCSS('text-decoration-line', 'none');
    // Act
    const detailsButton = inspector.getByRole('button', { name: 'View full details', exact: true });
    await detailsButton.click();
    const drawer = page.getByRole('dialog', { name: 'Element details', exact: true });
    // Assert
    await expect(drawer).toContainText(graph.nodes[1]!.source!.version);
    await expect(drawer.getByRole('link', { name: 'Open source record', exact: true })).toHaveAttribute('href', `${root}/boundaries`);
    await drawer.getByText('Source properties (1)', { exact: true }).click();
    await expect(drawer).toContainText('Detailed canonical context');
    expect(await drawer.evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
    await page.screenshot({ path: info.outputPath(`element-details-${width}.png`) });
    await page.keyboard.press('Escape');
    await expect(drawer).toHaveCount(0);
    await expect(detailsButton).toBeFocused();
    await inspector.scrollIntoViewIfNeeded();
    await page.screenshot({ path: info.outputPath(`compact-inspector-${width}.png`) });
  });
  test(`canvas connect, add, rename and remove are accessible at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1050 });
    const { writes } = await installDesign(context, baseURL!);
    await page.goto(`${root}/profile/MissionAndPurpose`);
    const tabStyle = await page.getByRole('navigation', { name: 'System task views' }).getByRole('link', { name: 'Users', exact: true })
      .evaluate(element => ({ color: getComputedStyle(element).color, decoration: getComputedStyle(element).textDecorationLine }));
    await page.getByRole('navigation', { name: 'System task views' }).getByRole('link', { name: 'System design', exact: true }).click();
    const canvas = page.getByTestId('design-canvas');
    await expect(canvas.locator('[data-id="storage"]')).toBeVisible();
    await expect(canvas).toHaveAttribute('aria-busy', 'false');
    await expect(page.getByText('Arranging diagram…', { exact: true })).toHaveCount(0);
    await page.getByRole('button', { name: 'Fit to view', exact: true }).click();
    // Act: pointer and keyboard routes both propose a connection, not an approved flow.
    if (width === 1440) {
      const source = canvas.locator('[data-id="system"] .react-flow__handle-right');
      const target = canvas.locator('[data-id="storage"] .react-flow__handle-left');
      await source.dragTo(target);
    } else {
      await canvas.locator('[data-id="system"]').click();
      await page.getByRole('button', { name: 'Connect selected element', exact: true }).click();
      await page.getByRole('dialog').getByRole('combobox', { name: 'Destination element', exact: true }).selectOption('storage');
    }
    const flow = page.getByRole('dialog', { name: 'Edit data flow', exact: true });
    await expect(flow.getByRole('combobox', { name: 'Source element', exact: true })).toHaveValue('system');
    await expect(flow.getByRole('combobox', { name: 'Destination element', exact: true })).toHaveValue('storage');
    await flow.getByLabel('Purpose', { exact: true }).fill('Interactive proposed flow');
    await flow.getByRole('button', { name: 'Apply to draft', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Relationships (2)', exact: true })).toBeVisible();

    await page.getByRole('button', { name: 'Add element', exact: true }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Database', exact: true }).click();
    await page.getByRole('dialog').getByLabel('Label', { exact: true }).fill('Planned database');
    await page.getByRole('dialog').getByRole('button', { name: 'Apply to draft', exact: true }).click();
    await page.getByRole('button', { name: 'Rename selected element', exact: true }).click();
    await page.getByRole('dialog').getByLabel('Label', { exact: true }).fill('Renamed planned database');
    await page.getByRole('dialog').getByRole('button', { name: 'Apply to draft', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Open Renamed planned database', exact: true })).toBeVisible();

    await page.getByRole('button', { name: 'Open Mission records', exact: true }).click();
    await page.getByRole('button', { name: 'Remove selected element', exact: true }).click();
    const removal = page.getByRole('dialog', { name: 'Remove from working design', exact: true });
    await expect(removal).toContainText('2 connected relationship');
    await removal.getByRole('button', { name: 'Cancel', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Open Mission records', exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Remove selected element', exact: true }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Remove from draft', exact: true }).click();
    await page.getByRole('button', { name: 'Save draft', exact: true }).click();
    await page.getByRole('dialog').getByLabel('Reason', { exact: true }).fill('Explicit architecture draft corrections');
    await page.getByRole('button', { name: 'Confirm save draft', exact: true }).click();
    // Assert
    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(writes).toHaveLength(1);
    expect(writes[0]!.body.nodes).toEqual(expect.arrayContaining([expect.objectContaining({ label: 'Renamed planned database',
      kind: 'DesignComponent', boundaryDisposition: 'Undetermined' })]));
    expect(writes[0]!.body.edges).toEqual([]);
    const currentStyle = await page.getByRole('navigation', { name: 'System task views' }).getByRole('link', { name: 'Users', exact: true })
      .evaluate(element => ({ color: getComputedStyle(element).color, decoration: getComputedStyle(element).textDecorationLine }));
    expect(currentStyle).toEqual(tabStyle);
    await page.reload();
    await expect(page.getByRole('button', { name: 'Open Renamed planned database', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Open Mission records', exact: true })).toHaveCount(0);
    await page.screenshot({ path: info.outputPath(`canvas-editing-${width}.png`), fullPage: true });
  });
  test(`governed seven-tab workspace, real diagram and keyboard records at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1050 });
    const { writes } = await installDesign(context, baseURL!);
    await page.goto(`${root}/profile/SystemDesign`);
    // Act / Assert
    await expect(page.getByRole('heading', { name: 'System design', exact: true })).toBeVisible();
    const tabs = page.getByRole('navigation', { name: 'System task views' });
    await expect(tabs.getByRole('link')).toHaveText(['Mission', 'Users', 'Environment & hosting', 'Data', 'Inventory & boundary', 'Ports & interconnections', 'System design']);
    await expect(tabs.locator('[aria-current="page"]')).toHaveText('System design');
    await expect(page.getByRole('navigation', { name: 'System navigation' }).getByRole('link', { name: 'System design', exact: true })).toHaveCount(0);
    await expect(page.getByTestId('design-canvas').locator('.react-flow__node').first()).toBeVisible();
    await expect(page.getByTestId('design-canvas')).toHaveAttribute('aria-busy', 'false');
    await page.getByTestId('design-canvas').locator('[data-id="storage"]').press('Enter');
    await expect(page.getByRole('complementary', { name: 'Selected element inspector' })).toContainText('Mission records');
    await expect(page.getByRole('region', { name: 'Design governance' })).toContainText('50%');
    await expect(page.getByRole('region', { name: 'System definition contributions' })).toContainText('Azure');
    for (const name of ['Authorization boundary', 'Network architecture', 'Data flows', 'System context']) {
      await page.getByRole('button', { name, exact: true }).click();
      await expect(page.getByRole('button', { name, exact: true })).toHaveAttribute('aria-pressed', 'true');
      await expect(page.getByTestId('design-canvas').locator('.react-flow__node').first()).toBeVisible();
    }
    await page.getByRole('button', { name: 'Open Mission records' }).focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('complementary', { name: 'Selected element inspector' })).toContainText('System Component');
    await page.getByRole('button', { name: 'Edit selected record' }).click();
    await page.getByRole('dialog').getByLabel('Label', { exact: true }).fill('Reviewed mission storage');
    await page.getByRole('button', { name: 'Apply to draft' }).click();
    await tabs.getByRole('link', { name: 'Mission', exact: true }).click();
    await expect(page.getByRole('dialog', { name: 'Unsaved System design changes' })).toBeVisible();
    expect(page.url()).toContain('/profile/SystemDesign');
    await page.getByRole('button', { name: 'Keep editing' }).click();
    await expect(page.getByRole('button', { name: 'Open Reviewed mission storage' })).toBeVisible();
    await page.getByRole('button', { name: 'Save draft', exact: true }).click();
    await page.getByRole('dialog').getByLabel('Reason', { exact: true }).fill('Corrected reviewed storage label');
    await page.getByRole('button', { name: 'Confirm save draft' }).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(writes[0]!.body.expectedRevision).toBe(2);
    expect(writes[0]!.body.reason).toBe('Corrected reviewed storage label');
    await page.reload();
    await expect(page.getByRole('button', { name: 'Open Reviewed mission storage' })).toBeVisible();
    expect(page.url()).toContain('/profile/SystemDesign');
    await expect(page.getByRole('region', { name: 'SSP output readiness' })).toContainText('Unapproved');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`system-design-${width}.png`), fullPage: true });
    await page.getByTestId('design-canvas').screenshot({ path: info.outputPath(`system-design-graph-${width}.png`) });
  });
}

test('separate layout persistence, proposals, source provenance and baseline review', async ({ page, context, baseURL }) => {
  // Arrange
  const graph = designFixture();
  graph.proposals = [{
    id: 'proposal-a', kind: 'ObservedAddition', recordId: 'storage', sourceFingerprint: 'observed-v1', state: 'Pending',
    originalNode: { ...graph.nodes[1]!, reviewState: 'Observed', boundaryDisposition: 'Undetermined' }, conflictsWithHigherPrecedence: true,
  }];
  const { writes, layouts } = await installDesign(context, baseURL!, graph);
  await page.goto(`${root}/profile/SystemDesign`);
  // Act / Assert
  await expect(page.getByTestId('design-canvas').locator('.react-flow__node').first()).toBeVisible();
  await page.getByRole('button', { name: 'Automatic layout', exact: true }).click();
  await page.getByRole('button', { name: 'Save presentation', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Presentation saved separately' })).toBeVisible();
  expect(layouts.get('Context')!.positions.system).toBeDefined();
  expect(writes[0]!.path).toBe(`${endpoint}/layout`);
  await page.getByRole('button', { name: 'Authorization boundary', exact: true }).click();
  await page.getByRole('button', { name: 'Collapse Authorization boundary · In boundary', exact: true }).click();
  await page.getByRole('button', { name: 'Save presentation', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Save presentation', exact: true })).toBeDisabled();
  expect(layouts.get('Boundary')!.collapsedGroups).toEqual([]);
  expect(layouts.get('Boundary')!.visibility['presentation-group:Boundary:Authorization boundary · In boundary']).toBe(false);
  await page.reload();
  await expect(page.getByRole('button', { name: 'Expand Authorization boundary · In boundary', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Open Synthetic Mission System', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Compare to baseline' }).click();
  await expect(page.getByRole('dialog')).toContainText('synthetic-approved-hash');
  await expect(page.getByRole('table', { name: 'Baseline changes' })).toContainText('Original label');
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: 'Preview review package' }).click();
  await expect(page.getByRole('dialog')).toContainText('working-ssp-hash');
  await page.getByText('Generated narrative and structured output', { exact: true }).click();
  await expect(page.getByRole('dialog')).toContainText('Exact synthetic working design narrative');
  await expect(page.getByRole('dialog').getByRole('link', { name: 'Preview approved SSP output' }))
    .toHaveAttribute('href', `${root}/documents/preview?source=approved&contribution=SystemDesign`);
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: 'Review proposals (1)' }).click();
  await expect(page.getByRole('dialog')).toContainText('higher-precedence');
  await page.getByRole('button', { name: 'Defer', exact: true }).click();
  await page.getByLabel('Reason', { exact: true }).fill('Await source ownership verification');
  await page.getByRole('button', { name: 'Confirm proposal decision' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(writes.find(write => write.path.endsWith('/decision'))!.body).toMatchObject({ action: 'defer', reason: 'Await source ownership verification', expectedRevision: 2 });
  await page.getByRole('button', { name: 'Review proposals (1)' }).click();
  await expect(page.getByRole('dialog')).toContainText('Deferred');
  await expect(page.getByRole('button', { name: 'Recover proposal' })).toBeVisible();
});

test('denied reads, stale write conflicts and explicit reload preserve the local draft', async ({ page, context, baseURL }) => {
  // Arrange
  const { writes } = await installDesign(context, baseURL!);
  let denied = true;
  let conflict = true;
  await context.route(`**${endpoint}`, async route => {
    if (denied) return route.fulfill({ status: 403, json: { error: 'Design access denied for this workspace.' } });
    if (conflict && route.request().method() === 'PUT') {
      conflict = false;
      return route.fulfill({ status: 409, json: { error: 'Revision changed. Reconcile latest source records.' } });
    }

    return route.fallback();
  });
  await page.goto(`${root}/profile/SystemDesign`);
  // Act / Assert
  await expect(page.getByRole('alert')).toContainText('Design access denied');
  denied = false;
  await page.getByRole('button', { name: 'Retry', exact: true }).click();
  await page.getByRole('button', { name: 'Open Mission records' }).click();
  await page.getByRole('button', { name: 'Edit selected record' }).click();
  await page.getByRole('dialog').getByLabel('Label', { exact: true }).fill('Locally corrected storage');
  await page.getByRole('button', { name: 'Apply to draft' }).click();
  await page.getByRole('button', { name: 'Save draft', exact: true }).click();
  await page.getByLabel('Reason', { exact: true }).fill('Correct local storage');
  await page.getByRole('button', { name: 'Confirm save draft' }).click();
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('Revision changed');
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Open Locally corrected storage' })).toBeVisible();
  await page.getByRole('button', { name: 'Review latest server revision' }).click();
  await expect(page.getByRole('dialog', { name: 'Resolve revision conflict' })).toContainText('Locally corrected storage');
  await page.getByRole('button', { name: 'Keep local draft' }).click();
  await expect(page.getByRole('button', { name: 'Open Locally corrected storage' })).toBeVisible();
  expect(writes).toEqual([]);
});

test('existing System definition draft is protected when entering System design', async ({ page, context, baseURL }) => {
  // Arrange
  await installDesign(context, baseURL!);
  await page.goto(`${root}/profile/MissionAndPurpose`);
  const mission = page.getByPlaceholder("Describe the system's mission...");
  await mission.fill('Unsaved canonical mission must survive');
  // Act
  await page.getByRole('navigation', { name: 'System task views' }).getByRole('link', { name: 'System design', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Unsaved System definition changes' });
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: 'Keep editing' }).click();
  // Assert
  await expect(page).toHaveURL(new RegExp('/profile/MissionAndPurpose$'));
  await expect(mission).toHaveValue('Unsaved canonical mission must survive');
});

test('read-only, stale and large graph retains every structured record within render budgets', async ({ page, context, baseURL }) => {
  // Arrange
  const graph = designFixture();
  graph.nodes = Array.from({ length: 1201 }, (_, index) => ({ ...graph.nodes[1]!, id: `node-${index}`, label: `Recorded element ${index}` }));
  graph.edges = [];
  graph.sourcesStale = true;
  graph.actions = { canEdit: false, canSubmit: false, canWithdraw: false, canReview: false, canReconcile: false };
  const { writes } = await installDesign(context, baseURL!, graph);
  const start = Date.now();
  await page.goto(`${root}/profile/SystemDesign`);
  // Act / Assert
  await expect(page.getByText(/Interactive rendering is limited to/)).toBeVisible();
  expect(Date.now() - start).toBeLessThan(15000);
  await expect(page.getByRole('table', { name: 'Design records' }).locator('tbody tr')).toHaveCount(50);
  await expect(page.getByText('1–50 of 1201 elements')).toBeVisible();
  await page.getByRole('button', { name: 'Next records' }).click();
  await expect(page.getByText('51–100 of 1201 elements')).toBeVisible();
  await page.getByRole('searchbox', { name: 'Search design' }).fill('Recorded element 1200');
  await expect(page.getByRole('button', { name: 'Open Recorded element 1200' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Save draft', exact: true })).toHaveCount(0);
  await expect(page.getByText(/Canonical sources changed since this revision/)).toBeVisible();
  expect(writes).toEqual([]);
});

test('browser back is cancellable before the editor unmounts', async ({ page, context, baseURL }) => {
  // Arrange
  await installDesign(context, baseURL!);
  await page.goto(`${root}/profile/MissionAndPurpose`);
  await page.getByRole('navigation', { name: 'System task views' }).getByRole('link', { name: 'System design', exact: true }).click();
  await page.getByRole('button', { name: 'Open Mission records' }).click();
  await page.getByRole('button', { name: 'Edit selected record' }).click();
  await page.getByRole('dialog').getByLabel('Label', { exact: true }).fill('Unsaved storage');
  await page.getByRole('button', { name: 'Apply to draft' }).click();
  // Act
  await page.evaluate(() => history.back());
  // Assert
  await expect(page.getByRole('dialog', { name: 'Unsaved System design changes' })).toBeVisible();
  await page.getByRole('button', { name: 'Keep editing' }).click();
  await expect(page).toHaveURL(new RegExp('/profile/SystemDesign$'));
  await expect(page.getByRole('button', { name: 'Open Unsaved storage' })).toBeVisible();
  await page.evaluate(() => history.back());
  await page.getByRole('button', { name: 'Discard and leave' }).click();
  await expect(page).toHaveURL(new RegExp('/profile/MissionAndPurpose$'));
});

test('selected diagram view is deep-linked and survives refresh', async ({ page, context, baseURL }) => {
  // Arrange
  await installDesign(context, baseURL!);
  await context.route(`**${endpoint}/layout/Context`, route => route.fulfill({ json: {
    ...designLayoutFixture(), positions: { system: { x: 520, y: 0 }, storage: { x: 0, y: 0 } },
  } }));
  await page.goto(`${root}/profile/SystemDesign`);
  await expect(page.getByTestId('design-canvas').locator('[data-id="system"]')).toBeVisible();
  await expect(page.getByTestId('design-canvas').locator('[data-id="storage"]')).toBeVisible();
  expect((await page.getByTestId('design-canvas').locator('[data-id="system"]').boundingBox())!.x)
    .toBeLessThan((await page.getByTestId('design-canvas').locator('[data-id="storage"]').boundingBox())!.x);
  // Act
  await page.getByRole('button', { name: 'Network architecture', exact: true }).click();
  await page.reload();
  // Assert
  await expect(page.getByRole('button', { name: 'Network architecture', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await expect(page).toHaveURL(/designView=Network/);
  await page.evaluate(() => document.documentElement.classList.add('dark'));
  await expect(page.getByTestId('design-canvas').locator('.react-flow')).toHaveClass(/dark/);
  await page.evaluate(() => document.documentElement.classList.remove('dark'));
  await expect(page.getByTestId('design-canvas').locator('.react-flow')).toHaveClass(/light/);
});
