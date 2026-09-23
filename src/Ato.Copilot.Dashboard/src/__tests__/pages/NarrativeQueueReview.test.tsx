import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import NarrativeWorkspace from '../../pages/NarrativeWorkspace';
import * as library from '../../api/narrativeLibrary';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';

const permissions = vi.hoisted(() => ({ canRead: true, canAuthorNarratives: true, canReviewNarratives: false }));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => ({ roles: ['Isso'], systemAccess: { systemId: 'system-a', permissions } }),
}));
vi.mock('../../api/narrativeLibrary', () => ({
  getReferences: vi.fn(), getProposals: vi.fn(), getNarrativeAccess: vi.fn(),
  importReference: vi.fn(), publishReference: vi.fn(), generateProposal: vi.fn(), reviewProposal: vi.fn(),
  getProposalById: vi.fn(), generateQueuedProposal: vi.fn(), getImpactReceipts: vi.fn(),
}));
vi.mock('../../api/narratives', () => ({ getNarratives: vi.fn().mockResolvedValue([{ controlId: 'AC-1', version: 7 }]) }));
vi.mock('../../pages/Narratives', () => ({ default: () => <h2>Control Narratives</h2> }));
vi.mock('../../hooks/useSettings', () => ({ useSettings: () => ({ settings: {}, updateSettings: vi.fn() }) }));

const queued: library.NarrativeProposal = {
  id: '44444444-4444-4444-4444-444444444444', controlId: 'AC-1', narrativeType: 'Technical', baseVersion: 7,
  beforeContent: 'Preserved approved narrative', proposedContent: '', stateHash: 'opaque-state', provenance: { changeOrigin: { SubscriptionId: 'subscription-a' } },
  conflicts: [], missingEvidence: [], status: 'PendingGeneration', revision: 1, createdAt: '2026-09-21T00:00:00Z',
  createdBy: 'provider-actor', reviewedAt: null, reviewedBy: null, reviewNote: null, acceptedVersion: null,
  isStale: false, canReview: false, generationErrorCode: null, changeSourceKind: 'CspCapability', changeSourceId: 'capability-a',
};
function open(proposalId = queued.id) {
  return render(<MemoryRouter initialEntries={[`/workspaces/organizations/org-a/systems/system-a/narratives/review?proposal=${proposalId}`]}>
    <WorkspaceNavigationProvider workspace={{ kind: 'organization', tenantId: 'org-a' }}>
      <Routes><Route path="/workspaces/organizations/:tenantId/systems/:id/narratives/*" element={<NarrativeWorkspace />} /></Routes>
    </WorkspaceNavigationProvider>
  </MemoryRouter>);
}
beforeEach(() => {
  vi.clearAllMocks();
  permissions.canRead = true;
  permissions.canAuthorNarratives = true;
  permissions.canReviewNarratives = false;
  vi.mocked(library.getReferences).mockResolvedValue([]);
  vi.mocked(library.getProposals).mockResolvedValue([queued]);
  vi.mocked(library.getProposalById).mockResolvedValue(null);
  vi.mocked(library.getImpactReceipts).mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 50 });
  vi.mocked(library.getNarrativeAccess).mockResolvedValue({
    tenantId: 'org-a', systemName: 'Synthetic system', canAuthor: false, canPublishShared: false, capabilities: [],
  });
});

