import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { HostingPanel } from '../../features/provider-authorizations/HostingPanel';
import * as hosting from '../../features/provider-authorizations/hostingApi';
import * as api from '../../features/provider-authorizations/api';
import { downloadAuthenticatedFile } from '../../api/downloads';
import { PackageImportError } from '../../features/package-imports/request';
import type { HostingAssignment, HostingScopeRevision } from '../../features/provider-authorizations/hostingTypes';
import type { AzureScope, ExternalDecision, Page } from '../../features/provider-authorizations/types';
import { boundary, offering as initialOffering } from './testData';
import '../package-imports/crypto';

vi.mock('../../features/provider-authorizations/hostingApi', () => ({
  listHostingScopes: vi.fn(), listHostingAssignments: vi.fn(), createHostingScope: vi.fn(), createHostingAssignment: vi.fn(),
}));
vi.mock('../../features/provider-authorizations/api', () => ({
  getOffering: vi.fn(), listBoundaries: vi.fn(), listDecisions: vi.fn(), listDecisionHistory: vi.fn(),
}));
vi.mock('../../api/downloads', () => ({ downloadAuthenticatedFile: vi.fn() }));

const offering = { ...initialOffering, currentHostingScopeRevisionId: 'hosting-a' };
const scope: AzureScope = {
  cloud: 'AzureUSGovernment', directoryTenantId: '11111111-1111-1111-1111-111111111111',
  subscriptionId: '22222222-2222-2222-2222-222222222222',
  resourceId: '/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/synthetic',
};
const citation = {
  packageId: '44444444-4444-4444-4444-444444444444', artifactId: '55555555-5555-5555-5555-555555555555',
  archivePath: 'source.pdf', locator: 'page 3', quote: 'Synthetic limited service scope.',
};
const revision: HostingScopeRevision = {
  offeringId: offering.offeringId, offeringRevision: 4, snapshot: { revisionId: 'hosting-a', revision: 1, snapshotHash: 'scope-hash' },
  predecessorRevisionId: null, name: 'Limited hosting', permittedScopes: [scope],
  exclusions: [{ scope: { ...scope, resourceId: `${scope.resourceId}/providers/Microsoft.Storage/storageAccounts/excluded` }, rationale: 'Not allocated' }],
  citations: [citation], impactReviewId: null,
};
const assignment: HostingAssignment = {
  assignmentId: 'assignment-a', revision: 1, offeringId: offering.offeringId, systemId: 'system-a',
  hostingScope: revision.snapshot, assignedScopes: [scope], relationshipState: 'Undetermined',
};
const decision: ExternalDecision = {
  recordId: 'reference-a', offeringId: offering.offeringId, revisionId: 'reference-revision-a', revision: 2,
  snapshotHash: 'reference-hash', boundaryRevisionId: boundary.boundaryRevisionId, sourceCandidateRefs: [],
  recordKind: 'InheritedMicrosoftReference', reference: 'Synthetic Microsoft reference',
  issuingAuthority: 'Source authority', decisionAsStated: 'Source assertion only', issuedOn: null,
  effectiveOn: null, expiresOn: null, expiryBasis: 'NotRecorded', scopeStatement: 'Named services only.',
  conditions: ['Customer configuration remains required'], citations: [citation],
  metadataReviewState: 'Recorded', currentStanding: 'CurrentAsRecorded', recordedBy: 'test-reviewer',
  recordedAt: '2026-09-24T12:00:00Z', impactReviewRequired: true,
};
const page = <T,>(items: T[], current = 1, total = items.length): Page<T> => ({ items, page: current, pageSize: 1, total });
const onChanged = vi.fn();
const mount = () => render(<HostingPanel offering={offering} onChanged={onChanged} />);
const section = (name: string) => within(screen.getByRole('region', { name }));
const change = (label: string, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } });
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(done => { resolve = done; });
  return { promise, resolve };
}
async function prepareScope() {
  fireEvent.click(await screen.findByRole('button', { name: 'Prepare successor of hosting revision 1' }));
  change('Hosting scope name', 'Revised hosting');
}
async function prepareAssignment() {
  fireEvent.click(await screen.findByRole('button', { name: 'Assign using hosting revision 1' }));
  change('Customer tenant ID', '33333333-3333-3333-3333-333333333333');
  change('Customer system ID', 'system-a');
  const editor = section('Create hosting assignment');
  fireEvent.click(editor.getByRole('button', { name: 'Add scope' }));
  fireEvent.change(editor.getByLabelText('directoryTenantId 1'), { target: { value: scope.directoryTenantId } });
  fireEvent.change(editor.getByLabelText('subscriptionId 1'), { target: { value: scope.subscriptionId } });
  fireEvent.change(editor.getByLabelText('resourceId 1'), { target: { value: scope.resourceId } });
}
const confirmScope = () => fireEvent.click(screen.getByLabelText('I confirm this exact technical scope revision, not authorization coverage.'));

