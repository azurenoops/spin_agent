import { describe, expect, it } from 'vitest';
import { layoutDesign, pageRecords, relationshipLabel, relationshipOrigin, architectureForView, GRAPH_NODE_LIMIT, GRAPH_EDGE_LIMIT } from '../../features/system-design/graphAdapter';
import { designFixture } from './fixtures';

describe('replaceable System design layout', () => {
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
