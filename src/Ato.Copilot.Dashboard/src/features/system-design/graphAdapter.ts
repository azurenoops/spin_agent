import ELK from 'elkjs/lib/elk.bundled.js';
import type { DesignEdge, DesignNode } from '../../api/systemDesign';

export const GRAPH_NODE_LIMIT = 300;
export const GRAPH_EDGE_LIMIT = 600;
export type DesignView = 'Context' | 'Boundary' | 'Network' | 'DataFlows';
export type Positions = Record<string, { x: number; y: number }>;
interface LayoutNode { id: string; label: string; groupId?: string | null }
interface LayoutEdge { id: string; sourceNodeId: string; targetNodeId: string }

export function isArchitectureNode(node: Pick<DesignNode, 'kind'> & { diagramRole?: string }): boolean {
  return node.diagramRole !== 'SourceRecord'
    && !['ProfileSection', 'PpsEntry', 'InformationType', 'LeveragedAuthorization', 'MonitoringObservation'].includes(node.kind);
}

const recordedRelationships: Record<string, string> = {
  Membership: 'Part of system',
  UsesService: 'Uses provider service',
  Attachment: 'Attached subscription',
  Containment: 'Within selected environment',
  HostingAssociation: 'Related hosting scope',
};

export function isRecordedAssociation(edge: DesignEdge): boolean {
  return edge.source?.type === 'RecordedRelationship' && (Object.hasOwn(recordedRelationships, edge.relationshipType)
    || edge.relationshipType === 'Access');
}

export function isDataFlow(edge: DesignEdge): boolean {
  return ['DataFlow', 'Interconnection', 'UserAuthored', 'NetworkConnection', 'Dependency'].includes(edge.relationshipType)
    || edge.relationshipType === 'Access' && !isRecordedAssociation(edge);
}
export function relationshipLabel(edge: DesignEdge): string {
  if (isRecordedAssociation(edge)) return edge.relationshipType === 'Access'
    ? 'Recorded user access' : recordedRelationships[edge.relationshipType]!;
  return edge.relationshipType.replace(/([a-z])([A-Z])/g, '$1 $2');
}

export function architectureForView(nodes: DesignNode[], edges: DesignEdge[], view: DesignView) {
  const architecture = nodes.filter(isArchitectureNode);
  const ids = new Set(architecture.map(node => node.id));
  const connections = edges.filter(edge => ids.has(edge.sourceNodeId) && ids.has(edge.targetNodeId)
    && (view !== 'DataFlows' || isDataFlow(edge)));
  const flowIds = new Set(connections.flatMap(edge => [edge.sourceNodeId, edge.targetNodeId]));
  return { nodes: view === 'DataFlows' ? architecture.filter(node => flowIds.has(node.id) || node.kind === 'System') : architecture, edges: connections };
}

export const RELATIONSHIP_ORIGINS = {
  canonical: { label: 'Verified canonical relationship', shortLabel: 'Verified source', pattern: '' },
  recorded: { label: 'Source-recorded association (not a network flow)', shortLabel: 'Source recorded', pattern: '10 2' },
  authored: { label: 'User-authored design relationship', shortLabel: 'User-authored', pattern: '8 3' },
  azure: { label: 'Azure-observed relationship', shortLabel: 'Azure observed', pattern: '3 3' },
  imported: { label: 'Imported relationship', shortLabel: 'Imported', pattern: '12 3 3 3' },
  ai: { label: 'AI-suggested relationship', shortLabel: 'AI suggested', pattern: '1 4' },
  undetermined: { label: 'Undetermined relationship', shortLabel: 'Undetermined', pattern: '6 5' },
} as const;

export function relationshipOrigin(edge: DesignEdge) {
  if (isRecordedAssociation(edge)) return RELATIONSHIP_ORIGINS.recorded;
  if (!edge.source) return ['Draft', 'Working'].includes(edge.projectionStatus)
    ? RELATIONSHIP_ORIGINS.authored : RELATIONSHIP_ORIGINS.undetermined;
  if (edge.source.type === 'AzureObservation') return RELATIONSHIP_ORIGINS.azure;
  if (edge.source.precedence === 5) return RELATIONSHIP_ORIGINS.imported;
  if (edge.source.precedence === 6) return RELATIONSHIP_ORIGINS.ai;
  return edge.source.precedence >= 1 && edge.source.precedence <= 3 && ['Approved', 'Reviewed'].includes(edge.source.reviewState)
    ? RELATIONSHIP_ORIGINS.canonical : RELATIONSHIP_ORIGINS.undetermined;
}

export function pageRecords<T>(records: T[], page: number, size = 50): T[] {
  return records.slice(Math.max(0, page) * size, (Math.max(0, page) + 1) * size);
}

export async function layoutDesign(nodes: LayoutNode[], edges: LayoutEdge[], view: DesignView, manual: Positions): Promise<Positions> {
  const sorted = [...nodes].sort((a, b) => (a.groupId ?? '').localeCompare(b.groupId ?? '') || a.id.localeCompare(b.id));
  const groups = [...new Set(sorted.map(node => node.groupId ?? ''))];
  if ((view === 'Boundary' || view === 'Network') && groups.length > 1 && !Object.keys(manual).length) {
    const positions: Positions = {};
    let offset = 40;
    for (const group of groups) {
      const members = sorted.filter(node => (node.groupId ?? '') === group);
      const groupPositions = await layoutDesign(members, edges, view, {});
      for (const [id, position] of Object.entries(groupPositions)) positions[id] = { x: position.x + offset, y: position.y + 50 };
      offset += Math.max(...Object.values(groupPositions).map(position => position.x)) + 350;
    }
    return positions;
  }
  const ids = new Set(sorted.map(node => node.id));
  const elk = new ELK();
  const result = await elk.layout({
    id: 'system-design',
    layoutOptions: {
      'elk.algorithm': 'layered', 'elk.direction': view === 'Network' ? 'DOWN' : 'RIGHT',
      'elk.randomSeed': '1', 'elk.spacing.nodeNode': '65', 'elk.layered.spacing.nodeNodeBetweenLayers': '95',
      'elk.layered.considerModelOrder.strategy': 'NODES_AND_EDGES',
    },
    children: sorted.map(node => ({ id: node.id, width: 210, height: 90 })),
    edges: [...edges].filter(edge => ids.has(edge.sourceNodeId) && ids.has(edge.targetNodeId))
      .sort((a, b) => a.id.localeCompare(b.id))
      .map(edge => ({ id: edge.id, sources: [edge.sourceNodeId], targets: [edge.targetNodeId] })),
  });
  const positions: Positions = {};
  const occupied = Object.values(manual);
  let newY = occupied.length ? Math.max(...occupied.map(position => position.y)) + 160 : 0;
  for (const node of result.children ?? []) {
    if (manual[node.id]) positions[node.id] = manual[node.id]!;
    else if (occupied.length) {
      positions[node.id] = { x: 40, y: newY };
      newY += 160;
    } else positions[node.id] = { x: node.x ?? 0, y: node.y ?? 0 };
  }
  return positions;
}
