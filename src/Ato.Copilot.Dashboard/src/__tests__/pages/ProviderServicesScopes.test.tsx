import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import ProviderServicesScopes from '../../features/systems/ProviderServicesScopes';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import * as api from '../../api/systemEnvironments';
import * as relationshipApi from '../../features/provider-relationships/api';
import { allocationResponse } from '../provider-relationships/fixtures';

const access = vi.hoisted(() => ({ canRead: true, systemId: 'system-a' }));
vi.mock('../../api/systemEnvironments', () => ({
  getSystemEnvironments: vi.fn(), getSystemProviderScopeChoices: vi.fn(), addSystemProviderScope: vi.fn(),
  previewSystemProviderScopeRemoval: vi.fn(), removeSystemProviderScope: vi.fn(),
  previewEnvironmentHostingLink: vi.fn(), commitEnvironmentHostingLink: vi.fn(),
}));
vi.mock('../../features/provider-relationships/api', () => ({ listAllProviderRelationships: vi.fn() }));
vi.mock('../../features/provider-relationships/ProviderScopeReview', () => ({
  default: ({ item }: { item: { relationshipId: string } }) => <p>Canonical review: {item.relationshipId}</p>,
}));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => ({
    workspace: { kind: 'organization', tenantId: 'org-a', mode: 'ordinary' },
    systemAccess: { systemId: access.systemId, permissions: { canRead: access.canRead } },
  }),
}));
const workspace: api.SystemEnvironmentsResponse = {
  systemId: 'system-a', version: 4,
  permissions: { canManageEnvironments: true, canCheckAccess: false, canRunAssessments: false,
    canManageMonitoring: false, canRegisterSubscriptions: false },
  attachments: [], legacyReferences: [], hostingLinks: [], providerScopes: [],
};
const scope: api.SystemProviderScope = {
  assignmentId: 'scope-a', assignmentVersion: 3, relationshipId: 'relationship-a', offeringId: 'offering-a',
  offeringName: 'Collaboration', providerName: 'Service Provider', hostingScopeRevisionId: 'release-a',
  providerId: 'provider-a', hostingScopeRevision: 12,
  hostingScopeName: 'Shared mail', state: 'Active', relationshipState: 'Undetermined', reviewRequired: true,
  assignedScopes: [{ kind: 'Service', serviceId: 'mail-a', serviceName: 'Shared mail', environment: 'Microsoft365DoD', tenantReference: null }],
  selectionVersion: 2,
  responsibilityReview: { state: 'ReviewRequired', canReview: true, canConfirm: false, reason: 'Source duties changed.' },
};
const publishedDuties: api.ProviderScopePublishedDuties = {
  state: 'Available', reason: null, capabilities: [{
    capabilityId: 'capability-a', capabilityName: 'Published mail protection', description: 'Captured mail filtering responsibilities.',
    releaseId: 'capability-release-a', releaseRevision: 7, releaseSnapshotHash: 'release-hash-a', contentHash: 'content-hash-a',
    applicabilityContextId: 'context-a', providerControlIds: ['SC-7'], sharedControlIds: ['SI-4'], customerControlIds: ['AC-2', 'AU-6'],
  }],
};
const choice: api.SystemProviderScopeChoice = {
  offeringId: 'offering-a', offeringVersion: 8, offeringName: 'Collaboration', providerName: 'Service Provider',
  providerId: 'provider-a', hostingScopeRevision: 12,
  hostingScopeRevisionId: 'release-a', hostingScopeName: 'Shared mail', permittedScopes: scope.assignedScopes,
  exclusions: [], eligibilitySource: 'Published offering',
};
const changed = vi.fn();
function attachment(id: string, name: string): api.SystemEnvironmentAttachment {
  return {
    attachmentId: id, systemId: 'system-a', version: 1, source: 'OrganizationOwned',
    registration: { registrationId: id, ownerTenantId: 'org-a', subscriptionId: id, directoryTenantId: 'directory-a',
      cloud: 'AzureCloud', displayName: name, status: 'Selected', lastVerifiedAt: '2026-09-29' },
    allocationId: null, allocationVersion: null, offeringId: scope.offeringId, offeringName: scope.offeringName,
    hostingAssignmentId: null, hostingReviewState: 'Undetermined', attachmentState: 'Attached',
    scope: { revisionId: 'scope-version-a', version: 1, reviewState: 'PendingReview', resourceIds: [],
      exclusions: [], sharedDependencyResourceIds: [], discoveredAt: '2026-09-29' },
    assessmentAccess: { state: 'NotChecked', checkedAt: null, reason: null },
    monitoringAccess: { state: 'NotChecked', checkedAt: null, reason: null },
    monitoring: { configured: false, enabled: false, health: 'NotEvaluated', evaluatedAt: null, reason: null },
    readiness: { state: 'NotChecked', checkedAt: null, reason: null },
    provenance: { source: 'ManualVerified', externalId: null, sourceRevision: null,
      reconciliationState: 'Verified', evidenceReference: null, recordedAt: '2026-09-29' }, updatedAt: '2026-09-29',
  };
}
function mount(busy = false) {
  return render(<MemoryRouter><WorkspaceNavigationProvider workspace={{ kind: 'organization', tenantId: 'org-a' }}>
    <ProviderServicesScopes systemId="system-a" systemName="Mission Alpha" busy={busy} onChanged={changed} />
  </WorkspaceNavigationProvider></MemoryRouter>);
}
beforeEach(() => {
  vi.resetAllMocks();
  access.canRead = true; access.systemId = 'system-a';
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', ''); };
  HTMLDialogElement.prototype.close = function () { this.removeAttribute('open'); };
  vi.mocked(api.getSystemEnvironments).mockResolvedValue(workspace);
  vi.mocked(api.getSystemProviderScopeChoices).mockResolvedValue({ systemId: 'system-a', version: 4, canManage: true, choices: [] });
});

