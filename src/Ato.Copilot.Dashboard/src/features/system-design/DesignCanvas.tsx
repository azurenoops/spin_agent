import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { applyNodeChanges, Background, Controls, MarkerType, ReactFlow, ReactFlowProvider, useNodesInitialized, useReactFlow, useUpdateNodeInternals, type Node, type Edge, type NodeProps } from '@xyflow/react';
import type { DesignEdge, DesignLayout, DesignNode, SystemDesignGraph } from '../../api/systemDesign';
import { GRAPH_EDGE_LIMIT, GRAPH_NODE_LIMIT, architectureForView, nodeDetails, flowLabel, azureScope, propertyValue,
  contextEntityDetails, isContextConstraint, isNonTechnicalInteraction, layoutDesign, relationshipOrigin, RELATIONSHIP_ORIGINS, type DesignView } from './graphAdapter';
import { boundaryGroupName, boundaryNodeDetails, boundaryDisposition } from './graphAdapter';
import { logicalGroupName, logicalDetails, LOGICAL_TYPES, logicalType } from './graphAdapter';
import { dataFlowGroup, dataFlowRole, dataFlowDetails } from './graphAdapter';
import { networkGroupName, networkDetails } from './graphAdapter';
import { isDataFlow, isNetworkAssociation } from './graphAdapter';
import { deploymentGroup, deploymentDetails, deploymentScope, deploymentZone, deploymentZoneLabel, SACA_REFERENCE_ROLES, SACA_ZONES } from './graphAdapter';
import '@xyflow/react/dist/style.css';

function GroupFrame({ data }: NodeProps) {
  return <strong className="sd-group-title">{typeof data.label === 'string' ? data.label : ''}</strong>;
}
const nodeTypes = { group: GroupFrame };

