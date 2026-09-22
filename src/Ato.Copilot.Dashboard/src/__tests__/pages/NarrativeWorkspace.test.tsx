import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import NarrativeWorkspace from '../../pages/NarrativeWorkspace';
import * as library from '../../api/narrativeLibrary';
import type { SystemWorkspacePermissions } from '../../features/workspaces/types';

const workspace = vi.hoisted(() => ({
  session: null as { roles: string[]; systemAccess: { systemId: string; permissions: Partial<SystemWorkspacePermissions> } } | null,
}));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => workspace.session }));

vi.mock('../../api/narrativeLibrary', () => ({
  getReferences: vi.fn(), getProposals: vi.fn(), getNarrativeAccess: vi.fn(),
  importReference: vi.fn(), publishReference: vi.fn(), generateProposal: vi.fn(), reviewProposal: vi.fn(),
}));
vi.mock('../../api/narratives', () => ({ getNarratives: vi.fn().mockResolvedValue([{ controlId: 'AC-2', version: 7 }]) }));
vi.mock('../../pages/Narratives', () => ({ default: () => <h2>Control Narratives</h2> }));
vi.mock('../../hooks/useSettings', () => ({ useSettings: () => ({ settings: {}, updateSettings: vi.fn() }) }));

const draft = { id: 'import-1', referenceKey: 'reference-1', title: 'Access policy', scope: 'System', scopeId: 'system-1',
  sourceName: 'reference.txt', sourceSha256: 'hash', version: 1, revision: 1, isPublished: false,
  createdAt: '2026-01-01T00:00:00Z', createdBy: 'author', publishedAt: null, publishedBy: null,
  passages: [{ controlId: null, narrativeType: null, content: 'Review quarterly.' }] };

function open(view = 'library') {
  return render(<MemoryRouter initialEntries={[`/systems/system-1/narratives/${view}`]}>
    <Routes><Route path="/systems/:id/narratives/*" element={<NarrativeWorkspace />} /></Routes>
  </MemoryRouter>);
}

beforeEach(() => {
  vi.clearAllMocks();
  workspace.session = null;
  vi.mocked(library.getReferences).mockResolvedValue([]);
  vi.mocked(library.getProposals).mockResolvedValue([]);
  vi.mocked(library.getNarrativeAccess).mockResolvedValue({ tenantId: 'tenant-1', systemName: 'Synthetic system',
    canAuthor: true, canPublishShared: false, capabilities: [] });
  vi.mocked(library.importReference).mockResolvedValue(draft);
  vi.mocked(library.publishReference).mockResolvedValue({ ...draft, isPublished: true, revision: 2 });
});

