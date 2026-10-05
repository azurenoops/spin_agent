import ELK from 'elkjs/lib/elk.bundled.js';
import type { DesignEdge, DesignNode } from '../../api/systemDesign';

export const GRAPH_NODE_LIMIT = 300;
export const GRAPH_EDGE_LIMIT = 600;
export type DesignView = 'Context' | 'Boundary' | 'Logical' | 'DataFlows' | 'Network' | 'AzureDeployment';
export type Positions = Record<string, { x: number; y: number }>;
interface LayoutNode { id: string; label: string; groupId?: string | null; kind?: string }
interface LayoutEdge { id: string; sourceNodeId: string; targetNodeId: string }

export function isArchitectureNode(node: Pick<DesignNode, 'kind'> & { diagramRole?: string; properties?: Record<string, string | null> }): boolean {
  return node.diagramRole !== 'SourceRecord'
    && !['ProfileSection', 'PpsEntry', 'InformationType', 'LeveragedAuthorization', 'MonitoringObservation', 'PolicyReference', 'ContextConstraint', 'BoundaryDefinition', 'AuthorizationScope', 'LogicalConstruct', 'DataFlowElement'].includes(node.kind)
    && node.properties?.ComponentType !== 'Policy';
}
export const SACA_ZONES = ['OnPremisesDisn', 'SecureCloudAccessBoundary', 'AzureCloud', 'Undetermined'];
export const SACA_ROLES = ['BCAP', 'VDSS', 'VDMS', 'TCCM', 'CNAP', 'Workload', 'SharedService', 'Undetermined'];
export const SACA_REFERENCE_ROLES = ['BCAP', 'VDSS', 'VDMS', 'TCCM', 'CNAP'];
export function isDeploymentSeed(node: DesignNode): boolean {
  return isAzureNode(node) || node.kind === 'ProviderReference' || node.sacaZone != null || node.sacaRole != null || node.deploymentScopeNodeId != null
    || node.deploymentOwner != null || node.deploymentEvidenceReference != null || node.deploymentSecurityFunctions != null;
}
export function isDeploymentRelationship(edge: DesignEdge, nodes: DesignNode[]): boolean {
  return isDataFlow(edge) || isNetworkAssociation(edge) || (edge.relationshipType === 'GovernanceInteraction'
    || edge.relationshipType === 'GovernanceAssignment' && edge.source?.type === 'RecordedRelationship')
    && nodes.some(n => n.sacaRole === 'TCCM' && [edge.sourceNodeId, edge.targetNodeId].includes(n.id));
}
export function deploymentScope(node: DesignNode, nodes: DesignNode[], edges: DesignEdge[]): DesignNode | undefined {
  if (node.kind === 'Environment') return node;
  if (node.deploymentScopeNodeId) return nodes.find(n => n.id === node.deploymentScopeNodeId && n.kind === 'Environment');
  const scopeIds = new Set(edges.filter(e => e.relationshipType === 'Containment' && e.source?.type === 'RecordedRelationship'
    && e.sourceNodeId === node.id).map(e => e.targetNodeId));
  const scopes = nodes.filter(n => n.kind === 'Environment' && scopeIds.has(n.id));
  return scopes.length === 1 ? scopes[0] : undefined;
}
export function deploymentZone(node: DesignNode): string {
  return node.sacaZone ?? (isAzureNode(node) ? 'AzureCloud' : 'Undetermined');
}
export function deploymentZoneLabel(zone: string): string {
  const labels: Record<string, string> = { OnPremisesDisn: '01 On-premises / DISN', SecureCloudAccessBoundary: '02 Secure cloud access boundary',
    AzureCloud: '03 Azure cloud (identity requires recorded scope)', Undetermined: '04 Undetermined deployment context' };
  return labels[zone] ?? labels.Undetermined!;
}
export function deploymentGroup(node: DesignNode, nodes: DesignNode[], edges: DesignEdge[]): string {
  if (node.sacaRole === 'TCCM') return '05 TCCM business role (not an appliance)';
  const scope = deploymentScope(node, nodes, edges);
  return [deploymentZoneLabel(deploymentZone(node)), scope ? propertyValue(scope, 'cloud') ?? 'Cloud not recorded' : 'Cloud not recorded',
    scope ? propertyValue(scope, 'subscriptionId') ?? azureScope(node).subscription ?? 'Subscription not recorded' : azureScope(node).subscription ?? 'Subscription not recorded',
    azureScope(node).resourceGroup ?? 'Resource group not recorded', boundaryGroupName(node, nodes)].join(' / ');
}
export function deploymentDetails(node: DesignNode, nodes: DesignNode[], edges: DesignEdge[]): string[] {
  const scope = deploymentScope(node, nodes, edges);
  return [node.sacaRole === 'TCCM' ? 'TCCM business role · AO appointment unverified' : `SACA role: ${node.sacaRole ?? 'Not recorded'}`,
    ...nodeDetails(node), node.deploymentOwner ? `Responsibility: ${node.deploymentOwner}` : undefined,
    node.networkAddress || propertyValue(node, 'IpAddress'), node.networkSegment,
    node.hostingImpactLevel ? `Claimed hosting ${node.hostingImpactLevel} · accreditation unverified` : undefined,
    scope ? `Cloud: ${propertyValue(scope, 'cloud') ?? 'Not recorded'}` : 'Cloud identity not verified',
    scope && propertyValue(scope, 'directoryTenantId') ? `Directory: ${propertyValue(scope, 'directoryTenantId')}` : undefined]
    .filter((v): v is string => !!v).map(v => v.length > 140 ? `${v.slice(0, 140)}...` : v);
}

