import { act, fireEvent, render, screen, within, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import EnvironmentAssociations from '../../components/forms/EnvironmentAssociations';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import * as hostingApi from '../../features/provider-relationships/api';
import * as capabilityApi from '../../features/workspace-operations/system-capabilities/systemCapabilityApi';
import { allocationResponse } from '../provider-relationships/fixtures';

const access = vi.hoisted(() => ({ systemId: 'system-a', canRead: true }));
vi.mock('../../features/provider-relationships/api', () => ({ listAllProviderRelationships: vi.fn(), associateProviderRelationship: vi.fn() }));
vi.mock('../../features/workspace-operations/system-capabilities/systemCapabilityApi', () => ({ listSystemCapabilities: vi.fn() }));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => ({
    workspace: { kind: 'organization', tenantId: 'org-a', mode: 'ordinary' },
    systemAccess: { systemId: access.systemId, permissions: { canRead: access.canRead } },
  }),
}));
const prefill = vi.fn();
const associated = { ...allocationResponse, relationshipId: 'relationship-a', revision: 7, assignmentRevision: 4,
  state: 'SeparateBoundaryConsumer' as const, authorizationRevisionId: 'authorization-3', boundaryRevisionId: 'boundary-2',
  reviewedBy: 'reviewer-a', reviewedAt: '2026-09-20T12:00:00Z',
  assignedScopes: [...allocationResponse.assignedScopes, { kind: 'Service' as const, serviceId: 'service-a',
    serviceName: 'Shared mail', environment: 'Microsoft365DoD' as const, tenantReference: 'tenant-mail' }],
};
function mount(readOnly = false, busy = false) {
  return render(<MemoryRouter><WorkspaceNavigationProvider workspace={{ kind: 'organization', tenantId: 'org-a' }}>
    <EnvironmentAssociations systemId="system-a" hostingModel="Hybrid" description="Original description"
      readOnly={readOnly} busy={busy} onPrefill={prefill} />
  </WorkspaceNavigationProvider></MemoryRouter>);
}
beforeEach(() => {
  vi.resetAllMocks();
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', ''); };
  HTMLDialogElement.prototype.close = function () { this.removeAttribute('open'); };
  access.systemId = 'system-a';
  access.canRead = true;
  vi.mocked(hostingApi.listAllProviderRelationships).mockResolvedValue([associated]);
  vi.mocked(capabilityApi.listSystemCapabilities).mockResolvedValue({
    items: [], page: 1, pageSize: 10, total: 0, scope: 'applied', grouping: 'capability', boundaries: [],
    permissions: { canRead: true, canManage: true, canReviewResponsibilities: false, canAuthorNarratives: false,
      canManageEvidence: false, canReviewNarratives: false },
  });
});

