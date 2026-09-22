import type { ReactNode } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { SystemWorkspacePermissions } from '../../features/workspaces/types';
import type { EvidenceArtifactDto } from '../../types/evidence';
import TodoPanel from '../../components/cards/TodoPanel';
import OverrideReviewPage from '../../pages/OverrideReviewPage';
import EvidenceSection from '../../components/EvidenceSection';
import ValidationEvidencePanel from '../../features/compliance/components/ValidationEvidencePanel';
import AddValidationLinkModal from '../../features/compliance/components/AddValidationLinkModal';
import { getProfileTodos } from '../../api/systemProfile';
import { approveOrgControlOverride, rejectOrgControlOverride, listOrgControlOverrides } from '../../api/orgControlOverrides';
import { collectEvidence, deleteEvidence, downloadEvidence, getControlEvidence } from '../../api/evidence';
import { addValidationLink, deleteValidationLink, getControlValidationLinks } from '../../features/compliance/api/complianceApi';
import apiClient from '../../api/client';

const state = vi.hoisted(() => ({
  role: 'AO',
  session: null as { roles: string[]; systemAccess: { systemId: string; permissions: Partial<SystemWorkspacePermissions> } } | null,
}));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => state.session }));
vi.mock('../../hooks/useSettings', () => ({ useSettings: () => ({ settings: { role: state.role } }) }));
vi.mock('../../hooks/usePolling', () => ({
  usePolling: () => ({
    data: { systemId: 'system-a', systemName: 'Synthetic system', currentPhase: 'Prepare',
      items: [{ id: 'todo-a', label: 'Deferred test gate', detail: 'Synthetic prerequisite', category: 'deferred', deferredId: 'gate-a' }] },
    loading: false, error: null, refresh: vi.fn(),
  }),
}));
vi.mock('../../api/client', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
vi.mock('../../api/systemProfile', () => ({ getProfileTodos: vi.fn() }));
vi.mock('../../api/orgControlOverrides', () => ({
  listOrgControlOverrides: vi.fn(), approveOrgControlOverride: vi.fn(), rejectOrgControlOverride: vi.fn(),
}));
vi.mock('../../api/evidence', () => ({
  getControlEvidence: vi.fn(), collectEvidence: vi.fn(), downloadEvidence: vi.fn(), deleteEvidence: vi.fn(),
}));
vi.mock('../../features/compliance/api/complianceApi', () => ({
  getControlValidationLinks: vi.fn(), addValidationLink: vi.fn(), deleteValidationLink: vi.fn(),
}));
vi.mock('../../components/EvidenceUploadDialog', () => ({ default: () => null }));
vi.mock('../../components/layout/PageLayout', () => ({ default: ({ children }: { children: ReactNode }) => <main>{children}</main> }));
vi.mock('../../components/layout/PageHero', () => ({ default: () => null }));

function scoped(element: ReactNode, legacy = false) {
  return <MemoryRouter initialEntries={[legacy ? '/systems/system-a' : '/workspaces/organizations/org-a/systems/system-a']}>{element}</MemoryRouter>;
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(apiClient.post).mockReset().mockResolvedValue({ data: { resolved: true } });
  state.role = 'AO';
  state.session = { roles: ['MissionOwner'], systemAccess: { systemId: 'system-a', permissions: { canRead: true, canEditProfile: true } } };
  vi.mocked(getProfileTodos).mockResolvedValue({ hasProfileTasks: true, incompleteSections: [
    { sectionType: 'MissionAndPurpose', label: 'Mission purpose', status: 'Draft', reviewerComments: null },
  ], revisionSections: [], flaggedControls: [] });
  vi.mocked(listOrgControlOverrides).mockResolvedValue([{
    id: 'override-a', controlId: 'AC-1', implementationStatus: 'Planned', inheritanceApplicability: null,
    justification: 'Synthetic override', createdAt: '2026-09-01T00:00:00Z', createdBy: 'synthetic',
    updatedAt: '2026-09-01T00:00:00Z', updatedBy: 'synthetic',
  }]);
  vi.mocked(getControlEvidence).mockResolvedValue({ direct: [], inherited: [], automated: [] });
  vi.mocked(collectEvidence).mockResolvedValue({
    evidenceId: 'evidence-a', controlId: 'AC-1', evidenceType: 'ConfigurationExport',
    collectedAt: '2026-09-01T00:00:00Z', contentHash: 'synthetic',
  });
  afterEach(() => vi.restoreAllMocks());
  vi.mocked(getControlValidationLinks).mockResolvedValue({ systemId: 'system-a', controlId: 'AC-1', total: 0, links: [] });
});