const recordedRelationships: Record<string, string> = {
  Membership: 'Part of system',
  UsesService: 'Uses provider service',
  Attachment: 'Attached subscription',
  Containment: 'Within selected environment',
  HostingAssociation: 'Related hosting scope',
  GovernanceAssignment: 'Recorded governance assignment (not traffic)',
  LogicalAssociation: 'Recorded logical association (not traffic)',
};

export const LOGICAL_TYPES = ['Performer', 'Activity', 'InformationData', 'Rule', 'Goal', 'Capability', 'Service', 'Project'];
export const LOGICAL_PREDICATES = ['Performs', 'Provides', 'Supports', 'Governs', 'Enables', 'Produces', 'Consumes', 'Realizes'];
export function logicalType(node: DesignNode): string | undefined {
  if (node.kind === 'DataFlowElement') return undefined;
  if (node.kind === 'LogicalConstruct') return propertyValue(node, 'logicalType');
  if (node.kind === 'InformationType') return 'InformationData';
  if (isContextConstraint(node)) return 'Rule';
  if (['Environment', 'BoundaryDefinition', 'AuthorizationScope'].includes(node.kind)) return 'ScopeReference';
  if (['ProfileSection', 'PpsEntry', 'LeveragedAuthorization', 'MonitoringObservation'].includes(node.kind)) return undefined;
  if (node.diagramRole === 'SourceRecord') return undefined;
  if (node.kind === 'ProviderReference' || propertyValue(node, 'ComponentType') === 'Service') return 'Service';
  return 'Performer';
}
export function logicalGroupName(node: DesignNode): string {
  const type = logicalType(node);
  return `${['Activity', 'ScopeReference'].includes(type ?? '') ? 'Supporting' : 'Principal'} constructs · ${type}`;
}
export function logicalDetails(node: DesignNode): string[] {
  const symbols: Record<string, string> = { Performer: '[P]', Activity: '[A]', InformationData: '[D]', Rule: '[R]', Goal: '[G]', Capability: '[C]', Service: '[S]', Project: '[J]', ScopeReference: '[Ref]' };
  const type = logicalType(node) ?? 'Unclassified';
  return [`${symbols[type] ?? '[?]'} ${type}`, `Layer: ${propertyValue(node, 'logicalLayer') ?? 'Not recorded'}`,
    propertyValue(node, 'capabilityCategory', 'description', 'Description')].filter((value): value is string => !!value)
    .map(value => value.length > 140 ? `${value.slice(0, 140)}...` : value);
}
export function isRecordedAssociation(edge: DesignEdge): boolean {
  return edge.source?.type === 'RecordedRelationship' && (Object.hasOwn(recordedRelationships, edge.relationshipType)
    || edge.relationshipType === 'Access');
}