describe('exact queued narrative review', () => {
  it('retrieves the requested queue ID beyond the 500-item list window', async () => {
    // Arrange
    vi.mocked(library.getProposals).mockResolvedValue(Array.from({ length: 500 }, (_, index) => ({
      ...queued, id: `00000000-0000-0000-0000-${index.toString().padStart(12, '0')}`,
    })));
    vi.mocked(library.getProposalById).mockResolvedValue(queued);

    // Act
    open();

    // Assert
    expect(await screen.findByText(/Generation pending/)).toBeInTheDocument();
    expect(library.getProposalById).toHaveBeenCalledWith('system-a', queued.id);
    expect(screen.getByLabelText('Proposed change')).toHaveValue(queued.id);
  });

  it('retries the same failed proposal with its revision instead of creating a replacement', async () => {
    // Arrange
    const failed = { ...queued, status: 'GenerationFailed', revision: 2, generationErrorCode: 'GENERATION_TIMEOUT' };
    const generated = { ...queued, status: 'Draft', revision: 3, proposedContent: 'Generated after retry' };
    vi.mocked(library.getNarrativeAccess).mockResolvedValue({
      tenantId: 'org-a', systemName: 'Synthetic system', canAuthor: false, canPublishShared: false, canGenerate: true, capabilities: [],
    });
    vi.mocked(library.getProposals).mockResolvedValueOnce([failed]).mockResolvedValue([generated]);
    vi.mocked(library.generateQueuedProposal).mockResolvedValue(generated);
    open();

    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Retry generation' }));

    // Assert
    await waitFor(() => expect(library.generateQueuedProposal).toHaveBeenCalledWith('system-a', queued.id, 2));
    expect(await screen.findByRole('button', { name: 'Approve v8' })).toBeDisabled();
    expect(screen.getByLabelText('Proposed change')).toHaveValue(queued.id);
    expect(library.generateProposal).not.toHaveBeenCalled();
    expect(library.reviewProposal).not.toHaveBeenCalled();
    await waitFor(() => expect(library.getImpactReceipts).toHaveBeenCalledWith('system-a', queued.id, 1, expect.any(AbortSignal)));
    expect(screen.getByRole('region', { name: 'Source delivery history' })).toBeInTheDocument();
  });

  it('cannot generate queued work without an explicit generation grant', async () => {
    // Arrange / Act
    open();

    // Assert
    expect(await screen.findByRole('button', { name: 'Generate queued draft' })).toBeDisabled();
    expect(library.generateQueuedProposal).not.toHaveBeenCalled();
  });

  it('resolves the queue ID without presenting an empty proposal as a generated draft', async () => {
    // Arrange / Act
    open();
    // Assert
    expect(await screen.findByText(/Generation pending/)).toBeInTheDocument();
    expect(screen.getByLabelText('Proposed change')).toHaveValue(queued.id);
    expect(screen.getByText('Preserved approved narrative')).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Proposed v8' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Approve|Return for revision/ })).not.toBeInTheDocument();
    expect(library.generateProposal).not.toHaveBeenCalled();
    expect(library.reviewProposal).not.toHaveBeenCalled();
  });

  it('shows a generation failure code, then refreshes the same proposal without issuing generation or approval', async () => {
    // Arrange
    vi.mocked(library.getProposals).mockResolvedValueOnce([{ ...queued, status: 'GenerationFailed', revision: 2, generationErrorCode: 'GENERATION_TIMEOUT' }])
      .mockResolvedValueOnce([{ ...queued, status: 'Draft', revision: 3, proposedContent: 'Generated draft pending human review', canReview: true }]);
    open();
    expect(await screen.findByRole('alert')).toHaveTextContent('GENERATION_TIMEOUT');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Refresh proposal status' }));
    // Assert
    await waitFor(() => expect(library.getProposals).toHaveBeenCalledTimes(2));
    expect(await screen.findByRole('button', { name: 'Approve v8' })).toBeDisabled();
    expect(screen.getByLabelText('Proposed change')).toHaveValue(queued.id);
    expect(library.generateProposal).not.toHaveBeenCalled();
    expect(library.reviewProposal).not.toHaveBeenCalled();
  });

  it.each([queued.id, ''])('does not silently replace an explicit queue ID (%s) with a different reviewable draft', async requestedId => {
    // Arrange
    permissions.canReviewNarratives = true;
    vi.mocked(library.getProposals).mockResolvedValue([{ ...queued, id: 'other-proposal', status: 'Draft', proposedContent: 'Unrelated proposed content', canReview: true }]);
    // Act
    open(requestedId);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('requested proposal');
    expect(screen.queryByText('Unrelated proposed content')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Approve v8' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'View other proposals' })).toHaveAttribute('href', '/workspaces/organizations/org-a/systems/system-a/narratives/review');
  });

  it('drops prior review controls if a status refresh is denied', async () => {
    // Arrange
    permissions.canReviewNarratives = true;
    vi.mocked(library.getProposals).mockResolvedValueOnce([{ ...queued, status: 'Draft', proposedContent: 'Generated proposal', canReview: true }])
      .mockRejectedValueOnce(new Error('Proposal access withdrawn.'));
    open();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Approve v8' })).toBeEnabled());
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Refresh proposal status' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Proposal access withdrawn.');
    expect(screen.queryByRole('button', { name: 'Approve v8' })).not.toBeInTheDocument();
    expect(library.reviewProposal).not.toHaveBeenCalled();
  });
});
