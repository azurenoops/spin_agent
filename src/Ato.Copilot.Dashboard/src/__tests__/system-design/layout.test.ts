import { describe, expect, it } from 'vitest';
import { layoutDesign, pageRecords, relationshipLabel, relationshipOrigin, architectureForView, nodeDetails, flowLabel, azureScope, boundaryGroupName, deploymentScope, deploymentGroup, GRAPH_NODE_LIMIT, GRAPH_EDGE_LIMIT } from '../../features/system-design/graphAdapter';
import { designFixture } from './fixtures';

describe('replaceable System design layout', () => {
  it.each(['SharedService', 'SeparatelyAuthorized'])('keeps %s outside the Context center despite recorded membership', relationship => {
    // Arrange
    const graph = designFixture();
    graph.nodes[1] = { ...graph.nodes[1]!, boundaryDisposition: 'Undetermined', boundaryRelationship: relationship };
    graph.edges.push({ ...graph.edges[0]!, id: 'membership', sourceNodeId: 'storage', targetNodeId: 'system',
      relationshipType: 'Membership', source: { ...graph.nodes[0]!.source!, type: 'RecordedRelationship' } });
    // Act
    const context = architectureForView(graph.nodes, graph.edges, 'Context');
    // Assert
    expect(context.nodes.map(n => n.id)).toEqual(['system', 'storage']);
    expect(context.edges.find(e => e.id === 'flow')?.targetNodeId).toBe('storage');
  });
  it('resolves Government scope only from an explicit or unique source containment, never ambiguous matching', () => {
    // Arrange
    const graph = designFixture();
    const node = { ...graph.nodes[1]!, properties: { resourceId: '/subscriptions/sub-a/resourceGroups/rg-a/providers/Microsoft.App/containerApps/app' } };
    const a = { ...node, id: 'scope-a', kind: 'Environment', properties: { cloud: 'AzureUSGovernment', subscriptionId: 'sub-a' } };
    const b = { ...a, id: 'scope-b' };
    const edge = { ...graph.edges[0]!, sourceNodeId: node.id, targetNodeId: a.id, relationshipType: 'Containment',
      source: { ...graph.nodes[0]!.source!, type: 'RecordedRelationship' } };
    // Act / Assert
    expect(deploymentScope(node, [node, a, b], [edge])).toBe(a);
    expect(deploymentGroup(node, [node, a], [edge])).toContain('AzureUSGovernment / sub-a / rg-a');
    expect(deploymentScope(node, [node, a, b], [edge, { ...edge, id: 'second', targetNodeId: b.id }])).toBeUndefined();
    expect(deploymentScope({ ...node, deploymentScopeNodeId: b.id }, [node, a, b], [edge])).toBe(b);
    expect(deploymentScope(node, [node, a], [{ ...edge, source: null }])).toBeUndefined();
    expect(deploymentGroup({ ...node, sacaRole: 'TCCM' }, [node], [])).toContain('business role (not an appliance)');
  });
  it('includes explicitly recorded SACA zones and CSP references without requiring Azure name matching', () => {
    // Arrange
    const graph = designFixture();
    graph.nodes.push({ ...graph.nodes[1]!, id: 'bcap', kind: 'DesignComponent', sacaZone: 'SecureCloudAccessBoundary', sacaRole: 'BCAP' },
      { ...graph.nodes[1]!, id: 'local', kind: 'ExternalSystem', sacaZone: 'OnPremisesDisn' },
      { ...graph.nodes[1]!, id: 'tccm', kind: 'ActorGroup', sacaRole: 'TCCM' },
      { ...graph.nodes[1]!, id: 'provider', kind: 'ProviderReference' },
      { ...graph.nodes[1]!, id: 'name-only', label: 'Azure Government Key Vault', properties: {} });
    // Act
    const azure = architectureForView(graph.nodes, graph.edges, 'AzureDeployment');
    // Assert
    expect(azure.nodes.map(n => n.id)).toEqual(['bcap', 'local', 'tccm', 'provider']);
    expect(azure.edges).toEqual([]);
  });
  it('connects source-recorded network membership service and access without fabricating traffic', () => {
    // Arrange
    const graph = designFixture();
    graph.nodes.push({ ...graph.nodes[1]!, id: 'actor', kind: 'ActorGroup' });
    const source = { ...graph.nodes[0]!.source!, type: 'RecordedRelationship' };
    graph.edges = [
      { ...graph.edges[0]!, id: 'membership', relationshipType: 'Membership', source },
      { ...graph.edges[0]!, id: 'service', relationshipType: 'UsesService', source },
      { ...graph.edges[0]!, id: 'access', relationshipType: 'Access', source, sourceNodeId: 'actor', targetNodeId: 'system' },
      { ...graph.edges[0]!, id: 'governance', relationshipType: 'GovernanceAssignment', source, sourceNodeId: 'actor' },
      { ...graph.edges[0]!, id: 'forged', relationshipType: 'Membership', source: null },
    ];
    // Act
    const network = architectureForView(graph.nodes, graph.edges, 'Network');
    // Assert
    expect(network.nodes.map(n => n.id)).toEqual(['system', 'storage', 'actor']);
    expect(network.edges.map(e => e.id)).toEqual(['membership', 'service', 'access']);
    expect(flowLabel(network.edges[0]!, 'Network')).toContain('not network traffic');
    expect(architectureForView(graph.nodes, graph.edges, 'DataFlows').edges).toEqual([]);
  });
  it('separates network scope and segments and never draws hosting or governance as traffic', () => {
    // Arrange
    const graph = designFixture();
    graph.nodes.push({ ...graph.nodes[1]!, id: 'isolated', kind: 'DesignComponent', networkRole: 'Firewall', networkSegment: 'DMZ' },
      { ...graph.nodes[1]!, id: 'environment', kind: 'Environment' },
      { ...graph.nodes[1]!, id: 'governance', kind: 'ActorGroup' });
    graph.edges.push({ ...graph.edges[0]!, id: 'hosting', sourceNodeId: 'storage', targetNodeId: 'environment',
      relationshipType: 'HostingAssociation', source: { ...graph.nodes[0]!.source!, type: 'RecordedRelationship' } });
    // Act
    const network = architectureForView(graph.nodes, graph.edges, 'Network');
    // Assert
    expect(network.nodes.map(n => n.id)).toEqual(['system', 'storage', 'isolated']);
    expect(network.edges.map(e => e.id)).toEqual(['flow']);
  });
  it('keeps standalone authored DFD functions and stores, but never turns CSP attachment into a flow', () => {
    // Arrange
    const graph = designFixture();
    graph.nodes.push({ ...graph.nodes[1]!, id: 'function', kind: 'DataFlowElement', dataFlowRole: 'Function' },
      { ...graph.nodes[1]!, id: 'store', kind: 'DataFlowElement', dataFlowRole: 'DataStore' });
    graph.edges.push({ ...graph.edges[0]!, id: 'attachment', sourceNodeId: 'function', targetNodeId: 'store',
      relationshipType: 'Attachment', source: { ...graph.nodes[0]!.source!, type: 'RecordedRelationship' } });
    // Act
    const dfd = architectureForView(graph.nodes, graph.edges, 'DataFlows');
    const boundary = architectureForView(graph.nodes, graph.edges, 'Boundary');
    // Assert
    expect(dfd.nodes.map(n => n.id)).toEqual(['system', 'storage', 'function', 'store']);
    expect(dfd.edges.map(e => e.id)).toEqual(['flow']);
    expect(boundary.nodes.map(n => n.id)).not.toContain('function');
  });
  it('includes actual logical constructs and preserves semantic predicates without treating them as transport', () => {
    // Arrange
    const graph = designFixture();
    const template = graph.nodes[1]!;
    graph.nodes.push(
      { ...template, id: 'actor', kind: 'ActorGroup' },
      { ...template, id: 'information', kind: 'InformationType', diagramRole: 'SourceRecord' },
      { ...template, id: 'rule', kind: 'PolicyReference', diagramRole: 'SourceRecord' },
      { ...template, id: 'goal', kind: 'LogicalConstruct', diagramRole: 'SourceRecord', properties: { logicalType: 'Goal' } });
    graph.edges.push({ ...graph.edges[0]!, id: 'supports', sourceNodeId: 'system', targetNodeId: 'goal',
      relationshipType: 'Supports', purpose: 'Recorded mission effect', protocol: null, port: null });
    // Act
    const logical = architectureForView(graph.nodes, graph.edges, 'Logical');
    const physical = architectureForView(graph.nodes, graph.edges, 'Boundary');
    // Assert
    expect(logical.nodes.map(node => node.id)).toEqual(['system', 'storage', 'actor', 'information', 'rule', 'goal']);
    expect(logical.edges.map(edge => edge.id)).toContain('supports');
    expect(flowLabel(graph.edges[1]!, 'Logical')).toContain('Supports');
    expect(physical.nodes.map(node => node.id)).not.toContain('goal');
  });
  it('uses named boundary scopes and never merges external authorization or governance into the resource box', () => {
    // Arrange
    const graph = designFixture();
    const source = graph.nodes[0]!.source!;
    graph.nodes.push({ ...graph.nodes[0]!, id: 'definition', kind: 'BoundaryDefinition', label: 'Mission production',
      diagramRole: 'SourceRecord', source: { ...source, type: 'BoundaryDefinition', id: 'scope-a' } });
    const local = { ...graph.nodes[1]!, boundaryDisposition: 'InBoundary', boundaryDefinitionId: 'scope-a' };
    const provider = { ...local, kind: 'ProviderReference' };
    const separate = { ...local, boundaryRelationship: 'SeparatelyAuthorized' };
    // Act
    const names = [local, provider, separate, { ...local, kind: 'ActorGroup' }].map(node => boundaryGroupName(node, graph.nodes));
    // Assert
    expect(names).toEqual(['Authorization boundary · Mission production', 'Authorization boundary · Mission production',
      'Outside authorization boundary', 'External actors / governance (not components)']);
    expect(architectureForView(graph.nodes, graph.edges, 'Boundary').nodes.some(node => node.kind === 'BoundaryDefinition')).toBe(false);
  });
  it('centers the system context and preserves CSP, non-CSP and performer interfaces while collapsing internals', () => {
    // Arrange
    const graph = designFixture();
    const template = graph.nodes[1]!;
    graph.nodes = [
      graph.nodes[0]!,
      { ...template, id: 'app', boundaryDisposition: 'InBoundary' },
      { ...template, id: 'csp', kind: 'ProviderReference', label: 'Recorded CSP service' },
      { ...template, id: 'non-csp', kind: 'ExternalSystem', label: 'Organization-managed support', boundaryDisposition: 'OutOfBoundary' },
      { ...template, id: 'performer', kind: 'ActorGroup', label: 'Recorded mission users' },
      { ...template, id: 'policy', kind: 'PolicyReference', diagramRole: 'SourceRecord' },
    ];
    graph.edges = [
      { ...graph.edges[0]!, id: 'internal', sourceNodeId: 'system', targetNodeId: 'app' },
      { ...graph.edges[0]!, id: 'csp-flow', sourceNodeId: 'app', targetNodeId: 'csp', relationshipType: 'ServiceFlow' },
      { ...graph.edges[0]!, id: 'non-csp-flow', sourceNodeId: 'non-csp', targetNodeId: 'app', relationshipType: 'ResourceFlow' },
      { ...graph.edges[0]!, id: 'access', sourceNodeId: 'performer', targetNodeId: 'app' },
    ];
    // Act
    const context = architectureForView(graph.nodes, graph.edges, 'Context');
    // Assert
    expect(context.nodes.map(node => node.id)).toEqual(['system', 'csp', 'non-csp', 'performer']);
    expect(context.edges.map(edge => [edge.id, edge.sourceNodeId, edge.targetNodeId])).toEqual([
      ['csp-flow', 'system', 'csp'], ['non-csp-flow', 'non-csp', 'system'], ['access', 'performer', 'system'],
    ]);
    expect(graph.edges[1]!.sourceNodeId).toBe('app');
    expect(graph.nodes).toHaveLength(6);
  });
  it('places the ATO system between surrounding entities without rearranging saved manual placement', async () => {
    // Arrange
    const nodes = [{ id: 'center', label: 'ATO system', kind: 'System' },
      { id: 'actor', label: 'Operator', kind: 'ActorGroup' }, { id: 'peer', label: 'Support service', kind: 'ExternalSystem' }];
    // Act
    const positions = await layoutDesign(nodes, [], 'Context', {});
    const retained = await layoutDesign(nodes, [], 'Context', { center: { x: 88, y: 99 } });
    // Assert
    expect(positions.actor!.x).toBeLessThan(positions.center!.x);
    expect(positions.peer!.x).toBeGreaterThan(positions.center!.x);
    expect(retained.center).toEqual({ x: 88, y: 99 });
  });
  it('selects recorded Azure deployment and logical architecture without inventing flow endpoints', () => {
    // Arrange
    const graph = designFixture();
    graph.nodes.push(
      { ...graph.nodes[1]!, id: 'environment', kind: 'Environment', properties: { subscriptionId: 'sub-a' } },
      { ...graph.nodes[1]!, id: 'resource', properties: { AzureResourceId: '/subscriptions/sub-a/resourceGroups/rg/providers/Microsoft.App/containerApps/api' } },
      { ...graph.nodes[1]!, id: 'actor', kind: 'ActorGroup', properties: {} },
      { ...graph.nodes[1]!, id: 'azure-in-name-only', label: 'Azure example', properties: {} });
    const edges = [{ ...graph.edges[0]!, id: 'containment', sourceNodeId: 'resource', targetNodeId: 'environment',
      relationshipType: 'Containment', source: { ...graph.nodes[0]!.source!, type: 'RecordedRelationship' } },
      { ...graph.edges[0]!, id: 'external-flow', sourceNodeId: 'actor', targetNodeId: 'resource' }];
    // Act
    const azure = architectureForView(graph.nodes, edges, 'AzureDeployment');
    const logical = architectureForView(graph.nodes, edges, 'Logical');
    // Assert
    expect(azure.nodes.map(node => node.id)).toEqual(['environment', 'resource', 'actor']);
    expect(azure.edges.map(edge => edge.id)).toEqual(['containment', 'external-flow']);
    expect(azureScope(graph.nodes.find(node => node.id === 'resource')!)).toEqual({ subscription: 'sub-a', resourceGroup: 'rg' });
    expect(azureScope(graph.nodes.find(node => node.id === 'azure-in-name-only')!)).toEqual({ subscription: undefined, resourceGroup: undefined });
    expect(logical.nodes.some(node => node.kind === 'ActorGroup')).toBe(true);
    expect(logical.nodes.some(node => node.kind === 'Environment')).toBe(true);
    expect(logical.nodes.map(node => node.id)).toContain('storage');
    expect(graph.nodes).toHaveLength(6);
  });
  it('keeps isolated architecture out of the data-flow view and reports Azure scope absence without using names', () => {
    // Arrange
    const graph = designFixture();
    graph.nodes.push({ ...graph.nodes[1]!, id: 'isolated', label: 'Azure resource mentioned in a name' });
    // Act
    const flow = architectureForView(graph.nodes, graph.edges, 'DataFlows');
    const azure = architectureForView(graph.nodes, graph.edges, 'AzureDeployment');
    // Assert
    expect(flow.nodes.map(node => node.id)).toEqual(['system', 'storage']);
    expect(azure.nodes).toEqual([]);
    expect(azure.edges).toEqual([]);
  });
  it('uses recorded platform, access and protection fields in readable diagram labels', () => {
    // Arrange
    const graph = designFixture();
    const actor = { ...graph.nodes[0]!, kind: 'ActorGroup', properties: { AccessMethod: 'CAC/PIV + MFA', DataSensitivityLevel: 'CUI' } };
    const app = { ...graph.nodes[1]!, properties: { ComponentType: 'Application', SubType: 'Container App', OperatingSystem: 'Linux' } };
    // Act
    const actorDetails = nodeDetails(actor);
    const appDetails = nodeDetails(app);
    // Assert
    expect(actorDetails).toContain('CAC/PIV + MFA');
    expect(actorDetails).toContain('CUI');
    expect(appDetails).toContain('Container App');
    expect(flowLabel(graph.edges[0]!, 'Boundary')).toBe('TCP / 443 · HTTPS · TLS · CUI');
    expect(flowLabel({ ...graph.edges[0]!, protocol: null, port: null, service: null, protection: null }, 'Network')).toBe('PPS not recorded · Protection not recorded · CUI · Stack not recorded · Crossing: Unknown');
  });
  it('keeps source contribution rows inspectable without drawing them as architecture boxes', () => {
    // Arrange
    const graph = designFixture();
    const sources = ['ProfileSection', 'PpsEntry', 'InformationType', 'LeveragedAuthorization'].map((kind, index) => ({
      ...graph.nodes[1]!, id: `source-${index}`, kind, label: `Recorded ${kind}`, diagramRole: 'SourceRecord' as const,
    }));
    graph.nodes.push(...sources);
    // Act
    const visible = architectureForView(graph.nodes, graph.edges, 'Context');
    // Assert
    expect(visible.nodes.map(node => node.id)).toEqual(['system', 'storage']);
    expect(visible.edges.map(edge => edge.id)).toEqual(['flow']);
    expect(graph.nodes).toHaveLength(6);
  });
  it('draws recorded associations in architecture views without claiming traffic or hiding manual access flows', () => {
    // Arrange
    const graph = designFixture();
    const association = { ...graph.edges[0]!, id: 'membership', relationshipType: 'Membership',
      source: { ...graph.nodes[1]!.source!, type: 'RecordedRelationship' }, port: null, protocol: null, protection: null };
    const access = { ...association, id: 'recorded-access', relationshipType: 'Access',
      source: { ...graph.nodes[1]!.source!, type: 'RecordedRelationship' } };
    const manualAccess = { ...graph.edges[0]!, id: 'manual-access', relationshipType: 'Access', source: null };
    // Act
    const context = architectureForView(graph.nodes, [association, access, manualAccess], 'Context');
    const flows = architectureForView(graph.nodes, context.edges, 'DataFlows');
    // Assert
    expect(context.edges).toHaveLength(3);
    expect(flows.edges.map(edge => edge.id)).toEqual(['manual-access']);
    expect(relationshipLabel(association)).toBe('Part of system');
    expect(relationshipOrigin(access).label).toContain('not a network flow');
    expect(flowLabel(association, 'Boundary')).toBe('Part of system');
    expect(flowLabel({ ...access, purpose: 'CAC/PIV + MFA' }, 'Boundary')).toBe('Recorded access · CAC/PIV + MFA');
    expect(flowLabel({ ...access, purpose: null }, 'Boundary')).toBe('Recorded user access');
  });
  it('is deterministic and preserves existing manual placement as new nodes arrive', async () => {
    // Arrange
    const nodes = [{ id: 'b', label: 'Database', groupId: 'inside' }, { id: 'a', label: 'Application', groupId: 'inside' }];
    const edges = [{ id: 'flow', sourceNodeId: 'a', targetNodeId: 'b' }];
    // Act
    const first = await layoutDesign(nodes, edges, 'Context', {});
    const second = await layoutDesign([...nodes].reverse(), edges, 'Context', {});
    const added = await layoutDesign([...nodes, { id: 'c', label: 'New source' }], edges, 'Context', { a: { x: 12, y: 34 } });
    // Assert
    expect(first).toEqual(second);
    expect(added.a).toEqual({ x: 12, y: 34 });
    expect(added.c).toBeDefined();
    expect(first.a!.x).toBeLessThan(first.b!.x);
  });
  it('provides bounded pages without losing records for very large accessible tables', () => {
    // Arrange
    const records = Array.from({ length: 10001 }, (_, id) => ({ id: String(id) }));
    // Act
    const pages = Array.from({ length: Math.ceil(records.length / 50) }, (_, page) => pageRecords(records, page));
    // Assert
    expect(pages.flat()).toEqual(records);
    expect(pages.every(page => page.length <= 50)).toBe(true);
    expect(GRAPH_NODE_LIMIT).toBeLessThanOrEqual(300);
    expect(GRAPH_EDGE_LIMIT).toBeLessThanOrEqual(600);
  });
  it.each(['Boundary', 'Network'] as const)('keeps %s groups spatially explicit rather than overlapping', async view => {
    // Arrange
    const nodes = [
      { id: 'inside-a', label: 'App', groupId: 'Inside' }, { id: 'inside-b', label: 'Database', groupId: 'Inside' },
      { id: 'outside', label: 'External', groupId: 'Outside' },
    ];
    // Act
    const positions = await layoutDesign(nodes, [{ id: 'crossing', sourceNodeId: 'inside-a', targetNodeId: 'outside' }], view, {});
    // Assert
    expect(positions.outside!.x).toBeGreaterThan(Math.max(positions['inside-a']!.x, positions['inside-b']!.x) + 260);
  });
  it('places larger boundary groups in two columns without altering retained manual positions', async () => {
    // Arrange
    const nodes = ['a', 'b', 'c', 'd'].map(id => ({ id, label: id, groupId: 'Inside' }));
    nodes.push({ id: 'outside', label: 'Outside', groupId: 'Outside' });
    // Act
    const positions = await layoutDesign(nodes, [], 'Boundary', {});
    const retained = await layoutDesign(nodes, [], 'Boundary', { a: { x: 71, y: 83 } });
    // Assert
    expect(positions.a!.x).toBe(positions.c!.x);
    expect(positions.b!.x).toBe(positions.d!.x);
    expect(positions.c!.y).toBeGreaterThan(positions.a!.y);
    expect(positions.outside!.x).toBeGreaterThan(positions.b!.x + 260);
    expect(retained.a).toEqual({ x: 71, y: 83 });
  });
  it('labels origin separately from review state and never verifies an unreviewed canonical relationship', () => {
    // Arrange
    const edge = designFixture().edges[0]!;
    const source = designFixture().nodes[0]!.source!;
    const inputs = [
      { ...edge, source: { ...source, precedence: 2, reviewState: 'Approved' } },
      edge,
      { ...edge, source: { ...source, type: 'AzureObservation', precedence: 4 } },
      { ...edge, source: { ...source, precedence: 5 } },
      { ...edge, source: { ...source, precedence: 6 } },
      { ...edge, source: { ...source, precedence: 7, reviewState: 'Unreviewed' } },
    ];
    // Act
    const origins = inputs.map(relationshipOrigin);
    // Assert
    expect(origins.map(origin => origin.label)).toEqual([
      'Verified canonical relationship', 'User-authored design relationship', 'Azure-observed relationship',
      'Imported relationship', 'AI-suggested relationship', 'Undetermined relationship',
    ]);
    expect(new Set(origins.map(origin => origin.pattern)).size).toBe(6);
  });
});
