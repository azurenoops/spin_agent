import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';
import type { ReactFlowProps } from '@xyflow/react';
import DesignCanvas from '../../features/system-design/DesignCanvas';
import { designFixture, designLayoutFixture } from './fixtures';
import type { DesignLayout } from '../../api/systemDesign';

const state = vi.hoisted(() => ({ props: null as ReactFlowProps | null, fit: vi.fn(), layout: vi.fn() }));
vi.mock('@xyflow/react', async importOriginal => ({
  ...await importOriginal<typeof import('@xyflow/react')>(),
  ReactFlowProvider: ({ children }: { children: ReactNode }) => <>{children}</>,
  ReactFlow: (props: ReactFlowProps) => { state.props = props; return <div data-testid="renderer">
    {props.nodes?.map(node => <span key={node.id}>{String(node.data.label && node.type === 'group' ? node.data.label : node.id)}</span>)}
  </div>; },
  useReactFlow: () => ({ fitView: state.fit }),
  useNodesInitialized: () => true,
  useUpdateNodeInternals: () => vi.fn(),
  Background: () => null, Controls: () => null, MarkerType: { ArrowClosed: 'arrowclosed' },
}));
vi.mock('../../features/system-design/graphAdapter', async importOriginal => ({
  ...await importOriginal<typeof import('../../features/system-design/graphAdapter')>(), layoutDesign: state.layout,
}));
beforeEach(() => {
  vi.clearAllMocks(); document.documentElement.classList.remove('dark');
  state.layout.mockImplementation(async (nodes: { id: string }[], _edges: unknown, _view: string, positions: DesignLayout['positions']) =>
    Object.fromEntries(nodes.map((node, index) => [node.id, positions[node.id] ?? { x: index * 300, y: 80 }])));
});
function setup(options: { view?: 'Context' | 'Boundary' | 'Network' | 'DataFlows'; layout?: DesignLayout; pristine?: boolean; large?: boolean } = {}) {
  const graph = designFixture();
  if (options.large) graph.nodes = Array.from({ length: 301 }, (_, index) => ({ ...graph.nodes[0]!, id: `node-${index}` }));
  const onLayout = vi.fn(); const onSelect = vi.fn(); const onInitialPositions = vi.fn(); const onConnect = vi.fn(); const onRemove = vi.fn();
  const props = { graph, view: options.view ?? 'Context' as const, layout: options.layout ?? designLayoutFixture(),
    search: '', filter: '', selected: null, editable: true, pristine: options.pristine ?? true, onLayout, onSelect, onInitialPositions, onConnect, onRemove };
  return { ...render(<DesignCanvas {...props} />), props, onLayout, onSelect, onInitialPositions, onConnect, onRemove };
}
describe('System design renderer adapter', () => {
  it('preserves positions for filtered or hidden nodes when a visible node is dragged', async () => {
    // Arrange
    const result = setup({ layout: { ...designLayoutFixture(), version: 2,
      positions: { system: { x: 15, y: 20 }, storage: { x: 701, y: 333 } }, visibility: { storage: false } } });
    await waitFor(() => expect(state.props?.nodes?.length).toBe(1));
    // Act
    act(() => state.props!.onNodeDragStop?.({} as never, { ...state.props!.nodes![0]!, position: { x: 44, y: 55 } }, []));
    // Assert
    expect(result.onLayout).toHaveBeenCalledWith(expect.objectContaining({
      positions: { system: { x: 44, y: 55 }, storage: { x: 701, y: 333 } },
    }));
  });
  it('retains measured handle geometry when the layout is recalculated for existing nodes', async () => {
    // Arrange
    const result = setup();
    await waitFor(() => expect(state.props?.nodes?.length).toBe(2));
    act(() => { state.props!.onNodesChange?.([{ type: 'dimensions', id: 'system', dimensions: { width: 210, height: 90 } }]); });
    expect(state.props!.nodes!.find(node => node.id === 'system')?.measured).toEqual({ width: 210, height: 90 });
    // Act
    result.rerender(<DesignCanvas {...result.props} layout={{ ...result.props.layout, version: 1,
      positions: { system: { x: 17, y: 21 } } }} />);
    // Assert
    await waitFor(() => expect(state.props!.nodes!.find(node => node.id === 'system')?.position).toEqual({ x: 17, y: 21 }));
    expect(state.props!.nodes!.find(node => node.id === 'system')?.measured).toEqual({ width: 210, height: 90 });
  });
  it('opens draft connections from handles and refuses pointer mutation in read-only mode', async () => {
    // Arrange
    const result = setup();
    await waitFor(() => expect(state.props?.nodes?.length).toBe(2));
    // Act
    act(() => { state.props?.onConnect?.({ source: 'system', target: 'storage', sourceHandle: null, targetHandle: null }); });
    // Assert
    expect(state.props?.nodesConnectable).toBe(true);
    expect(result.onConnect).toHaveBeenCalledWith('system', 'storage');
    result.rerender(<DesignCanvas {...result.props} editable={false} />);
    expect(state.props?.nodesConnectable).toBe(false);
    act(() => { state.props?.onConnect?.({ source: 'storage', target: 'system', sourceHandle: null, targetHandle: null }); });
    expect(result.onConnect).toHaveBeenCalledTimes(1);
  });
  it('replaces unsaved placeholder grids with view layout and honors saved manual positions', async () => {
    // Arrange
    const layout = { ...designLayoutFixture(), positions: { system: { x: 900, y: 800 } } };
    const result = setup({ layout });
    // Act
    await waitFor(() => expect(result.onInitialPositions).toHaveBeenCalled());
    // Assert
    expect(state.layout).toHaveBeenLastCalledWith(expect.any(Array), expect.any(Array), 'Context', {});
    const saved = { ...layout, version: 3 };
    result.rerender(<DesignCanvas {...result.props} layout={saved} />);
    await waitFor(() => expect(state.layout).toHaveBeenLastCalledWith(expect.any(Array), expect.any(Array), 'Context', saved.positions));
  });
  it('supports fit, layout reset, node/edge selection, keyboard selection and manual presentation updates', async () => {
    // Arrange
    const result = setup();
    await waitFor(() => expect(state.props?.nodes?.length).toBe(2));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Fit to view' }));
    act(() => {
      state.props!.onNodeClick?.({} as never, state.props!.nodes![0]!);
      state.props!.onEdgeClick?.({} as never, state.props!.edges![0]!);
      state.props!.onNodesChange?.([{ type: 'select', id: 'system', selected: true }]);
      state.props!.onEdgesChange?.([{ type: 'select', id: 'flow', selected: true }]);
      state.props!.onNodeDragStop?.({} as never, { ...state.props!.nodes![0]!, position: { x: 27, y: 42 } }, []);
      state.props!.onMoveEnd?.({} as never, { x: 10, y: 20, zoom: 1.4 });
    });
    // Assert
    expect(state.fit).toHaveBeenCalledWith({ padding: .2 });
    expect(result.onSelect).toHaveBeenCalledWith('system');
    expect(result.onSelect).toHaveBeenCalledWith('flow');
    expect(result.onLayout).toHaveBeenCalledWith(expect.objectContaining({ viewport: { x: 10, y: 20, zoom: 1.4 } }));
    fireEvent.click(screen.getByRole('button', { name: 'Reset layout' }));
    await waitFor(() => expect(result.onLayout).toHaveBeenCalledWith(expect.objectContaining({ collapsedGroups: [], visibility: {}, edgeRouting: {} })));
  });
  it('keeps computed grouping presentation-only and persists explicit collapsed canonical group IDs', async () => {
    // Arrange
    const result = setup({ view: 'Boundary' });
    await waitFor(() => expect(state.props?.nodes?.length).toBe(4));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Collapse Authorization boundary · In boundary' }));
    // Assert
    expect(result.onLayout).toHaveBeenLastCalledWith(expect.objectContaining({
      collapsedGroups: [], visibility: { 'presentation-group:Boundary:Authorization boundary · In boundary': false },
    }));
    const collapsed = result.onLayout.mock.lastCall![0];
    result.rerender(<DesignCanvas {...result.props} layout={collapsed} />);
    await waitFor(() => expect(state.props?.nodes?.some(node => node.id === 'system')).toBe(false));
    fireEvent.click(screen.getByRole('button', { name: 'Expand Authorization boundary · In boundary' }));
    expect(result.onLayout).toHaveBeenLastCalledWith(expect.objectContaining({
      visibility: { 'presentation-group:Boundary:Authorization boundary · In boundary': true },
    }));
    const graph = { ...result.props.graph, groups: [{ id: 'canonical-boundary', label: 'Authorization boundary · In boundary', kind: 'Boundary', nodeIds: ['system'] }] };
    result.rerender(<DesignCanvas {...result.props} graph={graph} />);
    await waitFor(() => expect(screen.queryByText('Arranging diagram…')).not.toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Collapse Authorization boundary · In boundary' }));
    expect(result.onLayout).toHaveBeenLastCalledWith(expect.objectContaining({ collapsedGroups: ['canonical-boundary'] }));
  });
  it('retains complete-table fallback at the node budget without invoking expensive layout', () => {
    // Arrange
    setup({ large: true });
    // Act / Assert
    expect(screen.getByRole('status')).toHaveTextContent('301 elements');
    expect(screen.getByRole('status')).toHaveTextContent('no source records have been dropped');
    expect(state.layout).not.toHaveBeenCalled();
  });
  it('does not draw profile sections and PPS as architecture elements or let them consume layout budget', async () => {
    // Arrange
    const result = setup();
    const sources = Array.from({ length: 350 }, (_, index) => ({ ...result.props.graph.nodes[0]!,
      id: `profile-${index}`, label: `Profile source ${index}`, kind: 'ProfileSection' }));
    // Act
    result.rerender(<DesignCanvas {...result.props} graph={{ ...result.props.graph,
      nodes: [...result.props.graph.nodes, ...sources] }} />);
    // Assert
    await waitFor(() => expect(state.props?.nodes?.length).toBe(2));
    expect(state.layout.mock.lastCall?.[0]).toHaveLength(2);
    expect(screen.queryByText(/Interactive rendering is limited/)).not.toBeInTheDocument();
  });
  it('reports layout failure and retries, then follows application theme and flow labels', async () => {
    // Arrange
    state.layout.mockRejectedValueOnce(new Error('Synthetic layout failure'));
    const result = setup({ view: 'DataFlows' });
    // Act
    expect(await screen.findByRole('alert')).toHaveTextContent('Synthetic layout failure');
    fireEvent.click(screen.getByRole('button', { name: 'Retry layout' }));
    // Assert
    await waitFor(() => expect(result.onLayout).toHaveBeenCalled());
    expect(state.props!.edges![0]!.label).toBe('Mission records · TLS · User-authored');
    act(() => document.documentElement.classList.add('dark'));
    await waitFor(() => expect(state.props!.colorMode).toBe('dark'));
    result.rerender(<DesignCanvas {...result.props} view="Network" />);
    await waitFor(() => expect(state.props!.edges![0]!.label).toBe('443 / TCP / HTTPS · User-authored'));
  });
  it('keeps hidden records explicit, can restore visibility, and preserves edge routing', async () => {
    // Arrange
    const layout = { ...designLayoutFixture(), visibility: { storage: false }, edgeRouting: { flow: 'straight' } };
    const result = setup({ layout });
    // Act
    expect(screen.getByText(/1 elements hidden by presentation preferences/)).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Show all elements' }));
    // Assert
    expect(result.onLayout).toHaveBeenCalledWith(expect.objectContaining({ visibility: {} }));
    result.rerender(<DesignCanvas {...result.props} layout={{ ...layout, visibility: {} }} />);
    await waitFor(() => expect(state.props?.edges?.[0]?.type).toBe('straight'));
  });
});