describe('independent provider services and scopes', () => {
  it('offers a provider action without any subscriptions or Azure permissions', async () => {
    // Arrange
    mount();
    // Act
    await screen.findByText('No provider scopes selected.');
    // Assert
    expect(screen.getByRole('heading', { name: 'Provider services & scopes' })).toBeVisible();
    expect(screen.getByText('Select the provider services and scopes this system uses.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Add provider scope' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: /Azure|subscription/i })).not.toBeInTheDocument();
    expect(changed).not.toHaveBeenCalled();
  });
  it('does not read another system or a denied workspace', () => {
    // Arrange
    access.canRead = false;
    // Act
    mount();
    // Assert
    expect(screen.getByText(/authorized organization workspace/i)).toBeVisible();
    expect(api.getSystemEnvironments).not.toHaveBeenCalled();
  });
  it('retries a failed read without reporting an empty successful state', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockRejectedValueOnce(new Error('Provider scopes unavailable'));
    mount();
    // Act
    await screen.findByRole('alert');
    // Assert
    expect(screen.queryByText('No provider scopes selected.')).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    // Assert
    await screen.findByText('No provider scopes selected.');
  });
  it('shows standalone scopes and canonical responsibility navigation without a reconciliation warning', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [scope] });
    mount();
    // Act
    await screen.findByText('Collaboration');
    // Assert
    expect(screen.getByText('Service Provider')).toBeVisible();
    expect(screen.getByText('Shared mail')).toBeVisible();
    expect(screen.getByText('No subscriptions linked (optional).')).toBeVisible();
    expect(screen.getByRole('link', { name: 'Review responsibilities' })).toHaveAttribute(
      'href', '/workspaces/organizations/org-a/systems/system-a/inheritance/subscriptions');
    expect(screen.queryByText(/reconciliation required/i)).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'View scope' }));
    // Assert
    expect(screen.getByRole('dialog', { name: 'Provider scope details' })).toBeVisible();
    expect(within(screen.getByRole('dialog')).getByText(/release-a/)).toBeVisible();
    expect(within(screen.getByRole('dialog')).getByText('Released scope revision: 12')).toBeVisible();
    expect(changed).not.toHaveBeenCalled();
  });
  it('shows an accessible empty chooser and restores focus without saving', async () => {
    // Arrange
    mount();
    await screen.findByText('No provider scopes selected.');
    const add = screen.getByRole('button', { name: 'Add provider scope' });
    add.focus();
    // Act
    fireEvent.click(add);
    await screen.findByText('No eligible released provider scopes are available.');
    fireEvent.click(screen.getByRole('button', { name: 'Close dialog' }));
    // Assert
    expect(add).toHaveFocus();
    expect(api.addSystemProviderScope).not.toHaveBeenCalled();
    expect(changed).not.toHaveBeenCalled();
  });
  it('displays optional named subscriptions only for exact active links', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [scope],
      attachments: [attachment('linked-a', 'Mission production'), attachment('unlinked-a', 'Unrelated production')],
      hostingLinks: [
        { linkId: 'link-a', attachmentId: 'linked-a', assignmentId: scope.assignmentId,
          version: 1, state: 'Linked', source: 'Explicit', updatedAt: '2026-09-29' },
        { linkId: 'link-b', attachmentId: 'unlinked-a', assignmentId: scope.assignmentId,
          version: 2, state: 'Unlinked', source: 'Explicit', updatedAt: '2026-09-29' },
      ],
    });
    mount();
    // Act
    await screen.findByText('Collaboration');
    // Assert
    expect(screen.getByText('Mission production')).toBeVisible();
    expect(screen.queryByText('Unrelated production')).not.toBeInTheDocument();
    expect(screen.queryByText('No subscriptions linked (optional).')).not.toBeInTheDocument();
  });
  it('disables mutations while the parent is saving its independent draft', async () => {
    // Arrange
    mount(true);
    // Act
    await screen.findByText('No provider scopes selected.');
    // Assert
    expect(screen.getByRole('button', { name: 'Add provider scope' })).toBeDisabled();
  });
  it('chooses a provider and released scope, reviews applicability, and adds without subscription or allocation fields', async () => {
    // Arrange
    vi.mocked(api.getSystemProviderScopeChoices).mockResolvedValue({
      systemId: 'system-a', version: 4, canManage: true, choices: [choice],
    });
    vi.mocked(api.addSystemProviderScope).mockResolvedValue({ ...workspace, version: 5, providerScopes: [scope] });
    mount();
    // Act
    await screen.findByText('No provider scopes selected.');
    fireEvent.click(screen.getByRole('button', { name: 'Add provider scope' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Service Provider' }));
    fireEvent.click(screen.getByRole('button', { name: 'Collaboration — Shared mail' }));
    // Assert
    expect(screen.getByRole('heading', { name: 'Applicability & customer duties' })).toBeVisible();
    expect(screen.getByText('Customer-duty content and responsibility-review status are unavailable in this picker.')).toBeVisible();
    const duties = within(screen.getByRole('dialog')).getByRole('link', { name: 'Inspect recorded customer duties (opens in a new tab)' });
    expect(duties).toHaveAttribute('href', '/workspaces/organizations/org-a/systems/system-a/inheritance/subscriptions');
    expect(duties).toHaveAttribute('target', '_blank');
    expect(duties).toHaveAttribute('rel', 'noopener noreferrer');
    expect(screen.getByText(/Published offering/)).toBeVisible();
    expect(screen.getByRole('button', { name: 'Save relationship' })).toBeDisabled();
    expect(api.addSystemProviderScope).not.toHaveBeenCalled();
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: /reviewed the applicability/i }));
    fireEvent.click(screen.getByRole('button', { name: 'Save relationship' }));
    // Assert
    await waitFor(() => expect(api.addSystemProviderScope).toHaveBeenCalledWith('system-a', {
      expectedVersion: 4, offeringId: 'offering-a', expectedOfferingVersion: 8, hostingScopeRevisionId: 'release-a',
    }, expect.any(String)));
    await waitFor(() => expect(changed).toHaveBeenCalledTimes(1));
  });
  it('does not expose choices when the server denies provider management', async () => {
    // Arrange
    vi.mocked(api.getSystemProviderScopeChoices).mockResolvedValue({
      systemId: 'system-a', version: 4, canManage: false, choices: [choice],
    });
    mount();
    // Act
    await screen.findByText('No provider scopes selected.');
    fireEvent.click(screen.getByRole('button', { name: 'Add provider scope' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('permission');
    expect(screen.queryByRole('button', { name: 'Service Provider' })).not.toBeInTheDocument();
  });
  it('does not infer responsibility acceptance from a reviewed provider relationship', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [{
      ...scope, reviewRequired: false, relationshipState: 'SeparateBoundaryConsumer', responsibilityReview: undefined,
    }] });
    mount();
    // Act
    await screen.findByText('Collaboration');
    // Assert
    expect(screen.getByText('Responsibility review: unavailable here. Inspect the authoritative responsibility matrix for current decisions.')).toBeVisible();
    expect(screen.queryByText(/Responsibilities accepted|Responsibilities confirmed/)).not.toBeInTheDocument();
  });
  it('shows captured published duties and pinned source details before saving a relationship', async () => {
    // Arrange
    vi.mocked(api.getSystemProviderScopeChoices).mockResolvedValue({
      systemId: 'system-a', version: 4, canManage: true, choices: [{ ...choice, publishedDuties }],
    });
    mount();
    // Act
    await screen.findByText('No provider scopes selected.');
    fireEvent.click(screen.getByRole('button', { name: 'Add provider scope' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Service Provider' }));
    fireEvent.click(screen.getByRole('button', { name: 'Collaboration — Shared mail' }));
    // Assert
    const duties = within(screen.getByRole('region', { name: 'Published provider duties' }));
    expect(duties.getByRole('heading', { name: 'Published mail protection' })).toBeVisible();
    expect(duties.getByText('Captured mail filtering responsibilities.')).toBeVisible();
    expect(duties.getByText('Capability release revision: 7')).toBeVisible();
    expect(duties.getByText('SC-7')).toBeVisible();
    expect(duties.getByText('SI-4')).toBeVisible();
    expect(duties.getByText('AC-2, AU-6')).toBeVisible();
    expect(screen.queryByText('Customer-duty content and responsibility-review status are unavailable in this picker.')).not.toBeInTheDocument();
    expect(api.addSystemProviderScope).not.toHaveBeenCalled();
    // Act
    fireEvent.click(duties.getByText('Pinned published source'));
    // Assert
    expect(duties.getByText('Release: capability-release-a')).toBeVisible();
    expect(duties.getByText('Release snapshot hash: release-hash-a')).toBeVisible();
    expect(duties.getByText('Content hash: content-hash-a')).toBeVisible();
    expect(duties.getByText('Applicability context: context-a')).toBeVisible();
  });
  it.each([
    { state: 'Reviewed', canReview: true, canConfirm: true, action: 'Review responsibilities' },
    { state: 'ReviewRequired', canReview: false, canConfirm: false, action: 'View responsibilities' },
    { state: 'NotAdopted', canReview: true, canConfirm: false, action: 'Review responsibilities' },
  ] as const)('uses authoritative responsibility state $state and permission $canReview', async review => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [{
      ...scope, reviewRequired: review.state === 'Reviewed', responsibilityReview: { ...review, reason: 'Authoritative source reason.' },
    }] });
    mount();
    // Act
    await screen.findByText('Collaboration');
    // Assert
    expect(screen.getByText(`Responsibility review: ${review.state.replace(/([a-z])([A-Z])/g, '$1 $2')}`)).toBeVisible();
    expect(screen.getByText('Authoritative source reason.')).toBeVisible();
    expect(screen.getByRole('link', { name: review.action })).toBeVisible();
    if (!review.canReview) expect(screen.queryByRole('link', { name: 'Review responsibilities' })).not.toBeInTheDocument();
    expect(screen.getByText(review.canReview
      ? review.canConfirm ? 'Review and confirmation are available in the responsibility matrix.' : 'Review is available; confirmation is not currently permitted.'
      : 'View only. Responsibility review and confirmation are not permitted here.')).toBeVisible();
  });
  it('shows the authoritative unavailable-duty reason instead of replacing it with generic guidance', async () => {
    // Arrange
    vi.mocked(api.getSystemProviderScopeChoices).mockResolvedValue({
      systemId: 'system-a', version: 4, canManage: true, choices: [{ ...choice,
        publishedDuties: { state: 'Unavailable', reason: 'This release has no published capability duties.', capabilities: [] } }],
    });
    mount();
    // Act
    await screen.findByText('No provider scopes selected.');
    fireEvent.click(screen.getByRole('button', { name: 'Add provider scope' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Service Provider' }));
    fireEvent.click(screen.getByRole('button', { name: 'Collaboration — Shared mail' }));
    // Assert
    expect(screen.getByText('This release has no published capability duties.')).toBeVisible();
    expect(screen.queryByText('Customer-duty content and responsibility-review status are unavailable in this picker.')).not.toBeInTheDocument();
  });
  it('keeps identically named providers separate by authoritative provider identity', async () => {
    // Arrange
    vi.mocked(api.getSystemProviderScopeChoices).mockResolvedValue({
      systemId: 'system-a', version: 4, canManage: true, choices: [
        { ...choice, providerName: 'Shared provider' },
        { ...choice, providerId: 'provider-b', providerName: 'Shared provider',
          offeringId: 'offering-b', offeringName: 'Other provider offering', hostingScopeRevisionId: 'release-b', hostingScopeRevision: 25 },
      ],
    });
    mount();
    // Act
    await screen.findByText('No provider scopes selected.');
    fireEvent.click(screen.getByRole('button', { name: 'Add provider scope' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Shared provider (provider-a)' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Collaboration — Shared mail' })).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Other provider offering — Shared mail' })).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Back to providers' }));
    fireEvent.click(screen.getByRole('button', { name: 'Shared provider (provider-b)' }));
    fireEvent.click(screen.getByRole('button', { name: 'Other provider offering — Shared mail' }));
    // Assert
    expect(screen.getByText('Released scope revision: 25')).toBeVisible();
  });
  it.each([
    { providerId: '', hostingScopeRevision: 12 },
    { providerId: 'provider-a', hostingScopeRevision: 0 },
  ])('blocks choices missing canonical identity or immutable revision: %j', async invalid => {
    // Arrange
    vi.mocked(api.getSystemProviderScopeChoices).mockResolvedValue({
      systemId: 'system-a', version: 4, canManage: true, choices: [{ ...choice, ...invalid }],
    });
    mount();
    // Act
    await screen.findByText('No provider scopes selected.');
    fireEvent.click(screen.getByRole('button', { name: 'Add provider scope' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Provider identities or released scope revisions are unavailable');
    expect(screen.queryByRole('button', { name: 'Service Provider' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Refresh provider choices' })).toBeEnabled();
    expect(api.addSystemProviderScope).not.toHaveBeenCalled();
  });
  it('keeps a failed save open and retries exactly the same idempotent intent', async () => {
    // Arrange
    vi.mocked(api.getSystemProviderScopeChoices).mockResolvedValue({
      systemId: 'system-a', version: 4, canManage: true, choices: [choice],
    });
    vi.mocked(api.addSystemProviderScope).mockRejectedValueOnce(new Error('Connection interrupted'))
      .mockResolvedValueOnce({ ...workspace, version: 5, providerScopes: [scope] });
    mount();
    // Act
    await screen.findByText('No provider scopes selected.');
    fireEvent.click(screen.getByRole('button', { name: 'Add provider scope' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Service Provider' }));
    fireEvent.click(screen.getByRole('button', { name: 'Collaboration — Shared mail' }));
    fireEvent.click(screen.getByRole('checkbox', { name: /reviewed the applicability/i }));
    fireEvent.click(screen.getByRole('button', { name: 'Save relationship' }));
    await screen.findByText('Connection interrupted');
    // Assert
    expect(changed).not.toHaveBeenCalled();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save relationship' }));
    // Assert
    await waitFor(() => expect(api.addSystemProviderScope).toHaveBeenCalledTimes(2));
    expect(vi.mocked(api.addSystemProviderScope).mock.calls[0]).toEqual(vi.mocked(api.addSystemProviderScope).mock.calls[1]);
  });
  it('uses the canonical relationship review component with fresh authorized data', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [scope] });
    vi.mocked(relationshipApi.listAllProviderRelationships).mockResolvedValue([{
      ...allocationResponse, relationshipId: scope.relationshipId, assignmentId: scope.assignmentId,
      canReviewRelationship: true, state: 'Undetermined',
    }]);
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Manage relationship' }));
    fireEvent.click(screen.getByRole('button', { name: 'Review provider relationship' }));
    // Assert
    expect(await screen.findByText('Canonical review: relationship-a')).toBeVisible();
    expect(relationshipApi.listAllProviderRelationships).toHaveBeenCalledWith('system-a', expect.any(AbortSignal));
  });
  it('does not invent ordinary review permission for a covered-scope determination', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [scope] });
    vi.mocked(relationshipApi.listAllProviderRelationships).mockResolvedValue([{
      ...allocationResponse, relationshipId: scope.relationshipId, assignmentId: scope.assignmentId,
      canReviewRelationship: true, state: 'ExplicitlyCoveredByRecordedScope',
    }]);
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Manage relationship' }));
    fireEvent.click(screen.getByRole('button', { name: 'Review provider relationship' }));
    // Assert
    expect(await screen.findByText(/Covered-scope decisions require the assigned Authorizing Official/)).toBeVisible();
    expect(screen.queryByText('Canonical review: relationship-a')).not.toBeInTheDocument();
  });
  it('shows loading and blocks closing while an add is in flight', async () => {
    // Arrange
    vi.mocked(api.getSystemProviderScopeChoices).mockResolvedValue({
      systemId: 'system-a', version: 4, canManage: true, choices: [choice],
    });
    vi.mocked(api.addSystemProviderScope).mockReturnValue(new Promise(() => {}));
    mount();
    // Act
    await screen.findByText('No provider scopes selected.');
    fireEvent.click(screen.getByRole('button', { name: 'Add provider scope' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Service Provider' }));
    fireEvent.click(screen.getByRole('button', { name: 'Collaboration — Shared mail' }));
    fireEvent.click(screen.getByRole('checkbox', { name: /reviewed the applicability/i }));
    fireEvent.click(screen.getByRole('button', { name: 'Save relationship' }));
    // Assert
    expect(screen.getByRole('dialog')).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByRole('button', { name: 'Close dialog' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Saving…' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Back to scopes' })).toBeDisabled();
    expect(changed).not.toHaveBeenCalled();
  });
  it('does not report an add succeeded when the response omits the selected scope', async () => {
    // Arrange
    vi.mocked(api.getSystemProviderScopeChoices).mockResolvedValue({
      systemId: 'system-a', version: 4, canManage: true, choices: [choice],
    });
    vi.mocked(api.addSystemProviderScope).mockResolvedValue(workspace);
    mount();
    // Act
    await screen.findByText('No provider scopes selected.');
    fireEvent.click(screen.getByRole('button', { name: 'Add provider scope' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Service Provider' }));
    fireEvent.click(screen.getByRole('button', { name: 'Collaboration — Shared mail' }));
    fireEvent.click(screen.getByRole('checkbox', { name: /reviewed the applicability/i }));
    fireEvent.click(screen.getByRole('button', { name: 'Save relationship' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('did not confirm the selected provider scope');
    expect(changed).not.toHaveBeenCalled();
  });
  it.each([{}, { canCommit: true, blockers: [] }])('removes only the relationship with no active dependency blockers: %j', async restrictions => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [scope] });
    vi.mocked(api.previewSystemProviderScopeRemoval).mockResolvedValue({
      previewId: 'preview-a', systemId: 'system-a', allocationId: null, expectedVersion: 4,
      expiresAt: '2099-01-01T00:00:00Z', systems: [], requiresScopeReview: false,
      warnings: ['Existing subscriptions and review history are retained.'],
      ...restrictions,
    });
    vi.mocked(api.removeSystemProviderScope).mockResolvedValue({ ...workspace, version: 5 });
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Manage relationship' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Removal rationale' }), { target: { value: 'Service no longer used' } });
    fireEvent.click(screen.getByRole('button', { name: 'Preview removal' }));
    await screen.findByText('Existing subscriptions and review history are retained.');
    // Assert
    expect(api.previewSystemProviderScopeRemoval).toHaveBeenCalledWith('system-a', 'scope-a', {
      expectedVersion: 4, expectedAssignmentVersion: 3, expectedSelectionVersion: 2, rationale: 'Service no longer used',
    });
    expect(screen.getByRole('button', { name: 'Remove provider relationship' })).toBeDisabled();
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: /acknowledge/i }));
    fireEvent.click(screen.getByRole('button', { name: 'Remove provider relationship' }));
    // Assert
    await waitFor(() => expect(api.removeSystemProviderScope).toHaveBeenCalledWith('system-a', 'scope-a',
      { expectedVersion: 4, previewId: 'preview-a', rationale: 'Service no longer used', acknowledgeImpact: true }, expect.any(String)));
    await waitFor(() => expect(changed).toHaveBeenCalledTimes(1));
  });
  it('requires a fresh impact acknowledgment when removal rationale changes', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [scope] });
    vi.mocked(api.previewSystemProviderScopeRemoval).mockResolvedValue({
      previewId: 'preview-a', systemId: 'system-a', allocationId: null, expectedVersion: 4,
      expiresAt: '2099-01-01T00:00:00Z', systems: [], requiresScopeReview: false, warnings: [],
    });
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Manage relationship' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Removal rationale' }), { target: { value: 'Old rationale' } });
    fireEvent.click(screen.getByRole('button', { name: 'Preview removal' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: /acknowledge/i }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Removal rationale' }), { target: { value: 'New rationale' } });
    // Assert
    expect(screen.queryByRole('button', { name: 'Remove provider relationship' })).not.toBeInTheDocument();
    expect(api.removeSystemProviderScope).not.toHaveBeenCalled();
  });
  it.each([
    { canCommit: false, blockers: ['Adopted mail protection still depends on this provider scope.'] },
    { canCommit: false, blockers: [] },
    { canCommit: true, blockers: ['A current capability adoption must be resolved first.'] },
  ])('disables removal confirmation for a blocked preview: %j', async restriction => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [scope] });
    vi.mocked(api.previewSystemProviderScopeRemoval).mockResolvedValue({
      previewId: 'blocked-preview', systemId: 'system-a', allocationId: null, expectedVersion: 4,
      expiresAt: '2099-01-01T00:00:00Z', systems: [], requiresScopeReview: false, warnings: [], ...restriction,
    });
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Manage relationship' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Removal rationale' }), { target: { value: 'Retiring this service' } });
    fireEvent.click(screen.getByRole('button', { name: 'Preview removal' }));
    // Assert
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(restriction.blockers[0] ?? 'This change cannot be committed. Resolve the preview restrictions and prepare a fresh preview.');
    expect(screen.getByRole('checkbox', { name: /acknowledge this impact/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Remove provider relationship' })).toBeDisabled();
    const dependencies = screen.getByRole('link', { name: 'Review capability dependencies (opens in a new tab)' });
    expect(dependencies).toHaveAttribute('href', '/workspaces/organizations/org-a/systems/system-a/security-capabilities');
    expect(dependencies).toHaveAttribute('target', '_blank');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Remove provider relationship' }));
    // Assert
    expect(api.removeSystemProviderScope).not.toHaveBeenCalled();
    expect(changed).not.toHaveBeenCalled();
  });
  it('does not claim removal if the response still contains the active relationship', async () => {
    // Arrange
    const current = { ...workspace, providerScopes: [scope] };
    vi.mocked(api.getSystemEnvironments).mockResolvedValue(current);
    vi.mocked(api.previewSystemProviderScopeRemoval).mockResolvedValue({
      previewId: 'preview-a', systemId: 'system-a', allocationId: null, expectedVersion: 4,
      expiresAt: '2099-01-01T00:00:00Z', systems: [], requiresScopeReview: false, warnings: [],
    });
    vi.mocked(api.removeSystemProviderScope).mockResolvedValue(current);
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Manage relationship' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Removal rationale' }), { target: { value: 'No longer used' } });
    fireEvent.click(screen.getByRole('button', { name: 'Preview removal' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: /acknowledge/i }));
    fireEvent.click(screen.getByRole('button', { name: 'Remove provider relationship' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('did not confirm removal');
    expect(changed).not.toHaveBeenCalled();
  });
  it('rejects an expired removal preview before calling the server', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [scope] });
    vi.mocked(api.previewSystemProviderScopeRemoval).mockResolvedValue({
      previewId: 'preview-a', systemId: 'system-a', allocationId: null, expectedVersion: 4,
      expiresAt: '2000-01-01T00:00:00Z', systems: [], requiresScopeReview: false, warnings: [],
    });
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Manage relationship' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Removal rationale' }), { target: { value: 'No longer used' } });
    fireEvent.click(screen.getByRole('button', { name: 'Preview removal' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: /acknowledge/i }));
    fireEvent.click(screen.getByRole('button', { name: 'Remove provider relationship' }));
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('expired');
    expect(api.removeSystemProviderScope).not.toHaveBeenCalled();
    expect(changed).not.toHaveBeenCalled();
  });
  it.each(['Link', 'Unlink'] as const)('previews and acknowledges %s by exact subscription and scope versions', async action => {
    // Arrange
    const link: api.EnvironmentHostingLink = { linkId: 'link-a', attachmentId: 'attached-a',
      assignmentId: scope.assignmentId, version: 1, state: 'Linked', source: 'Explicit', updatedAt: '2026-09-29' };
    const current = { ...workspace, providerScopes: [scope], attachments: [attachment('attached-a', 'Mission production')],
      hostingLinks: action === 'Unlink' ? [link] : [] };
    vi.mocked(api.getSystemEnvironments).mockResolvedValue(current);
    vi.mocked(api.previewEnvironmentHostingLink).mockResolvedValue({
      previewId: 'link-preview-a', systemId: 'system-a', allocationId: null, expectedVersion: 4,
      expiresAt: '2099-01-01T00:00:00Z', systems: [], requiresScopeReview: false, warnings: ['Only this optional link changes.'],
    });
    vi.mocked(api.commitEnvironmentHostingLink).mockResolvedValue({ ...current, version: 5,
      hostingLinks: [{ ...link, state: action === 'Link' ? 'Linked' : 'Unlinked' }] });
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Manage relationship' }));
    const dialog = within(screen.getByRole('dialog'));
    if (action === 'Link') {
      fireEvent.change(dialog.getByRole('combobox', { name: 'Subscription to link' }), { target: { value: 'attached-a' } });
    } else {
      fireEvent.click(dialog.getByRole('button', { name: 'Remove link to Mission production' }));
    }
    fireEvent.change(dialog.getByRole('textbox', { name: 'Link change rationale' }), { target: { value: 'Update service applicability' } });
    fireEvent.click(dialog.getByRole('button', { name: 'Preview link change' }));
    await screen.findByText('Only this optional link changes.');
    // Assert
    expect(api.previewEnvironmentHostingLink).toHaveBeenCalledWith('system-a', {
      expectedVersion: 4, attachmentId: 'attached-a', expectedAttachmentVersion: 1,
      assignmentId: 'scope-a', expectedAssignmentVersion: 3, action, rationale: 'Update service applicability',
    });
    expect(dialog.getByRole('button', { name: 'Confirm link change' })).toBeDisabled();
    // Act
    fireEvent.click(dialog.getByRole('checkbox', { name: /acknowledge the link impact/i }));
    fireEvent.click(dialog.getByRole('button', { name: 'Confirm link change' }));
    // Assert
    await waitFor(() => expect(api.commitEnvironmentHostingLink).toHaveBeenCalledWith('system-a', {
      expectedVersion: 4, previewId: 'link-preview-a', rationale: 'Update service applicability', acknowledgeImpact: true,
    }, expect.any(String)));
    await waitFor(() => expect(changed).toHaveBeenCalledTimes(1));
    expect(api.removeSystemProviderScope).not.toHaveBeenCalled();
  });
  it('refreshes optional links without remounting or mutating the parent document', async () => {
    // Arrange
    const content = (refreshVersion: number) => <MemoryRouter><WorkspaceNavigationProvider workspace={{ kind: 'organization', tenantId: 'org-a' }}>
      <textarea aria-label="Parent draft" defaultValue="Unsaved environment details" />
      <ProviderServicesScopes systemId="system-a" refreshVersion={refreshVersion} onChanged={changed} />
    </WorkspaceNavigationProvider></MemoryRouter>;
    const view = render(content(0));
    await screen.findByText('No provider scopes selected.');
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [scope] });
    // Act
    view.rerender(content(1));
    // Assert
    expect(await screen.findByText('Collaboration')).toBeVisible();
    expect(api.getSystemEnvironments).toHaveBeenCalledTimes(2);
    expect(screen.getByRole('textbox', { name: 'Parent draft' })).toHaveValue('Unsaved environment details');
    expect(screen.getByRole('region', { name: 'Provider services & scopes' })).toHaveAttribute('id', 'provider-services-scopes');
    expect(screen.getByRole('region', { name: 'Provider services & scopes' })).toHaveAttribute('tabindex', '-1');
    expect(changed).not.toHaveBeenCalled();
  });
  it('consolidates only server-reported provider mapping warnings without inventing missing-link warnings', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [scope], legacyReferences: [
      { referenceId: 'invalid-scope', kind: 'ProviderScopeLink', displayName: 'Legacy mail scope',
        reconciliationState: 'ReconciliationRequired', reason: 'Recorded provider scope release is unavailable. Review the named relationship.' },
      { referenceId: 'invalid-scope', kind: 'ProviderScopeLink', displayName: 'Legacy mail scope',
        reconciliationState: 'ReconciliationRequired', reason: 'Recorded provider scope release is unavailable. Review the named relationship.' },
      { referenceId: 'legacy-sub', kind: 'AzureProfile', displayName: 'Legacy subscription',
        reconciliationState: 'ReconciliationRequired', reason: 'Needs resource review.' },
    ] });
    mount();
    // Act
    const warning = await screen.findByRole('alert');
    // Assert
    expect(warning).toHaveTextContent('Legacy mail scope');
    expect(screen.getAllByRole('heading', { name: 'An existing hosting relationship needs review.' })).toHaveLength(1);
    expect(screen.getAllByRole('button', { name: 'Review relationship' })).toHaveLength(1);
    expect(warning).toHaveTextContent('Recorded provider scope release is unavailable');
    expect(screen.queryByText('Legacy subscription')).not.toBeInTheDocument();
    expect(screen.getByText('No subscriptions linked (optional).')).toBeVisible();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Review relationship' }));
    // Assert
    const unresolved = within(screen.getByRole('dialog', { name: 'Review relationship' }));
    expect(unresolved.getByText('Recorded reference: invalid-scope')).toBeVisible();
    expect(unresolved.getByText(/cannot be matched to a current provider scope/)).toBeVisible();
    expect(unresolved.queryByRole('button', { name: /Save|Remove|Link subscription/ })).not.toBeInTheDocument();
  });
  it.each(['scope-a', 'relationship-a'])('routes warning reference %s to its exact provider relationship', async referenceId => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [scope], legacyReferences: [
      { referenceId, kind: 'HostingAssignment', displayName: 'Mail relationship',
        reconciliationState: 'ReconciliationRequired', reason: 'Release mapping requires an explicit review.' },
    ] });
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Review relationship' }));
    // Assert
    const dialog = within(screen.getByRole('dialog', { name: 'Manage provider relationship' }));
    expect(dialog.getByText('Release mapping requires an explicit review.')).toBeVisible();
    expect(dialog.getByText('Service Provider · Collaboration')).toBeVisible();
    expect(dialog.getByRole('button', { name: 'Review provider relationship' })).toBeVisible();
    expect(api.addSystemProviderScope).not.toHaveBeenCalled();
  });
});