describe('scoped hosting tasks', () => {
  it('prefills the exact supplied snapshot without mounting unrelated tasks or querying assignments', async () => {
    // Arrange
    render(<HostingPanel offering={offering} initialScope={revision} task="scope" onChanged={onChanged} />);
    // Act
    await waitFor(() => expect(screen.getByLabelText('Hosting scope name')).toBeEnabled());
    // Assert
    expect(screen.getByLabelText('Hosting scope name')).toHaveValue(revision.name);
    expect(within(screen.getByRole('group', { name: 'Permitted hosting scopes' })).getByLabelText('resourceId 1')).toHaveValue(scope.resourceId);
    expect(screen.queryByLabelText('Customer tenant ID')).not.toBeInTheDocument();
    expect(hosting.listHostingAssignments).not.toHaveBeenCalled();
    expect(api.listDecisions).not.toHaveBeenCalled();
    expect(api.listBoundaries).not.toHaveBeenCalled();
  });
  it('blocks saving and preserves inputs when the task-level hosting read fails', async () => {
    // Arrange
    vi.mocked(hosting.listHostingScopes).mockRejectedValueOnce(new PackageImportError('Unavailable', 404));
    render(<HostingPanel offering={offering} initialScope={revision} task="scope" onChanged={onChanged} />);
    // Act
    await screen.findByText(/Hosting configuration unavailable/);
    // Assert
    expect(screen.getByRole('button', { name: 'Save hosting scope revision' })).toBeDisabled();
    expect(screen.getByLabelText('Hosting scope name')).toHaveValue(revision.name);
    fireEvent.click(screen.getByRole('button', { name: 'Retry hosting data' }));
    await waitFor(() => expect(screen.getByLabelText('Hosting scope name')).toBeEnabled());
    expect(screen.getByLabelText('Hosting scope name')).toHaveValue(revision.name);
  });
  it('keeps the parent task locked for an uncertain hosting write', async () => {
    // Arrange
    const pending = vi.fn();
    vi.mocked(hosting.createHostingScope).mockRejectedValueOnce(new Error('Connection lost'));
    render(<HostingPanel offering={offering} initialScope={revision} task="scope" onChanged={onChanged} onPendingChange={pending} />);
    await waitFor(() => expect(screen.getByLabelText('Hosting scope name')).toBeEnabled());
    // Act
    confirmScope();
    fireEvent.click(screen.getByRole('button', { name: 'Save hosting scope revision' }));
    await screen.findByText(/Connection lost.*Outcome uncertain/);
    // Assert
    expect(pending).toHaveBeenLastCalledWith(true);
    expect(onChanged).not.toHaveBeenCalled();
  });
});
const confirmAssignment = () => fireEvent.click(screen.getByLabelText('I confirm this allocation grants no permissions or authorization coverage.'));

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(hosting.listHostingScopes).mockResolvedValue(page([revision]));
  vi.mocked(hosting.listHostingAssignments).mockResolvedValue(page([assignment]));
  vi.mocked(api.getOffering).mockResolvedValue(offering);
  vi.mocked(api.listBoundaries).mockResolvedValue(page([{ ...boundary,
    providerResponsibilities: ['Provider operates service'], customerResponsibilities: ['Customer secures workload'],
  }]));
  vi.mocked(api.listDecisions).mockResolvedValue(page([decision]));
  vi.mocked(api.listDecisionHistory).mockResolvedValue(page([decision]));
  vi.mocked(hosting.createHostingScope).mockResolvedValue({ ...revision, offeringRevision: 5,
    snapshot: { revisionId: 'hosting-b', revision: 2, snapshotHash: 'scope-next-hash' },
    predecessorRevisionId: 'hosting-a', name: 'Revised hosting', impactReviewId: 'impact-new',
  });
  vi.mocked(hosting.createHostingAssignment).mockResolvedValue(assignment);
  vi.mocked(downloadAuthenticatedFile).mockResolvedValue(undefined);
});