export function isDataFlow(edge: DesignEdge): boolean {
  return ['DataFlow', 'ServiceFlow', 'ResourceFlow', 'Interconnection', 'UserAuthored', 'NetworkConnection', 'Dependency'].includes(edge.relationshipType)
    || edge.relationshipType === 'Access' && !isRecordedAssociation(edge);
}
export const DFD_ROLES = ['Function', 'DataStore', 'ExternalEntity', 'Undetermined'];
export const DATA_LIFECYCLE = ['Receive', 'Process', 'Store', 'Distribute', 'Destroy'];
export function isFlowEndpoint(node: DesignNode): boolean {
  return isArchitectureNode(node) || node.kind === 'DataFlowElement'
    || node.kind === 'LogicalConstruct' && node.properties.logicalType === 'Activity' && node.dataFlowRole === 'Function';
}
export function dataFlowRole(node: DesignNode): string {
  return node.dataFlowRole ?? (node.kind === 'ExternalSystem' ? 'ExternalEntity' : 'Undetermined');
}
export function dataFlowGroup(node: DesignNode, nodes: DesignNode[]): string {
  return dataFlowRole(node) === 'ExternalEntity' ? 'External producers / consumers' : `${boundaryGroupName(node, nodes)} · functions / stores`;
}
export function dataFlowDetails(node: DesignNode): string[] {
  const symbols: Record<string, string> = { Function: '[F] Function', DataStore: '[DS] Data store', ExternalEntity: '[E] External producer / consumer', Undetermined: '[?] Unmapped endpoint' };
  return [symbols[dataFlowRole(node)] ?? symbols.Undetermined!, node.functionDescription,
    node.dataRetention ? `Retention: ${node.dataRetention}` : undefined, node.disposalMethod ? `Disposal: ${node.disposalMethod}` : undefined]
    .filter((v): v is string => !!v).map(v => v.length > 140 ? `${v.slice(0, 140)}...` : v);
}
export function relationshipLabel(edge: DesignEdge): string {
  if (isRecordedAssociation(edge)) return edge.relationshipType === 'Access'
    ? 'Recorded user access' : recordedRelationships[edge.relationshipType]!;
  return edge.relationshipType.replace(/([a-z])([A-Z])/g, '$1 $2');
}

export function isContextPerformer(node: DesignNode): boolean {
  return node.kind === 'ActorGroup' || propertyValue(node, 'contextEntityClass') === 'Performer'
    || propertyValue(node, 'ComponentType') === 'Person' || propertyValue(node, 'componentType') === 'Actor group';
}
export function isContextConstraint(node: DesignNode): boolean {
  return ['PolicyReference', 'ContextConstraint'].includes(node.kind) || propertyValue(node, 'ComponentType') === 'Policy';
}
export function boundaryDisposition(node: DesignNode): string {
  return ['SharedService', 'SeparatelyAuthorized'].includes(node.boundaryRelationship ?? '') ? 'OutOfBoundary' : node.boundaryDisposition;
}
export function boundaryGroupName(node: DesignNode, nodes: DesignNode[]): string {
  if (isContextPerformer(node)) return 'External actors / governance (not components)';
  if (node.kind === 'Environment') return 'Hosting scope references (not authorized components)';
  const disposition = boundaryDisposition(node);
  if (disposition === 'OutOfBoundary') return 'Outside authorization boundary';
  if (disposition !== 'InBoundary') return 'Boundary undetermined';
  const id = node.boundaryDefinitionId ?? propertyValue(node, 'boundaryId');
  const definition = nodes.find(item => item.kind === 'BoundaryDefinition' && item.source?.id === id);
  return definition ? `Authorization boundary · ${definition.label}` : 'Authorization boundary · In boundary';
}
export function boundaryNodeDetails(node: DesignNode): string[] {
  return [...nodeDetails(node), node.boundaryRelationship ? `Ownership: ${node.boundaryRelationship}` : undefined,
    node.securityResponsibility || propertyValue(node, 'Owner'), node.boundaryRationale].filter((value): value is string => !!value)
    .map(value => value.length > 140 ? `${value.slice(0, 140)}...` : value);
}
export const NETWORK_ROLES = ['Server', 'VirtualMachine', 'Application', 'Database', 'NetworkSegment', 'Router', 'Switch',
  'Firewall', 'LoadBalancer', 'VpnGateway', 'SecurityAppliance', 'MonitoringTool', 'ExternalSystem', 'Undetermined'];
