import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { applyNodeChanges, Background, Controls, MarkerType, ReactFlow, ReactFlowProvider, useNodesInitialized, useReactFlow, useUpdateNodeInternals, type Node, type Edge } from '@xyflow/react';
import type { DesignEdge, DesignLayout, DesignNode, SystemDesignGraph } from '../../api/systemDesign';
import { GRAPH_EDGE_LIMIT, GRAPH_NODE_LIMIT, architectureForView, isRecordedAssociation, layoutDesign, relationshipLabel, relationshipOrigin, RELATIONSHIP_ORIGINS, type DesignView } from './graphAdapter';
import '@xyflow/react/dist/style.css';

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
  if (view === 'Boundary') return node.boundaryDisposition === 'InBoundary' ? 'Authorization boundary · In boundary'
    : node.boundaryDisposition === 'OutOfBoundary' ? 'Outside authorization boundary' : 'Boundary undetermined';
  if (view === 'Network') return [node.kind === 'Environment' ? node.label : node.environment || 'Environment not recorded',
    node.properties.subscriptionId || node.properties.SubscriptionId, node.properties.resourceGroup || node.properties.ResourceGroup,
    node.networkZone || 'Zone not recorded', node.properties.tier || node.properties.Tier, node.provider].filter(Boolean).join(' / ');
  return graph.groups.find(group => group.nodeIds.includes(node.id))?.label ?? '';
}
function Canvas({ graph, layout, view, search, filter, selected, editable, pristine, onSelect, onLayout, onInitialPositions, onConnect, onRemove }: CanvasProps) {
  const flow = useReactFlow();
  const nodesInitialized = useNodesInitialized();
  const updateNodeInternals = useUpdateNodeInternals();
  const [nodes, setNodes] = useState<Node[]>([]);
  const [layoutError, setLayoutError] = useState('');
  const [arranging, setArranging] = useState(false);
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
      if (name) groups.set(name, [...groups.get(name) ?? [], node]);
      if (!isCollapsed(name)) result.push({
        id: node.id, position: positions[node.id] ?? { x: 0, y: 0 },
        data: { label: <><strong>{node.label}</strong><span>{node.kind} · {node.boundaryDisposition}</span><small>{node.reviewState}</small></> },
        className: 'sd-graph-node', ariaLabel: `${node.label}, ${node.kind}, ${node.boundaryDisposition}, ${node.reviewState}`,
        sourcePosition: 'right' as Node['sourcePosition'], targetPosition: 'left' as Node['targetPosition'],
      });
    }
    if (view === 'Boundary' || view === 'Network') {
      for (const [name, members] of groups) {
        const points = members.map(node => positions[node.id] ?? { x: 0, y: 0 });
        const minX = Math.min(...points.map(point => point.x)) - 24;
        const minY = Math.min(...points.map(point => point.y)) - 45;
        result.unshift({
          id: `group:${name}`, position: { x: minX, y: minY }, type: 'group', data: { label: name },
          draggable: false, selectable: false, focusable: false, className: 'sd-graph-group',
          style: { width: Math.max(...points.map(point => point.x)) - minX + 234,
            height: isCollapsed(name) ? 45 : Math.max(...points.map(point => point.y)) - minY + 118 },
        });
      }
    }
    return result;
  }, [visible, view, graph.groups, isCollapsed]);
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
      void flow.fitView({ padding: .2 });
  }, [nodesInitialized, arranging, pristine, layout.version, nodes.length, flow]);
  const edges: Edge[] = useMemo(() => {
    const ids = new Set(nodes.map(node => node.id));
    return architecture.edges.filter(edge => ids.has(edge.sourceNodeId) && ids.has(edge.targetNodeId)).map((edge: DesignEdge) => {
      const origin = relationshipOrigin(edge);
      const label = isRecordedAssociation(edge) ? relationshipLabel(edge)
        : view === 'Network' ? [edge.port, edge.protocol, edge.service].filter(Boolean).join(' / ') || 'PPS not recorded'
        : view === 'DataFlows' ? `${edge.informationType || 'Data not recorded'} · ${edge.protection || 'Protection not recorded'}` : relationshipLabel(edge);
      return {
      id: edge.id, source: edge.sourceNodeId, target: edge.targetNodeId,
      type: ['straight', 'step', 'smoothstep'].includes(layout.edgeRouting[edge.id] ?? '') ? layout.edgeRouting[edge.id] : 'default',
      label: `${label} · ${origin.shortLabel}`, style: { strokeDasharray: origin.pattern || undefined, strokeWidth: 1.7 },
      markerEnd: { type: MarkerType.ArrowClosed }, markerStart: edge.direction === 'Bidirectional' ? { type: MarkerType.ArrowClosed } : undefined,
      selected: selected === edge.id, focusable: true, ariaLabel: `${origin.label}; ${edge.reviewState}; ${edge.relationshipType}: ${edge.purpose || 'Purpose not recorded'}`,
    }; });
  }, [nodes, architecture.edges, view, selected, layout.edgeRouting]);
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
    {view === 'DataFlows' && architecture.edges.length === 0 && <p className="sd-notice">
      No documented data flows are recorded. Membership, hosting and access associations are shown in the other views; they do not establish data exchange.
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
    {(view === 'Boundary' || view === 'Network') && <div className="sd-groups" aria-label="Diagram groups">
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
    <div className="sd-canvas" data-testid="design-canvas" aria-busy={arranging || !nodesInitialized}
      data-relationship-count={edges.length} data-measured-node-count={nodes.filter(node => node.measured?.width && node.measured?.height).length}>
      <ReactFlow nodes={nodes.map(node => ({ ...node, selected: selected === node.id }))} edges={edges}
        colorMode={dark ? 'dark' : 'light'} fitView minZoom={.15} maxZoom={2}
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
    <ul className="sd-edge-legend" aria-label="Relationship origin legend">{Object.values(RELATIONSHIP_ORIGINS).map(origin =>
      <li key={origin.label}><svg viewBox="0 0 50 10" aria-hidden="true"><line x1="0" y1="5" x2="50" y2="5" stroke="currentColor" strokeWidth="2" strokeDasharray={origin.pattern || undefined} /></svg>
        <span>{origin.label}</span></li>)}</ul>
  </section>;
}
