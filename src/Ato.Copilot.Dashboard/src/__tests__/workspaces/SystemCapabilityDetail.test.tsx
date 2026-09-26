import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { StrictMode } from 'react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import '../helpers/dialog';
import SystemCapabilityDetail from '../../features/workspace-operations/system-capabilities/SystemCapabilityDetail';
import { systemCapabilityDetailFixture, systemSetupOperationFixture } from '../fixtures/systemCapabilityDetailSetup';
import { responsibilityItem } from '../helpers/capabilityResponsibilityFixture';

const api = vi.hoisted(() => ({
  getSystemCapability: vi.fn(), prepareSystemCapabilityRemoval: vi.fn(),
  getSystemCapabilityOperation: vi.fn(), completeSystemCapabilityOperation: vi.fn(),
  reviewSystemCapabilityNarrative: vi.fn(),
}));
const narratives = vi.hoisted(() => ({ generateProposal: vi.fn(), generateQueuedProposal: vi.fn(), getProposalById: vi.fn() }));
const sourceGeneration = vi.hoisted(() => ({ generateScopedSystemCapabilityProposal: vi.fn() }));
const evidence = vi.hoisted(() => ({ downloadEvidence: vi.fn() }));
const workspaceMode = vi.hoisted(() => ({ value: 'ordinary' }));
vi.mock('../../api/narrativeLibrary', () => narratives);
vi.mock('../../api/evidence', () => evidence);
vi.mock('../../features/workspace-operations/system-capabilities/systemCapabilityNarrativeRequests', () => sourceGeneration);
vi.mock('../../features/workspace-operations/system-capabilities/systemCapabilityApi', () => api);
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => ({ workspace: { displayName: 'Example organization', kind: 'organization', tenantId: 'tenant-a', mode: workspaceMode.value },
    identity: { directoryTenantId: 'directory-a', oid: 'actor-a' } }),
}));
vi.mock('../../components/layout/SystemLayout', () => ({ useSystemContext: () => ({ detail: { name: 'Selected system', systemId: 'system-a' } }) }));
vi.mock('../../api/capabilityResponsibilities', async importOriginal => ({
  ...await importOriginal<typeof import('../../api/capabilityResponsibilities')>(),
  getCapabilityResponsibilities: vi.fn(async () => ({ systemId: 'system-a', baselineId: 'baseline-a', canConfirm: true,
    items: [responsibilityItem('PendingReview', 'AC-1')], pendingImpacts: [] })),
}));

function Location() {
  const location = useLocation();
  return <output aria-label="Current route">{location.pathname}{location.search}</output>;
}
function mount(search = '', source: 'local' | 'provider' = 'provider') {
  return render(<MemoryRouter initialEntries={[`/systems/system-a/security-capabilities/${source}/capability-a${search}`]}>
    <SystemCapabilityDetail tenantId="tenant-a" systemId="system-a" source={source} recordId="capability-a" /><Location />
  </MemoryRouter>);
}
const proposal = () => ({
  id: 'proposal-a', controlId: 'AC-1', narrativeType: 'Policy', baseVersion: 2,
  beforeContent: 'Approved policy remains intact.', proposedContent: 'Proposed policy update.',
  stateHash: 'state-hash', provenance: { sourceRevision: 'source-1' }, conflicts: [], missingEvidence: [],
  status: 'Draft', revision: 3, createdAt: '2026-09-25T10:00:00Z', createdBy: 'different-author',
  reviewedAt: null, reviewedBy: null, reviewNote: null, acceptedVersion: null, isStale: false, canReview: true,
});