export const NETWORK_MEDIA = ['Local', 'Private', 'Internet', 'DISN', 'Other'];
export function isNetworkAssociation(edge: DesignEdge): boolean {
  return edge.source?.type === 'RecordedRelationship'
    && ['Membership', 'UsesService', 'Attachment', 'Containment', 'HostingAssociation', 'Access'].includes(edge.relationshipType);
}
export function isNetworkComponent(node: DesignNode): boolean {
  return isArchitectureNode(node) && node.kind !== 'Environment' && !isContextPerformer(node);
}
export function networkGroupName(node: DesignNode, nodes: DesignNode[]): string {
  if (isContextPerformer(node)) return 'User endpoints · not computing assets';
  return [boundaryGroupName(node, nodes), node.environment || propertyValue(node, 'Environment') || 'Environment not recorded',
    node.networkZone || 'Zone not recorded', node.networkSegment || 'Segment not recorded'].join(' / ');
}
export function networkDetails(node: DesignNode): string[] {
  return [node.networkRole ? `Network role: ${node.networkRole}` : 'Network role not recorded', ...nodeDetails(node),
    node.networkAddress || propertyValue(node, 'IpAddress'), node.hostingImpactLevel ? `Claimed hosting ${node.hostingImpactLevel} · authorization unverified` : undefined]
    .filter((v): v is string => !!v).map(v => v.length > 140 ? `${v.slice(0, 140)}...` : v);
}
export function isNonTechnicalInteraction(edge: DesignEdge): boolean {
  return ['GovernanceInteraction', 'ConstraintReference', ...LOGICAL_PREDICATES].includes(edge.relationshipType) || isRecordedAssociation(edge);
}
export function contextEntityDetails(node: DesignNode): string[] {
  const categories: Record<string, string> = {
    Operational: 'Operational participant', SecurityCompliance: 'Security & compliance',
    DataSource: 'Data source / destination', SupportService: 'Support service',
  };
  const category = propertyValue(node, 'contextEntityCategory')
    ?? (node.kind === 'Environment' || node.kind === 'ProviderReference' ? 'SupportService' : 'Operational');
  const csp = node.kind === 'ProviderReference' && ['SystemProviderScopeSelection', 'ProviderHostingAssignment', 'BoundaryComponentAssignment'].includes(node.source?.type ?? '');
  return [isContextPerformer(node) ? 'Performer' : 'System', categories[category] ?? 'Category not recorded',
    propertyValue(node, 'contextRole'), propertyValue(node, 'contextOrganization'),
    csp ? 'CSP reference · inheritance not established' : node.kind === 'Environment' ? 'Recorded environment scope' : undefined,
  ].filter((value): value is string => !!value);
}

export function propertyValue(node: DesignNode, ...keys: string[]): string | undefined {
  for (const key of keys) {
    const value = node.properties[key]?.trim();
    if (value) return value;
  }
  return undefined;
}

export function nodeDetails(node: DesignNode): string[] {
  const type = propertyValue(node, 'SubType', 'AzureResourceType', 'resourceType', 'ComponentType', 'Type')
    ?? node.kind.replace(/([a-z])([A-Z])/g, '$1 $2');
  return [...new Set([type, propertyValue(node, 'AccessMethod'), propertyValue(node, 'OperatingSystem', 'Version'),
    propertyValue(node, 'DataSensitivityLevel', 'SensitivityClassification'),
    node.environment || propertyValue(node, 'Environment', 'AzureLocation', 'Location'),
    node.networkZone, node.provider].filter((value): value is string => !!value))];
}