describe('compact associated provider scope', () => {
  it('exposes a separate review action only with server permission, regardless of profile edit access', async () => {
    // Arrange
    vi.mocked(hostingApi.listAllProviderRelationships).mockResolvedValue([{ ...associated, canReviewRelationship: true }]);
    mount(true);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Open' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Review provider relationship' })).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Copy hosting description to draft' })).not.toBeInTheDocument();
  });
  it('does not offer ordinary review for a covered-scope relationship even when review permission is present', async () => {
    // Arrange
    vi.mocked(hostingApi.listAllProviderRelationships).mockResolvedValue([{ ...associated,
      state: 'ExplicitlyCoveredByRecordedScope', canReviewRelationship: true }]);
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Open' }));
    // Assert
    expect(screen.queryByRole('button', { name: 'Review provider relationship' })).not.toBeInTheDocument();
    expect(screen.getByText(/requires an assigned AO/)).toBeVisible();
  });
  it('keeps available allocations inside a single right-side drawer with readable labels and collapsed technical details', async () => {
    // Arrange
    vi.mocked(hostingApi.listAllProviderRelationships).mockResolvedValue([
      { ...allocationResponse, providerName: 'Flankspeed', offeringName: 'Azure IL5 · Shared services',
        hostingScopeName: 'raw_scope__tenant_guid_123' },
      { ...allocationResponse, assignmentId: 'assignment-b' },
    ]);
    mount();
    await screen.findByText('No provider scope associated');
    const choose = screen.getByRole('button', { name: 'Choose provider hosting' });
    // Assert
    expect(screen.getByRole('heading', { name: 'Provider hosting' })).toBeVisible();
    const card = screen.getByRole('region', { name: 'Provider hosting' });
    expect(card).toHaveClass('rounded-[10px]', 'border', 'bg-white', 'p-[22px]');
    const banner = screen.getByRole('group', { name: 'Available provider hosting' });
    expect(banner).toHaveClass('flex', 'flex-wrap', 'items-center', 'justify-between');
    expect(banner).toContainElement(choose);
    expect(screen.getAllByRole('button', { name: 'Choose provider hosting' })).toHaveLength(1);
    expect(screen.getByText('2 scopes are available for this system.')).toBeVisible();
    expect(screen.queryByText('Flankspeed · Azure IL5 · Shared services')).not.toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.queryByText(/Applied capabilities/)).not.toBeInTheDocument();
    // Act
    choose.focus();
    fireEvent.click(choose);
    const drawer = screen.getByRole('dialog', { name: 'Choose provider hosting' });
    // Assert
    expect(drawer).toHaveClass('ml-auto');
    expect(within(drawer).getByText('Flankspeed · Azure IL5 · Shared services')).toBeVisible();
    expect(within(drawer).getByText('raw_scope__tenant_guid_123')).not.toBeVisible();
    // Act
    fireEvent.click(within(drawer).getAllByRole('button', { name: 'Associate CSP scope' })[0]!);
    // Assert
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    expect(screen.getByText('assignment-a')).not.toBeVisible();
    expect(screen.getByText('raw_scope__tenant_guid_123')).not.toBeVisible();
    const titleId = screen.getByRole('dialog').getAttribute('aria-labelledby');
    expect(titleId).toBeTruthy();
    expect(document.querySelectorAll(`[id="${titleId}"]`)).toHaveLength(1);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.getByRole('dialog', { name: 'Choose provider hosting' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Close dialog' }));
    // Assert
    expect(choose).toHaveFocus();
    expect(hostingApi.associateProviderRelationship).not.toHaveBeenCalled();
    expect(prefill).not.toHaveBeenCalled();
  });
  it('associates the selected available scope only after confirmation and rereads its revision', async () => {
    // Arrange
    vi.mocked(hostingApi.listAllProviderRelationships).mockResolvedValue([allocationResponse]);
    vi.mocked(hostingApi.associateProviderRelationship).mockImplementation(async () => {
      vi.mocked(hostingApi.listAllProviderRelationships).mockResolvedValue([{ ...allocationResponse, relationshipId: 'new-relationship', revision: 1 }]);
      return { relationshipId: 'new-relationship', assignmentId: allocationResponse.assignmentId, revision: 1, state: 'Undetermined' };
    });
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Choose provider hosting' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Associate CSP scope' }));
    const dialog = screen.getByRole('dialog', { name: 'Associate CSP scope' });
    // Assert
    expect(hostingApi.associateProviderRelationship).not.toHaveBeenCalled();
    expect(within(dialog).getByRole('button', { name: 'Confirm association' })).toBeDisabled();
    // Act
    fireEvent.click(within(dialog).getByRole('checkbox'));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Confirm association' }));
    // Assert
    await waitFor(() => expect(hostingApi.associateProviderRelationship).toHaveBeenCalledWith('system-a',
      { assignmentId: allocationResponse.assignmentId, expectedAssignmentRevision: allocationResponse.assignmentRevision }, expect.any(String)));
    expect(await screen.findByRole('table', { name: 'Associated provider scope' })).toHaveTextContent('Harbor hosting');
    expect(prefill).not.toHaveBeenCalled();
  });
  it('does not expose an association action when the server denies it', async () => {
    // Arrange
    vi.mocked(hostingApi.listAllProviderRelationships).mockResolvedValue([{ ...allocationResponse, canAssociate: false }]);
    // Act
    mount();
    await screen.findByText('1 scope is available for this system.');
    fireEvent.click(screen.getByRole('button', { name: 'Choose provider hosting' }));
    // Assert
    expect(screen.queryByRole('button', { name: 'Associate CSP scope' })).not.toBeInTheDocument();
    expect(screen.getByText(/Association is not currently permitted/)).toBeVisible();
  });
  it('rejects a changed allocation before posting and preserves confirmation on failure', async () => {
    // Arrange
    vi.mocked(hostingApi.listAllProviderRelationships).mockResolvedValueOnce([allocationResponse])
      .mockResolvedValue([{ ...allocationResponse, assignmentRevision: allocationResponse.assignmentRevision + 1 }]);
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Choose provider hosting' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Associate CSP scope' }));
    // Act
    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm association' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('allocation changed');
    expect(hostingApi.associateProviderRelationship).not.toHaveBeenCalled();
    expect(screen.getByRole('dialog')).toBeVisible();
  });
  it('retries the same confirmed intent with the same idempotency key after a failed request', async () => {
    // Arrange
    vi.mocked(hostingApi.listAllProviderRelationships).mockResolvedValue([allocationResponse]);
    vi.mocked(hostingApi.associateProviderRelationship).mockRejectedValue(new Error('Connection interrupted'));
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Choose provider hosting' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Associate CSP scope' }));
    fireEvent.click(screen.getByRole('checkbox'));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Confirm association' }));
    await screen.findByText('Connection interrupted');
    fireEvent.click(screen.getByRole('button', { name: 'Confirm association' }));
    // Assert
    await waitFor(() => expect(hostingApi.associateProviderRelationship).toHaveBeenCalledTimes(2));
    const calls = vi.mocked(hostingApi.associateProviderRelationship).mock.calls;
    expect(calls[0]![2]).toBe(calls[1]![2]);
    expect(screen.getByRole('checkbox')).toBeChecked();
    expect(prefill).not.toHaveBeenCalled();
  });
  it('locks cancellation and duplicate submissions while the fresh allocation read is pending', async () => {
    // Arrange
    let complete!: (rows: typeof allocationResponse[]) => void;
    vi.mocked(hostingApi.listAllProviderRelationships).mockResolvedValueOnce([allocationResponse])
      .mockImplementation(() => new Promise(resolve => { complete = resolve; }));
    vi.mocked(hostingApi.associateProviderRelationship).mockRejectedValue(new Error('Retry needed'));
    mount();
    await screen.findByText('No provider scope associated');
    fireEvent.click(screen.getByRole('button', { name: 'Choose provider hosting' }));
    fireEvent.click(screen.getByRole('button', { name: 'Associate CSP scope' }));
    fireEvent.click(screen.getByRole('checkbox'));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Confirm association' }));
    fireEvent.click(screen.getByRole('button', { name: 'Associating…' }));
    const dialog = screen.getByRole('dialog');
    fireEvent(dialog, new Event('cancel', { cancelable: true }));
    // Assert
    expect(dialog).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Close dialog' })).toBeDisabled();
    expect(screen.getByRole('checkbox')).toBeDisabled();
    expect(hostingApi.listAllProviderRelationships).toHaveBeenCalledTimes(2);
    expect(hostingApi.associateProviderRelationship).not.toHaveBeenCalled();
    // Act
    await act(async () => complete([allocationResponse]));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Retry needed');
    expect(hostingApi.associateProviderRelationship).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeEnabled();
  });
  it('rechecks server permission before posting rather than trusting the selected row', async () => {
    // Arrange
    vi.mocked(hostingApi.listAllProviderRelationships).mockResolvedValueOnce([allocationResponse])
      .mockResolvedValue([{ ...allocationResponse, canAssociate: false }]);
    mount();
    await screen.findByText('No provider scope associated');
    fireEvent.click(screen.getByRole('button', { name: 'Choose provider hosting' }));
    fireEvent.click(screen.getByRole('button', { name: 'Associate CSP scope' }));
    // Act
    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm association' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Association is no longer permitted');
    expect(hostingApi.associateProviderRelationship).not.toHaveBeenCalled();
  });
  it('does not post after unmounting while the fresh allocation read is pending', async () => {
    // Arrange
    let complete!: (rows: typeof allocationResponse[]) => void;
    vi.mocked(hostingApi.listAllProviderRelationships).mockResolvedValueOnce([allocationResponse])
      .mockImplementation(() => new Promise(resolve => { complete = resolve; }));
    const view = mount();
    await screen.findByText('No provider scope associated');
    fireEvent.click(screen.getByRole('button', { name: 'Choose provider hosting' }));
    fireEvent.click(screen.getByRole('button', { name: 'Associate CSP scope' }));
    fireEvent.click(screen.getByRole('checkbox'));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Confirm association' }));
    view.unmount();
    await act(async () => complete([allocationResponse]));
    // Assert
    expect(hostingApi.associateProviderRelationship).not.toHaveBeenCalled();
    expect(prefill).not.toHaveBeenCalled();
  });
  it('disables drawer entry and inspection while the environment form is busy', async () => {
    // Arrange / Act
    mount(false, true);
    await screen.findByRole('table');
    // Assert
    expect(screen.getByRole('button', { name: 'Choose provider hosting' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Open' })).toBeDisabled();
    expect(prefill).not.toHaveBeenCalled();
  });
  it('keeps scope association independent of a read-only profile draft', async () => {
    // Arrange
    vi.mocked(hostingApi.listAllProviderRelationships).mockResolvedValue([allocationResponse]);
    // Act
    mount(true);
    fireEvent.click(await screen.findByRole('button', { name: 'Choose provider hosting' }));
    // Assert
    expect(await screen.findByRole('button', { name: 'Associate CSP scope' })).toBeEnabled();
    expect(hostingApi.associateProviderRelationship).not.toHaveBeenCalled();
  });

  it('shows actual scope counts and independent review/state without treating available allocations as associated', async () => {
    // Arrange
    vi.mocked(hostingApi.listAllProviderRelationships).mockResolvedValue([
      { ...allocationResponse, assignmentId: 'available-a', offeringName: 'Available hosting' }, associated,
    ]);
    // Act
    mount();
    const table = await screen.findByRole('table', { name: 'Associated provider scope' });
    // Assert
    expect(within(table).getAllByRole('row')).toHaveLength(2);
    expect(table).toHaveTextContent('Harbor provider · Harbor hosting');
    expect(within(table).getByText('1 resource scope · 1 service scope')).toBeVisible();
    expect(within(table).getByText('Review required')).toBeVisible();
    expect(within(table).getByText('Separate boundary consumer')).toBeVisible();
    expect(within(table).queryByText('Available hosting')).not.toBeInTheDocument();
    expect(screen.queryByText('Available hosting')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Choose provider hosting' })).toBeVisible();
    expect(screen.queryByRole('link', { name: 'Associate hosting & capabilities' })).not.toBeInTheDocument();
    expect(prefill).not.toHaveBeenCalled();
  });

  it('opens exact provenance and scopes, links the real unselected task, and requires explicit draft confirmation', async () => {
    // Arrange
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Open' }));
    const dialog = screen.getByRole('dialog', { name: 'Provider scope details' });
    fireEvent.click(within(dialog).getByText('Details', { exact: true }));
    // Assert
    for (const value of ['relationship-a', 'assignment-a', 'offering-a', 'system-a', 'authorization-3', 'boundary-2', 'reviewer-a']) {
      expect(within(dialog).getByText(value)).toBeVisible();
    }
    expect(within(dialog).getByText('Relationship revision: 7')).toBeVisible();
    expect(within(dialog).getByText('Allocation revision: 4')).toBeVisible();
    expect(within(dialog).getByRole('link', { name: 'Open hosting task' })).toHaveAttribute('href',
      '/workspaces/organizations/org-a/systems/system-a/profile/EnvironmentAndDeployment/hosting');
    expect(within(dialog).getByText(/Select this allocation again/)).toBeVisible();
    expect(within(dialog).getByText(/does not authorize this mission system/)).toBeVisible();
    fireEvent.click(within(dialog).getByText('Hosting scope details'));
    expect(within(dialog).getByText('Resource scope: /subscriptions/subscription-a/resourceGroups/mission')).toBeVisible();
    expect(within(dialog).getByText('Service identifier: service-a')).toBeVisible();
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: 'Copy hosting description to draft' }));
    const review = screen.getByRole('dialog', { name: 'Copy hosting description to draft' });
    // Assert
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    expect(review).toContainElement(document.activeElement as HTMLElement);
    expect(within(review).getByRole('button', { name: 'Use in draft' })).toBeDisabled();
    expect(prefill).not.toHaveBeenCalled();
    // Act
    fireEvent.click(within(review).getByRole('checkbox'));
    fireEvent.click(within(review).getByRole('button', { name: 'Use in draft' }));
    // Assert
    expect(prefill).toHaveBeenCalledWith({ hostingModel: 'Hybrid',
      additionalDetails: 'Harbor hosting - Mission resource group, provided by Harbor provider.' });
  });

  it.each([
    ['Undetermined', true, 'Not reviewed'],
    ['Undetermined', false, 'Not reviewed'],
    ['SeparateBoundaryConsumer', false, 'Separate boundary consumer'],
    ['ExplicitlyCoveredByRecordedScope', false, 'Covered by recorded scope'],
    ['ExplicitlyCoveredByRecordedScope', true, 'Covered by recorded scope'],
  ] as const)('renders recorded %s state and review flag without inference', async (state, reviewRequired, label) => {
    // Arrange
    vi.mocked(hostingApi.listAllProviderRelationships).mockResolvedValue([{ ...associated, state, reviewRequired }]);
    // Act
    mount();
    const table = await screen.findByRole('table');
    // Assert
    expect(within(table).getByText(label)).toBeVisible();
    expect(within(table).queryByText('Review required') !== null).toBe(reviewRequired || state === 'Undetermined');
    expect(within(table).queryByText(state, { exact: true })).not.toBeInTheDocument();
    // Act
    fireEvent.click(within(table).getByRole('button', { name: 'Open' }));
    fireEvent.click(within(screen.getByRole('dialog')).getByText('Details', { exact: true }));
    // Assert
    expect(within(screen.getByRole('dialog')).getByText(state, { exact: true })).toBeVisible();
  });

  it('permits read-only inspection but not draft prefill or capability library changes', async () => {
    // Arrange / Act
    mount(true);
    fireEvent.click(await screen.findByRole('button', { name: 'Open' }));
    // Assert
    expect(screen.getByRole('dialog', { name: 'Provider scope details' })).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Copy hosting description to draft' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Add from library' })).not.toBeInTheDocument();
    expect(prefill).not.toHaveBeenCalled();
  });
  it('returns focus to the associated row after cancelling prefill without changing the draft', async () => {
    // Arrange
    mount();
    const open = await screen.findByRole('button', { name: 'Open' });
    open.focus();
    // Act
    fireEvent.click(open);
    fireEvent.click(screen.getByRole('button', { name: 'Copy hosting description to draft' }));
    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    // Assert
    expect(screen.getByRole('dialog', { name: 'Provider scope details' })).toBeVisible();
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Copy hosting description to draft' }));
    // Assert
    expect(screen.getByRole('checkbox')).not.toBeChecked();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Close dialog' }));
    // Assert
    expect(open).toHaveFocus();
    expect(prefill).not.toHaveBeenCalled();
  });

  it('never permits draft prefill from an unassociated allocation', async () => {
    // Arrange
    vi.mocked(hostingApi.listAllProviderRelationships).mockResolvedValue([allocationResponse]);
    // Act
    mount();
    await screen.findByText('No provider scope associated');
    fireEvent.click(screen.getByRole('button', { name: 'Choose provider hosting' }));
    expect(screen.getByText('Available CSP scopes (1)')).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Open' }));
    // Assert
    expect(screen.getByRole('dialog')).toHaveTextContent('Not associated');
    expect(screen.queryByRole('button', { name: 'Copy hosting description to draft' })).not.toBeInTheDocument();
  });

  it('keeps failed reads distinct from empty classifications and supports retry', async () => {
    // Arrange
    vi.mocked(hostingApi.listAllProviderRelationships).mockRejectedValueOnce(new Error('Hosting read failed'));
    // Act
    mount();
    await screen.findByText('Hosting read failed');
    // Assert
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.queryByText(/No hosting association/)).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    // Assert
    expect(await screen.findByRole('table')).toBeVisible();
  });

  it('does not classify associations until the complete read resolves', async () => {
    // Arrange
    let complete!: (rows: typeof associated[]) => void;
    vi.mocked(hostingApi.listAllProviderRelationships).mockReturnValue(new Promise(resolve => { complete = resolve; }));
    // Act
    mount();
    // Assert
    expect(screen.getAllByRole('status')[0]).toHaveTextContent('Loading workspace data');
    expect(screen.queryByText(/No hosting association/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Available CSP scopes/)).not.toBeInTheDocument();
    // Act
    complete([associated]);
    // Assert
    expect(await screen.findByRole('table')).toHaveTextContent('Harbor hosting');
  });

  it('records zero scope without inventing resources or successful review', async () => {
    // Arrange
    vi.mocked(hostingApi.listAllProviderRelationships).mockResolvedValue([{
      ...associated, assignedScopes: [], providerName: null, offeringName: null, hostingScopeName: null,
      state: 'Undetermined', reviewRequired: false,
    }]);
    // Act
    mount();
    const table = await screen.findByRole('table');
    // Assert
    expect(table).toHaveTextContent('0 recorded scopes');
    expect(table).toHaveTextContent('Provider not recorded');
    expect(table).toHaveTextContent('Offering name unavailable');
    expect(table).not.toHaveTextContent('Scope name not recorded');
    expect(table).toHaveTextContent('Not reviewed');
    expect(table).not.toHaveTextContent('Reviewed');
  });

  it.each([{ systemId: 'other-system', canRead: true }, { systemId: 'system-a', canRead: false }])(
    'does not read scope outside authorized system access', async permission => {
      // Arrange
      Object.assign(access, permission);
      // Act
      mount();
      // Assert
      expect(screen.getByText(/authorized organization workspace/)).toBeVisible();
      expect(hostingApi.listAllProviderRelationships).not.toHaveBeenCalled();
      expect(capabilityApi.listSystemCapabilities).not.toHaveBeenCalled();
    });
});
