import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as api from '../../api/systemDesign';
import GovernedBoundaryInventory from '../../features/systems/GovernedBoundaryInventory';
import SystemBoundaryInventory from '../../features/systems/SystemBoundaryInventory';
import { designFixture } from '../system-design/fixtures';
import type { DesignNode } from '../../api/systemDesign';
import '../helpers/dialog';

vi.mock('../../api/systemDesign', () => ({
  getSystemDesign: vi.fn(), saveSystemDesign: vi.fn(), reconcileSystemDesign: vi.fn(), reviewSystemDesign: vi.fn(),
}));
const graph = () => {
  const result = designFixture();
  result.nodes[1] = { ...result.nodes[1]!, environment: 'Production', deploymentOwner: 'Recorded operations team',
    boundaryRationale: 'Recorded workload scope', boundaryDisposition: 'InBoundary' };
  result.nodes.push({ ...result.nodes[1]!, id: 'provider', label: 'Provider service', kind: 'ProviderReference',
    deploymentOwner: null, boundaryRelationship: 'SharedService', boundaryDisposition: 'OutOfBoundary',
    source: { ...result.nodes[1]!.source!, type: 'BoundaryComponentAssignment', provenance: 'CSP reference' } });
  return result;
};
const mount = () => render(<MemoryRouter><GovernedBoundaryInventory systemId="system-a">
  <p>Existing boundary management</p>
</GovernedBoundaryInventory></MemoryRouter>);
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.getSystemDesign).mockResolvedValue(graph());
});
describe('Governed boundary inventory', () => {
  it('explains the system scope without inventing authorization or choosing a named definition', async () => {
    // Arrange
    const data = graph();
    data.availableNodes = ['mission-app', 'mission-api'].map(id => ({ ...data.nodes[1]!, id,
      kind: 'BoundaryDefinition', label: id, properties: { BoundaryType: 'Logical' },
      source: { ...data.nodes[1]!.source!, type: 'BoundaryDefinition', id } }));
    vi.mocked(api.getSystemDesign).mockResolvedValue(data);
    // Act
    mount();
    // Assert
    expect(await screen.findByRole('heading', { name: 'What belongs to this system?' })).toBeVisible();
    expect(screen.getByText('Your authorization boundary defines the system you are preparing for ATO review. Include your application, API, and other system-managed components. Record external services and their connections separately.')).toBeVisible();
    expect(screen.getByText('An app and its API usually belong to the same system scope—not separate authorization boundaries.')).toBeVisible();
    expect(screen.queryByRole('region', { name: 'Recorded system scope' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Inspect recorded definitions & resolve scope' })).not.toBeInTheDocument();
    expect(screen.getByText(/Draft.*Working revision 2/)).toBeVisible();
    expect(screen.getByRole('link', { name: 'Review scope changes' })).toHaveAttribute('href', '/systems/system-a/profile/SystemDesign?designView=Boundary');
    expect(api.saveSystemDesign).not.toHaveBeenCalled();
  });
  it('shows both statuses once above the introduction and canonical definitions before components', async () => {
    // Arrange
    const open = vi.fn();
    // Act
    render(<MemoryRouter><GovernedBoundaryInventory systemId="system-a"
      renderHeading={() => <nav aria-label="Task heading">Task navigation</nav>}
      recordStatus={<div role="status" aria-label="Boundary record status">2 boundaries defined / Viewing does not approve scope</div>}>
      <SystemBoundaryInventory boundaries={[]} onOpenBoundary={open}
        action={<button type="button">Add System Boundary</button>} />
    </GovernedBoundaryInventory></MemoryRouter>);
    // Assert
    const intro = await screen.findByRole('heading', { name: 'What belongs to this system?' });
    const definitions = screen.getByRole('region', { name: 'Recorded boundary inventory' });
    const canonicalStatus = screen.getByRole('status', { name: 'Boundary record status' });
    const governedStatus = screen.getByRole('status', { name: 'Inventory review status' });
    expect(screen.getByRole('navigation', { name: 'Task heading' }).nextElementSibling).toBe(canonicalStatus);
    expect(canonicalStatus.nextElementSibling).toBe(governedStatus);
    const support = screen.getByRole('complementary', { name: 'Document contribution and next tasks' });
    const columns = support.parentElement!;
    const main = columns.firstElementChild!;
    expect(governedStatus.nextElementSibling).toBe(columns);
    expect(main.firstElementChild).toBe(intro.closest('header'));
    expect(main).toContainElement(definitions);
    expect(main).toContainElement(screen.getByRole('table', { name: 'Components & system scope' }));
    expect(columns.lastElementChild).toBe(support);
    expect(screen.getAllByRole('complementary', { name: 'Document contribution and next tasks' })).toHaveLength(1);
    expect(canonicalStatus.parentElement).toBe(columns.parentElement);
    expect(canonicalStatus.parentElement).toBe(governedStatus.parentElement);
    expect(canonicalStatus.closest('aside')).toBeNull();
    expect(screen.getAllByRole('status', { name: 'Inventory review status' })).toHaveLength(1);
    expect(intro.closest('header')?.nextElementSibling).toBe(definitions);
    expect(definitions.compareDocumentPosition(screen.getByRole('table', { name: 'Components & system scope' }))
      & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(definitions.closest('details')).toBeNull();
    expect(within(definitions).getByRole('heading', { name: 'Recorded boundary definitions' })).toBeVisible();
    expect(within(definitions).getByText('No boundaries defined yet.')).toBeVisible();
    expect(within(definitions).getByRole('button', { name: 'Add System Boundary' })).toBeVisible();
    expect(screen.queryByText('Manage boundary definitions and source placements')).not.toBeInTheDocument();
    expect(screen.getByRole('status', { name: 'Inventory review status' })).toHaveTextContent('Working revision 2');
    expect(api.saveSystemDesign).not.toHaveBeenCalled();
    expect(open).not.toHaveBeenCalled();
  });
  it('retains unknown legacy scope and contradictions instead of silently treating them as outside', async () => {
    // Arrange
    const data = graph(); data.nodes[1]!.boundaryDisposition = 'LegacyUnknown'; data.nodes[2]!.boundaryDisposition = 'InBoundary';
    vi.mocked(api.getSystemDesign).mockResolvedValue(data);
    // Act
    mount();
    // Assert
    const table = await screen.findByRole('table', { name: 'Components & system scope' });
    expect(within(table).getByText('Unknown recorded decision: LegacyUnknown')).toBeVisible();
    expect(within(table).getByText(/Included in this system.*conflicts with recorded relationship/)).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Open Mission records' }));
    const decision = screen.getByLabelText('Does this component belong to this system?');
    expect(decision).toHaveValue('LegacyUnknown');
    expect(within(decision).getByRole('option', { name: 'Needs confirmation' })).toHaveValue('Undetermined');
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    expect(screen.getByRole('alert')).toHaveTextContent(/Choose a supported scope decision/);
  });
  it('allows app and API inclusion in the same system without manufacturing enclosing boundaries', async () => {
    // Arrange
    const data = graph();
    data.nodes.push({ ...data.nodes[1]!, id: 'api', label: 'Mission API', boundaryDisposition: 'Undetermined' });
    data.groups.push({ id: 'internal', label: 'Recorded delivery group', kind: 'Environment', nodeIds: ['api'] });
    vi.mocked(api.getSystemDesign).mockResolvedValue(data);
    // Act
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Open Mission API' }));
    fireEvent.change(screen.getByLabelText('Does this component belong to this system?'), { target: { value: 'InBoundary' } });
    // Assert
    expect(screen.getByText('Recorded delivery group (Environment)')).toBeVisible();
    expect(screen.getByText(/Internal groups, hosting environments and network zones do not establish authorization scope/)).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('row', { name: /Mission API/ })).toHaveTextContent('Included in this system');
    expect(api.saveSystemDesign).not.toHaveBeenCalled();
  });
  it('permits provider-hosted inclusion but rejects a declared shared service inclusion before staging', async () => {
    // Arrange
    const data = graph(); data.nodes[2]!.boundaryDisposition = 'InBoundary';
    vi.mocked(api.getSystemDesign).mockResolvedValue(data);
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Open Provider service' }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent(/SharedService.*must be outside/);
    fireEvent.change(screen.getByLabelText('Recorded scope relationship'), { target: { value: 'SystemManaged' } });
    fireEvent.change(screen.getByLabelText('Who operates or manages this component?'), { target: { value: 'Actual application operator' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    expect(screen.getByRole('row', { name: /Provider service/ })).toHaveTextContent('Included in this system');
    expect(api.saveSystemDesign).not.toHaveBeenCalled();
  });
  it('distinguishes recorded consumed service from an unused excluded component and exposes real connections', async () => {
    // Arrange
    const data = graph(); data.nodes[2]!.boundaryDisposition = 'OutOfBoundary';
    data.nodes.push({ ...data.nodes[1]!, id: 'unused', label: 'Unused retired host', boundaryDisposition: 'OutOfBoundary' });
    data.edges.push({ ...data.edges[0]!, id: 'uses-provider', sourceNodeId: 'storage', targetNodeId: 'provider',
      relationshipType: 'UsesService', purpose: 'Recorded service consumption' });
    vi.mocked(api.getSystemDesign).mockResolvedValue(data);
    // Act
    mount();
    // Assert
    const section = await screen.findByRole('region', { name: 'External systems & shared services' });
    expect(within(section).getByText('These support your system but are not automatically inside its authorization boundary. Document the connection, responsibility split, and supporting source records.')).toBeVisible();
    expect(within(section).getByText(/Outside this system.*Recorded service consumption/)).toBeVisible();
    expect(within(section).queryByText('Unused retired host')).not.toBeInTheDocument();
    expect(screen.getByRole('row', { name: /Unused retired host/ })).toHaveTextContent('Excluded component; use not recorded');
    expect(within(section).getByRole('link', { name: 'Document connections & responsibility split' }))
      .toHaveAttribute('href', '/systems/system-a/profile/PortsProtocolsAndServices');
  });
  it('does not replace unknown operators with a blank overlay and preserves fallback source values', async () => {
    // Arrange
    const data = graph(); data.nodes[1]!.deploymentOwner = null;
    data.nodes[1]!.properties.Owner = 'Recorded source operator';
    vi.mocked(api.getSystemDesign).mockResolvedValue(data);
    // Act
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Open Mission records' }));
    // Assert
    expect(screen.getByLabelText('Who operates or manages this component?')).toHaveValue('Recorded source operator');
  });
  it.each([403, 422, 500])('retains scope edits and reason on server save failure %s without reporting success', async status => {
    // Arrange
    vi.mocked(api.saveSystemDesign).mockRejectedValue({ response: { status, data: { error: 'Server rejected scope save' } } });
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Open Mission records' }));
    fireEvent.change(screen.getByLabelText('Inclusion / exclusion rationale'), { target: { value: 'Retained user rationale' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save scope draft' }));
    fireEvent.change(screen.getByLabelText('Reason for change'), { target: { value: 'Retained review reason' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Confirm save' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Server rejected scope save');
    expect(screen.getByLabelText('Reason for change')).toHaveValue('Retained review reason');
    expect(screen.queryByText('Scope draft saved. Reviewed baseline unchanged.')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    fireEvent.click(screen.getByRole('button', { name: 'Open Mission records' }));
    expect(screen.getByLabelText('Inclusion / exclusion rationale')).toHaveValue('Retained user rationale');
  });
  it('reports stale sources and missing baseline while keeping source management visible without writes', async () => {
    // Arrange
    const data = graph(); data.sourcesStale = true; data.approvedRevision = null;
    vi.mocked(api.getSystemDesign).mockResolvedValue(data);
    // Act
    mount();
    // Assert
    expect(await screen.findByText(/Source records changed.*Reconcile and review/)).toBeVisible();
    expect(screen.getByText('No approved design baseline recorded.')).toBeVisible();
    expect(screen.getByText('Existing boundary management')).toBeVisible();
    expect(screen.getByText('Existing boundary management').closest('details')).toBeNull();
    expect(api.reconcileSystemDesign).not.toHaveBeenCalled();
    expect(api.saveSystemDesign).not.toHaveBeenCalled();
  });
  it('shows computing components, actual scope and CSP provenance without treating source review as approval', async () => {
    // Arrange / Act
    mount();
    const table = await screen.findByRole('table', { name: 'Components & system scope' });
    // Assert
    expect(within(table).getAllByRole('row')).toHaveLength(3);
    expect(within(table).getAllByRole('columnheader').map(cell => cell.textContent))
      .toEqual(['Component', 'Type / source', 'Environment', 'System scope decision', 'Operator / manager', 'Review', 'Open']);
    expect(within(table).getByText('Recorded operations team')).toBeVisible();
    expect(within(table).getByText(/Provider-linked record/)).toBeVisible();
    expect(within(table).getByText('Outside this system')).toBeVisible();
    expect(screen.queryByText('10/12', { exact: true })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save scope draft' })).toBeDisabled();
    expect(api.saveSystemDesign).not.toHaveBeenCalled();
    expect(api.reconcileSystemDesign).not.toHaveBeenCalled();
  });
  it('stages missing environment and owner then saves the complete existing graph with the current revision', async () => {
    // Arrange
    const saved = graph(); saved.revision++;
    vi.mocked(api.saveSystemDesign).mockResolvedValue(saved);
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Open Provider service' }));
    const editor = screen.getByRole('dialog', { name: 'Edit inventory component' });
    // Act
    fireEvent.change(within(editor).getByLabelText('Who operates or manages this component?'), { target: { value: 'Recorded service owner' } });
    fireEvent.click(within(editor).getByRole('button', { name: 'Apply to draft' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save scope draft' }));
    const confirm = screen.getByRole('dialog', { name: 'Save inventory draft' });
    fireEvent.change(within(confirm).getByLabelText('Reason for change'), { target: { value: 'Document recorded service owner' } });
    fireEvent.click(within(confirm).getByRole('button', { name: 'Confirm save' }));
    // Assert
    await waitFor(() => expect(api.saveSystemDesign).toHaveBeenCalledOnce());
    const [id, body] = vi.mocked(api.saveSystemDesign).mock.calls[0]!;
    expect(id).toBe('system-a'); expect(body.expectedRevision).toBe(2);
    expect(body.edges).toEqual(graph().edges); expect(body.groups).toEqual(graph().groups);
    expect(body.nodes.find(n => n.id === 'provider')).toMatchObject({ deploymentOwner: 'Recorded service owner',
      source: graph().nodes.find(n => n.id === 'provider')!.source, reviewState: 'Unapproved' });
    expect(api.reviewSystemDesign).not.toHaveBeenCalled();
  });
  it('keeps unsaved values after a revision conflict and explicitly reports the error', async () => {
    // Arrange
    vi.mocked(api.saveSystemDesign).mockRejectedValue({ response: { status: 409 } });
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Open Mission records' }));
    fireEvent.change(screen.getByLabelText('Environment'), { target: { value: 'Recovery' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save scope draft' }));
    fireEvent.change(screen.getByLabelText('Reason for change'), { target: { value: 'Recorded environment' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirm save' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(/revision.*changed/i);
    expect(screen.getByText('Recovery')).toBeVisible();
  });
  it('does not edit an under-review graph and does not fabricate per-component approval', async () => {
    // Arrange
    const locked = graph(); locked.governanceStatus = 'UnderReview'; locked.actions.canEdit = false;
    vi.mocked(api.getSystemDesign).mockResolvedValue(locked);
    // Act
    mount();
    // Assert
    await screen.findByRole('table');
    expect(screen.getByRole('button', { name: 'Save scope draft' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Add component' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Open Provider service' }));
    expect(screen.queryByRole('button', { name: 'Apply to draft' })).not.toBeInTheDocument();
    expect(api.saveSystemDesign).not.toHaveBeenCalled();
  });
  it('excludes source-only records actors hosting scopes and identity, and counts unknown scope without guessing', async () => {
    // Arrange
    const data = graph();
    data.nodes.push(...['Environment', 'ActorGroup', 'ProfileSection', 'BoundaryDefinition'].map((kind): DesignNode =>
      ({ ...data.nodes[1]!, id: kind, kind, label: kind })));
    data.nodes[1]!.boundaryDisposition = 'Undetermined';
    vi.mocked(api.getSystemDesign).mockResolvedValue(data);
    // Act
    mount();
    // Assert
    const table = await screen.findByRole('table');
    expect(within(table).getAllByRole('row')).toHaveLength(3);
    expect(screen.getByRole('button', { name: 'Resolve 1 scope decision' })).toBeVisible();
    expect(screen.queryByText('Approved baseline: version 3')).not.toBeInTheDocument();
  });
  it('reconciles source changes with an explicit reason without accepting proposals or saving local edits', async () => {
    // Arrange
    const reconciled = graph();
    reconciled.proposals = [{ id: 'discovery', kind: 'Added', recordId: 'new-source', state: 'Pending',
      sourceFingerprint: 'recorded-fingerprint', originalNode: { ...reconciled.nodes[1]!, id: 'new-source' },
      conflictsWithHigherPrecedence: false }];
    vi.mocked(api.reconcileSystemDesign).mockResolvedValue(reconciled);
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Reconcile discoveries' }));
    fireEvent.change(screen.getByLabelText('Reason for change'), { target: { value: 'Compare recorded discoveries' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirm reconciliation' }));
    // Assert
    await waitFor(() => expect(api.reconcileSystemDesign).toHaveBeenCalledWith('system-a',
      { expectedRevision: 2, reason: 'Compare recorded discoveries' }));
    expect(screen.getByRole('link', { name: 'Review 1 component proposals' })).toBeVisible();
    expect(api.saveSystemDesign).not.toHaveBeenCalled();
    expect(api.reviewSystemDesign).not.toHaveBeenCalled();
  });
  it('adds a documented manual component locally with unknown boundary and no invented source', async () => {
    // Arrange
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Add component' }));
    fireEvent.click(screen.getByRole('button', { name: 'Add manually' }));
    fireEvent.change(screen.getByLabelText('Component name'), { target: { value: 'Recorded manual gateway' } });
    fireEvent.change(screen.getByLabelText('Component type'), { target: { value: 'VPN gateway' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Assert
    const row = screen.getByRole('row', { name: /Recorded manual gateway/ });
    expect(within(row).getByText('Needs confirmation')).toBeVisible();
    expect(within(row).getByText(/VPN gateway.*Manual design record/)).toBeVisible();
    expect(api.saveSystemDesign).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Reconcile discoveries' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Save scope draft' })).toBeEnabled();
  });
  it('requires an explicit working revision before editing an approved baseline', async () => {
    // Arrange
    const approved = graph(); approved.governanceStatus = 'Approved';
    approved.actions.canEdit = false; approved.actions.canDeriveDraft = true;
    vi.mocked(api.getSystemDesign).mockResolvedValue(approved);
    vi.mocked(api.reviewSystemDesign).mockResolvedValue(graph());
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Start working revision' }));
    fireEvent.change(screen.getByLabelText('Reason for change'), { target: { value: 'Record approved inventory changes for review' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirm working revision' }));
    // Assert
    await waitFor(() => expect(api.reviewSystemDesign).toHaveBeenCalledWith('system-a',
      { expectedRevision: 2, reason: 'Record approved inventory changes for review', action: 'derive_draft' }));
    expect(screen.getByRole('button', { name: 'Add component' })).toBeEnabled();
    expect(api.saveSystemDesign).not.toHaveBeenCalled();
  });
  it('reports load errors, keeps canonical management available and supports explicit retry', async () => {
    // Arrange
    vi.mocked(api.getSystemDesign).mockRejectedValueOnce(new Error('Recorded inventory unavailable'));
    mount();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Recorded inventory unavailable');
    expect(screen.getByText('Existing boundary management')).toBeVisible();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry inventory' }));
    // Assert
    expect(await screen.findByRole('table')).toBeVisible();
  });
  it('offers only unselected component sources and stages the exact CSP provenance rather than guessing from names', async () => {
    // Arrange
    const data = graph();
    const csp: DesignNode = { ...data.nodes[1]!, id: 'csp-source', label: 'Recorded hosted service',
      kind: 'ProviderReference', source: { ...data.nodes[1]!.source!, type: 'CspInheritedComponent',
        id: 'recorded-csp-id', version: 'exact-source-version', provenance: 'CSP reference' } };
    data.availableNodes = [csp, data.nodes[1]!, { ...data.nodes[1]!, id: 'actor', kind: 'ActorGroup' }];
    vi.mocked(api.getSystemDesign).mockResolvedValue(data);
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Review 1 source candidates' }));
    const picker = screen.getByRole('dialog', { name: 'Add inventory component' });
    expect(within(picker).queryByText('Mission records')).not.toBeInTheDocument();
    fireEvent.click(within(picker).getByRole('button', { name: 'Use recorded component Recorded hosted service' }));
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Assert
    const row = screen.getByRole('row', { name: /Recorded hosted service/ });
    expect(within(row).getByText(/Provider-linked record/)).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Review 1 source candidates' })).not.toBeInTheDocument();
    expect(api.saveSystemDesign).not.toHaveBeenCalled();
  });
  it('selects an existing named scope with its exact source record even before it is part of the design', async () => {
    // Arrange
    const data = graph();
    const scope: DesignNode = { ...data.nodes[1]!, id: 'scope-node', kind: 'BoundaryDefinition', diagramRole: 'SourceRecord',
      label: 'Recorded named scope', source: { ...data.nodes[1]!.source!, type: 'BoundaryDefinition', id: 'scope-id', version: 'scope-version' } };
    data.availableNodes = [scope];
    vi.mocked(api.getSystemDesign).mockResolvedValue(data);
    vi.mocked(api.saveSystemDesign).mockResolvedValue(data);
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Open Mission records' }));
    fireEvent.change(screen.getByLabelText('Named boundary scope'), { target: { value: 'scope-id' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save scope draft' }));
    fireEvent.change(screen.getByLabelText('Reason for change'), { target: { value: 'Select recorded scope' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirm save' }));
    // Assert
    await waitFor(() => expect(api.saveSystemDesign).toHaveBeenCalledOnce());
    const body = vi.mocked(api.saveSystemDesign).mock.calls[0]![1];
    expect(body.nodes.find(node => node.id === 'scope-node')).toEqual(scope);
    expect(body.nodes.find(node => node.id === 'storage')?.boundaryDefinitionId).toBe('scope-id');
    expect(body.edges).toEqual(data.edges);
    expect(api.reviewSystemDesign).not.toHaveBeenCalled();
  });
});
