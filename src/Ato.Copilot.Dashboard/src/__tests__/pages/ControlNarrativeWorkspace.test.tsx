import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import ControlNarrativeWorkspace from '../../features/narratives/ControlNarrativeWorkspace';
import * as api from '../../api/controlNarrativeWorkspace';

vi.mock('../../api/controlNarrativeWorkspace', () => ({
  getControlNarrativeWorkspace: vi.fn(),
  getControlNarrativeDetail: vi.fn(),
}));
vi.mock('../../api/requirementCoverage', () => ({
  getRequirementCoverage: vi.fn().mockImplementation(async (systemId: string, controlId: string) => ({
    systemId, controlId, framework: null, catalogVersion: null, sourceUri: null,
    baselineRevision: 0, narrativeVersion: null, parent: null, enhancements: [], requirements: [],
    parameters: [], parameterValues: {}, gaps: ['Catalog source needs reconciliation.'], proposals: [],
    canAuthor: false, canReview: false, canBind: false,
  })),
  saveRequirementResponses: vi.fn(), reviewRequirementResponses: vi.fn(), proposeEnhancement: vi.fn(),
  acceptEnhancement: vi.fn(), returnEnhancement: vi.fn(), getRequirementCatalogs: vi.fn(), bindRequirementCatalog: vi.fn(),
}));
vi.mock('../../features/compliance/components/ValidationEvidencePanel', () => ({
  default: ({ controlId }: { controlId: string }) => <p>Evidence for {controlId}</p>,
}));

const statement = {
  state: 'Missing',
  hasContent: false,
  hasApprovedContent: false,
  proposalId: null,
  proposalStatus: null,
  isStale: false,
};
const item = {
  id: 'implementation-1',
  controlId: 'AC-2',
  controlTitle: 'Account Management',
  family: 'AC',
  implementationStatus: 'Planned',
  currentVersion: 3,
  approvalStatus: 'Draft',
  policy: statement,
  technical: { ...statement, state: 'Draft', hasContent: true },
  nextAction: 'AuthorPolicy',
  nextActionLabel: 'Add policy statement',
  nextActionReason: null,
};
const workspace = {
  systemId: 'system-1',
  counts: { needsAttention: 1, allControls: 1, approvedStatements: 0, proposedUpdates: 0 },
  items: [item],
  permissions: { canAuthor: true, canReview: false, canManageEvidence: true },
};
const detail = {
  systemId: 'system-1',
  id: item.id, controlId: item.controlId, controlTitle: item.controlTitle, family: item.family,
  implementationStatus: item.implementationStatus, approvalStatus: item.approvalStatus, currentVersion: item.currentVersion,
  statements: {
    policy: { currentContent: null, approvedContent: null, state: 'Missing' },
    technical: { currentContent: 'Accounts are federated.', approvedContent: null, state: 'Draft' },
  },
  proposals: [],
  responsibilities: [],
  history: [{ versionNumber: 3, status: 'Draft', authoredBy: 'ISSO', authoredAt: '2026-09-01T00:00:00Z', changeReason: 'Updated federation flow', reviews: [] }],
  permissions: { canAuthor: true, authorReason: null, canReview: false,
    reviewReason: 'Narrative review permission is required.', canManageEvidence: true, evidenceReason: null },
};

function open(entry = '/systems/system-1/narratives?view=needs-attention') {
  return render(<MemoryRouter initialEntries={[entry]}>
    <Routes>
      <Route path="/systems/:id/narratives" element={<ControlNarrativeWorkspace systemId="system-1"
        onOpenEditor={vi.fn()} onOpenLibrary={vi.fn()} onReviewProposal={vi.fn()} />} />
    </Routes>
  </MemoryRouter>);
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getControlNarrativeWorkspace).mockResolvedValue(workspace);
  vi.mocked(api.getControlNarrativeDetail).mockResolvedValue(detail);
});