describe('Narratives workspace', () => {
  it('does not turn a MissionOwner reference-author grant into narrative generation authority', async () => {
    // Arrange
    workspace.session = { roles: ['MissionOwner'], systemAccess: { systemId: 'system-1', permissions: { canRead: true, canAuthorNarratives: false } } };
    // Act
    open('narratives');
    // Assert
    expect(await screen.findByRole('button', { name: /Generate/ })).toBeDisabled();
    expect(library.generateProposal).not.toHaveBeenCalled();
  });

  it('allows real multi-role narrative generation independently from reference-author permission', async () => {
    // Arrange
    workspace.session = { roles: ['MissionOwner', 'Issm'], systemAccess: { systemId: 'system-1', permissions: { canRead: true, canAuthorNarratives: true } } };
    vi.mocked(library.getNarrativeAccess).mockResolvedValue({ tenantId: 'tenant-1', systemName: 'Synthetic system',
      canAuthor: false, canPublishShared: false, capabilities: [] });
    // Act
    open('narratives');
    // Assert
    expect(await screen.findByRole('button', { name: /Generate/ })).toBeEnabled();
  });

  it('rejects forged shared-reference scope even when system reference authoring is allowed', async () => {
    // Arrange
    workspace.session = { roles: ['MissionOwner'], systemAccess: { systemId: 'system-1', permissions: { canRead: true } } };
    open('import');
    await screen.findByText('Synthetic system', { selector: 'p' });
    fireEvent.change(screen.getByLabelText('Reference title'), { target: { value: 'Access policy' } });
    fireEvent.change(screen.getByLabelText('Paste narratives'), { target: { value: 'Review quarterly.' } });
    // Act
    fireEvent.change(screen.getByLabelText('Reference scope'), { target: { value: 'Organization' } });
    fireEvent.click(screen.getByRole('button', { name: 'Extract passages' }));
    // Assert
    expect(library.importReference).not.toHaveBeenCalled();
    expect(await screen.findByRole('alert')).toHaveTextContent(/permission/i);
  });

  it('requires current review permission as well as per-proposal authorization', async () => {
    // Arrange
    workspace.session = { roles: ['Issm'], systemAccess: { systemId: 'system-1', permissions: { canRead: true, canReviewNarratives: false } } };
    vi.mocked(library.getProposals).mockResolvedValue([{ id: 'proposal-1', controlId: 'AC-2', narrativeType: 'Technical', baseVersion: 7,
      beforeContent: 'Old accounts', proposedContent: 'Federated accounts', stateHash: 'SYNTHETIC', provenance: {}, conflicts: [],
      missingEvidence: [], status: 'Draft', revision: 1, createdAt: '2026-01-01T00:00:00Z',
      createdBy: 'author', reviewedAt: null, reviewedBy: null, reviewNote: null, acceptedVersion: null, isStale: false, canReview: true }]);
    // Act
    open('review?proposal=proposal-1');
    // Assert
    expect(await screen.findByRole('button', { name: 'Approve v8' })).toBeDisabled();
    expect(library.reviewProposal).not.toHaveBeenCalled();
  });

  it('navigates from the library to the real import form', async () => {
    // Arrange
    open();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Upload narratives' }));
    // Assert
    expect(screen.getByRole('heading', { name: 'Import & map' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '02 \u00b7 Narratives' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '02 \u00b7 Library' })).toBeInTheDocument();
    expect(screen.getByLabelText('Reference title')).toBeInTheDocument();
  });

  it('requires complete mappings and acknowledgement before publishing', async () => {
    // Arrange
    open('import');
    await screen.findByText('Synthetic system', { selector: 'p' });
    fireEvent.change(screen.getByLabelText('Reference title'), { target: { value: 'Access policy' } });
    fireEvent.change(screen.getByLabelText('Paste narratives'), { target: { value: 'Review quarterly.' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Extract passages' }));
    const publish = await screen.findByRole('button', { name: 'Publish references' });
    // Assert
    expect(publish).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Control 1'), { target: { value: 'AC-2' } });
    fireEvent.change(screen.getByLabelText('Narrative type 1'), { target: { value: 'Policy' } });
    expect(publish).toBeDisabled();
    fireEvent.click(screen.getByLabelText('I reviewed these reference claims and mappings'));
    expect(publish).toBeEnabled();
    fireEvent.click(publish);
    await waitFor(() => expect(library.publishReference).toHaveBeenCalledWith('system-1', 'import-1', 1,
      [{ controlId: 'AC-2', narrativeType: 'Policy', content: 'Review quarterly.' }]));
  });

  it('keeps review actions disabled when the server denies review authority', async () => {
    // Arrange
    vi.mocked(library.getProposals).mockResolvedValue([{ id: 'proposal-1', controlId: 'AC-2', narrativeType: 'Technical', baseVersion: 7,
      beforeContent: 'Old accounts', proposedContent: 'Federated accounts', stateHash: 'SYNTHETIC', provenance: {}, conflicts: [],
      missingEvidence: ['Access review record missing'], status: 'Draft', revision: 1, createdAt: '2026-01-01T00:00:00Z',
      createdBy: 'author', reviewedAt: null, reviewedBy: null, reviewNote: null, acceptedVersion: null, isStale: false, canReview: false }]);
    // Act
    open('review?proposal=proposal-1');
    // Assert
    expect(await screen.findByRole('button', { name: 'Approve v8' })).toBeDisabled();
    expect(screen.getByText('Access review record missing')).toBeInTheDocument();
    expect(library.reviewProposal).not.toHaveBeenCalled();
  });
});