export function azureScope(node: DesignNode): { subscription?: string; resourceGroup?: string } {
  const arm = (propertyValue(node, 'AzureResourceId', 'resourceId') ?? '')
    .match(/^\/subscriptions\/([^/]+)\/resourceGroups\/([^/]+)\/providers\/[^/]+\//i);
  return {
    subscription: propertyValue(node, 'subscriptionId', 'SubscriptionId') ?? arm?.[1],
    resourceGroup: propertyValue(node, 'resourceGroup', 'ResourceGroup', 'AzureResourceGroup') ?? arm?.[2],
  };
}

export function flowLabel(edge: DesignEdge, view: DesignView): string {
  if (LOGICAL_PREDICATES.includes(edge.relationshipType))
    return `${edge.relationshipType} · ${edge.purpose || 'Purpose not recorded'} (not traffic)`;
  if (['GovernanceInteraction', 'ConstraintReference'].includes(edge.relationshipType))
    return `${edge.purpose || 'Purpose not recorded'} · ${relationshipLabel(edge)} (not traffic)`;
  if (isRecordedAssociation(edge)) {
    if (['Network', 'AzureDeployment'].includes(view)) return `${edge.relationshipType === 'Access' ? 'Recorded user access' : relationshipLabel(edge)} (not network traffic)`;
    if (view === 'Logical' && edge.relationshipType === 'LogicalAssociation') return edge.purpose ?? relationshipLabel(edge);
    return edge.relationshipType === 'Access' && edge.purpose ? `Recorded access · ${edge.purpose}` : relationshipLabel(edge);
  }
  if (view === 'DataFlows') return [
    edge.informationType || 'Data not recorded', edge.classification, edge.protection || 'Protection not recorded',
    edge.lifecycleStage ? `Lifecycle: ${edge.lifecycleStage}` : undefined,
    edge.boundaryCrossing === 'Yes' ? `Crossing · ${edge.agreementStatus || 'Agreement not recorded'}` : undefined,
  ].filter(Boolean).join(' · ');
  if (view === 'Boundary' || view === 'Network' || view === 'AzureDeployment') {
    return [[edge.protocol, edge.port].filter(Boolean).join(' / ') || 'PPS not recorded',
      edge.service, edge.protection || 'Protection not recorded',
      ...(['Boundary', 'Network'].includes(view) ? [edge.classification || edge.informationType, edge.interconnectionId ? `Interconnection · ${edge.agreementStatus || 'Agreement not recorded'}` : undefined] : []),
      ...(view === 'Network' ? [edge.protocolStack ? `Stack: ${edge.protocolStack}` : 'Stack not recorded',
        edge.connectionMedium, `Crossing: ${edge.boundaryCrossing}`] : [])].filter(Boolean).join(' · ');
  }
  return [edge.purpose || relationshipLabel(edge), edge.service].filter(Boolean).join(' · ');
}

function isAzureNode(node: DesignNode): boolean {
  return node.kind === 'Environment' || /^\/subscriptions\/[^/]+\/resourceGroups\/[^/]+\/providers\/[^/]+\//i.test(
    propertyValue(node, 'AzureResourceId', 'resourceId') ?? '');
}