describe('Todo domain permissions', () => {
  it.each(['AO', 'ISSM'])('uses server profile access despite the forged %s preference', async role => {
    // Arrange
    state.role = role;
    render(scoped(<TodoPanel systemId="system-a" />));
    // Act
    await screen.findByText('Mission purpose');
    // Assert
    expect(getProfileTodos).toHaveBeenCalledWith('system-a');
    expect(screen.getByRole('button', { name: 'Resolve' })).toHaveAttribute('aria-disabled', 'true');
  });

  it('blocks deferred resolution without its own projection and reports handler denial', async () => {
    // Arrange
    state.session!.systemAccess.permissions.canManageSystem = true;
    render(scoped(<TodoPanel systemId="system-a" />));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Resolve' }));
    // Assert
    expect(apiClient.post).not.toHaveBeenCalled();
    expect(await screen.findByRole('alert')).toHaveTextContent(/permission/i);
  });

  it('preserves legacy resolution and surfaces a non-gate server failure', async () => {
    // Arrange
    state.session = null;
    vi.mocked(apiClient.post).mockRejectedValue(new Error('Resolution rejected.'));
    render(scoped(<TodoPanel systemId="system-a" />, true));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Resolve' }));
    // Assert
    expect(apiClient.post).toHaveBeenCalledWith('/systems/system-a/deferred-prerequisites/gate-a/resolve');
    expect(await screen.findByRole('alert')).toHaveTextContent('Resolution rejected.');
  });

  it('does not fetch profile tasks for missing canonical profile permission even with a forged MissionOwner persona', () => {
    // Arrange
    state.role = 'MissionOwner';
    state.session = null;
    // Act
    render(scoped(<TodoPanel systemId="system-a" />));
    // Assert
    expect(getProfileTodos).not.toHaveBeenCalled();
  });

  it('preserves actionable gate failures for an authorized legacy resolution', async () => {
    // Arrange
    state.session = null;
    vi.mocked(apiClient.post).mockRejectedValue({ response: { status: 422, data: {
      gateName: 'Boundary definition', message: 'Complete the boundary first.',
      actionLink: '/systems/system-a/boundaries', actionLabel: 'Manage Boundaries',
    } } });
    render(scoped(<TodoPanel systemId="system-a" />, true));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Resolve' }));
    // Assert
    expect(await screen.findByText('Cannot Resolve Yet')).toBeInTheDocument();
    expect(screen.getByText('Complete the boundary first.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Manage Boundaries/ })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    expect(screen.queryByText('Cannot Resolve Yet')).not.toBeInTheDocument();
  });
});

describe('Override review domain permissions', () => {
  it('does not infer organization override review from a browser assessor or system narrative-review flag', async () => {
    // Arrange
    state.role = 'SecurityControlAssessor';
    state.session!.systemAccess.permissions.canReviewNarratives = true;
    // Act
    render(scoped(<OverrideReviewPage />));
    await screen.findByText('AC-1');
    // Assert
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reject' })).not.toBeInTheDocument();
    expect(screen.queryByText(/Switch to/)).not.toBeInTheDocument();
    expect(approveOrgControlOverride).not.toHaveBeenCalled();
  });

  it('preserves legacy assessor approval', async () => {
    // Arrange
    state.role = 'SecurityControlAssessor';
    state.session = null;
    render(scoped(<OverrideReviewPage />, true));
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Approve' }));
    // Assert
    await waitFor(() => expect(approveOrgControlOverride).toHaveBeenCalledWith('AC-1'));
  });

  it('preserves legacy rejection and closes its write affordance when a canonical session replaces it', async () => {
    // Arrange
    state.role = 'SecurityControlAssessor';
    state.session = null;
    const view = render(scoped(<OverrideReviewPage />, true));
    fireEvent.click(await screen.findByRole('button', { name: 'Reject' }));
    fireEvent.change(screen.getByPlaceholderText(/Explain why this override/), { target: { value: 'Synthetic review note' } });
    // Act
    state.session = { roles: ['Sca'], systemAccess: { systemId: 'system-a', permissions: { canReviewNarratives: true } } };
    view.rerender(scoped(<OverrideReviewPage />, true));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm Reject' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Confirm Reject' })).toBeDisabled();
    expect(rejectOrgControlOverride).not.toHaveBeenCalled();
    // Act
    state.session = null;
    view.rerender(scoped(<OverrideReviewPage />, true));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm Reject' }));
    // Assert
    await waitFor(() => expect(rejectOrgControlOverride).toHaveBeenCalledWith('AC-1', 'Synthetic review note'));
  });
});