export default function DesignCanvas(props: CanvasProps) {
  return <ReactFlowProvider key={props.view}><Canvas {...props} /></ReactFlowProvider>;
}
interface CanvasProps {
  graph: SystemDesignGraph; layout: DesignLayout; view: DesignView; search: string; filter: string;
  selected: string | null; editable: boolean; pristine: boolean; onSelect: (id: string) => void;
  onLayout: (layout: DesignLayout) => void;
  onInitialPositions: (positions: DesignLayout['positions']) => void;
  onConnect: (sourceId: string, targetId: string) => void;
  onRemove: (recordId: string) => void;
}
function groupName(node: DesignNode, view: DesignView, graph: SystemDesignGraph) {
  if (view === 'Boundary') return boundaryGroupName(node, graph.nodes);
  if (view === 'Logical') return logicalGroupName(node);
  if (view === 'DataFlows') return dataFlowGroup(node, graph.nodes);
  if (view === 'Network') return networkGroupName(node, graph.nodes);
  if (view === 'AzureDeployment') return deploymentGroup(node, graph.nodes, graph.edges);
  return graph.groups.find(group => group.nodeIds.includes(node.id))?.label ?? '';
}
function Canvas({ graph, layout, view, search, filter, selected, editable, pristine, onSelect, onLayout, onInitialPositions, onConnect, onRemove }: CanvasProps) {
  const flow = useReactFlow();
  const nodesInitialized = useNodesInitialized();
  const updateNodeInternals = useUpdateNodeInternals();
  const [nodes, setNodes] = useState<Node[]>([]);
  const [layoutError, setLayoutError] = useState('');
  const [arranging, setArranging] = useState(false);
  const [showNetworkAssociations, setShowNetworkAssociations] = useState(true);
  const [dark, setDark] = useState(() => document.documentElement.classList.contains('dark'));
  const generation = useRef(0);
  const presentation = useRef(layout);
  const initialPositions = useRef(onInitialPositions);
  presentation.current = layout;
  initialPositions.current = onInitialPositions;
  useEffect(() => {
    const observer = new MutationObserver(() => setDark(document.documentElement.classList.contains('dark')));
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] });
    return () => observer.disconnect();
  }, []);
  const architecture = useMemo(() => architectureForView(graph.nodes, graph.edges, view), [graph.nodes, graph.edges, view]);
  const visible = useMemo(() => architecture.nodes.filter(node => (!search || `${node.label} ${node.kind} ${node.source?.id ?? ''}`.toLowerCase().includes(search.toLowerCase()))
    && (!filter || node.boundaryDisposition === filter) && layout.visibility[node.id] !== false), [architecture.nodes, search, filter, layout.visibility]);
  const overBudget = visible.length > GRAPH_NODE_LIMIT || architecture.edges.length > GRAPH_EDGE_LIMIT;
  const isCollapsed = useCallback((name: string) => {
    const group = graph.groups.find(item => item.label === name);
    return group ? layout.collapsedGroups.includes(group.id) : layout.visibility[`presentation-group:${view}:${name}`] === false;
  }, [graph.groups, layout.collapsedGroups, layout.visibility, view]);
  const toggleGroup = (name: string) => {
    const group = graph.groups.find(item => item.label === name);
    if (group) onLayout({ ...layout, collapsedGroups: isCollapsed(name)
      ? layout.collapsedGroups.filter(id => id !== group.id) : [...layout.collapsedGroups, group.id] });
    else onLayout({ ...layout, visibility: { ...layout.visibility, [`presentation-group:${view}:${name}`]: isCollapsed(name) } });
  };
  const decorate = useCallback((positions: DesignLayout['positions']): Node[] => {
    const result: Node[] = [];
    const groups = new Map<string, DesignNode[]>();
    for (const node of visible) {
      const name = groupName(node, view, graph);
      const disposition = ['Boundary', 'Network', 'Context'].includes(view) ? boundaryDisposition(node) : node.boundaryDisposition;
      if (name) groups.set(name, [...groups.get(name) ?? [], node]);
      if (!isCollapsed(name)) result.push({
        id: node.id, position: positions[node.id] ?? { x: 0, y: 0 },
        data: { label: <><strong>{node.label}</strong>
          <span>{view === 'Context' ? node.kind === 'System' ? 'Information system under ATO review' : contextEntityDetails(node).join(' · ')
            : view === 'Boundary' ? boundaryNodeDetails(node).join(' · ') : view === 'Logical' ? logicalDetails(node).join(' · ')
              : view === 'DataFlows' ? dataFlowDetails(node).join(' · ') : view === 'Network' ? networkDetails(node).join(' · ')
                : view === 'AzureDeployment' ? deploymentDetails(node, graph.nodes, graph.edges).join(' · ') : nodeDetails(node).join(' · ')}</span>
          <small>{disposition === 'InBoundary'
            ? ['Boundary', 'Network'].includes(view) ? 'Included design scope · authorization coverage unverified' : 'In boundary'
            : disposition === 'OutOfBoundary' ? 'External' : 'Boundary undetermined'}</small>
          <small>Source review: {node.source?.reviewState ?? node.reviewState}</small>
          {view === 'Context' && propertyValue(node, 'contextActivities') && <small>{propertyValue(node, 'contextActivities')}</small>}
          {view === 'AzureDeployment' && <small title={propertyValue(node, 'AzureResourceId', 'resourceId')}>
            Subscription: {deploymentScope(node, graph.nodes, graph.edges)?.properties.subscriptionId ?? azureScope(node).subscription ?? 'Not recorded'} · Resource group: {azureScope(node).resourceGroup ?? 'Not recorded'}
          </small>}
        </> },
        className: `sd-graph-node sd-boundary-${disposition.toLowerCase()}${view === 'Context' && node.kind === 'System' ? ' sd-context-center' : ''}${view === 'DataFlows' ? ` sd-dfd-${dataFlowRole(node).toLowerCase()}` : ''}`,
        ariaLabel: `${node.label}, ${(view === 'Logical' ? logicalDetails(node) : view === 'DataFlows' ? dataFlowDetails(node) : view === 'Network' ? networkDetails(node)
          : view === 'AzureDeployment' ? deploymentDetails(node, graph.nodes, graph.edges) : nodeDetails(node)).join(', ')}, ${disposition}, ${node.reviewState}`,
        sourcePosition: 'right' as Node['sourcePosition'], targetPosition: 'left' as Node['targetPosition'],
      });
    }
    if (view === 'Context') {
      const center = visible.find(node => node.kind === 'System');
      if (center) {
        const point = positions[center.id] ?? { x: 0, y: 0 };
        result.unshift({ id: 'context-system-frame', type: 'group', position: { x: point.x - 24, y: point.y - 50 },
          data: { label: `System context scope · ${graph.governanceStatus === 'Approved' ? 'approved' : 'working'} design revision ${graph.revision}` },
          draggable: false, selectable: false, focusable: false, className: 'sd-graph-group',
          style: { width: 350, height: 270 } });
      }
    }
    if (view === 'Boundary' || view === 'Network' || view === 'AzureDeployment' || view === 'Logical' || view === 'DataFlows') {
      for (const [name, members] of groups) {
        const points = members.map(node => positions[node.id] ?? { x: 0, y: 0 });
        const minX = Math.min(...points.map(point => point.x)) - 24;
        const minY = Math.min(...points.map(point => point.y)) - 45;
        result.unshift({
          id: `group:${name}`, position: { x: minX, y: minY }, type: 'group',
          data: { label: `${name} · ${graph.governanceStatus === 'Approved' ? 'approved' : 'working'} revision ${graph.revision}` },
          draggable: false, selectable: false, focusable: false, className: 'sd-graph-group',
          style: { width: Math.max(...points.map(point => point.x)) - minX + 254,
            height: isCollapsed(name) ? 45 : Math.max(...points.map(point => point.y)) - minY + 165 },
        });
      }
    }
    return result;
  }, [visible, view, graph.nodes, graph.edges, graph.groups, graph.governanceStatus, graph.revision, isCollapsed]);
  const applyLayoutPositions = useCallback((positions: DesignLayout['positions']) => {
    const next = decorate(positions);
    setNodes(current => {
      const prior = new Map(current.map(node => [node.id, node]));
      return next.map(node => {
        const old = prior.get(node.id);
        // React Flow discards handle bounds when a controlled node loses its measurements.
        return old?.type === node.type && old?.measured ? { ...node, measured: old.measured } : node;
      });
    });
  }, [decorate]);
  useEffect(() => {
    const current = ++generation.current;
    if (overBudget) { setNodes([]); return; }
    setArranging(true);
    setLayoutError('');
    const source = visible.map(node => ({ ...node, groupId: groupName(node, view, graph) }));
    layoutDesign(source, architecture.edges, view, layout.version === 0 && pristine ? {} : layout.positions).then(positions => {
      if (current !== generation.current) return;
      applyLayoutPositions(positions);
      if (layout.version === 0 && pristine) initialPositions.current(positions);
      setArranging(false);
    }).catch(error => {
      if (current !== generation.current) return;
      setLayoutError(error instanceof Error ? error.message : 'Automatic layout unavailable. Use the structured records below.');
      setArranging(false);
    });
    return () => { generation.current++; };
  }, [visible, architecture.edges, view, layout.positions, layout.collapsedGroups, layout.version, pristine, overBudget, applyLayoutPositions]);
  const nodeSignature = nodes.map(node => node.id).join('\n');
  useEffect(() => {
    if (!arranging && nodeSignature) updateNodeInternals(nodeSignature.split('\n'));
  }, [arranging, nodeSignature, updateNodeInternals]);
  useEffect(() => {
    if (nodesInitialized && !arranging && pristine && layout.version === 0 && nodes.length > 0)
      void flow.fitView({ padding: .2, minZoom: .8 });
  }, [nodesInitialized, arranging, pristine, layout.version, nodes.length, flow]);
  const edges: Edge[] = useMemo(() => {
    const ids = new Set(nodes.map(node => node.id));
    return architecture.edges.filter(edge => ids.has(edge.sourceNodeId) && ids.has(edge.targetNodeId)
      && (view !== 'Network' || showNetworkAssociations || !isNetworkAssociation(edge))).map((edge: DesignEdge) => {
      const origin = relationshipOrigin(edge);
      const label = flowLabel(edge, view);
      return {
      id: edge.id, source: edge.sourceNodeId, target: edge.targetNodeId,
      type: ['straight', 'step', 'smoothstep'].includes(layout.edgeRouting[edge.id] ?? '') ? layout.edgeRouting[edge.id] : 'default',
      label: `${label} · ${origin.shortLabel}`, style: { strokeDasharray: origin.pattern || undefined, strokeWidth: 1.7 },
      markerEnd: isNonTechnicalInteraction(edge) && view !== 'Logical' ? undefined : { type: MarkerType.ArrowClosed },
      markerStart: edge.direction === 'Bidirectional' && !isNonTechnicalInteraction(edge) ? { type: MarkerType.ArrowClosed } : undefined,
      selected: selected === edge.id, focusable: true, ariaLabel: `${origin.label}; ${edge.reviewState}; ${edge.relationshipType}: ${edge.purpose || 'Purpose not recorded'}`,
    }; });
  }, [nodes, architecture.edges, view, selected, layout.edgeRouting, showNetworkAssociations]);
  const reset = async (clearPreferences = false) => {
    const current = ++generation.current;
    setArranging(true);
    try {
      const source = clearPreferences && architecture.nodes.length <= GRAPH_NODE_LIMIT ? architecture.nodes : visible;
      const positions = await layoutDesign(source.map(node => ({ ...node, groupId: groupName(node, view, graph) })), architecture.edges, view, {});
      if (current !== generation.current) return;
      applyLayoutPositions(positions);
      onLayout({ ...presentation.current, positions, viewport: { x: 0, y: 0, zoom: 1 },
        ...(clearPreferences ? { collapsedGroups: [], visibility: {}, edgeRouting: {} } : {}) });
      setLayoutError('');
    } catch (error) { setLayoutError(error instanceof Error ? error.message : 'Automatic layout failed.'); }
    finally { if (current === generation.current) setArranging(false); }
  };
  if (overBudget) return <div className="sd-notice" role="status">This view contains {visible.length} elements and {architecture.edges.length} relationships.
    Interactive rendering is limited to {GRAPH_NODE_LIMIT} elements / {GRAPH_EDGE_LIMIT} relationships. Narrow the search or use the complete paginated structured records below; no source records have been dropped.</div>;
  return <section aria-label="Interactive design graph">
    {view === 'Network' && <section aria-label="Network connection status">
      <label><input type="checkbox" checked={showNetworkAssociations} onChange={event => setShowNetworkAssociations(event.target.checked)} /> Show recorded associations (not network traffic)</label>
      {architecture.edges.filter(isDataFlow).length === 0 && <p className="sd-notice" role="status">
        No technical network interfaces are documented in this design. Dashed links show recorded membership, service use or access only; they do not establish routes, ports or data flows. Use Add network interface to document a known exchange.
      </p>}
      <p className="sd-muted">{architecture.edges.filter(isDataFlow).length} technical interfaces · {architecture.edges.filter(isNetworkAssociation).length} recorded associations. This switch changes display only; it does not save or change the approved design.</p>
    </section>}
    {view === 'Boundary' && <section aria-label="Boundary scope and authorization">
      <p className="sd-muted">Named boxes show recorded design scope, not automatic authorization coverage. Included hardware/software/network resources are distinct from external systems, shared services, unknown scope, people and hosting references.</p>
      <p className="sd-muted">CSP linkage does not establish inclusion, accepted inheritance, ATO or cATO. Multiple named scopes do not establish separate authorizations. SSP and the recorded AO decision must substantiate the final scope.</p>
      <p className="sd-notice">Component-to-decision coverage is not verified by current records. Reviewed design inclusion is not an authorization decision.</p>
      {graph.nodes.filter(node => node.kind === 'AuthorizationScope').length === 0
        ? <p className="sd-muted">No authorization decision reference is recorded in this design.</p>
        : <ul>{graph.nodes.filter(node => node.kind === 'AuthorizationScope').map(record => <li key={record.id}>
          <button type="button" className="sd-text-button" onClick={() => onSelect(record.id)}>Inspect {record.label}</button>
          <p className="sd-muted">Decision date: {propertyValue(record, 'DecisionDate') ?? 'Not recorded'} · Expiration: {propertyValue(record, 'ExpirationDate') ?? 'Not recorded'} · Individual component coverage: not verified</p>
        </li>)}</ul>}
      {layout.version > 0 && <p className="sd-muted">Saved placement is retained. Use Automatic layout and Save presentation to adopt the named boundary arrangement.</p>}
    </section>}
    {view === 'Context' && <div className="sd-context-guidance">
      <p className="sd-muted">High-level ATO context: the central system summarizes internal implementation. External performers, systems and support services include CSP and non-CSP records equally.</p>
      <p className="sd-muted">Data/service/resource flows retain their recorded purpose and original endpoints in the inspector. Dashed assignments and scope associations are not technical traffic or accepted inheritance.</p>
      <p className="sd-muted">Context framing is not an authorization decision or a substitute for reviewed Boundary records. This is an SV-1-aligned overview, not formal DoDAF compliance certification.</p>
      {layout.version > 0 && <p className="sd-muted">Your saved Context placement is retained. Use Automatic layout to center the system and arrange the external participants, then Save presentation if you want to retain that placement.</p>}
      {!architecture.nodes.some(node => node.kind === 'System') && <p role="alert" className="sd-notice">The recorded central system is missing. Restore its canonical identity before using the context diagram.</p>}
      {architecture.nodes.filter(node => node.kind !== 'System' && !architecture.edges.some(edge => edge.sourceNodeId === node.id || edge.targetNodeId === node.id)).length > 0 &&
        <p className="sd-notice">Some external candidates have no recorded context interaction. Their presence alone does not establish an exchange; inspect their source and record what they do.</p>}
    </div>}
    {view === 'DataFlows' && architecture.edges.length === 0 && <p className="sd-notice">
      No documented data flows are recorded. Membership, hosting and access associations are shown in the other views; they do not establish data exchange.
    </p>}
    {view === 'AzureDeployment' && architecture.nodes.length === 0 && <p className="sd-notice">
      No attached Azure environment or exact ARM resource identity is recorded, and no scoped provider/SACA deployment references are present. Record authorized scope in Environment &amp; hosting; a provider name alone does not establish deployment.
    </p>}
    {graph.nodes.length > architecture.nodes.length && <p className="sd-muted">
      {graph.nodes.length - architecture.nodes.length} source or contextual records are retained in structured records, not drawn as architecture boxes in this view.
    </p>}
    <div className="sd-canvas-tools">
      <button type="button" className="sd-button" onClick={() => flow.fitView({ padding: .2 })}>Fit to view</button>
      <button type="button" className="sd-button" disabled={arranging} onClick={() => void reset()}>Automatic layout</button>
      <button type="button" className="sd-button" disabled={arranging} onClick={() => void reset(true)}>Reset layout</button>
      <span className="sd-muted">{editable ? 'Drag between connection handles to propose a flow. Double-click an element to inspect it.' : 'Read-only diagram.'} Pan to explore; structured records remain available below.</span>
    </div>
    {(view === 'Boundary' || view === 'Network' || view === 'AzureDeployment') && <div className="sd-groups" aria-label="Diagram groups">
      {[...new Set(visible.map(node => groupName(node, view, graph)))].map(group => <button key={group} type="button" className="sd-button"
        aria-expanded={!isCollapsed(group)} onClick={() => toggleGroup(group)}>
        {isCollapsed(group) ? 'Expand' : 'Collapse'} {group}
      </button>)}
    </div>}
    {graph.nodes.some(node => layout.visibility[node.id] === false) && <p className="sd-notice">
      {graph.nodes.filter(node => layout.visibility[node.id] === false).length} elements hidden by presentation preferences. All remain in the structured records.
      <button className="sd-button" onClick={() => onLayout({ ...layout, visibility: {} })}>Show all elements</button>
    </p>}
    {layoutError && <p role="alert" className="sd-notice">{layoutError} <button onClick={() => void reset()}>Retry layout</button></p>}
    {arranging && <p role="status">Arranging diagram…</p>}
    <div className="sd-canvas" data-testid="design-canvas" aria-busy={arranging || nodes.length > 0 && !nodesInitialized}
      data-relationship-count={edges.length} data-measured-node-count={nodes.filter(node => node.measured?.width && node.measured?.height).length}>
      <ReactFlow nodes={nodes.map(node => ({ ...node, selected: selected === node.id }))} edges={edges} nodeTypes={nodeTypes}
        colorMode={dark ? 'dark' : 'light'} fitView fitViewOptions={{ padding: .2, minZoom: .8 }} minZoom={.15} maxZoom={2}
        defaultViewport={layout.viewport} nodesConnectable={editable} nodesDraggable={editable} deleteKeyCode={editable ? ['Backspace', 'Delete'] : null}
        onConnect={connection => {
          if (editable && graph.nodes.some(node => node.id === connection.source) && graph.nodes.some(node => node.id === connection.target))
            onConnect(connection.source, connection.target);
        }}
        onBeforeDelete={async ({ nodes: deletingNodes, edges: deletingEdges }) => {
          const record = deletingNodes.find(node => node.type !== 'group') ?? deletingEdges[0];
          if (editable && record) onRemove(record.id);
          return false;
        }}
        onNodeDoubleClick={(_event, node) => onSelect(node.id)}
        onNodeClick={(_event, node) => onSelect(node.id)} onEdgeClick={(_event, edge) => onSelect(edge.id)}
        onNodesChange={changes => {
          const chosen = changes.find(change => change.type === 'select' && change.selected);
          if (chosen && 'id' in chosen) onSelect(chosen.id);
          setNodes(current => applyNodeChanges(changes, current));
        }}
        onEdgesChange={changes => {
          const chosen = changes.find(change => change.type === 'select' && change.selected);
          if (chosen && 'id' in chosen) onSelect(chosen.id);
        }}
        onNodeDragStop={(_event, node) => onLayout({ ...presentation.current, positions: {
          ...presentation.current.positions,
          ...Object.fromEntries(nodes.filter(item => item.type !== 'group').map(item => [item.id, item.position])), [node.id]: node.position,
        } })}
        onMoveEnd={(event, viewport) => { if (event && editable) onLayout({ ...presentation.current, viewport }); }}>
        <Background /><Controls showInteractive={false} />
      </ReactFlow>
    </div>
    <p className="sd-muted">Legend: boundary disposition and review state are written on each element. Arrows show recorded direction; observations are not approved flows.</p>
    {view === 'AzureDeployment' && <section aria-label="SACA deployment legend">
      <h3>SACA/SCCA deployment context</h3>
      <ul>{SACA_ZONES.map(zone => <li key={zone}>{deploymentZoneLabel(zone)}: {architecture.nodes.filter(n => n.sacaRole !== 'TCCM' && deploymentZone(n) === zone).length} recorded elements</li>)}</ul>
      <p className="sd-notice">Not recorded / applicability requires review: {SACA_REFERENCE_ROLES.filter(role => !architecture.nodes.some(n => n.sacaRole === role)).join(', ') || 'All role categories have records; implementation/authority still requires review'}. No reference component is automatically implemented.</p>
      {!architecture.nodes.some(n => deploymentScope(n, graph.nodes, graph.edges)?.properties.cloud === 'AzureUSGovernment') &&
        <p className="sd-notice">No exact recorded Azure Government cloud scope is available in this design. Commercial, provider-only or proposed records must not be presented as a verified Government deployment.</p>}
      <p className="sd-muted">BCAP protects DISN; VDSS supports workload security; VDMS supports host/shared services; CNAP is separately recorded identity-based access. TCCM is a business performer appointed by the AO, not a vault/appliance. Appointment and credential management plan remain unverified.</p>
      <p className="sd-muted">Azure Government identity must come from an exact recorded cloud scope. CSP references without scope remain undetermined; organization-managed/non-CSP components use the same rules. Roles, control descriptions and claimed IL do not establish SCCA compliance, inherited responsibility, healthy monitoring or authorization.</p>
      <p className="sd-muted">Technical exchanges have arrows and PPS/protection details. Dashed source associations and governance are not traffic. Saved placement remains unchanged until Automatic layout and Save presentation.</p>
      <a href="https://learn.microsoft.com/en-us/azure/azure-government/compliance/secure-azure-computing-architecture" target="_blank" rel="noopener noreferrer">Microsoft SACA reference guidance</a>
    </section>}
    {view === 'Network' && <section aria-label="Network architecture legend">
      <h3>SV-1/SV-2-aligned network scope and interfaces</h3>
      <p className="sd-muted">Frames separate named included design scope, outside/shared/separately authorized dependencies and undetermined scope, then environment, zone and network segment. User endpoints are not computing inventory. Technical interfaces have arrowheads; source-recorded associations are dashed, without arrows, and are not network paths.</p>
      <p className="sd-muted">Labels show PPS, protocol stack, data/protection, crossing and agreement. Device role, IP/CIDR, claimed hosting IL, DISN medium and standards/control references require evidence review; they do not prove authorization, accepted inheritance, standards compliance or healthy monitoring.</p>
      <h4>Recorded hosting context (not network devices)</h4>
      <ul>{graph.nodes.filter(n => n.kind === 'Environment').map(n => <li key={n.id}>{n.label} · {n.source?.type ?? 'Source not recorded'} · {n.source?.reviewState ?? n.reviewState}</li>)}</ul>
      {!graph.nodes.some(n => n.kind === 'Environment') && <p className="sd-muted">No hosting context recorded; standalone/non-CSP inventory is still shown.</p>}
    </section>}
    {view === 'DataFlows' && <section aria-label="Data flow legend">
      <h3>SV-4-aligned system functions and information exchanges</h3>
      <p className="sd-muted">[F] Function box · [DS] Open data-store rectangle · [E] External producer/consumer box · [?] Endpoint role not mapped. Arrows show recorded producer-to-consumer identities; inbound/outbound describes system scope, not reversed endpoints. Bidirectional exchanges have two arrows.</p>
      <p className="sd-muted">Named scope frames are recorded design inclusion, not verified authorization coverage. CSP hosting/subscription links and logical/governance associations are not data flows. Missing function, lifecycle, retention or disposal details require review; no formal DoDAF, FedRAMP or CMMC conformance is asserted.</p>
    </section>}
    {view === 'Logical' && <section aria-label="Logical architecture legend">
      <h3>DM2-aligned system records</h3>
      <p className="sd-muted">[P] Performer · [A] Activity · [D] Information and Data · [R] Rule · [G] Goal · [C] Capability · [S] Service · [J] Project · [Ref] Scope reference. These are application symbols, not a claim of standardized DoDAF notation or PES conformance.</p>
      <p className="sd-muted">Predicates explain performs, provides, supports, governs, enables, produces, consumes and realizes. Realizes records refinement (reification). Associations are not traffic. CSP subscription and hosting references do not establish inheritance, implementation or authorization.</p>
      <p className="sd-muted">Not recorded: {LOGICAL_TYPES.filter(type => !graph.nodes.some(node => logicalType(node) === type)).join(', ') || 'All construct categories represented; relationships still require review'}.</p>
    </section>}
    {view === 'Boundary' && <ul className="sd-boundary-legend" aria-label="Authorization boundary legend">
      <li>Included design resources: hardware, software and network segments in the labelled scope box; authorization coverage unverified.</li>
      <li>External systems / interconnections: outside or separate/shared ownership, with actual recorded technical interfaces.</li>
      <li>Undetermined scope: unresolved membership, not assumed inclusion.</li>
      <li>Actors and hosting references: documentation context, not authorized computing components.</li>
      <li>Arrows: technical data/service/resource flows; source-recorded associations and governance are not packet flows.</li>
    </ul>}
    <p className="sd-muted">Starting zoom favors readable labels. Pan to inspect the design, or use Fit to view to show every element.</p>
    {view === 'Context' && <section aria-label="Context constraints">
      <h3>Recorded policies, standards &amp; constraints</h3>
      {graph.nodes.filter(isContextConstraint).length === 0
        ? <p className="sd-muted">No policy or constraint reference is recorded in this design. Record applicable references in Policies or add a governed constraint draft; do not assume applicability from a standard's name.</p>
        : <ul className="sd-context-references">{graph.nodes.filter(isContextConstraint).map(record => <li key={record.id}>
          <button type="button" className="sd-text-button" onClick={() => onSelect(record.id)}>Inspect constraint {record.label}</button>
          <p className="sd-muted">{propertyValue(record, 'rationale') ?? 'Rationale not recorded'} · {propertyValue(record, 'retention') ?? record.reviewState} · Applicability approval not established</p>
        </li>)}</ul>}
    </section>}
    <ul className="sd-edge-legend" aria-label="Relationship origin legend">{Object.values(RELATIONSHIP_ORIGINS).map(origin =>
      <li key={origin.label}><svg viewBox="0 0 50 10" aria-hidden="true"><line x1="0" y1="5" x2="50" y2="5" stroke="currentColor" strokeWidth="2" strokeDasharray={origin.pattern || undefined} /></svg>
        <span>{origin.label}</span></li>)}</ul>
  </section>;
}