export function architectureForView(nodes: DesignNode[], edges: DesignEdge[], view: DesignView) {
  let architecture = nodes.filter(isArchitectureNode);
  if (view === 'Context') {
    const centers = architecture.filter(node => node.kind === 'System');
    if (centers.length > 1) throw new Error('System context requires one recorded central system.');
    const center = centers[0];
    if (center) {
      const members = new Set(edges.filter(edge => edge.relationshipType === 'Membership'
        && edge.source?.type === 'RecordedRelationship' && edge.targetNodeId === center.id).map(edge => edge.sourceNodeId));
      const internal = new Set(architecture.filter(node => node.id !== center.id && !isContextPerformer(node)
        && !['ProviderReference', 'Environment'].includes(node.kind) && boundaryDisposition(node) !== 'OutOfBoundary'
        && (boundaryDisposition(node) === 'InBoundary' || members.has(node.id))).map(node => node.id));
      const known = new Set(architecture.map(node => node.id));
      return {
        nodes: architecture.filter(node => !internal.has(node.id)),
        edges: edges.filter(edge => known.has(edge.sourceNodeId) && known.has(edge.targetNodeId)).map(edge => ({
          ...edge, sourceNodeId: internal.has(edge.sourceNodeId) ? center.id : edge.sourceNodeId,
          targetNodeId: internal.has(edge.targetNodeId) ? center.id : edge.targetNodeId,
        })).filter(edge => edge.sourceNodeId !== edge.targetNodeId),
      };
    }
  }
  if (view === 'Logical') architecture = nodes.filter(node => logicalType(node) !== undefined);
  if (view === 'DataFlows') architecture = nodes.filter(isFlowEndpoint);
  if (view === 'Network') {
    const participants = new Set(edges.filter(e => isDataFlow(e) || isNetworkAssociation(e)).flatMap(e => [e.sourceNodeId, e.targetNodeId]));
    architecture = architecture.filter(node => isNetworkComponent(node) || isContextPerformer(node) && participants.has(node.id));
  }
  if (view === 'AzureDeployment') {
    const seeds = new Set(architecture.filter(isDeploymentSeed).map(node => node.id));
    const connected = edges.filter(edge => isDeploymentRelationship(edge, nodes) && (seeds.has(edge.sourceNodeId) || seeds.has(edge.targetNodeId)))
      .flatMap(edge => [edge.sourceNodeId, edge.targetNodeId]);
    const ids = new Set([...seeds, ...connected]);
    architecture = architecture.filter(node => ids.has(node.id));
  }
  const ids = new Set(architecture.map(node => node.id));
  const connections = edges.filter(edge => ids.has(edge.sourceNodeId) && ids.has(edge.targetNodeId)
    && (view === 'AzureDeployment' ? isDeploymentRelationship(edge, nodes) : view === 'Network' ? isDataFlow(edge) || isNetworkAssociation(edge) : view !== 'DataFlows' || isDataFlow(edge)));
  const flowIds = new Set(connections.flatMap(edge => [edge.sourceNodeId, edge.targetNodeId]));
  return { nodes: view === 'DataFlows' ? architecture.filter(node => flowIds.has(node.id) || node.kind === 'System'
    || ['Function', 'DataStore', 'ExternalEntity'].includes(node.dataFlowRole ?? '')) : architecture, edges: connections };
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
  if (view === 'DataFlows' && !Object.keys(manual).length) {
    const positions: Positions = {};
    let y = 50;
    for (const group of [...new Set(sorted.map(n => n.groupId ?? ''))]) {
      const members = sorted.filter(n => (n.groupId ?? '') === group);
      const ordered = [...members].sort((a, b) => edges.filter(e => e.targetNodeId === a.id).length - edges.filter(e => e.targetNodeId === b.id).length || a.id.localeCompare(b.id));
      ordered.forEach((n, i) => { positions[n.id] = { x: 40 + i % 3 * 325, y: y + Math.floor(i / 3) * 210 }; });
      y += Math.ceil(ordered.length / 3) * 210 + 70;
    }
    return positions;
  }
  const center = view === 'Context' ? sorted.find(node => node.kind === 'System') : undefined;
  if (center && !Object.keys(manual).length) {
    const others = sorted.filter(node => node.id !== center.id);
    const left = others.filter(node => node.kind === 'ActorGroup' || edges.some(edge => edge.sourceNodeId === node.id && edge.targetNodeId === center.id));
    const leftIds = new Set(left.map(node => node.id));
    const right = others.filter(node => !leftIds.has(node.id));
    const positions: Positions = { [center.id]: { x: 400, y: Math.max(0, Math.max(left.length, right.length) - 1) * 100 } };
    left.forEach((node, index) => { positions[node.id] = { x: 0, y: index * 200 }; });
    right.forEach((node, index) => { positions[node.id] = { x: 850, y: index * 200 }; });
    return positions;
  }
  const groups = [...new Set(sorted.map(node => node.groupId ?? ''))];
  if (['Boundary', 'Network', 'AzureDeployment', 'Logical'].includes(view) && groups.length > 1 && !Object.keys(manual).length) {
    const positions: Positions = {};
    let offset = 40;
    for (const group of groups) {
      const members = sorted.filter(node => (node.groupId ?? '') === group);
      const groupPositions = ['Boundary', 'Logical'].includes(view) && members.length > 2
        ? Object.fromEntries(members.map((node, index) => [node.id, { x: index % 2 * 325, y: Math.floor(index / 2) * 200 }]))
        : await layoutDesign(members, edges, view, {});
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
    children: sorted.map(node => ({ id: node.id, width: 230, height: 130 })),
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