describe('Narrative evidence mutation permissions', () => {
  const artifact: EvidenceArtifactDto = {
    id: 'evidence-a', source: 'Manual', fileName: 'record.txt', contentType: 'text/plain', fileSizeBytes: 1024,
    artifactCategory: 'PolicyDocument', narrativeType: 'Policy', controlId: 'AC-1', controlImplementationId: 'implementation-a',
    securityCapabilityId: null, description: 'Synthetic evidence', uploadedBy: 'synthetic', uploadedAt: '2026-09-01T00:00:00Z', contentHash: 'synthetic',
  };
  it('disables evidence collection and attachment for a MissionOwner regardless of browser persona', async () => {
    // Arrange
    render(scoped(<EvidenceSection systemId="system-a" controlId="AC-1" />));
    // Act
    await screen.findByText('No evidence attached to this control.');
    fireEvent.click(screen.getByRole('button', { name: 'Collect Evidence' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Collect Evidence' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Attach Evidence' })).toBeDisabled();
    expect(collectEvidence).not.toHaveBeenCalled();
  });

  it('allows server-granted evidence collection and disables it after revocation', async () => {
    // Arrange
    state.session!.roles = ['MissionOwner', 'Isso'];
    state.session!.systemAccess.permissions.canManageEvidence = true;
    const view = render(scoped(<EvidenceSection systemId="system-a" controlId="AC-1" />));
    await screen.findByText('No evidence attached to this control.');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Collect Evidence' }));
    await waitFor(() => expect(collectEvidence).toHaveBeenCalledWith('system-a', 'AC-1'));
    state.session!.systemAccess.permissions.canManageEvidence = false;
    view.rerender(scoped(<EvidenceSection systemId="system-a" controlId="AC-1" />));
    // Assert
    expect(screen.getByRole('button', { name: 'Attach Evidence' })).toBeDisabled();
    expect(screen.getByRole('button', { name: /Collect/ })).toBeDisabled();
  });

  it('does not infer validation-link management from evidence or review flags', async () => {
    // Arrange
    state.session!.systemAccess.permissions = { canRead: true, canManageEvidence: true, canReviewNarratives: true };
    // Act
    render(scoped(<ValidationEvidencePanel systemId="system-a" controlId="AC-1" canManage />));
    await screen.findByText('No validation links attached to this control.');
    // Assert
    expect(screen.queryByRole('button', { name: 'Add validation link' })).not.toBeInTheDocument();
  });

  it('guards direct validation-link form submission without a projected permission', async () => {
    // Arrange
    render(scoped(<AddValidationLinkModal systemId="system-a" controlId="AC-1" onClose={vi.fn()} onAdded={vi.fn()} />));
    fireEvent.change(screen.getByLabelText('Target'), { target: { value: 'https://example.test/evidence' } });
    // Act
    fireEvent.submit(screen.getByRole('dialog'));
    // Assert
    expect(addValidationLink).not.toHaveBeenCalled();
    expect(await screen.findByRole('alert')).toHaveTextContent(/permission/i);
  });

  it('renders evidence read-only after revocation, and confirms deletion only with a current grant', async () => {
    // Arrange
    state.session!.systemAccess.permissions.canManageEvidence = true;
    vi.mocked(getControlEvidence).mockResolvedValue({
      direct: [artifact], automated: [{ ...artifact, id: 'scan-a', source: 'Automated', fileName: null }],
      inherited: [{ ...artifact, id: 'shared-a', inheritedFromCapability: 'Shared test capability' }],
    });
    vi.mocked(deleteEvidence).mockResolvedValue(undefined);
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    const view = render(scoped(<EvidenceSection systemId="system-a" controlId="AC-1" />));
    await screen.findByText('Inherited from Shared test capability');
    // Act
    fireEvent.click(screen.getAllByTitle('Delete')[0]!);
    expect(deleteEvidence).not.toHaveBeenCalled();
    vi.mocked(window.confirm).mockReturnValue(true);
    fireEvent.click(screen.getAllByTitle('Delete')[0]!);
    // Assert
    await waitFor(() => expect(deleteEvidence).toHaveBeenCalledWith('system-a', 'evidence-a'));
    // Act
    state.session!.systemAccess.permissions.canManageEvidence = false;
    view.rerender(scoped(<EvidenceSection systemId="system-a" controlId="AC-1" />));
    // Assert
    expect(screen.queryByTitle('Delete')).not.toBeInTheDocument();
    expect(screen.getAllByTitle('Download')).toHaveLength(2);
  });

  it('surfaces failed evidence downloads without losing read-only evidence visibility', async () => {
    // Arrange
    vi.mocked(getControlEvidence).mockResolvedValue({ direct: [artifact], inherited: [], automated: [] });
    vi.mocked(downloadEvidence).mockRejectedValue(new Error('Download unavailable'));
    render(scoped(<EvidenceSection systemId="system-a" controlId="AC-1" />));
    // Act
    fireEvent.click(await screen.findByTitle('Download'));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Unable to download evidence.');
    expect(screen.getByText('record.txt')).toBeInTheDocument();
  });

  it('preserves legacy validation-link creation and deletion without granting canonical authority', async () => {
    // Arrange
    state.session = null;
    const link = { id: 'link-a', linkType: 'ExternalUrl' as const, linkTarget: 'https://example.test/evidence',
      description: null, addedBy: 'synthetic', addedAt: '2026-09-01T00:00:00Z', validatedAt: null, isAutomated: false };
    vi.mocked(addValidationLink).mockResolvedValue(link);
    vi.mocked(getControlValidationLinks).mockResolvedValue({ systemId: 'system-a', controlId: 'AC-1', total: 1, links: [link] });
    vi.mocked(deleteValidationLink).mockResolvedValue(undefined);
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    render(scoped(<ValidationEvidencePanel systemId="system-a" controlId="AC-1" canManage />, true));
    fireEvent.click(await screen.findByRole('button', { name: 'Add validation link' }));
    fireEvent.change(screen.getByLabelText('Type'), { target: { value: 'ExternalUrl' } });
    fireEvent.change(screen.getByLabelText('Target'), { target: { value: link.linkTarget } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Add link' }));
    await waitFor(() => expect(addValidationLink).toHaveBeenCalledWith('system-a', 'AC-1', {
      linkType: 'ExternalUrl', linkTarget: link.linkTarget, description: undefined,
    }));
    fireEvent.click(screen.getByRole('button', { name: 'Delete validation link' }));
    // Assert
    await waitFor(() => expect(deleteValidationLink).toHaveBeenCalledWith('system-a', 'AC-1', 'link-a'));
  });
});
