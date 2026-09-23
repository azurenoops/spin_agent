import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import EvidenceRepository from '../../pages/EvidenceRepository';
import EvidenceUploadDialog from '../../components/EvidenceUploadDialog';
import EvidenceDetailPanel from '../../components/EvidenceDetailPanel';
import * as evidence from '../../api/evidence';
import apiClient from '../../api/client';
import { useWorkspaceSession, type WorkspaceSession } from '../../features/workspaces/WorkspaceBoundary';
import type { SystemWorkspacePermissions } from '../../features/workspaces/types';
import type { EvidenceArtifactDto } from '../../types/evidence';
import { invokeClick, workspaceSession } from '../helpers/domainPermissions';

vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: vi.fn() }));
vi.mock('../../api/client', () => ({ default: { get: vi.fn().mockResolvedValue({ data: [] }) } }));
vi.mock('../../api/evidence', () => ({
  listEvidence: vi.fn(), getEvidenceSummary: vi.fn().mockResolvedValue(null),
  getEvidence: vi.fn(), getEvidenceVersions: vi.fn().mockResolvedValue([]),
  downloadEvidence: vi.fn(), downloadEvidenceVersion: vi.fn(),
  deleteEvidence: vi.fn(), collectEvidence: vi.fn(), uploadEvidence: vi.fn(), replaceEvidence: vi.fn(),
}));
const systemId = 'evidence-system';
const artifact: EvidenceArtifactDto = {
  id: 'artifact-a', source: 'Manual', fileName: 'evidence.json', contentType: 'application/json',
  fileSizeBytes: 2, artifactCategory: 'Other', narrativeType: 'Unclassified', controlId: 'AC-1',
  controlImplementationId: 'impl-a', securityCapabilityId: null, description: 'Evidence',
  uploadedBy: 'Synthetic user', uploadedAt: '2026-01-01T00:00:00Z', contentHash: 'test-hash',
};
function session(permissions: Partial<SystemWorkspacePermissions> = {}, roles = ['MissionOwner']): WorkspaceSession {
  return workspaceSession(systemId, permissions, roles);
}
function wrapped(children: React.ReactNode, legacy = false) {
  const prefix = legacy ? '' : '/workspaces/org/tenant-a';
  return (
    <MemoryRouter initialEntries={[`${prefix}/systems/${systemId}/evidence`]}>
      <Routes><Route path={`${prefix}/systems/:id/evidence`} element={children} /></Routes>
    </MemoryRouter>
  );
}
function upload() {
  return <EvidenceUploadDialog systemId={systemId} controlImplementationId="impl-a" onClose={vi.fn()} onUploaded={vi.fn()} />;
}
function panel() {
  return <EvidenceDetailPanel systemId={systemId} evidenceId={artifact.id} onClose={vi.fn()} onActionComplete={vi.fn()} />;
}
function chooseFile(container: HTMLElement) {
  fireEvent.change(container.querySelector('input[type="file"]')!, {
    target: { files: [new File(['{}'], 'evidence.json', { type: 'application/json' })] },
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(useWorkspaceSession).mockReturnValue(session());
  vi.mocked(apiClient.get).mockResolvedValue({ data: [] });
  vi.mocked(evidence.listEvidence).mockResolvedValue({ items: [artifact], page: 1, pageSize: 50, totalCount: 1 });
  vi.mocked(evidence.getEvidence).mockResolvedValue(artifact);
  vi.mocked(evidence.replaceEvidence).mockResolvedValue(artifact);
  vi.mocked(evidence.uploadEvidence).mockResolvedValue(artifact);
  vi.mocked(evidence.deleteEvidence).mockResolvedValue(undefined);
  vi.spyOn(window, 'confirm').mockReturnValue(true);
});
afterEach(() => { cleanup(); localStorage.clear(); vi.restoreAllMocks(); });

describe('Evidence mutation authorization (#1017)', () => {
  it.each(['AO', 'ISSM'])('ignores MissionOwner forged %s preference for repository controls and handlers', async role => {
    // Arrange
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({ role }));
    render(wrapped(<EvidenceRepository />));
    await screen.findByText('evidence.json');

    // Act
    await invokeClick(screen.getByTitle('Delete'));
    await invokeClick(screen.getByRole('button', { name: 'Collect automated evidence for this control' }));

    // Assert
    expect(screen.getByRole('button', { name: 'Upload Evidence' })).toBeDisabled();
    expect(screen.getByTitle('Delete')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Collect automated evidence for this control' })).toBeDisabled();
    expect(screen.getAllByRole('alert').some(alert => /permission|not authorized/i.test(alert.textContent ?? ''))).toBe(true);
    expect(evidence.deleteEvidence).not.toHaveBeenCalled();
    expect(evidence.collectEvidence).not.toHaveBeenCalled();
  });

  it('honors the granted flag for multiple roles without canManageSystem', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageEvidence: true, canManageSystem: false }, ['MissionOwner', 'ISSO']));
    render(wrapped(<EvidenceRepository />));
    await screen.findByText('evidence.json');

    // Act
    await act(async () => {
      fireEvent.click(screen.getByTitle('Delete'));
      fireEvent.click(screen.getByRole('button', { name: 'Collect automated evidence for this control' }));
    });

    // Assert
    expect(screen.getByRole('button', { name: 'Upload Evidence' })).toBeEnabled();
    expect(evidence.deleteEvidence).toHaveBeenCalledWith(systemId, artifact.id);
    expect(evidence.collectEvidence).toHaveBeenCalledWith(systemId, 'AC-1');
  });

  it.each([null, session(), session({ canManageEvidence: false, canManageSystem: true })])('fails closed for missing or denied permissions', async access => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(access);
    const view = render(wrapped(upload()));
    chooseFile(view.container);

    // Act
    await invokeClick(screen.getByRole('button', { name: 'Upload Evidence' }));

    // Assert
    expect(screen.getByRole('button', { name: 'Upload Evidence' })).toBeDisabled();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission|not authorized/i);
    expect(evidence.uploadEvidence).not.toHaveBeenCalled();
  });

  it('rechecks an already open upload dialog after permission loss', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageEvidence: true }));
    const view = render(wrapped(upload()));
    chooseFile(view.container);
    expect(screen.getByRole('button', { name: 'Upload Evidence' })).toBeEnabled();

    // Act
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageEvidence: false }));
    view.rerender(wrapped(upload()));
    await invokeClick(screen.getByRole('button', { name: 'Upload Evidence' }));

    // Assert
    expect(screen.getByRole('button', { name: 'Upload Evidence' })).toBeDisabled();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission|not authorized/i);
    expect(evidence.uploadEvidence).not.toHaveBeenCalled();
  });

  it('rechecks replacement and deletion inside an open panel after revocation without blocking downloads', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageEvidence: true }));
    const view = render(wrapped(panel()));
    fireEvent.click(await screen.findByRole('button', { name: 'Replace' }));
    chooseFile(view.container);

    // Act
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageEvidence: false }));
    view.rerender(wrapped(panel()));
    await invokeClick(screen.getByRole('button', { name: 'Upload Replacement' }));
    await invokeClick(screen.getByRole('button', { name: 'Delete' }));

    // Assert
    expect(screen.getByRole('button', { name: 'Upload Replacement' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Replace' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Delete' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Download' })).toBeEnabled();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission|not authorized/i);
    expect(evidence.replaceEvidence).not.toHaveBeenCalled();
    expect(evidence.deleteEvidence).not.toHaveBeenCalled();
  });

  it.each([false, true])('allows an authorized upload (legacy=%s)', async legacy => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(legacy ? null : session({ canManageEvidence: true }));
    const view = render(wrapped(upload(), legacy));
    chooseFile(view.container);

    // Act
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Upload Evidence' })); });

    // Assert
    expect(evidence.uploadEvidence).toHaveBeenCalledWith(expect.objectContaining({ systemId, controlImplementationId: 'impl-a' }));
  });

  it('allows an authorized replacement', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageEvidence: true }));
    const view = render(wrapped(panel()));
    fireEvent.click(await screen.findByRole('button', { name: 'Replace' }));
    chooseFile(view.container);

    // Act
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Upload Replacement' })); });

    // Assert
    expect(evidence.replaceEvidence).toHaveBeenCalledWith(expect.objectContaining({ systemId, evidenceId: artifact.id }));
  });
});