describe('Control narrative workspace', () => {
  it('groups catalog enhancements directly beneath their parent with independent statement states', async () => {
    // Arrange
    const parent = { ...item, id: 'parent', controlId: 'AC-11', controlTitle: 'Device Lock' };
    const child = { ...item, id: 'child', controlId: 'AC-11(1)', controlTitle: 'Pattern-hiding Displays',
      parentControlId: 'AC-11', policy: { ...statement, state: 'Approved', hasApprovedContent: true } };
    vi.mocked(api.getControlNarrativeWorkspace).mockResolvedValue({
      ...workspace, items: [child, item, parent], counts: { ...workspace.counts, allControls: 3 },
    });

    // Act
    open('/systems/system-1/narratives?view=all');

    // Assert
    const table = await screen.findByRole('table');
    const rows = within(table).getAllByRole('row').slice(1);
    const parentRow = within(table).getByRole('row', { name: /Device Lock/ });
    const childRow = within(table).getByRole('row', { name: /Pattern-hiding Displays/ });
    expect(rows.map(row => row.querySelector('strong')?.textContent))
      .toEqual(['Device Lock', 'Pattern-hiding Displays', 'Account Management']);
    expect(rows[1]).toHaveClass('cnw-enhancement-row');
    expect(within(childRow).getByText('Enhancement of AC-11')).toBeVisible();
    expect(within(childRow).getByLabelText('Policy Approved')).toBeVisible();
    expect(within(parentRow).getByLabelText('Policy Missing')).toBeVisible();
    expect(within(parentRow).getByText('1 enhancement shown')).toBeVisible();
    expect(screen.getByText('Showing 3 of 3 records')).toBeVisible();
  });

  it('provides one navigation-only parent context for child-only filtered pages without changing totals', async () => {
    // Arrange
    const children = [1, 2].map(number => ({
      ...item, id: `child-${number}`, controlId: `AC-11(${number})`,
      controlTitle: `Enhancement ${number}`, parentControlId: 'AC-11',
    }));
    vi.mocked(api.getControlNarrativeWorkspace).mockResolvedValue({
      ...workspace, items: children, counts: { ...workspace.counts, allControls: 27 },
    });
    vi.mocked(api.getControlNarrativeDetail).mockResolvedValue({
      ...detail, controlId: 'AC-11', controlTitle: 'Device Lock',
    });

    // Act
    open('/systems/system-1/narratives?view=all&search=Enhancement&family=AC&status=Planned&page=2');
    const parent = await screen.findByRole('button', { name: 'View parent control AC-11' });

    // Assert
    expect(screen.getAllByText('Parent context · not a matching record on this page')).toHaveLength(1);
    const context = parent.closest('tr')!;
    expect(within(context).queryByLabelText(/Policy/)).not.toBeInTheDocument();
    expect(screen.getByText('Showing 2 of 27 records')).toBeVisible();
    expect(screen.getByText('Page 2 of 2')).toBeVisible();
    expect(api.getControlNarrativeWorkspace).toHaveBeenCalledWith('system-1', {
      view: 'all-controls', search: 'Enhancement', family: 'AC', status: 'Planned', page: 2, pageSize: 25,
    }, expect.any(AbortSignal));

    // Act
    fireEvent.click(parent);
    expect(await screen.findByRole('dialog', { name: 'AC-11 Device Lock' })).toBeVisible();
    fireEvent.keyDown(document, { key: 'Escape' });

    // Assert
    await waitFor(() => expect(parent).toHaveFocus());
    expect(screen.getByText('Page 2 of 2')).toBeVisible();
    expect(screen.getByDisplayValue('Enhancement')).toBeVisible();
  });

  it('does not infer children from display syntax and follows non-NIST catalog relationships', async () => {
    // Arrange
    vi.mocked(api.getControlNarrativeWorkspace).mockResolvedValue({
      ...workspace, items: [
        { ...item, id: 'standalone', controlId: 'AC-11(a)', controlTitle: 'Standalone catalog record' },
        { ...item, id: 'custom-child', controlId: 'CUSTOM-B', controlTitle: 'Catalog child', parentControlId: 'CUSTOM-A' },
        { ...item, id: 'custom-parent', controlId: 'CUSTOM-A', controlTitle: 'Catalog parent' },
        { ...item, id: 'custom-child-2', controlId: 'CUSTOM-C', controlTitle: 'Second child', parentControlId: 'CUSTOM-A' },
      ],
    });

    // Act
    open();

    // Assert
    const rows = within(await screen.findByRole('table')).getAllByRole('row').slice(1);
    expect(rows[0]).not.toHaveClass('cnw-enhancement-row');
    expect(rows.map(row => row.querySelector('strong')?.textContent))
      .toEqual(['Standalone catalog record', 'Catalog parent', 'Catalog child', 'Second child']);
    expect(screen.getByText('2 enhancements shown')).toBeVisible();
    expect(rows[2]).toHaveClass('cnw-enhancement-row');
    expect(rows[3]).toHaveClass('cnw-enhancement-row');
  });

  it('shows consistent views, readable controls, independent statement states, and the next step', async () => {
    // Arrange / Act
    open();

    // Assert
    expect(await screen.findByRole('heading', { name: 'Document how your controls work' })).toBeVisible();
    expect(screen.getByRole('tab', { name: 'Needs attention (1)' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tab', { name: 'All controls (1)' })).toBeVisible();
    expect(screen.getByRole('tab', { name: 'Approved statements (0)' })).toBeVisible();
    expect(screen.getByText('Account Management')).toBeVisible();
    expect(screen.getByLabelText('Policy Missing')).toBeVisible();
    expect(screen.getByLabelText('Technical Draft')).toBeVisible();
    const action = screen.getByRole('button', { name: 'Add policy statement' });
    expect(action).toBeEnabled();
    fireEvent.click(action);
    const dialog = await screen.findByRole('dialog', { name: 'AC-2 Account Management' });
    expect(within(dialog).getByRole('button', { name: 'Close control details' })).toHaveFocus();
    fireEvent.keyDown(document, { key: 'Escape' });
    await waitFor(() => expect(action).toHaveFocus());
  });

  it('opens a direct-linked drawer, switches statement/evidence/history independently, and closes with Escape', async () => {
    // Arrange
    open('/systems/system-1/narratives?view=all&control=AC-2&statement=technical');

    // Act / Assert
    expect(await screen.findByRole('dialog', { name: 'AC-2 Account Management' })).toBeVisible();
    expect(screen.getByRole('tab', { name: 'Technical statement' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByText('Accounts are federated.')).toBeVisible();

    // Act
    fireEvent.click(screen.getByRole('tab', { name: 'Evidence' }));
    expect(await screen.findByText('Evidence for AC-2')).toBeVisible();
    fireEvent.click(screen.getByRole('tab', { name: 'History' }));
    expect(screen.getByText('Updated federation flow')).toBeVisible();
    fireEvent.keyDown(document, { key: 'Escape' });

    // Assert
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('keeps request failures explicit instead of replacing them with zero counts', async () => {
    // Arrange
    vi.mocked(api.getControlNarrativeWorkspace).mockRejectedValue(new Error('Narrative service unavailable.'));

    // Act
    open();

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Narrative service unavailable.');
    expect(screen.queryByRole('tab', { name: /All controls \(0\)/ })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeVisible();
  });

  it('uses filtered server counts rather than recomputing totals from the current page', async () => {
    // Arrange
    vi.mocked(api.getControlNarrativeWorkspace).mockResolvedValue({
      ...workspace,
      counts: { ...workspace.counts, needsAttention: 31, allControls: 48 },
    });

    // Act
    open('/systems/system-1/narratives?view=all');

    // Assert
    expect(await screen.findByRole('tab', { name: 'Needs attention (31)' })).toBeVisible();
    expect(screen.getByRole('tab', { name: 'All controls (48)' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByText('Page 1 of 2')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Next' })).toBeEnabled();
  });

  it('retains approved content and explains why a stale proposal cannot be reviewed by a view-only user', async () => {
    // Arrange
    vi.mocked(api.getControlNarrativeWorkspace).mockResolvedValue({
      ...workspace, permissions: { canAuthor: false, canReview: false, canManageEvidence: false },
    });
    vi.mocked(api.getControlNarrativeDetail).mockResolvedValue({
      ...detail,
      statements: {
        ...detail.statements,
        policy: {
          currentContent: 'Approved account policy.',
          approvedContent: 'Approved account policy.',
          state: 'Approved',
        },
      },
      proposals: [{
        id: 'proposal-1', narrativeType: 'Policy', beforeContent: 'Approved account policy.',
        proposedContent: 'Changed account policy.', status: 'Draft', revision: 2, isStale: true,
        canReview: false, createdAt: '2026-09-01T00:00:00Z', createdBy: 'source-worker',
        cause: 'Provider responsibility changed.', sourceId: null, provenance: {}, conflicts: [],
        missingEvidence: [], reviewReason: 'The proposal is stale and must be regenerated.',
      }],
      permissions: { canAuthor: false, authorReason: 'Narrative author permission is required.',
        canReview: false, reviewReason: 'Narrative review permission is required.',
        canManageEvidence: false, evidenceReason: 'Evidence management permission is required.' },
    });

    // Act
    open('/systems/system-1/narratives?control=AC-2&statement=policy');

    // Assert
    expect((await screen.findAllByText('Approved account policy.'))[0]).toBeVisible();
    expect(screen.getByText(/cannot be approved because its source or narrative version changed/i)).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Review proposed change' })).not.toBeInTheDocument();
  });
});
