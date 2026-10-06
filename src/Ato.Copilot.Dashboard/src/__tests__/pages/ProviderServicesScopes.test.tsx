import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import ProviderServicesScopes from '../../features/systems/ProviderServicesScopes';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import * as api from '../../api/systemEnvironments';
import * as relationshipApi from '../../features/provider-relationships/api';
import { allocationResponse, capability } from '../provider-relationships/fixtures';

const access = vi.hoisted(() => ({ canRead: true, systemId: 'system-a' }));
vi.mock('../../api/systemEnvironments', () => ({
  getSystemEnvironments: vi.fn(), getSystemProviderScopeChoices: vi.fn(), addSystemProviderScope: vi.fn(),
  previewSystemProviderScopeRemoval: vi.fn(), removeSystemProviderScope: vi.fn(),
  previewEnvironmentHostingLink: vi.fn(), commitEnvironmentHostingLink: vi.fn(),
}));
vi.mock('../../features/provider-relationships/api', async importOriginal => ({
  ...await importOriginal<typeof import('../../features/provider-relationships/api')>(),
  listAllProviderRelationships: vi.fn(), listApplicableProviderCapabilities: vi.fn(),
}));
vi.mock('../../features/provider-relationships/ProviderScopeReview', () => ({
  default: ({ item, onCancel }: { item: { relationshipId: string }; onCancel: () => void }) =>
    <><p>Canonical review: {item.relationshipId}</p><button type="button" onClick={onCancel}>Cancel canonical review</button></>,
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
function content(busy = false, refreshVersion = 0) {
  return <MemoryRouter><WorkspaceNavigationProvider workspace={{ kind: 'organization', tenantId: 'org-a' }}>
    <ProviderServicesScopes systemId="system-a" systemName="Mission Alpha" busy={busy} refreshVersion={refreshVersion} onChanged={changed} />
  </WorkspaceNavigationProvider></MemoryRouter>;
}
function mount(busy = false) {
  return render(content(busy));
}
async function openReview() {
  const action = await screen.findByRole('button', { name: 'Review Collaboration' });
  await act(async () => {
    fireEvent.click(action);
  });
  const panel = screen.getByRole('dialog', { name: 'Review provider offering' });
  for (const summary of panel.querySelectorAll('summary')) {
    fireEvent.click(summary);
  }
}
beforeEach(() => {
  vi.resetAllMocks();
  access.canRead = true; access.systemId = 'system-a';
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', ''); };
  HTMLDialogElement.prototype.close = function () { this.removeAttribute('open'); };
  vi.mocked(api.getSystemEnvironments).mockResolvedValue(workspace);
  vi.mocked(relationshipApi.listAllProviderRelationships).mockResolvedValue([]);
  vi.mocked(relationshipApi.listApplicableProviderCapabilities).mockResolvedValue({ items: [], page: 1, pageSize: 25, total: 0 });
  vi.mocked(api.getSystemProviderScopeChoices).mockResolvedValue({ systemId: 'system-a', version: 4, canManage: true, choices: [] });
});

describe('independent provider services and scopes', () => {
  it('keeps an unavailable capability inspectable when its captured name is blank', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [{
      ...scope, publishedDuties: { state: 'Unavailable', reason: 'Invalid published duties.', capabilities: [], totalCapabilities: 1,
        unavailableCapabilities: [{ capabilityId: 'missing', capabilityName: '   ', releaseId: 'missing-release',
          releaseRevision: 2, reason: 'Published duty content is missing or invalid.' }] },
    }] });
    mount();
    // Act
    await openReview();
    // Assert
    expect(screen.getByText('Capability name unavailable', { selector: 'summary' })).toBeVisible();
    expect(screen.getByRole('dialog')).toHaveTextContent('Published duty content is missing or invalid.');
  });
  it('distinguishes captured scope-projection flags from canonical relationship review', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [{
      ...scope, relationshipState: 'SeparateBoundaryConsumer', reviewRequired: true,
    }] });
    vi.mocked(relationshipApi.listAllProviderRelationships).mockResolvedValue([{
      ...allocationResponse, assignmentId: scope.assignmentId, relationshipId: scope.relationshipId,
      state: 'SeparateBoundaryConsumer', reviewRequired: false, reviewedAt: '2026-10-06',
    }]);
    mount();
    // Act
    await openReview();
    // Assert
    const status = screen.getByLabelText('Review status');
    expect(status).toHaveTextContent('System relationshipReview recorded');
    expect(screen.getByText('Scope projection review flag: Review required')).toBeVisible();
    expect(screen.getByRole('dialog')).not.toHaveTextContent('Relationship: Separate Boundary Consumer · Review required');
  });
  it('retries failed review reads explicitly and inspects recorded status without a mutation', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [{
      ...scope, responsibilityReview: { state: 'Reviewed', canReview: true, canConfirm: false, reason: 'Current canonical review.' },
    }] });
    vi.mocked(relationshipApi.listAllProviderRelationships).mockRejectedValueOnce(new Error('Current source unavailable'))
      .mockResolvedValue([{ ...allocationResponse, assignmentId: scope.assignmentId, relationshipId: scope.relationshipId, reviewRequired: false }]);
    mount();
    const opener = await screen.findByRole('button', { name: 'Review Collaboration' });
    // Act
    fireEvent.click(opener);
    fireEvent.click(await screen.findByRole('button', { name: 'Retry review records' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect recorded review' }));
    fireEvent.click(screen.getByRole('button', { name: 'Inspect source and prerequisites' }));
    // Assert
    expect(screen.getByText('Source details and prerequisites').closest('details')).toHaveAttribute('open');
    expect(relationshipApi.listAllProviderRelationships).toHaveBeenCalledTimes(2);
    expect(api.removeSystemProviderScope).not.toHaveBeenCalled();
  });
  it('retains the mounted canonical editor when returning to the focused review', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [scope] });
    vi.mocked(relationshipApi.listAllProviderRelationships).mockResolvedValue([{
      ...allocationResponse, assignmentId: scope.assignmentId, relationshipId: scope.relationshipId,
      canReviewRelationship: true, reviewRequired: true,
    }]);
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Review Collaboration' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Review relationship' }));
    const editor = screen.getByText('Canonical review: relationship-a');
    fireEvent.click(screen.getByRole('button', { name: 'Cancel canonical review' }));
    // Assert
    expect(editor).not.toBeVisible();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Review relationship' }));
    // Assert
    expect(screen.getByText('Canonical review: relationship-a')).toBe(editor);
    expect(editor).toBeVisible();
  });
  it.each([0, 1, 51])('discloses the actual %i published capabilities with bounded pages and no inferred review outcome', async count => {
    // Arrange
    const entries = Array.from({ length: count }, (_, index) => ({
      ...publishedDuties.capabilities[0]!, capabilityId: `cap-${index}`, capabilityName: `Recorded capability ${index}`,
      releaseId: `release-${index}`,
    }));
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [{
      ...scope, reviewRequired: false, publishedDuties: { ...publishedDuties, capabilities: entries, totalCapabilities: count },
      responsibilityReview: { state: 'Reviewed', canReview: true, canConfirm: false, reason: 'Recorded canonical review.' },
    }] });
    vi.mocked(relationshipApi.listAllProviderRelationships).mockResolvedValue([{
      ...allocationResponse, assignmentId: scope.assignmentId, relationshipId: scope.relationshipId,
      reviewRequired: false, reviewedAt: '2026-10-06',
    }]);
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Review Collaboration' }));
    const panel = screen.getByRole('dialog');
    await within(panel).findByRole('button', { name: 'Inspect recorded review' });
    fireEvent.click(within(panel).getByText(`What's included · ${count} ${count === 1 ? 'capability' : 'capabilities'}`));
    // Assert
    expect(panel).toHaveTextContent('Review recorded');
    expect(panel).not.toHaveTextContent('Setup needed');
    expect(panel.querySelectorAll('section[aria-label="Published provider duties"] > details')).toHaveLength(Math.min(10, count));
    if (count === 0) expect(within(panel).getByText('No capabilities are bound to this published scope.')).toBeVisible();
    if (count > 0) {
      fireEvent.click(within(panel).getByText('Recorded capability 0', { selector: 'summary' }));
      expect(within(panel).getAllByText('Captured mail filtering responsibilities.')[0]).toBeVisible();
    }
    if (count > 10) {
      fireEvent.click(within(panel).getByRole('button', { name: 'Next capabilities' }));
      expect(within(panel).getByText('Recorded capability 10', { selector: 'summary' })).toBeVisible();
      expect(panel).not.toHaveTextContent('Recorded capability 0');
      fireEvent.click(within(panel).getByRole('button', { name: 'Previous capabilities' }));
      expect(within(panel).getByText('Recorded capability 0', { selector: 'summary' })).toBeVisible();
    }
  });
  it('groups shared blockers once while retaining affected names, per-capability detail and raw diagnostics', async () => {
    // Arrange
    const capabilities = [0, 1].map(index => ({ ...capability, assignmentId: scope.assignmentId,
      capabilityId: `cap-${index}`, capabilityName: `Actual capability ${index}`, canProposeAdoption: false,
      reasonCodes: ['PROVIDER_DECISION_REQUIRED'], applicabilityState: 'ReviewRequired',
    }));
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [scope] });
    vi.mocked(relationshipApi.listAllProviderRelationships).mockResolvedValue([{
      ...allocationResponse, assignmentId: scope.assignmentId, relationshipId: scope.relationshipId, reviewRequired: false,
    }]);
    vi.mocked(relationshipApi.listApplicableProviderCapabilities).mockResolvedValue({ items: capabilities, page: 1, pageSize: 25, total: 2 });
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Review Collaboration' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Resolve adoption prerequisites' }));
    // Assert
    const group = screen.getByRole('region', { name: 'Shared source prerequisites' });
    expect(within(group).getAllByText('A recorded provider decision is required')).toHaveLength(1);
    expect(group).toHaveTextContent('Actual capability 0, Actual capability 1');
    expect(within(group).queryByText('PROVIDER_DECISION_REQUIRED')).not.toBeInTheDocument();
    fireEvent.click(screen.getByText('Raw applicability diagnostics'));
    expect(screen.getAllByText('PROVIDER_DECISION_REQUIRED')).toHaveLength(2);
  });
  it('includes missing duty entries in the source count instead of implying they are resolved', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [{
      ...scope, publishedDuties: { ...publishedDuties, state: 'Unavailable', totalCapabilities: 2,
        unavailableCapabilities: [{ capabilityId: 'missing', capabilityName: 'Missing published capability',
          releaseId: 'missing-release', releaseRevision: 2, reason: 'Captured duties unavailable.' }] },
    }] });
    mount();
    // Act
    const opener = await screen.findByRole('button', { name: 'Review Collaboration' });
    await act(async () => { fireEvent.click(opener); });
    fireEvent.click(screen.getByText("What's included · 2 capabilities"));
    fireEvent.click(screen.getByText('Missing published capability', { selector: 'summary' }));
    // Assert
    expect(screen.getByText('1 capability has missing published duty content.')).toBeVisible();
    expect(screen.getByText('Captured duties unavailable.')).toBeVisible();
    expect(screen.getByText('Published capability release: 2')).toBeVisible();
  });
  it('does not offer a writable relationship action to a read-only identity', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace,
      permissions: { ...workspace.permissions, canManageEnvironments: false }, providerScopes: [scope] });
    vi.mocked(relationshipApi.listAllProviderRelationships).mockResolvedValue([{
      ...allocationResponse, assignmentId: scope.assignmentId, relationshipId: scope.relationshipId,
      canAssociate: false, canReviewRelationship: false, reviewRequired: true,
    }]);
    mount();
    // Act
    await openReview();
    // Assert
    expect(screen.getByRole('button', { name: 'Inspect unresolved review' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Review provider relationship' })).toBeDisabled();
    expect(screen.queryByRole('textbox', { name: 'Removal rationale' })).not.toBeInTheDocument();
    expect(screen.getByRole('dialog')).toHaveTextContent('server does not permit');
    expect(screen.getByRole('dialog').querySelectorAll('a[target="_blank"]')).toHaveLength(0);
  });
  it('reads later applicability pages before choosing an action', async () => {
    // Arrange
    const rows = Array.from({ length: 26 }, (_, index) => ({ ...capability, assignmentId: scope.assignmentId,
      capabilityId: `cap-${index}`, relationshipReviewRequired: false,
      reasonCodes: index === 25 ? ['HOSTING_CONTEXT_STALE'] : [], canProposeAdoption: index !== 25 }));
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [scope] });
    vi.mocked(relationshipApi.listAllProviderRelationships).mockResolvedValue([{
      ...allocationResponse, assignmentId: scope.assignmentId, relationshipId: scope.relationshipId, reviewRequired: false,
    }]);
    vi.mocked(relationshipApi.listApplicableProviderCapabilities).mockImplementation(async (_, query) => ({
      items: rows.slice((query.page - 1) * 25, query.page * 25), page: query.page, pageSize: 25, total: 26,
    }));
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Review Collaboration' }));
    // Assert
    expect(await screen.findByRole('button', { name: 'Review changed source' })).toBeEnabled();
    expect(relationshipApi.listApplicableProviderCapabilities).toHaveBeenCalledTimes(2);
  });
  it('initial offering review is focused with capabilities, source and maintenance collapsed', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [{ ...scope, publishedDuties }] });
    vi.mocked(relationshipApi.listAllProviderRelationships).mockResolvedValue([{
      ...allocationResponse, assignmentId: scope.assignmentId, relationshipId: scope.relationshipId,
      reviewRequired: true, canReviewRelationship: true,
    }]);
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Review Collaboration' }));
    const panel = screen.getByRole('dialog', { name: 'Review provider offering' });
    // Assert
    expect(await within(panel).findByRole('button', { name: 'Review relationship' })).toBeEnabled();
    expect(within(panel).getByRole('heading', { name: 'Collaboration' })).toBeVisible();
    expect(panel).toHaveTextContent('Service Provider · Mission Alpha');
    expect(panel.querySelector('details')?.open).toBe(false);
    expect(within(panel).getByRole('heading', { name: 'Published mail protection', hidden: true })).not.toBeVisible();
    expect(within(panel).queryByRole('button', { name: 'Confirm responsibilities' })).not.toBeInTheDocument();
    expect(within(panel).getByRole('textbox', { hidden: true })).not.toBeVisible();
    expect(panel.querySelectorAll('a[target="_blank"]')).toHaveLength(0);
  });
  it('retains review input and blocks mutations while the register refresh fails', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValueOnce({ ...workspace, providerScopes: [scope] })
      .mockRejectedValueOnce(new Error('Current register unavailable'));
    const view = mount();
    await openReview();
    fireEvent.change(screen.getByRole('textbox', { name: 'Removal rationale' }), { target: { value: 'Preserve concurrent review input' } });
    // Act
    view.rerender(content(false, 1));
    await within(screen.getByRole('dialog')).findByText('Current register unavailable');
    // Assert
    expect(screen.getByRole('textbox', { name: 'Removal rationale' })).toHaveValue('Preserve concurrent review input');
    expect(screen.getByRole('button', { name: 'Preview removal' })).toBeDisabled();
    expect(api.removeSystemProviderScope).not.toHaveBeenCalled();
  });
  it('reads applicability for the exact assignment without deriving adoption permission from publication', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [scope] });
    vi.mocked(relationshipApi.listApplicableProviderCapabilities).mockResolvedValue({
      items: [{ ...capability, assignmentId: scope.assignmentId, canProposeAdoption: false,
        applicabilityState: 'PendingReview', reasonCodes: ['Source revision requires review'] }],
      page: 1, pageSize: 25, total: 1,
    });
    mount();
    // Act
    await openReview();
    // Assert
    expect(relationshipApi.listApplicableProviderCapabilities).toHaveBeenCalledWith('system-a', {
      page: 1, assignmentId: scope.assignmentId, offeringId: scope.offeringId,
    }, expect.any(AbortSignal));
    expect(screen.getByRole('dialog')).toHaveTextContent('Applicability: PendingReview');
    expect(screen.getByRole('dialog')).toHaveTextContent('Source revision requires review');
    expect(screen.getByRole('dialog')).toHaveTextContent('does not currently permit proposing adoption');
  });
  it('shows association prerequisites only from the current canonical record', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [{ ...scope, relationshipId: null }] });
    vi.mocked(relationshipApi.listAllProviderRelationships).mockResolvedValue([{
      ...allocationResponse, assignmentId: scope.assignmentId, canAssociate: true, relationshipId: null,
    }]);
    mount();
    // Act
    await openReview();
    // Assert
    const association = screen.getByRole('link', { name: 'Associate provider relationship' });
    expect(association).toHaveAttribute('href', '/workspaces/organizations/org-a/systems/system-a/profile/EnvironmentAndDeployment/hosting?offeringId=offering-a&assignmentId=scope-a&hostingScopeRevisionId=release-a');
    expect(association).not.toHaveAttribute('target');
    expect(screen.getByRole('button', { name: 'Review provider relationship' })).toBeDisabled();
  });
  it('does not report failed applicability reads as an empty successful list', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [scope] });
    vi.mocked(relationshipApi.listApplicableProviderCapabilities).mockRejectedValue(new Error('Applicability source denied'));
    mount();
    // Act
    await openReview();
    // Assert
    expect(within(screen.getByRole('dialog')).getByRole('alert')).toHaveTextContent('Applicability source denied');
    expect(screen.queryByText(/No applicable capability entries returned/)).not.toBeInTheDocument();
  });
  it('keeps relationship-removal input when closing is canceled', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [scope] });
    mount();
    await openReview();
    // Act
    fireEvent.change(screen.getByRole('textbox', { name: 'Removal rationale' }), { target: { value: 'Retained draft rationale' } });
    fireEvent.click(screen.getByRole('button', { name: 'Close dialog' }));
    fireEvent.click(screen.getByRole('button', { name: 'Keep editing' }));
    // Assert
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    expect(screen.getByRole('textbox', { name: 'Removal rationale' })).toHaveValue('Retained draft rationale');
    expect(api.removeSystemProviderScope).not.toHaveBeenCalled();
  });
  it('consolidates the register into one Review and separates pinned source from system acceptance', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [{
      ...scope, publishedDuties, responsibilityReview: {
        state: 'NotAdopted', canReview: true, canConfirm: false, reason: 'No current capability adoption is bound to this relationship.',
      },
    }] });
    mount();
    // Act
    const table = await screen.findByRole('table', { name: 'Provider services & scopes' });
    // Assert
    expect(within(table).getAllByRole('columnheader').map(cell => cell.textContent)).toEqual([
      'Offering', 'System relationship', 'Responsibility review', 'Action',
    ]);
    expect(within(table).getAllByRole('button')).toHaveLength(1);
    expect(within(table).queryByRole('link')).not.toBeInTheDocument();
    expect(table).not.toHaveTextContent('release-a');
    // Act
    await act(async () => { fireEvent.click(within(table).getByRole('button', { name: 'Review Collaboration' })); });
    for (const summary of screen.getByRole('dialog').querySelectorAll('summary')) fireEvent.click(summary);
    // Assert
    const panel = screen.getByRole('dialog', { name: 'Review provider offering' });
    expect(panel).toHaveTextContent('Selected scope release: 12');
    expect(panel).toHaveTextContent('No current capability adoption is bound to this relationship.');
    expect(panel).toHaveTextContent('not system adoption or accepted responsibilities');
    expect(within(panel).getByRole('link', { name: 'Review responsibilities' })).not.toHaveAttribute('target');
    expect(within(panel).queryByRole('button', { name: 'Confirm responsibilities' })).not.toBeInTheDocument();
  });
  it('uses a compact provider register and retains revision/provenance in the existing inspector', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...workspace, providerScopes: [scope] });
    mount();
    // Act
    const table = await screen.findByRole('table', { name: 'Provider services & scopes' });
    // Assert
    expect(within(table).getAllByRole('columnheader').map(cell => cell.textContent)).toEqual([
      'Offering', 'System relationship', 'Responsibility review', 'Action',
    ]);
    expect(table).toHaveTextContent('Undetermined');
    expect(table).toHaveTextContent('Review Required');
    expect(table).not.toHaveTextContent('Scope release revision');
    // Act
    await act(async () => { fireEvent.click(within(table).getByRole('button', { name: 'Review Collaboration' })); });
    fireEvent.click(screen.getByText('Source details and prerequisites'));
    fireEvent.click(screen.getByText('Source and technical metadata'));
    // Assert
    const dialog = screen.getByRole('dialog', { name: 'Review provider offering' });
    expect(dialog).toHaveTextContent('Released scope revision: 12');
    expect(dialog).toHaveTextContent('Assignment revision: 3 · Selection revision: 2');
    expect(dialog).toHaveTextContent('release-a');
    expect(api.addSystemProviderScope).not.toHaveBeenCalled();
  });
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
    expect(screen.getByText('Service Provider · Selected scope release 12')).toBeVisible();
    expect(screen.queryByText('No subscriptions linked (optional).')).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Review responsibilities/ })).not.toBeInTheDocument();
    expect(screen.queryByText(/reconciliation required/i)).not.toBeInTheDocument();
    // Act
    await openReview();
    // Assert
    expect(screen.getByRole('dialog', { name: 'Review provider offering' })).toBeVisible();
    expect(within(screen.getByRole('dialog')).getByText(/release-a/)).toBeVisible();
    expect(within(screen.getByRole('dialog')).getByText('Released scope revision: 12')).toBeVisible();
    expect(screen.getByRole('link', { name: 'Review responsibilities' })).toHaveAttribute(
      'href', '/workspaces/organizations/org-a/systems/system-a/inheritance/subscriptions?offeringId=offering-a&assignmentId=scope-a&hostingScopeRevisionId=release-a');
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
    await openReview();
    // Assert
    expect(screen.getByText('Mission production')).toBeVisible();
    expect(screen.getByRole('combobox', { name: 'Subscription to link' })).toHaveTextContent('Unrelated production');
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
    expect(within(screen.getByRole('table')).getByText('Unavailable')).toBeVisible();
    await openReview();
    expect(screen.getByText('Responsibility review is unavailable here.')).toBeVisible();
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
    expect(within(screen.getByRole('table')).getByText(review.state.replace(/([a-z])([A-Z])/g, '$1 $2'))).toBeVisible();
    // Act
    await openReview();
    // Assert
    expect(within(screen.getByRole('dialog')).getByText('Authoritative source reason.')).toBeVisible();
    expect(screen.getByRole('link', { name: review.action })).toBeVisible();
    if (!review.canReview) expect(screen.queryByRole('link', { name: 'Review responsibilities (opens in a new tab)' })).not.toBeInTheDocument();
    expect(within(screen.getByRole('dialog')).getByText(review.canReview
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
    await openReview();
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
    await openReview();
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
    await openReview();
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
    await openReview();
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
    await openReview();
    fireEvent.change(screen.getByRole('textbox', { name: 'Removal rationale' }), { target: { value: 'Retiring this service' } });
    fireEvent.click(screen.getByRole('button', { name: 'Preview removal' }));
    // Assert
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(restriction.blockers[0] ?? 'This change cannot be committed. Resolve the preview restrictions and prepare a fresh preview.');
    expect(screen.getByRole('checkbox', { name: /acknowledge this impact/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Remove provider relationship' })).toBeDisabled();
    const dependencies = screen.getByRole('link', { name: 'Review capability dependencies' });
    expect(dependencies).toHaveAttribute('href', '/workspaces/organizations/org-a/systems/system-a/security-capabilities');
    expect(dependencies).not.toHaveAttribute('target');
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
    await openReview();
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
    await openReview();
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
    await openReview();
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
    expect(screen.queryByText('No subscriptions linked (optional).')).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Review relationship' }));
    // Assert
    const unresolved = within(screen.getByRole('dialog', { name: 'Review relationship' }));
    fireEvent.click(unresolved.getByText('Recorded relationship warning'));
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
    const action = await screen.findByRole('button', { name: 'Review relationship' });
    await act(async () => { fireEvent.click(action); });
    // Assert
    const dialog = within(screen.getByRole('dialog', { name: 'Review provider offering' }));
    for (const summary of screen.getByRole('dialog').querySelectorAll('summary')) fireEvent.click(summary);
    expect(dialog.getByText('Release mapping requires an explicit review.')).toBeVisible();
    expect(dialog.getByRole('heading', { name: 'Collaboration' })).toBeVisible();
    expect(dialog.getByRole('button', { name: 'Review provider relationship' })).toBeVisible();
    expect(api.addSystemProviderScope).not.toHaveBeenCalled();
  });
});