describe('offering hosting scope and assignment panel', () => {
  it('shows genuine loading and empty states without inferring universal coverage', async () => {
    // Arrange
    const load = deferred<Page<HostingScopeRevision>>();
    vi.mocked(hosting.listHostingScopes).mockReturnValue(load.promise);
    vi.mocked(hosting.listHostingAssignments).mockResolvedValue(page([]));
    mount();
    // Act
    expect(section('Hosting scope revisions').getByRole('status')).toHaveTextContent('Loading');
    await act(async () => load.resolve(page([])));
    // Assert
    expect(section('Hosting scope revisions').getByText(/No hosting scope revisions/)).toBeInTheDocument();
    expect(section('Hosting assignments').getByText(/No hosting assignments/)).toBeInTheDocument();
    expect(screen.getByText(/Empty scope never means universal coverage/)).toBeInTheDocument();
    expect(onChanged).not.toHaveBeenCalled();
  });

  it('renders exact immutable scopes, exclusions, hashes and technical relationship state', async () => {
    // Arrange
    mount();
    // Act
    await screen.findByRole('button', { name: 'Assign using hosting revision 1' });
    // Assert
    const scopes = section('Hosting scope revisions');
    expect(scopes.getAllByText(scope.directoryTenantId)).toHaveLength(2);
    expect(scopes.getAllByText(scope.subscriptionId).length).toBeGreaterThan(0);
    expect(scopes.getByText('scope-hash')).toBeInTheDocument();
    expect(scopes.getByText('Not allocated')).toBeInTheDocument();
    expect(section('Hosting assignments').getByText('Undetermined')).toBeInTheDocument();
    expect(screen.getByText(/does not create Azure resources/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /grant|confirm covered/i })).not.toBeInTheDocument();
  });

  it('paginates scope and assignment reads independently, using server totals', async () => {
    // Arrange
    vi.mocked(hosting.listHostingScopes).mockResolvedValue(page([revision], 1, 2));
    vi.mocked(hosting.listHostingAssignments).mockResolvedValue(page([assignment], 1, 2));
    mount();
    await screen.findByRole('button', { name: 'Assign using hosting revision 1' });
    vi.mocked(hosting.listHostingScopes).mockResolvedValue(page([{ ...revision, name: 'Historical page' }], 2, 2));
    // Act
    fireEvent.click(section('Hosting scope revisions').getByRole('button', { name: 'Next' }));
    // Assert
    await waitFor(() => expect(hosting.listHostingScopes).toHaveBeenLastCalledWith(offering.offeringId, 2, expect.any(AbortSignal)));
    expect(await screen.findByText('Historical page')).toBeInTheDocument();
    expect(hosting.listHostingAssignments).toHaveBeenCalledTimes(1);
    vi.mocked(hosting.listHostingAssignments).mockResolvedValue(page([assignment], 2, 2));
    fireEvent.click(section('Hosting assignments').getByRole('button', { name: 'Next' }));
    await waitFor(() => expect(hosting.listHostingAssignments).toHaveBeenLastCalledWith(offering.offeringId, 2, expect.any(AbortSignal)));
  });

  it.each([401, 403, 404, 500])('surfaces %s reads with retry, never as empty successful results', async status => {
    // Arrange
    vi.mocked(hosting.listHostingScopes).mockRejectedValueOnce(new PackageImportError('Scope unavailable; select an authorized provider workspace.', status));
    mount();
    // Act
    const alert = await within(screen.getByRole('region', { name: 'Hosting scope revisions' })).findByRole('alert');
    // Assert
    expect(alert).toHaveTextContent('Scope unavailable');
    expect(section('Hosting scope revisions').queryByText(/No hosting scope revisions/)).not.toBeInTheDocument();
    fireEvent.click(within(alert).getByRole('button', { name: 'Retry' }));
    expect(await screen.findByRole('button', { name: 'Assign using hosting revision 1' })).toBeInTheDocument();
  });

  it('requires explicit confirmation before saving a successor with exact scopes and evidence', async () => {
    // Arrange
    mount();
    await prepareScope();
    expect(screen.getByRole('button', { name: 'Save hosting scope revision' })).toBeDisabled();
    // Act
    confirmScope();
    fireEvent.click(screen.getByRole('button', { name: 'Save hosting scope revision' }));
    // Assert
    await waitFor(() => expect(hosting.createHostingScope).toHaveBeenCalledWith(offering.offeringId, {
      expectedOfferingRevision: 4, predecessorRevisionId: 'hosting-a', name: 'Revised hosting',
      permittedScopes: [scope], exclusions: revision.exclusions, citations: [citation],
    }, expect.any(String)));
    expect(onChanged).toHaveBeenCalledOnce();
    expect(await screen.findByText(/Saved hosting scope revision 2/)).toHaveTextContent('hosting-b');
    expect(screen.getByText(/Saved hosting scope revision 2/)).toHaveTextContent('impact-new');
    expect(revision.name).toBe('Limited hosting');
  });

  it('keeps the form editable while confirmation and scope fields are incomplete', async () => {
    // Arrange
    render(<HostingPanel offering={{ ...offering, currentHostingScopeRevisionId: null }} onChanged={onChanged} />);
    await screen.findByRole('button', { name: 'Assign using hosting revision 1' });
    const editor = section('Prepare hosting scope revision');
    // Act
    change('Hosting scope name', 'New scope');
    fireEvent.click(editor.getByRole('button', { name: 'Add scope' }));
    // Assert
    expect(editor.getByLabelText('directoryTenantId 1')).toBeEnabled();
    expect(editor.getByLabelText('directoryTenantId 1')).toBeInvalid();
    expect(screen.getByRole('button', { name: 'Save hosting scope revision' })).toBeDisabled();
  });

  it('retains the identical intent and key on uncertain failure despite a newer offering revision', async () => {
    // Arrange
    vi.mocked(hosting.createHostingScope).mockRejectedValueOnce(new Error('Connection lost'));
    const view = mount();
    await prepareScope();
    confirmScope();
    fireEvent.click(screen.getByRole('button', { name: 'Save hosting scope revision' }));
    await screen.findByText(/Outcome uncertain/);
    const first = vi.mocked(hosting.createHostingScope).mock.calls[0];
    // Act
    await act(async () => view.rerender(<HostingPanel offering={{ ...offering, revision: 9 }} onChanged={onChanged} />));
    expect(screen.getByLabelText('Hosting scope name')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Prepare successor of hosting revision 1' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Retry same operation' }));
    // Assert
    await waitFor(() => expect(onChanged).toHaveBeenCalledOnce());
    expect(vi.mocked(hosting.createHostingScope).mock.calls[1]).toEqual(first);
  });

  it('retains stale scope input and explicitly reloads concurrency metadata before reconfirmation', async () => {
    // Arrange
    vi.mocked(hosting.createHostingScope).mockRejectedValueOnce(new PackageImportError('AUTHORIZATION_CONTEXT_STALE', 409));
    mount();
    await prepareScope();
    confirmScope();
    fireEvent.click(screen.getByRole('button', { name: 'Save hosting scope revision' }));
    await screen.findByText(/AUTHORIZATION_CONTEXT_STALE/);
    vi.mocked(api.getOffering).mockResolvedValue({ ...offering, revision: 8, currentHostingScopeRevisionId: 'hosting-new-current' });
    // Act
    const reload = screen.getByRole('button', { name: 'Reload current offering revision' });
    await waitFor(() => expect(reload).toBeEnabled());
    fireEvent.click(reload);
    await waitFor(() => expect(screen.getByText(/Expected offering revision: 8/)).toBeInTheDocument());
    // Assert
    expect(screen.getByLabelText('Hosting scope name')).toHaveValue('Revised hosting');
    expect(screen.getByText(/Predecessor revision: hosting-new-current/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save hosting scope revision' })).toBeDisabled();
    await waitFor(() => expect(screen.getByLabelText('I confirm this exact technical scope revision, not authorization coverage.')).toBeEnabled());
    confirmScope();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save hosting scope revision' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Save hosting scope revision' }));
    await waitFor(() => expect(hosting.createHostingScope).toHaveBeenLastCalledWith(offering.offeringId,
      expect.objectContaining({ expectedOfferingRevision: 8, predecessorRevisionId: 'hosting-new-current', name: 'Revised hosting' }), expect.any(String)));
  });

  it('never infers an assignment from a selected scope, and submits only confirmed explicit allocation', async () => {
    // Arrange
    mount();
    await prepareAssignment();
    expect(screen.getByRole('button', { name: 'Save hosting assignment' })).toBeDisabled();
    // Act
    confirmAssignment();
    fireEvent.click(screen.getByRole('button', { name: 'Save hosting assignment' }));
    // Assert
    await waitFor(() => expect(hosting.createHostingAssignment).toHaveBeenCalledWith(offering.offeringId, {
      targetTenantId: '33333333-3333-3333-3333-333333333333', systemId: 'system-a',
      hostingScopeRevisionId: 'hosting-a', assignedScopes: [scope], references: [],
    }, expect.any(String)));
    expect(await screen.findByText(/Saved hosting assignment assignment-a/)).toHaveTextContent('Undetermined');
    expect(onChanged).toHaveBeenCalledOnce();
  });

  it('requires an explicit snapshot and at least one assignment scope, even with IDs and confirmation', async () => {
    // Arrange
    mount();
    await screen.findByRole('button', { name: 'Assign using hosting revision 1' });
    change('Customer tenant ID', '33333333-3333-3333-3333-333333333333');
    change('Customer system ID', 'system-a');
    // Act
    confirmAssignment();
    // Assert
    expect(screen.getByRole('button', { name: 'Save hosting assignment' })).toBeDisabled();
    expect(hosting.createHostingAssignment).not.toHaveBeenCalled();
  });

  it.each([
    `${scope.resourceId.replace(scope.subscriptionId, `${scope.subscriptionId}extra`)}`,
    `/subscriptions/${scope.subscriptionId}/resourceGroups/../outside`,
    `/subscriptions/${scope.subscriptionId}/resourceGroups/*`,
  ])('rejects malformed or prefix-collision resource scopes without silent disabled state: %s', async resourceId => {
    // Arrange
    mount();
    await prepareAssignment();
    const editor = section('Create hosting assignment');
    // Act
    fireEvent.change(editor.getByLabelText('resourceId 1'), { target: { value: resourceId } });
    confirmAssignment();
    // Assert
    expect(screen.getByRole('button', { name: 'Save hosting assignment' })).toBeDisabled();
    expect(editor.getByText(/Resource scope must use the exact subscription ID and valid path segments/)).toBeInTheDocument();
    expect(hosting.createHostingAssignment).not.toHaveBeenCalled();
  });

  it('requires reconfirmation after changing confirmed scope material', async () => {
    // Arrange
    mount();
    await prepareScope();
    confirmScope();
    // Act
    change('Hosting scope name', 'Another explicit revision');
    // Assert
    expect(screen.getByLabelText('I confirm this exact technical scope revision, not authorization coverage.')).not.toBeChecked();
    expect(screen.getByRole('button', { name: 'Save hosting scope revision' })).toBeDisabled();
  });

  it('shows assignment read denial, not an empty list or an enabled allocation action', async () => {
    // Arrange
    vi.mocked(hosting.listHostingAssignments).mockRejectedValue(new PackageImportError('Allocation read denied.', 403));
    mount();
    await prepareAssignment();
    // Act
    confirmAssignment();
    // Assert
    expect(await section('Hosting assignments').findByRole('alert')).toHaveTextContent('Allocation read denied');
    expect(section('Hosting assignments').queryByText(/No hosting assignments/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save hosting assignment' })).toBeDisabled();
  });

  it('surfaces reload failure without clearing the pending scope draft', async () => {
    // Arrange
    vi.mocked(api.getOffering).mockRejectedValueOnce(new PackageImportError('Current offering access denied.', 403));
    mount();
    await prepareScope();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Reload current offering revision' }));
    // Assert
    expect(await screen.findByText('Current offering access denied.')).toBeInTheDocument();
    expect(screen.getByLabelText('Hosting scope name')).toHaveValue('Revised hosting');
    expect(onChanged).not.toHaveBeenCalled();
  });

  it('paginates responsibility and inherited history records and surfaces their errors', async () => {
    // Arrange
    vi.mocked(api.listBoundaries).mockResolvedValueOnce(page([boundary], 1, 2));
    vi.mocked(api.listDecisionHistory).mockResolvedValueOnce(page([decision], 1, 2))
      .mockRejectedValueOnce(new PackageImportError('History is unavailable.', 403));
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'History of Synthetic Microsoft reference' }));
    await section('Inherited reference history').findByText('reference-revision-a');
    // Act
    fireEvent.click(section('Boundary responsibilities').getByRole('button', { name: 'Next' }));
    fireEvent.click(section('Inherited reference history').getByRole('button', { name: 'Next' }));
    // Assert
    await waitFor(() => expect(api.listBoundaries).toHaveBeenLastCalledWith(offering.offeringId, 2, expect.any(AbortSignal)));
    expect(await section('Inherited reference history').findByRole('alert')).toHaveTextContent('History is unavailable.');
    expect(section('Inherited reference history').queryByText('reference-revision-a')).not.toBeInTheDocument();
  });

  it('does not show late data or retained inputs under a different offering identity', async () => {
    // Arrange
    const oldRead = deferred<Page<HostingScopeRevision>>();
    vi.mocked(hosting.listHostingScopes).mockReturnValueOnce(oldRead.promise).mockResolvedValue(page([]));
    const view = mount();
    change('Customer system ID', 'old-system');
    // Act
    await act(async () => {
      view.rerender(<HostingPanel offering={{ ...offering, offeringId: 'other-offering' }} onChanged={onChanged} />);
      oldRead.resolve(page([revision]));
    });
    // Assert
    expect(screen.getByLabelText('Customer system ID')).toHaveValue('');
    expect(screen.queryByRole('button', { name: 'Assign using hosting revision 1' })).not.toBeInTheDocument();
    expect(hosting.listHostingScopes).toHaveBeenLastCalledWith('other-offering', 1, expect.any(AbortSignal));
  });

  it('blocks duplicate submits until a durable assignment response and keeps input on rejection', async () => {
    // Arrange
    const pending = deferred<HostingAssignment>();
    vi.mocked(hosting.createHostingAssignment).mockReturnValue(pending.promise);
    mount();
    await prepareAssignment();
    confirmAssignment();
    const button = screen.getByRole('button', { name: 'Save hosting assignment' });
    // Act
    fireEvent.click(button);
    fireEvent.submit(button.closest('form')!);
    // Assert
    expect(hosting.createHostingAssignment).toHaveBeenCalledOnce();
    expect(onChanged).not.toHaveBeenCalled();
    expect(screen.getByLabelText('Customer system ID')).toBeDisabled();
    await act(async () => pending.resolve(assignment));
    expect(onChanged).toHaveBeenCalledOnce();
  });

  it.each([400, 403, 409, 422])('retains customer inputs and snapshot on assignment rejection %s', async status => {
    // Arrange
    vi.mocked(hosting.createHostingAssignment).mockRejectedValueOnce(new PackageImportError('Assignment denied or stale; review the retained target.', status));
    mount();
    await prepareAssignment();
    confirmAssignment();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save hosting assignment' }));
    // Assert
    expect(await screen.findByText(/Assignment denied or stale/)).toBeInTheDocument();
    expect(screen.getByLabelText('Customer tenant ID')).toHaveValue('33333333-3333-3333-3333-333333333333');
    expect(screen.getByLabelText('Customer system ID')).toHaveValue('system-a');
    expect(section('Create hosting assignment').getByText(/Selected hosting revision: hosting-a/)).toBeInTheDocument();
    expect(onChanged).not.toHaveBeenCalled();
  });

  it('retries an uncertain assignment with its original snapshot, scopes and key', async () => {
    // Arrange
    vi.mocked(hosting.createHostingAssignment).mockRejectedValueOnce(new Error('Response lost'));
    mount();
    await prepareAssignment();
    confirmAssignment();
    fireEvent.click(screen.getByRole('button', { name: 'Save hosting assignment' }));
    await screen.findByText(/Outcome uncertain/);
    const first = vi.mocked(hosting.createHostingAssignment).mock.calls[0];
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry same operation' }));
    // Assert
    await waitFor(() => expect(onChanged).toHaveBeenCalledOnce());
    expect(vi.mocked(hosting.createHostingAssignment).mock.calls[1]).toEqual(first);
  });

  it('shows explicit owner/provider/customer responsibilities from retained boundary versions', async () => {
    // Arrange
    mount();
    // Act
    const context = section('Boundary responsibilities');
    await context.findByText('Provider operates service');
    // Assert
    expect(context.getByText('Customer secures workload')).toBeInTheDocument();
    expect(context.getByText(/Provider owner: provider-1/)).toBeInTheDocument();
    expect(context.getByText(/boundary-1/)).toBeInTheDocument();
    expect(context.getByText(/not acceptance of customer responsibilities/)).toBeInTheDocument();
  });

  it('shows inherited reference revisions without treating recorded metadata as verification or scope', async () => {
    // Arrange
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'History of Synthetic Microsoft reference' }));
    // Assert
    await waitFor(() => expect(api.listDecisionHistory).toHaveBeenCalledWith(offering.offeringId, 'reference-a', 1, expect.any(AbortSignal)));
    const history = section('Inherited reference history');
    expect(await history.findByText('reference-revision-a')).toBeInTheDocument();
    expect(history.getByText('reference-hash')).toBeInTheDocument();
    expect(history.getByText('NotRecorded')).toBeInTheDocument();
    expect(history.getByText(/Current as recorded; not independently verified/)).toBeInTheDocument();
    expect(history.getByText('Customer configuration remains required')).toBeInTheDocument();
  });

  it('keeps paging available when a decision page has no inherited references', async () => {
    // Arrange
    vi.mocked(api.listDecisions).mockResolvedValueOnce(page([{ ...decision, recordKind: 'ProviderDecision' }], 1, 2));
    mount();
    const context = section('Inherited Microsoft references');
    await context.findByText(/No inherited Microsoft references on this page/);
    // Act
    fireEvent.click(context.getByRole('button', { name: 'Next' }));
    // Assert
    await waitFor(() => expect(api.listDecisions).toHaveBeenLastCalledWith(offering.offeringId, 2, expect.any(AbortSignal)));
    expect(await context.findByText('Synthetic Microsoft reference')).toBeInTheDocument();
  });

  it('downloads source identities via the authenticated helper and surfaces denial', async () => {
    // Arrange
    vi.mocked(downloadAuthenticatedFile).mockRejectedValueOnce(new Error('Protected source access denied'));
    mount();
    const sources = section('Hosting scope revisions');
    // Act
    fireEvent.click(await sources.findByRole('button', { name: 'Download source source.pdf' }));
    // Assert
    await waitFor(() => expect(downloadAuthenticatedFile).toHaveBeenCalledWith(
      `/api/csp/package-imports/${citation.packageId}/artifacts/${citation.artifactId}/content`, 'source.pdf', expect.any(AbortSignal)));
    expect(await sources.findByRole('alert')).toHaveTextContent('Protected source access denied');
    expect(sources.queryByRole('link', { name: /source.pdf/ })).not.toBeInTheDocument();
  });
});
