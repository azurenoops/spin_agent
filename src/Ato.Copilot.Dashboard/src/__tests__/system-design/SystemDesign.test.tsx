import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';
import SystemDesign from '../../features/system-design/SystemDesign';
import { designFixture, designLayoutFixture } from './fixtures';

const api = vi.hoisted(() => ({
  getSystemDesign: vi.fn(), saveSystemDesign: vi.fn(), reviewSystemDesign: vi.fn(), reconcileSystemDesign: vi.fn(),
  decideDesignProposal: vi.fn(), getSystemDesignHistory: vi.fn(), getApprovedSystemDesign: vi.fn(), getDesignLayout: vi.fn(), saveDesignLayout: vi.fn(),
  buildSystemDesign: vi.fn(),
}));
const exportsApi = vi.hoisted(() => ({ getSspPreview: vi.fn() }));
vi.mock('../../api/systemDesign', () => api);
vi.mock('../../api/exports', () => exportsApi);
vi.mock('../../features/system-design/DesignCanvas', () => ({ default: ({ onConnect }: { onConnect: (source: string, target: string) => void }) =>
  <div aria-label="Interactive design graph"><button onClick={() => onConnect('system', 'storage')}>Synthetic handle connection</button></div> }));
vi.mock('../../features/workspace-operations/SetupDialog', () => ({
  default: ({ title, description, children }: { title: string; description?: string; children: ReactNode }) => <div role="dialog" aria-label={title}>{description && <p>{description}</p>}{children}</div>,
}));
const mount = () => render(<MemoryRouter initialEntries={['/systems/system-a/profile/SystemDesign']}><SystemDesign systemId="system-a" /></MemoryRouter>);
beforeEach(() => {
  vi.resetAllMocks();
  api.getSystemDesign.mockResolvedValue(designFixture());
  api.getDesignLayout.mockImplementation((_id, view) => Promise.resolve(designLayoutFixture(view)));
  api.getSystemDesignHistory.mockResolvedValue([]);
  api.getApprovedSystemDesign.mockResolvedValue(null);
  exportsApi.getSspPreview.mockResolvedValue({ content: '{"system-security-plan":{"description":"Exact approved design narrative"}}',
    sourceState: 'ApprovedSources', contentHash: 'approved-output-hash', generatedAt: '2026-09-30T10:00:00Z', sourceGaps: [] });
});
describe('governed System design workspace', () => {
  it('builds recorded relationships on the server without synthesizing or approving connections in the browser', async () => {
    // Arrange
    const built = designFixture();
    built.revision = 3;
    built.edges.push({ ...built.edges[0]!, id: 'member-a', relationshipType: 'Membership', source: built.nodes[1]!.source });
    api.buildSystemDesign.mockResolvedValue(built);
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Build from recorded information' }));
    const dialog = screen.getByRole('dialog', { name: 'Confirm build from recorded information' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm build from recorded information' }));
    // Assert
    await waitFor(() => expect(api.buildSystemDesign).toHaveBeenCalledWith('system-a',
      { expectedRevision: 2, reason: 'Assemble architecture from current source-recorded relationships; preserve reviewed and manual decisions.' }));
    expect(screen.getByRole('button', { name: 'Relationships (2)' })).toBeVisible();
    expect(api.saveSystemDesign).not.toHaveBeenCalled();
    expect(api.reviewSystemDesign).not.toHaveBeenCalled();
  });
  it('retains source-only records separately from architecture elements', async () => {
    // Arrange
    const graph = designFixture();
    graph.nodes.push({ ...graph.nodes[1]!, id: 'profile-source', kind: 'ProfileSection', label: 'Reviewed user context', diagramRole: 'SourceRecord' });
    api.getSystemDesign.mockResolvedValue(graph);
    mount();
    // Act
    await screen.findByRole('button', { name: 'Elements (2)' });
    expect(screen.queryByRole('button', { name: 'Open Reviewed user context' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Source records (1)' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Open Reviewed user context' })).toBeVisible();
  });
  it('keeps provenance and empty fields out of the compact inspector but exposes complete details', async () => {
    // Arrange
    mount(); fireEvent.click(await screen.findByRole('button', { name: 'Open Mission records' }));
    const inspector = screen.getByRole('complementary', { name: 'Selected element inspector' });
    // Assert
    expect(inspector).toHaveTextContent('Component');
    expect(within(inspector).queryByText('Version 1 · Canonical · Reviewed')).not.toBeInTheDocument();
    expect(within(inspector).queryByText('Not recorded')).not.toBeInTheDocument();
    expect(within(inspector).getByRole('button', { name: 'Edit selected record' })).toHaveTextContent('Edit');
    expect(screen.getByRole('link', { name: 'Review gaps' })).toHaveClass('sd-button');
    expect(screen.getByRole('button', { name: 'Review SSP content' })).toHaveClass('sd-button');
    // Act
    fireEvent.click(within(inspector).getByRole('button', { name: 'View full details' }));
    // Assert
    const details = screen.getByRole('dialog', { name: 'Element details' });
    expect(details).toHaveTextContent('SystemComponent');
    expect(details).toHaveTextContent('storage');
    expect(details).toHaveTextContent('Canonical');
    expect(within(details).getByRole('link', { name: 'Open source record' })).toHaveAttribute('href', '/systems/system-a/boundaries');
  });
  it('stages canvas connections with the selected endpoints before saving or approval', async () => {
    // Arrange
    mount();
    await screen.findByRole('heading', { name: 'System design' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Synthetic handle connection' }));
    // Assert
    expect(screen.getByLabelText('Source element')).toHaveValue('system');
    expect(screen.getByLabelText('Destination element')).toHaveValue('storage');
    expect(api.saveSystemDesign).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText('Purpose'), { target: { value: 'Proposed mission connection' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    expect(screen.getByRole('button', { name: 'Relationships (2)' })).toBeVisible();
  });
  it('offers rename and removes a selected element with connected-edge confirmation from the draft only', async () => {
    // Arrange
    mount(); fireEvent.click(await screen.findByRole('button', { name: 'Open Mission records' }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Rename selected element' }));
    fireEvent.change(screen.getByLabelText('Label'), { target: { value: 'Renamed design storage' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    fireEvent.click(screen.getByRole('button', { name: 'Remove selected element' }));
    const confirm = screen.getByRole('dialog', { name: 'Remove from working design' });
    // Assert
    expect(confirm).toHaveTextContent('1 connected relationship');
    expect(confirm).toHaveTextContent('Canonical records and approved baselines are not deleted');
    fireEvent.click(within(confirm).getByRole('button', { name: 'Remove from draft' }));
    expect(screen.queryByRole('button', { name: 'Open Renamed design storage' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Relationships (0)' })).toBeVisible();
    expect(api.saveSystemDesign).not.toHaveBeenCalled();
  });
  it('provides a proposed component palette without claiming a canonical or Azure source', async () => {
    // Arrange
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Add element' }));
    fireEvent.click(screen.getByRole('button', { name: 'Database' }));
    fireEvent.change(screen.getByLabelText('Label'), { target: { value: 'Proposed database' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Open Proposed database' })).toBeVisible();
    expect(screen.getByRole('complementary', { name: 'Selected element inspector' })).toHaveTextContent('No canonical source attached');
  });
  it('adds an explicitly proposed external design node without inventing canonical provenance', async () => {
    // Arrange
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Add external system' }));
    fireEvent.click(screen.getByRole('button', { name: 'Propose external system' }));
    fireEvent.change(screen.getByLabelText('Label'), { target: { value: 'Proposed external partner' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Open Proposed external partner' })).toBeVisible();
    expect(api.saveSystemDesign).not.toHaveBeenCalled();
    expect(screen.getByRole('complementary', { name: 'Selected element inspector' })).toHaveTextContent('Undetermined');
  });
  it('adds only an available server-projected canonical component to the working draft', async () => {
    // Arrange
    const graph = designFixture();
    graph.availableNodes = [{ ...graph.nodes[1]!, id: 'canonical-extra', label: 'Available canonical resource' }];
    api.getSystemDesign.mockResolvedValue(graph);
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Add existing element' }));
    fireEvent.click(screen.getByRole('button', { name: 'Add Available canonical resource to draft' }));
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Open Available canonical resource' })).toBeVisible();
    expect(api.saveSystemDesign).not.toHaveBeenCalled();
  });
  it('renders server counts, contributions, governance, gaps and four views without invented readiness', async () => {
    // Arrange
    mount();
    // Act
    await screen.findByRole('heading', { name: 'System design' });
    // Assert
    expect(screen.getByText('50%')).toBeVisible();
    expect(screen.getByText('Boundary disposition requires review.')).toBeVisible();
    expect(screen.getByRole('region', { name: 'SSP output readiness' })).toHaveTextContent('Unapproved');
    expect(screen.getByRole('region', { name: 'System definition contributions' })).toHaveTextContent('Azure');
    for (const name of ['System context', 'Authorization boundary', 'Network architecture', 'Data flows'])
      expect(screen.getByRole('button', { name })).toBeVisible();
  });
  it('follows the definition-to-design composition with an authorized next review gate and baseline notice', async () => {
    // Arrange
    mount();
    // Act
    const definition = await screen.findByRole('heading', { name: 'System definition', level: 1 });
    const design = screen.getByRole('heading', { name: 'System design', level: 2 });
    // Assert
    expect(definition.compareDocumentPosition(design) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(screen.getByRole('region', { name: 'Next review gate' })).toContainElement(screen.getByRole('button', { name: 'Submit for review' }));
    expect(screen.getByText('Changes from approved baseline v1')).toBeVisible();
    expect(screen.getByRole('table', { name: 'Design gaps' })).toHaveTextContent('System Owner');
  });
  it('edits structured records, retains local work after concurrency failure, and retries explicitly', async () => {
    // Arrange
    api.saveSystemDesign.mockRejectedValueOnce({ error: 'Revision changed. Reload before saving.' })
      .mockResolvedValueOnce({ ...designFixture(), revision: 3 });
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Open Mission records' }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Edit selected record' }));
    const dialog = screen.getByRole('dialog', { name: 'Edit design element' });
    fireEvent.change(within(dialog).getByLabelText('Label'), { target: { value: 'Reviewed storage' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Apply to draft' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Correct canonical presentation' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirm save draft' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Revision changed');
    expect(screen.getByRole('button', { name: 'Open Reviewed storage' })).toBeVisible();
    expect(api.saveSystemDesign).toHaveBeenCalledWith('system-a', expect.objectContaining({
      expectedRevision: 2, reason: 'Correct canonical presentation', nodes: expect.arrayContaining([expect.objectContaining({ label: 'Reviewed storage' })]),
    }));
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel' }));
    api.getSystemDesign.mockResolvedValue({ ...designFixture(), revision: 7 });
    fireEvent.click(screen.getByRole('button', { name: 'Review latest server revision' }));
    const conflict = await screen.findByRole('dialog', { name: 'Resolve revision conflict' });
    expect(within(conflict).getByRole('heading', { name: 'Server revision v7' })).toBeVisible();
    fireEvent.click(within(conflict).getByRole('button', { name: 'Keep local draft' }));
    expect(screen.getByRole('button', { name: 'Open Reviewed storage' })).toBeVisible();
  });
  it('does not offer write actions when capabilities deny them', async () => {
    // Arrange
    const graph = designFixture();
    graph.actions = { canEdit: false, canSubmit: false, canWithdraw: false, canReview: false, canReconcile: false };
    api.getSystemDesign.mockResolvedValue(graph);
    // Act
    mount();
    await screen.findByRole('button', { name: 'Open Mission records' });
    // Assert
    expect(screen.queryByRole('button', { name: 'Save draft' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Submit for review' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reconcile changes' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Build from recorded information' })).not.toBeInTheDocument();
    expect(screen.getByText(/Read-only access/)).toBeVisible();
  });
  it('reports failures rather than displaying an empty graph and supports retry', async () => {
    // Arrange
    api.getSystemDesign.mockRejectedValueOnce(new Error('Source projection unavailable'));
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Retry' }));
    // Assert
    await waitFor(() => expect(api.getSystemDesign).toHaveBeenCalledTimes(2));
    expect(await screen.findByRole('button', { name: 'Open Mission records' })).toBeVisible();
  });
  it('shows exact approved SSP output rather than inventing draft contribution readiness', async () => {
    // Arrange
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Preview review package' }));
    const dialog = await screen.findByRole('dialog', { name: 'Review package preview' });
    await waitFor(() => expect(exportsApi.getSspPreview).toHaveBeenCalledWith('system-a', undefined, 'working'));
    // Assert
    expect(await within(dialog).findByText('Exact working-source SSP / OSCAL preview')).toBeVisible();
    expect(within(dialog).getByRole('link', { name: 'Preview approved SSP output' })).toHaveAttribute(
      'href', '/systems/system-a/documents/preview?source=approved&contribution=SystemDesign');
    expect(within(dialog).getByText('approved-output-hash')).toBeVisible();
    fireEvent.click(within(dialog).getByText('Generated narrative and structured output'));
    expect(within(dialog).getByText(/Exact approved design narrative/)).toBeVisible();
  });
  it('creates a data flow only with selected existing endpoints, keeping all protection fields', async () => {
    // Arrange
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Add data flow' }));
    const dialog = screen.getByRole('dialog', { name: 'Edit data flow' });
    // Act
    fireEvent.change(within(dialog).getByLabelText('Source element'), { target: { value: 'system' } });
    fireEvent.change(within(dialog).getByLabelText('Destination element'), { target: { value: 'storage' } });
    fireEvent.change(within(dialog).getByLabelText('Purpose'), { target: { value: 'Approved business use pending design review' } });
    fireEvent.change(within(dialog).getByLabelText('Protection mechanism'), { target: { value: 'Mutual TLS' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Apply to draft' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Relationships (2)' })).toBeVisible();
    expect(screen.getByRole('complementary', { name: 'Selected element inspector' })).toHaveTextContent('Mutual TLS');
    expect(api.saveSystemDesign).not.toHaveBeenCalled();
    expect(screen.getByText(/Unsaved design changes/)).toBeVisible();
  });
  it('reports presentation read errors while retaining the accessible graph records', async () => {
    // Arrange
    api.getDesignLayout.mockRejectedValueOnce({ error: 'Presentation read denied' });
    mount();
    // Act
    const failure = await screen.findByRole('alert');
    // Assert
    expect(failure).toHaveTextContent('Presentation read denied');
    expect(screen.getByRole('button', { name: 'Retry presentation' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Open Mission records' })).toBeVisible();
  });
  it('adds existing canonical elements through retained proposals, never fabricated source IDs', async () => {
    // Arrange
    const graph = designFixture();
    graph.proposals = [{ id: 'source-proposal', kind: 'Added', recordId: 'new-source', sourceFingerprint: 'source-2',
      state: 'Pending', originalNode: { ...graph.nodes[1]!, id: 'new-source', label: 'Canonical storage source' }, conflictsWithHigherPrecedence: false }];
    api.getSystemDesign.mockResolvedValue(graph);
    api.decideDesignProposal.mockResolvedValue(graph);
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Add existing element' }));
    const picker = screen.getByRole('dialog', { name: 'Add existing design element' });
    fireEvent.click(within(picker).getByRole('button', { name: 'Review addition: Canonical storage source' }));
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Verified canonical storage membership' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirm proposal decision' }));
    // Assert
    await waitFor(() => expect(api.decideDesignProposal).toHaveBeenCalledWith('system-a', 'source-proposal',
      { expectedRevision: 2, action: 'accept', reason: 'Verified canonical storage membership' }));
    expect(api.saveSystemDesign).not.toHaveBeenCalled();
  });
  it('uses the distinct server capability for a new working revision without editing approval', async () => {
    // Arrange
    const graph = designFixture();
    graph.governanceStatus = 'Approved';
    graph.actions = { canEdit: false, canSubmit: false, canWithdraw: false, canReview: false, canReconcile: false, canDeriveDraft: true };
    api.getSystemDesign.mockResolvedValue(graph);
    api.reviewSystemDesign.mockResolvedValue(designFixture());
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Start working revision' }));
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Review a planned system change' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirm start working revision' }));
    // Assert
    await waitFor(() => expect(api.reviewSystemDesign).toHaveBeenCalledWith('system-a',
      { expectedRevision: 2, action: 'derive_draft', reason: 'Review a planned system change' }));
  });
  it.each([
    { label: 'Submit for review', action: 'submit', capability: 'canSubmit' },
    { label: 'Withdraw', action: 'withdraw', capability: 'canWithdraw' },
    { label: 'Approve', action: 'approve', capability: 'canReview' },
    { label: 'Request revision', action: 'request_revision', capability: 'canReview' },
  ])('sends $action with the server revision and an explicit review reason', async ({ label, action, capability }) => {
    // Arrange
    const graph = designFixture();
    graph.actions = { canEdit: false, canSubmit: false, canWithdraw: false, canReview: false, canReconcile: false, [capability]: true };
    api.getSystemDesign.mockResolvedValue(graph);
    api.reviewSystemDesign.mockResolvedValue(designFixture());
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: label }));
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Recorded human governance rationale' } });
    fireEvent.click(screen.getByRole('button', { name: `Confirm ${label.toLowerCase()}` }));
    // Assert
    await waitFor(() => expect(api.reviewSystemDesign).toHaveBeenCalledWith('system-a',
      { expectedRevision: 2, action, reason: 'Recorded human governance rationale' }));
  });
  it.each([
    { label: 'Accept', action: 'accept', state: 'Pending' },
    { label: 'Reject', action: 'reject', state: 'Pending' },
    { label: 'Defer', action: 'defer', state: 'Pending' },
    { label: 'Recover proposal', action: 'recover', state: 'Rejected' },
  ])('supports proposal $action without inventing a replacement source', async ({ label, action, state }) => {
    // Arrange
    const graph = designFixture();
    graph.proposals = [{ id: 'proposal-a', kind: 'Modified', recordId: 'storage', sourceFingerprint: 'source-v2', state,
      originalNode: graph.nodes[1], conflictsWithHigherPrecedence: false }];
    api.getSystemDesign.mockResolvedValue(graph);
    api.decideDesignProposal.mockResolvedValue(designFixture());
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Review proposals (1)' }));
    fireEvent.click(screen.getByRole('button', { name: label }));
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Retain source decision evidence' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirm proposal decision' }));
    // Assert
    await waitFor(() => expect(api.decideDesignProposal).toHaveBeenCalledWith('system-a', 'proposal-a',
      { expectedRevision: 2, action, reason: 'Retain source decision evidence' }));
  });
  it('requires an edited proposal for higher-precedence conflicts and retains canonical provenance', async () => {
    // Arrange
    const graph = designFixture();
    graph.proposals = [{ id: 'proposal-a', kind: 'Modified', recordId: 'storage', sourceFingerprint: 'source-v2', state: 'Pending',
      originalNode: graph.nodes[1], conflictsWithHigherPrecedence: true }];
    api.getSystemDesign.mockResolvedValue(graph);
    api.decideDesignProposal.mockResolvedValue(designFixture());
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Review proposals (1)' }));
    const proposal = screen.getByRole('dialog', { name: 'Reconcile source proposals' });
    expect(within(proposal).getByRole('button', { name: 'Accept' })).toBeDisabled();
    fireEvent.click(within(proposal).getByRole('button', { name: 'Edit and accept' }));
    fireEvent.change(screen.getByLabelText('Label'), { target: { value: 'Reviewed conflict correction' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply to draft' }));
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Preserve higher-precedence scope decision' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirm proposal decision' }));
    // Assert
    await waitFor(() => expect(api.decideDesignProposal).toHaveBeenCalledWith('system-a', 'proposal-a', expect.objectContaining({
      expectedRevision: 2, action: 'edit_accept', node: expect.objectContaining({ label: 'Reviewed conflict correction', source: graph.nodes[1]!.source }),
    })));
  });
});