describe('System capability detail', () => {
  beforeEach(() => {
    vi.clearAllMocks(); sessionStorage.clear(); workspaceMode.value = 'ordinary';
    api.getSystemCapability.mockResolvedValue(systemCapabilityDetailFixture());
    api.prepareSystemCapabilityRemoval.mockResolvedValue({ operation: systemSetupOperationFixture('Removal'), existing: false });
    api.getSystemCapabilityOperation.mockResolvedValue(systemSetupOperationFixture('Removal'));
    api.completeSystemCapabilityOperation.mockResolvedValue(systemSetupOperationFixture('Removal', 'Completed'));
    api.reviewSystemCapabilityNarrative.mockResolvedValue({});
    narratives.getProposalById.mockResolvedValue(proposal());
    narratives.generateProposal.mockResolvedValue(proposal());
    narratives.generateQueuedProposal.mockResolvedValue(proposal());
    sourceGeneration.generateScopedSystemCapabilityProposal.mockResolvedValue(proposal());
    evidence.downloadEvidence.mockRejectedValue(new Error('Access to this protected reference was withdrawn.'));
  });

  it('renders selected system, source revision and actual component boundaries without a duplicate shell', async () => {
    // Arrange
    mount();
    // Act
    await screen.findByRole('heading', { name: 'Security monitoring', level: 1 });
    // Assert
    expect(within(screen.getByRole('complementary', { name: 'System capability applicability' })).getByText('Selected system')).toBeVisible();
    expect(screen.getByText('Provider collector')).toBeVisible();
    expect(screen.getAllByText('Workload boundary').length).toBeGreaterThan(0);
    expect(screen.getByText(/Provider source is read-only/i)).toBeVisible();
    expect(screen.queryByRole('navigation', { name: /system navigation/i })).not.toBeInTheDocument();
  });

  it('preserves tab and selected control in the URL with keyboard-accessible tabs', async () => {
    // Arrange
    mount('?tab=coverage&control=AC-1');
    await screen.findByRole('heading', { name: 'Security monitoring' });
    // Act
    await act(async () => { fireEvent.click(screen.getByRole('tab', { name: 'Evidence & narratives' })); });
    // Assert
    expect(screen.getByLabelText('Current route')).toHaveTextContent('tab=evidence');
    expect(screen.getByRole('tab', { name: 'Evidence & narratives' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tabpanel')).toHaveTextContent('Approved policy remains intact.');
  });

  it('does not globally block policy generation because another narrative has a responsibility dependency', async () => {
    // Arrange
    mount('?tab=evidence');
    // Act
    await screen.findByText('Approved policy remains intact.');
    // Assert
    expect(screen.getByRole('button', { name: 'Generate Policy proposal for AC-1' })).toBeEnabled();
    expect(screen.getByText('Technical source evidence needs review.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Generate Technical proposal for AC-1' })).toBeDisabled();
    expect(screen.getByText('Approved technical content.')).toBeVisible();
    expect(screen.getByText('System-log-review.pdf')).toBeVisible();
  });

  it('prepares an exact authorized provider removal and executes the persisted plan once', async () => {
    // Arrange
    mount();
    await screen.findByRole('heading', { name: 'Security monitoring' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Remove from this system' }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.click(await within(dialog).findByRole('checkbox', { name: /reviewed.*removal/i }));
    expect(within(dialog).getByText('Unsubscribe provider capability')).toBeVisible();
    expect(within(dialog).getByText('Security monitoring')).toBeVisible();
    expect(within(dialog).getByText('subscription-a')).not.toBeVisible();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Unsubscribe from this system' }));
    // Assert
    await waitFor(() => expect(api.completeSystemCapabilityOperation).toHaveBeenCalledOnce());
    expect(api.prepareSystemCapabilityRemoval.mock.calls[0]?.slice(0, 4)).toEqual(['tenant-a', 'system-a', 'provider', 'capability-a']);
    expect(api.prepareSystemCapabilityRemoval.mock.calls[0]?.[4]).toMatchObject({
      sourceRevision: 'source-1', relationshipRevision: 'relationship-1',
    });
    expect(api.completeSystemCapabilityOperation.mock.calls[0]?.slice(0, 4)).toEqual([
      'tenant-a', 'system-a', 'operation-a', { expectedRevision: 1 },
    ]);
    expect(dialog).toHaveTextContent(/component placements.*retained/i);
    expect(dialog).toHaveTextContent(/approved historical/i);
  });

  it('keeps removal unavailable until the asynchronous persisted preview is ready', async () => {
    // Arrange
    let releasePreview: (() => void) | undefined;
    api.prepareSystemCapabilityRemoval.mockImplementationOnce(() => new Promise(resolve => {
      releasePreview = () => resolve({ operation: systemSetupOperationFixture('Removal'), existing: false });
    }));
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Remove from this system' }));
    const dialog = await screen.findByRole('dialog');
    await waitFor(() => expect(api.prepareSystemCapabilityRemoval).toHaveBeenCalledOnce());
    expect(dialog).toHaveAttribute('aria-busy', 'true');
    expect(within(dialog).getByRole('status')).toHaveTextContent('Loading workspace data');
    expect(within(dialog).queryByRole('checkbox', { name: /reviewed.*removal/i })).not.toBeInTheDocument();
    expect(api.completeSystemCapabilityOperation).not.toHaveBeenCalled();
    // Act
    await act(async () => {
      if (!releasePreview) throw new Error('Removal preview was not requested.');
      releasePreview();
    });
    fireEvent.click(await within(dialog).findByRole('checkbox', { name: /reviewed.*removal/i }));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Unsubscribe from this system' }));
    // Assert
    await waitFor(() => expect(api.completeSystemCapabilityOperation).toHaveBeenCalledOnce());
    expect(api.completeSystemCapabilityOperation).toHaveBeenCalledWith(
      'tenant-a', 'system-a', 'operation-a', { expectedRevision: 1 });
    expect(await within(dialog).findByText('Removed Security monitoring from Selected system')).toBeVisible();
  });

  it('clears stale data and exposes a recoverable load error', async () => {
    // Arrange
    api.getSystemCapability.mockRejectedValue(new Error('Source no longer accessible.'));
    // Act
    mount();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Source no longer accessible.');
    expect(screen.queryByRole('button', { name: 'Remove from this system' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeVisible();
  });

  it('reviews a full proposal through the source-scoped adapter with its exact revision', async () => {
    // Arrange
    mount('?tab=evidence');
    fireEvent.click(await screen.findByRole('button', { name: 'View Policy proposal for AC-1' }));
    const dialog = await screen.findByRole('dialog');
    await within(dialog).findByText('Proposed policy update.');
    expect(within(dialog).getByRole('button', { name: 'Accept proposal' })).toBeDisabled();
    // Act
    fireEvent.click(within(dialog).getByRole('checkbox', { name: /reviewed the source/i }));
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Review note' }), { target: { value: 'Reviewed policy wording.' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Accept proposal' }));
    // Assert
    await waitFor(() => expect(api.reviewSystemCapabilityNarrative).toHaveBeenCalledOnce());
    expect(api.reviewSystemCapabilityNarrative).toHaveBeenCalledWith('tenant-a', 'system-a', 'provider', 'capability-a', 'proposal-a',
      { expectedRevision: 3, decision: 'Approve', note: 'Reviewed policy wording.' });
  });

  it('blocks a stale proposal read instead of approving its earlier revision', async () => {
    // Arrange
    narratives.getProposalById.mockResolvedValue({ ...proposal(), revision: 4 });
    mount('?tab=evidence');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'View Policy proposal for AC-1' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(/proposal changed/i);
    expect(screen.getByRole('button', { name: 'Accept proposal' })).toBeDisabled();
    expect(api.reviewSystemCapabilityNarrative).not.toHaveBeenCalled();
  });

  it('uses the independent current narrative author permission and version when generating', async () => {
    // Arrange
    const detail = systemCapabilityDetailFixture();
    detail.permissions.canReviewResponsibilities = false;
    api.getSystemCapability.mockResolvedValue(detail);
    mount('?tab=evidence');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Generate Policy proposal for AC-1' }));
    // Assert
    await waitFor(() => expect(sourceGeneration.generateScopedSystemCapabilityProposal).toHaveBeenCalledWith(
      'tenant-a', 'system-a', 'provider', 'capability-a',
      { controlId: 'AC-1', narrativeType: 'Policy', expectedVersion: 2, sourceRevision: 'source-1' }));
    expect(narratives.generateProposal).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.getByLabelText('Current route')).toHaveTextContent('/systems/system-a/narratives/review?proposal=proposal-a'));
  });

  it('rejects stale removal previews and asks to refresh the current capability', async () => {
    // Arrange
    api.prepareSystemCapabilityRemoval.mockRejectedValue(Object.assign(new Error('Relationship changed.'), { status: 409 }));
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Remove from this system' }));
    // Act
    const dialog = await screen.findByRole('dialog');
    // Assert
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Relationship changed.');
    expect(within(dialog).getByRole('button', { name: 'Refresh capability' })).toBeVisible();
    expect(within(dialog).queryByRole('button', { name: 'Unsubscribe from this system' })).not.toBeInTheDocument();
  });

  it('resumes a removal operation from its URL without preparing a second removal', async () => {
    // Arrange
    mount('?operationId=operation-a');
    // Act
    const dialog = await screen.findByRole('dialog');
    await within(dialog).findByText('Exact persisted changes');
    // Assert
    expect(api.getSystemCapabilityOperation).toHaveBeenCalledWith('tenant-a', 'system-a', 'operation-a', expect.any(AbortSignal));
    expect(api.prepareSystemCapabilityRemoval).not.toHaveBeenCalled();
    expect(within(dialog).getByRole('button', { name: 'Unsubscribe from this system' })).toBeDisabled();
  });

  it('loads removal recovery correctly under React strict effect replay', async () => {
    // Arrange
    render(<StrictMode><MemoryRouter initialEntries={['/systems/system-a/security-capabilities/provider/capability-a?operationId=operation-a']}>
      <SystemCapabilityDetail tenantId="tenant-a" systemId="system-a" source="provider" recordId="capability-a" />
    </MemoryRouter></StrictMode>);
    // Act
    const dialog = await screen.findByRole('dialog');
    // Assert
    expect(await within(dialog).findByText('Exact persisted changes')).toBeVisible();
    expect(api.prepareSystemCapabilityRemoval).not.toHaveBeenCalled();
  });

  it('never confirms a recovered removal prepared against a different source revision', async () => {
    // Arrange
    const changed = systemSetupOperationFixture('Removal');
    changed.selections[0]!.sourceRevision = 'older-source';
    api.getSystemCapabilityOperation.mockResolvedValue(changed);
    // Act
    mount('?operationId=operation-a');
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(/revision|does not match/i);
    expect(screen.queryByRole('button', { name: 'Unsubscribe from this system' })).not.toBeInTheDocument();
  });

  it('generates an existing queued proposal with its exact revision instead of creating a duplicate', async () => {
    // Arrange
    const detail = systemCapabilityDetailFixture();
    detail.narratives[0]!.proposals[0]!.status = 'PendingGeneration';
    api.getSystemCapability.mockResolvedValue(detail);
    mount('?tab=evidence');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Generate queued Policy proposal for AC-1' }));
    // Assert
    await waitFor(() => expect(narratives.generateQueuedProposal).toHaveBeenCalledWith('system-a', 'proposal-a', 3));
    expect(sourceGeneration.generateScopedSystemCapabilityProposal).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.getByLabelText('Current route')).toHaveTextContent('proposal=proposal-a'));
  });

  it('rechecks protected evidence access without exposing a storage link', async () => {
    // Arrange
    mount('?tab=evidence');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Open reference' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Access to this protected reference was withdrawn.');
    expect(evidence.downloadEvidence).toHaveBeenCalledWith('system-a', 'evidence-a');
    expect(screen.queryByRole('link', { name: 'Open reference' })).not.toBeInTheDocument();
  });

  it('requires a note for returning a proposal and sends the exact saved revision', async () => {
    // Arrange
    mount('?tab=evidence');
    fireEvent.click(await screen.findByRole('button', { name: 'View Policy proposal for AC-1' }));
    const dialog = await screen.findByRole('dialog');
    await within(dialog).findByText('Proposed policy update.');
    fireEvent.click(within(dialog).getByRole('checkbox', { name: /reviewed the source/i }));
    expect(within(dialog).getByRole('button', { name: 'Return for revision' })).toBeDisabled();
    // Act
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Review note' }), { target: { value: 'Clarify organizational responsibilities.' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Return for revision' }));
    // Assert
    await waitFor(() => expect(api.reviewSystemCapabilityNarrative).toHaveBeenCalledWith(
      'tenant-a', 'system-a', 'provider', 'capability-a', 'proposal-a',
      { expectedRevision: 3, decision: 'RequestRevision', note: 'Clarify organizational responsibilities.' }));
  });

  it('does not grant narrative review or evidence management from responsibility-review permission', async () => {
    // Arrange
    const detail = systemCapabilityDetailFixture();
    detail.permissions.canReviewNarratives = false;
    detail.permissions.canManageEvidence = false;
    api.getSystemCapability.mockResolvedValue(detail);
    mount('?tab=evidence');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'View Policy proposal for AC-1' }));
    const dialog = await screen.findByRole('dialog');
    await within(dialog).findByText('Proposed policy update.');
    // Assert
    expect(within(dialog).getByRole('button', { name: 'Accept proposal' })).toBeDisabled();
    expect(screen.getByText(/Evidence management permission is required/i)).toBeVisible();
    expect(api.reviewSystemCapabilityNarrative).not.toHaveBeenCalled();
  });

  it('distinguishes local unlink and retains approved history and component placements', async () => {
    // Arrange
    api.getSystemCapability.mockResolvedValue(systemCapabilityDetailFixture('local'));
    const operation = systemSetupOperationFixture('Removal');
    const local = { ...operation, selections: operation.selections.map(selection => ({ ...selection, source: 'local' })) };
    api.prepareSystemCapabilityRemoval.mockResolvedValue({ operation: local, existing: false });
    mount('', 'local');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Remove from this system' }));
    const dialog = await screen.findByRole('dialog');
    // Assert
    expect(await within(dialog).findByRole('button', { name: 'Unlink from this system' })).toBeDisabled();
    expect(dialog).toHaveTextContent(/unlinks only the organization capability/i);
    expect(dialog).toHaveTextContent(/Component placements are retained/i);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(api.completeSystemCapabilityOperation).not.toHaveBeenCalled();
  });

  it('refreshes an uncertain removal before permitting retry against the same operation', async () => {
    // Arrange
    api.completeSystemCapabilityOperation.mockRejectedValue(new Error('Response lost.'));
    api.getSystemCapabilityOperation.mockRejectedValueOnce(new Error('Saved result temporarily unavailable.'))
      .mockResolvedValue(systemSetupOperationFixture('Removal', 'Partial'));
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Remove from this system' }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.click(await within(dialog).findByRole('checkbox', { name: /reviewed.*removal/i }));
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: 'Unsubscribe from this system' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(/Unable to refresh saved removal outcomes/);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Refresh saved outcomes' }));
    // Assert
    expect(await within(dialog).findByRole('button', { name: 'Retry unfinished removal' })).toBeDisabled();
    expect(api.completeSystemCapabilityOperation).toHaveBeenCalledOnce();
    expect(api.prepareSystemCapabilityRemoval).toHaveBeenCalledOnce();
  });

  it('reloads data and permissions when the effective workspace mode changes', async () => {
    // Arrange
    const view = mount();
    await screen.findByRole('heading', { name: 'Security monitoring' });
    const denied = systemCapabilityDetailFixture();
    denied.permissions.canRead = false;
    api.getSystemCapability.mockResolvedValue(denied);
    workspaceMode.value = 'support';
    // Act
    view.rerender(<MemoryRouter><SystemCapabilityDetail tenantId="tenant-a" systemId="system-a"
      source="provider" recordId="capability-a" /><Location /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(/no longer readable/i);
    expect(screen.queryByRole('button', { name: 'Remove from this system' })).not.toBeInTheDocument();
  });

  it('opens a real control-specific review from coverage rather than conflating mapped controls', async () => {
    // Arrange
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'AC-1' }));
    // Assert
    expect(await screen.findByRole('region', { name: 'Review AC-1' })).toBeVisible();
    expect(screen.getByLabelText('Current route')).toHaveTextContent('control=AC-1');
    expect(screen.getByRole('tab', { name: 'Coverage & duties' })).toHaveAttribute('aria-selected', 'true');
    screen.getAllByRole('link', { name: 'Open full system responsibility review' }).forEach(link =>
      expect(link).toHaveAttribute('href', '/systems/system-a/inheritance/subscriptions'));
  });
});
