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
  test(`Context keeps externally owned membership and its interfaces visible at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    const graph = designFixture();
    graph.nodes[1] = { ...graph.nodes[1]!, boundaryRelationship: 'SeparatelyAuthorized', boundaryDisposition: 'Undetermined' };
    graph.edges.push({ ...graph.edges[0]!, id: 'membership', sourceNodeId: 'storage', targetNodeId: 'system',
      relationshipType: 'Membership', source: { ...graph.nodes[0]!.source!, type: 'RecordedRelationship' } });
    const { writes } = await installDesign(context, baseURL!, graph);
    // Act
    await page.goto(`${root}/profile/SystemDesign?designView=Context`);
    const canvas = page.getByTestId('design-canvas');
    await expect(canvas).toHaveAttribute('aria-busy', 'false');
    // Assert
    await expect(canvas.locator('[data-id="storage"]')).toHaveClass(/sd-boundary-outofboundary/);
    await expect(canvas.locator('[data-id="storage"]')).toContainText('External');
    await expect(canvas.locator('.react-flow__edge')).toHaveCount(2);
    expect(writes).toEqual([]);
  });

  test(`SACA deployment shows recorded zones cloud scope and TCCM performer with governed capture at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    const graph = designFixture();
    const template = graph.nodes[1]!;
    graph.nodes.push(
      { ...template, id: 'local', kind: 'ExternalSystem', label: 'Recorded mission network', sacaZone: 'OnPremisesDisn', boundaryDisposition: 'OutOfBoundary' },
      { ...template, id: 'bcap', label: 'Recorded organization BCAP', sacaZone: 'SecureCloudAccessBoundary', sacaRole: 'BCAP',
        deploymentOwner: 'Recorded boundary team', deploymentSecurityFunctions: 'Recorded filtering and inspection' },
      { ...template, id: 'environment', kind: 'Environment', label: 'Recorded Government scope',
        properties: { cloud: 'AzureUSGovernment', subscriptionId: 'sub-a', directoryTenantId: 'directory-a' } },
      { ...template, id: 'vdss', label: 'Recorded provider VDSS', kind: 'ProviderReference', sacaRole: 'VDSS', sacaZone: 'AzureCloud',
        deploymentScopeNodeId: 'environment', boundaryRelationship: 'SharedService', boundaryDisposition: 'OutOfBoundary' },
      { ...template, id: 'workload', label: 'Recorded workload', sacaRole: 'Workload', deploymentScopeNodeId: 'environment',
        properties: { resourceId: '/subscriptions/sub-a/resourceGroups/rg-a/providers/Microsoft.App/containerApps/workload' } },
      { ...template, id: 'tccm', label: 'Recorded TCCM performer', kind: 'ActorGroup', sacaRole: 'TCCM' });
    graph.edges = [
      { ...graph.edges[0]!, id: 'edge-access', sourceNodeId: 'local', targetNodeId: 'bcap' },
      { ...graph.edges[0]!, id: 'edge-inspect', sourceNodeId: 'bcap', targetNodeId: 'workload' },
      { ...graph.edges[0]!, id: 'hosting', sourceNodeId: 'environment', targetNodeId: 'vdss', relationshipType: 'HostingAssociation',
        source: { ...template.source!, type: 'RecordedRelationship' } },
      { ...graph.edges[0]!, id: 'credential-governance', sourceNodeId: 'tccm', targetNodeId: 'workload', relationshipType: 'GovernanceInteraction' }];
    const { writes } = await installDesign(context, baseURL!, graph);
    // Act
    await page.goto(`${root}/profile/SystemDesign?designView=AzureDeployment`);
    const canvas = page.getByTestId('design-canvas');
    await expect(canvas).toHaveAttribute('aria-busy', 'false');
    // Assert
    for (const text of ['01 On-premises / DISN', '02 Secure cloud access boundary', '03 Azure cloud',
      'AzureUSGovernment', 'directory-a', '05 TCCM business role (not an appliance)'])
      await expect(canvas).toContainText(text);
    await expect(canvas.locator('.react-flow__edge')).toHaveCount(4);
    await expect(page.getByRole('region', { name: 'SACA deployment legend' })).toContainText('VDMS, CNAP');
    // Act
    await page.getByRole('button', { name: 'Add deployment component', exact: true }).click();
    await page.getByLabel('Label', { exact: true }).fill('Recorded VDMS draft');
    await page.getByLabel('SACA deployment zone').selectOption('AzureCloud');
    await page.getByLabel('SACA / SCCA role').selectOption('VDMS');
    await page.getByRole('combobox', { name: 'Recorded deployment scope', exact: true }).selectOption('environment');
    await page.getByLabel('Deployment responsibility / owner').fill('Recorded mission service team');
    await page.getByLabel('Deployment evidence reference URL').fill('https://example.invalid/vdms');
    await page.getByLabel('Recorded deployment security functions').fill('Recorded host protection, isolation, IAM and monitoring');
    await page.getByRole('button', { name: 'Apply to draft' }).click();
    await page.getByRole('button', { name: 'Save draft', exact: true }).click();
    await page.getByLabel('Reason', { exact: true }).fill('Synthetic recorded SACA responsibilities');
    await page.getByRole('button', { name: 'Confirm save draft', exact: true }).click();
    await page.reload();
    // Assert
    await expect(canvas).toContainText('Recorded VDMS draft');
    expect(writes.find(w => w.path === endpoint)!.body.nodes).toEqual(expect.arrayContaining([expect.objectContaining({
      sacaRole: 'VDMS', deploymentScopeNodeId: 'environment', deploymentEvidenceReference: 'https://example.invalid/vdms',
    })]));
    // Act
    await page.getByRole('button', { name: 'Add TCCM performer reference', exact: true }).click();
    // Assert
    await expect(page.getByLabel('SACA / SCCA role')).toHaveValue('TCCM');
    await expect(page.getByRole('dialog')).toContainText('not Key Vault');
    await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  });

  test(`Recorded-only Network links remain visible as a nontraffic overlay at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    const graph = designFixture();
    graph.nodes.push({ ...graph.nodes[1]!, id: 'actor', kind: 'ActorGroup', label: 'Recorded users' });
    const source = { ...graph.nodes[0]!.source!, type: 'RecordedRelationship' };
    graph.edges = [{ ...graph.edges[0]!, id: 'membership', relationshipType: 'Membership', source },
      { ...graph.edges[0]!, id: 'access', relationshipType: 'Access', source, sourceNodeId: 'actor', targetNodeId: 'system' }];
    const { writes } = await installDesign(context, baseURL!, graph);
    // Act
    await page.goto(`${root}/profile/SystemDesign?designView=Network`);
    const canvas = page.getByTestId('design-canvas');
    await expect(canvas).toHaveAttribute('aria-busy', 'false');
    // Assert
    await expect(canvas.locator('.react-flow__edge')).toHaveCount(2);
    await expect(canvas).toContainText('not network traffic');
    await expect(page.getByRole('region', { name: 'Network connection status' })).toContainText('No technical network interfaces are documented');
    const paths = canvas.locator('.react-flow__edge-path');
    await expect(paths.first()).not.toHaveAttribute('marker-end', /.+/);
    // Act
    await page.getByRole('checkbox', { name: 'Show recorded associations (not network traffic)' }).uncheck();
    // Assert
    await expect(canvas.locator('.react-flow__edge')).toHaveCount(0);
    expect(writes).toEqual([]);
    // Act
    await page.getByRole('checkbox', { name: 'Show recorded associations (not network traffic)' }).check();
    // Assert
    await expect(canvas.locator('.react-flow__edge')).toHaveCount(2);
    expect(writes).toEqual([]);
  });

  test(`Network architecture separates named scope CSP and non-CSP segments and persists interfaces at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    const graph = designFixture();
    const template = graph.nodes[1]!;
    graph.nodes.push(
      { ...template, id: 'definition', kind: 'BoundaryDefinition', label: 'Recorded production scope', diagramRole: 'SourceRecord',
        source: { ...template.source!, type: 'BoundaryDefinition', id: 'scope-a' } },
      { ...template, id: 'local', kind: 'InventoryItem', label: 'Recorded non-CSP gateway', boundaryDisposition: 'InBoundary',
        boundaryDefinitionId: 'scope-a', networkRole: 'VpnGateway', networkSegment: 'Recorded LAN', networkAddress: '10.40.0.1' },
      { ...template, id: 'csp', kind: 'ProviderReference', label: 'Recorded shared CSP peer', boundaryDisposition: 'OutOfBoundary',
        boundaryRelationship: 'SharedService', networkRole: 'ExternalSystem', networkSegment: 'Recorded provider edge' },
      { ...template, id: 'hosting', kind: 'Environment', label: 'Recorded hosting reference' },
      { ...template, id: 'governance', kind: 'ActorGroup', label: 'Recorded security manager' });
    graph.edges = [
      { ...graph.edges[0]!, id: 'interface', sourceNodeId: 'local', targetNodeId: 'csp', protocolStack: 'HTTPS / TLS 1.3 / TCP / IPv4',
        standardsReference: 'https://example.invalid/standards', boundaryCrossing: 'Yes', interconnectionId: 'interface-source', agreementStatus: 'Signed' },
      { ...graph.edges[0]!, id: 'hosting-association', sourceNodeId: 'local', targetNodeId: 'hosting', relationshipType: 'HostingAssociation',
        source: { ...template.source!, type: 'RecordedRelationship' } }];
    const { writes } = await installDesign(context, baseURL!, graph);
    // Act
    await page.goto(`${root}/profile/SystemDesign?designView=Network`);
    const canvas = page.getByTestId('design-canvas');
    await expect(canvas).toHaveAttribute('aria-busy', 'false');
    // Assert
    await expect(canvas).toContainText('Authorization boundary · Recorded production scope');
    await expect(canvas).toContainText('Outside authorization boundary');
    await expect(canvas).toContainText('10.40.0.1');
    await expect(canvas.locator('[data-id="hosting"]')).toHaveCount(0);
    await expect(canvas.locator('[data-id="governance"]')).toHaveCount(0);
    await expect(canvas.locator('.react-flow__edge')).toHaveCount(1);
    await expect(page.getByRole('region', { name: 'Network architecture legend' })).toContainText('Recorded hosting reference');
    // Act
    await page.getByRole('button', { name: 'Add network component', exact: true }).click();
    await page.getByLabel('Label', { exact: true }).fill('Recorded firewall draft');
    await page.getByLabel('Network component role').selectOption('Firewall');
    await page.getByLabel('Network segment / enclave').fill('Recorded DMZ');
    await page.getByLabel('Network IP / CIDR address').fill('10.40.2.0/24');
    await page.getByLabel('Claimed hosting impact level').selectOption('IL5');
    await page.getByRole('button', { name: 'Apply to draft' }).click();
    await page.getByRole('button', { name: 'Add network interface', exact: true }).click();
    await page.getByRole('combobox', { name: 'Source element', exact: true }).selectOption('local');
    await page.getByRole('combobox', { name: 'Destination element', exact: true }).selectOption({ label: 'Recorded firewall draft' });
    await page.getByLabel('Protocol stack', { exact: true }).fill('HTTPS / TLS 1.3 / TCP / IPv4');
    await page.getByLabel('Standards profile reference URL').fill('https://example.invalid/standards');
    await page.getByLabel('Connection medium').selectOption('Private');
    await page.getByLabel('Security control references').fill('SC-7');
    await page.getByRole('button', { name: 'Apply to draft' }).click();
    await page.getByRole('button', { name: 'Save draft', exact: true }).click();
    await page.getByLabel('Reason', { exact: true }).fill('Synthetic recorded network updates');
    await page.getByRole('button', { name: 'Confirm save draft', exact: true }).click();
    await page.reload();
    // Assert
    await expect(canvas).toContainText('Recorded firewall draft');
    expect(writes.find(w => w.path === endpoint)!.body.edges).toEqual(expect.arrayContaining([
      expect.objectContaining({ connectionMedium: 'Private', securityControlReferences: 'SC-7' })]));
  });

  test(`DFD distinguishes CSP and non-CSP producers functions stores and captures lifecycle at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    const graph = designFixture();
    const template = graph.nodes[1]!;
    graph.nodes.push(
      { ...template, id: 'function', source: null, kind: 'DataFlowElement', label: 'Recorded processing',
        dataFlowRole: 'Function', functionDescription: 'Transform recorded inputs', boundaryDisposition: 'InBoundary' },
      { ...template, id: 'store', source: null, kind: 'DataFlowElement', label: 'Recorded mission repository',
        dataFlowRole: 'DataStore', dataRetention: 'Recorded retention', disposalMethod: 'Recorded disposal', boundaryDisposition: 'InBoundary' },
      { ...template, id: 'csp-producer', kind: 'ProviderReference', label: 'Recorded CSP producer',
        dataFlowRole: 'ExternalEntity', boundaryDisposition: 'OutOfBoundary' },
      { ...template, id: 'local-consumer', kind: 'ExternalSystem', label: 'Recorded non-CSP consumer', boundaryDisposition: 'OutOfBoundary' },
      { ...template, id: 'data-reference', kind: 'InformationType', diagramRole: 'SourceRecord', label: 'Recorded data type',
        source: { ...template.source!, id: 'data-ref', type: 'DataTypeEntry' },
        properties: { DataTypeName: 'Recorded data type', SensitivityClassification: 'CUI' } });
    graph.edges = [
      { ...graph.edges[0]!, id: 'receive', sourceNodeId: 'csp-producer', targetNodeId: 'function', lifecycleStage: 'Receive', direction: 'Inbound' },
      { ...graph.edges[0]!, id: 'store-flow', sourceNodeId: 'function', targetNodeId: 'store', lifecycleStage: 'Store' },
      { ...graph.edges[0]!, id: 'distribute', sourceNodeId: 'function', targetNodeId: 'local-consumer', lifecycleStage: 'Distribute' },
      { ...graph.edges[0]!, id: 'hosting-only', sourceNodeId: 'csp-producer', targetNodeId: 'store', relationshipType: 'HostingAssociation',
        source: { ...template.source!, type: 'RecordedRelationship' } }];
    const { writes } = await installDesign(context, baseURL!, graph);
    // Act
    await page.goto(`${root}/profile/SystemDesign?designView=DataFlows`);
    const canvas = page.getByTestId('design-canvas');
    await expect(canvas).toHaveAttribute('aria-busy', 'false');
    // Assert
    await expect(canvas.locator('[data-id="store"]')).toHaveClass(/sd-dfd-datastore/);
    await expect(canvas.locator('[data-id="function"]')).toHaveClass(/sd-dfd-function/);
    await expect(canvas.locator('[data-id="csp-producer"]')).toHaveClass(/sd-dfd-externalentity/);
    await expect(canvas.locator('.react-flow__edge')).toHaveCount(3);
    await expect(page.getByRole('region', { name: 'Data flow legend' })).toBeVisible();
    // Act
    await page.getByRole('button', { name: 'Add function / data store', exact: true }).click();
    await page.getByLabel('Label', { exact: true }).fill('Recorded disposal function');
    await page.getByLabel('System function / transformation description').fill('Destroy recorded data under the retained policy');
    await page.getByRole('button', { name: 'Apply to draft' }).click();
    await page.getByRole('button', { name: 'Add data exchange', exact: true }).click();
    await page.getByRole('combobox', { name: 'Source element', exact: true }).selectOption('store');
    await page.getByRole('combobox', { name: 'Destination element', exact: true }).selectOption({ label: 'Recorded disposal function' });
    await page.getByRole('combobox', { name: 'Recorded information type', exact: true }).selectOption('data-ref');
    await page.getByLabel('Data lifecycle stage').selectOption('Destroy');
    await page.getByLabel('Purpose', { exact: true }).fill('Apply recorded disposal policy');
    await page.getByRole('button', { name: 'Apply to draft' }).click();
    await page.getByRole('button', { name: 'Save draft', exact: true }).click();
    await page.getByLabel('Reason', { exact: true }).fill('Synthetic DFD lifecycle capture');
    await page.getByRole('button', { name: 'Confirm save draft', exact: true }).click();
    await page.reload();
    // Assert
    await expect(canvas).toContainText('Lifecycle: Destroy');
    const saved = writes.find(w => w.path === endpoint)!;
    expect(saved.body.edges).toEqual(expect.arrayContaining([expect.objectContaining({ informationTypeId: 'data-ref', lifecycleStage: 'Destroy' })]));
  });

  test(`Logical architecture captures and reloads a governed construct and typed relationship at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    const graph = designFixture();
    const { writes } = await installDesign(context, baseURL!, graph);
    // Act
    await page.goto(`${root}/profile/SystemDesign?designView=Logical`);
    await expect(page.getByTestId('design-canvas')).toHaveAttribute('aria-busy', 'false');
    await page.getByRole('button', { name: 'Add logical construct', exact: true }).click();
    await page.getByLabel('Label', { exact: true }).fill('Recorded mission goal');
    await page.getByLabel('Desired effects').fill('Recorded mission effect');
    await page.getByRole('button', { name: 'Apply to draft' }).click();
    await page.getByRole('button', { name: 'Add logical relationship', exact: true }).click();
    await page.getByRole('combobox', { name: 'Source element', exact: true }).selectOption('system');
    const destination = await page.getByRole('combobox', { name: 'Destination element', exact: true }).locator('option').allTextContents();
    expect(destination).toContain('Recorded mission goal');
    await page.getByRole('combobox', { name: 'Destination element', exact: true }).selectOption({ label: 'Recorded mission goal' });
    await page.getByLabel('Purpose', { exact: true }).fill('Supports the recorded mission effect');
    await page.getByRole('button', { name: 'Apply to draft' }).click();
    await page.getByRole('button', { name: 'Save draft', exact: true }).click();
    await page.getByLabel('Reason', { exact: true }).fill('Recorded logical relationships reviewed in synthetic test');
    await page.getByRole('button', { name: 'Confirm save draft', exact: true }).click();
    await page.reload();
    // Assert
    await expect(page.getByTestId('design-canvas')).toContainText('Recorded mission goal');
    await expect(page.getByTestId('design-canvas')).toContainText('Supports');
    expect(writes.filter(write => write.path === endpoint)).toHaveLength(1);
  });

  test(`DoD ABD keeps named CSP/non-CSP scope and separate authorizations distinct at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    const graph = designFixture();
    const template = graph.nodes[1]!;
    const source = template.source!;
    graph.nodes = [
      graph.nodes[0]!,
      { ...template, id: 'scope-a', kind: 'BoundaryDefinition', label: 'Mission production', diagramRole: 'SourceRecord',
        source: { ...source, type: 'BoundaryDefinition', id: 'boundary-a' } },
      { ...template, id: 'scope-b', kind: 'BoundaryDefinition', label: 'Mission test scope', diagramRole: 'SourceRecord',
        source: { ...source, type: 'BoundaryDefinition', id: 'boundary-b' } },
      { ...template, id: 'local', label: 'Organization-managed server', kind: 'InventoryItem', boundaryDisposition: 'InBoundary',
        boundaryDefinitionId: 'boundary-a', securityResponsibility: 'Recorded operations team', boundaryRationale: 'Explicit local workload resource',
        properties: { Type: 'Hardware', HardwareFunction: 'Server' } },
      { ...template, id: 'csp', label: 'Recorded CSP workload resource', kind: 'ProviderReference', boundaryDisposition: 'InBoundary',
        boundaryDefinitionId: 'boundary-a', securityResponsibility: 'Recorded system team', boundaryRationale: 'Reviewed workload-managed selection' },
      { ...template, id: 'segment', label: 'Recorded internal network segment', kind: 'DesignComponent', boundaryDisposition: 'InBoundary',
        boundaryDefinitionId: 'boundary-b', properties: { componentType: 'Network segment' } },
      { ...template, id: 'peer', label: 'Separate authorization peer', kind: 'ExternalSystem', boundaryDisposition: 'OutOfBoundary',
        boundaryRelationship: 'SeparatelyAuthorized', externalAuthorizationReference: 'https://example.invalid/decision' },
      { ...template, id: 'authority', label: 'Recorded AO contact', kind: 'ActorGroup', boundaryDisposition: 'InBoundary' },
      { ...template, id: 'decision', label: 'Recorded ATO · Expired', kind: 'AuthorizationScope', diagramRole: 'SourceRecord',
        properties: { currency: 'Expired', componentCoverage: 'Not verified' } },
    ];
    graph.edges = [{ ...graph.edges[0]!, id: 'internal', sourceNodeId: 'local', targetNodeId: 'segment' },
      { ...graph.edges[0]!, id: 'crossing', sourceNodeId: 'csp', targetNodeId: 'peer',
        interconnectionId: 'recorded-interconnection', agreementStatus: 'Signed', relationshipType: 'Interconnection' }];
    const { writes } = await installDesign(context, baseURL!, graph);
    // Act
    await page.goto(`${root}/profile/SystemDesign?designView=Boundary`);
    const canvas = page.getByTestId('design-canvas');
    await expect(canvas).toHaveAttribute('aria-busy', 'false');
    // Assert
    for (const name of ['Authorization boundary · Mission production', 'Authorization boundary · Mission test scope',
      'Outside authorization boundary', 'External actors / governance (not components)'])
      await expect(canvas.locator('.sd-graph-group').filter({ hasText: name })).toHaveCount(1);
    await expect(canvas.locator('[data-id="scope-a"]')).toHaveCount(0);
    await expect(canvas.locator('[data-id="decision"]')).toHaveCount(0);
    await expect(page.getByRole('region', { name: 'Boundary scope and authorization' })).toContainText('Recorded ATO · Expired');
    await expect(page.getByRole('list', { name: 'Authorization boundary legend' })).toBeVisible();
    await expect(canvas).toContainText('CUI');
    await expect(canvas).toContainText('Signed');
    await page.getByRole('button', { name: 'Open Organization-managed server', exact: true }).click();
    await page.getByRole('button', { name: 'Edit selected record', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Edit design element' });
    await expect(dialog.getByLabel('Named boundary scope')).toHaveValue('boundary-a');
    await dialog.getByLabel('Security responsibility').fill('Explicitly reviewed local responsibility');
    await dialog.getByLabel('Scope inclusion / exclusion rationale').fill('Explicit boundary scope rationale');
    await dialog.getByRole('button', { name: 'Apply to draft', exact: true }).click();
    expect(writes).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await canvas.screenshot({ path: info.outputPath(`abd-${width}.png`) });
  });
  test(`ATO context keeps CSP and non-CSP interfaces centered and captures missing context at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    const graph = designFixture();
    const template = graph.nodes[1]!;
    graph.nodes = [
      graph.nodes[0]!,
      { ...template, id: 'app', label: 'Internal workload', kind: 'Application', boundaryDisposition: 'InBoundary' },
      { ...template, id: 'csp', label: 'Recorded CSP support', kind: 'ProviderReference' },
      { ...template, id: 'local', label: 'Organization-managed operational peer', kind: 'ExternalSystem', boundaryDisposition: 'OutOfBoundary' },
      { ...template, id: 'board', label: 'Recorded security review board', kind: 'DesignComponent',
        properties: { contextEntityClass: 'Performer', contextEntityCategory: 'SecurityCompliance', contextRole: 'Governance review' } },
      { ...template, id: 'policy', label: 'Recorded governing standard', kind: 'PolicyReference', diagramRole: 'SourceRecord',
        properties: { rationale: 'Recorded policy constraint', retention: 'Retained' } },
    ];
    const edge = graph.edges[0]!;
    graph.edges = [
      { ...edge, id: 'internal', sourceNodeId: 'system', targetNodeId: 'app' },
      { ...edge, id: 'csp-call', sourceNodeId: 'app', targetNodeId: 'csp', relationshipType: 'ServiceFlow', purpose: 'Recorded service call' },
      { ...edge, id: 'local-feed', sourceNodeId: 'local', targetNodeId: 'app', relationshipType: 'ResourceFlow', purpose: 'Recorded operational resource flow' },
      { ...edge, id: 'governance', sourceNodeId: 'board', targetNodeId: 'system', relationshipType: 'GovernanceInteraction',
        purpose: 'Recorded readiness review', port: null, protocol: null, encryptionState: null, service: null, protection: null },
    ];
    const { writes } = await installDesign(context, baseURL!, graph);
    // Act
    await page.goto(`${root}/profile/SystemDesign`);
    const canvas = page.getByTestId('design-canvas');
    await expect(canvas).toHaveAttribute('aria-busy', 'false');
    // Assert
    await expect(canvas.locator('[data-id="app"]')).toHaveCount(0);
    await expect(canvas.locator('[data-id="internal"]')).toHaveCount(0);
    await expect(canvas.locator('[data-id="system"]')).toHaveClass(/sd-context-center/);
    await expect(canvas.locator('[data-id="csp"]')).toHaveCount(1);
    await expect(canvas.locator('[data-id="local"]')).toHaveCount(1);
    await expect(canvas.locator('[data-id="board"]')).toHaveCount(1);
    await expect(canvas.locator('.react-flow__edge')).toHaveCount(3);
    await expect(page.getByRole('region', { name: 'Context constraints' })).toContainText('Recorded governing standard');
    const center = (await canvas.locator('[data-id="system"]').boundingBox())!;
    const local = (await canvas.locator('[data-id="local"]').boundingBox())!;
    const provider = (await canvas.locator('[data-id="csp"]').boundingBox())!;
    expect(local.x).toBeLessThan(center.x);
    expect(provider.x).toBeGreaterThan(center.x);
    await page.getByRole('button', { name: 'Add context entity', exact: true }).click();
    const entity = page.getByRole('dialog', { name: 'Edit design element' });
    await entity.getByLabel('Label', { exact: true }).fill('Recorded mission authority');
    await entity.getByLabel('Context entity class').selectOption('Performer');
    await entity.getByLabel('Context category').selectOption('SecurityCompliance');
    await entity.getByLabel('Context role').fill('Mission governance');
    await entity.getByLabel('Organization / authority').fill('Recorded organization');
    await entity.getByRole('button', { name: 'Apply to draft', exact: true }).click();
    await page.getByRole('button', { name: 'Add constraint reference', exact: true }).click();
    const constraint = page.getByRole('dialog', { name: 'Edit design element' });
    await constraint.getByLabel('Label', { exact: true }).fill('Additional recorded constraint');
    await constraint.getByLabel('Reference type').selectOption('Standard');
    await constraint.getByLabel('Source reference URL', { exact: true }).fill('https://example.invalid/recorded-standard');
    await constraint.getByLabel('Applicability rationale').fill('Explicit system-specific reference');
    await constraint.getByRole('button', { name: 'Apply to draft', exact: true }).click();
    expect(writes).toEqual([]);
    await expect(page.getByRole('region', { name: 'Context constraints' })).toContainText('Additional recorded constraint');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await canvas.screenshot({ path: info.outputPath(`ato-context-${width}.png`) });
  });
  test(`six detailed design views preserve source meaning at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    const graph = designFixture();
    const template = graph.nodes[1]!;
    graph.nodes = [
      { ...graph.nodes[0]!, id: 'system', label: 'Synthetic Mission System' },
      { ...template, id: 'users', label: 'Mission users', kind: 'ActorGroup', boundaryDisposition: 'OutOfBoundary',
        properties: { AccessMethod: 'CAC/PIV + MFA' } },
      { ...template, id: 'app', label: 'Web application', kind: 'Application', boundaryDisposition: 'InBoundary',
        properties: { SubType: 'Container App', AzureResourceId: '/subscriptions/sub-a/resourceGroups/mission/providers/Microsoft.App/containerApps/web' } },
      { ...template, id: 'api', label: 'MCP API', kind: 'Application', boundaryDisposition: 'InBoundary', properties: { SubType: 'Container App' } },
      { ...template, id: 'database', label: 'Mission records', kind: 'Database', boundaryDisposition: 'InBoundary', properties: { DataSensitivityLevel: 'CUI' } },
      { ...template, id: 'provider', label: 'Recorded provider service', kind: 'ProviderReference', boundaryDisposition: 'Undetermined',
        properties: { ComponentType: 'Provider service' } },
      { ...template, id: 'environment', label: 'Attached mission scope', kind: 'Environment', properties: { subscriptionId: 'sub-a', resourceGroup: 'mission' } },
      { ...template, id: 'data', label: 'Mission information type', kind: 'InformationType', diagramRole: 'SourceRecord' },
    ];
    const flow = graph.edges[0]!;
    graph.edges = [
      { ...flow, id: 'access', sourceNodeId: 'users', targetNodeId: 'app', protocol: 'HTTPS', protection: 'TLS' },
      { ...flow, id: 'api-flow', sourceNodeId: 'app', targetNodeId: 'api', protocol: 'HTTPS', protection: 'mTLS' },
      { ...flow, id: 'records-flow', sourceNodeId: 'api', targetNodeId: 'database', protocol: 'TCP', port: '1433', protection: 'TLS 1.3' },
      { ...flow, id: 'provider-flow', sourceNodeId: 'api', targetNodeId: 'provider', protocol: 'HTTPS' },
      { ...flow, id: 'scope', sourceNodeId: 'app', targetNodeId: 'environment', relationshipType: 'Containment',
        source: { ...template.source!, type: 'RecordedRelationship' }, port: null, protocol: null, protection: null },
    ];
    const { writes } = await installDesign(context, baseURL!, graph);
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    // Act
    await page.goto(`${root}/profile/SystemDesign?designView=Context`);
    const selector = page.getByRole('group', { name: 'Diagram view' });
    const canvas = page.getByTestId('design-canvas');
    for (const [label, value] of [
      ['System context', 'Context'], ['Authorization boundary', 'Boundary'], ['Logical architecture', 'Logical'],
      ['Data flows', 'DataFlows'], ['Network architecture', 'Network'], ['Azure deployment', 'AzureDeployment'],
    ]) {
      await selector.getByRole('button', { name: label, exact: true }).click();
      // Assert
      await expect(page).toHaveURL(new RegExp(`designView=${value}`));
      await expect(selector.getByRole('button', { name: label, exact: true })).toHaveAttribute('aria-pressed', 'true');
      await expect(canvas).toHaveAttribute('aria-busy', 'false');
      await expect(canvas.locator('[data-id="data"]')).toHaveCount(value === 'Logical' ? 1 : 0);
      if (value === 'Boundary') {
        for (const group of ['Authorization boundary · In boundary', 'Boundary undetermined',
          'External actors / governance (not components)', 'Hosting scope references (not authorized components)'])
          await expect(canvas.locator('.sd-graph-group').filter({ hasText: group })).toHaveCount(1);
        await expect(canvas).toContainText('Container App');
        await expect(canvas).toContainText('CAC/PIV + MFA');
        await expect(canvas).toContainText('mTLS');
        await expect(canvas).toContainText('TLS 1.3');
        await expect(canvas.locator('[data-id="provider"]')).toHaveClass(/sd-boundary-undetermined/);
        await expect(canvas.locator('.react-flow__edge')).toHaveCount(5);
        expect((await canvas.locator('[data-id="app"]').boundingBox())!.width).toBeGreaterThanOrEqual(180);
      }
      if (value === 'Logical') {
        await expect(canvas.locator('[data-id="users"]')).toHaveCount(1);
        await expect(canvas.locator('[data-id="environment"]')).toHaveCount(1);
        await expect(page.getByRole('region', { name: 'Logical architecture legend' })).toBeVisible();
      }
      if (value === 'DataFlows') await expect(canvas.locator('[data-id="scope"]')).toHaveCount(0);
      if (value === 'AzureDeployment') {
        await expect(canvas.locator('[data-id="app"]')).toHaveCount(1);
        await expect(canvas.locator('[data-id="environment"]')).toHaveCount(1);
        await expect(canvas.locator('[data-id="database"]')).toHaveCount(0);
      }
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: info.outputPath(`detailed-${value}-${width}.png`), fullPage: true });
      await canvas.screenshot({ path: info.outputPath(`canvas-${value}-${width}.png`) });
    }
    expect(writes).toEqual([]);
    expect(errors).toEqual([]);
  });
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
    await expect(canvas.locator('.react-flow__edge')).toHaveCount(2);
    await expect(canvas.locator('[data-id="storage"]')).toHaveCount(0);
    await expect(canvas.locator('[data-id="membership"]')).toHaveCount(0);
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
    await expect(canvas.locator('.react-flow__edge')).toHaveCount(2);
    await page.reload();
    await canvas.scrollIntoViewIfNeeded();
    await page.getByRole('button', { name: 'Fit to view', exact: true }).click();
    await expect(canvas).toHaveAttribute('data-relationship-count', '2');
    await expect(canvas).toHaveAttribute('data-measured-node-count', '4');
    await expect(canvas).toHaveAttribute('aria-busy', 'false');
    await expect(canvas.locator('.react-flow__edge')).toHaveCount(2);
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
    await expect(tabs.getByRole('link')).toHaveText(['Mission', 'Users', 'Environment & hosting', 'Data', 'Components & system scope', 'Ports & interconnections', 'System design']);
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
