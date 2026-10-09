import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import '../helpers/dialog';
import ConnectedSystemEnvironments from '../../features/systems/ConnectedSystemEnvironments';
import * as api from '../../api/systemEnvironments';
vi.mock('../../features/systems/ProviderServicesScopes', () => ({ default: () => <section aria-label="Provider services & scopes" /> }));
vi.mock('../../api/systemEnvironments', () => ({
  getSystemEnvironments: vi.fn(), getEnvironmentChoices: vi.fn(), discoverEnvironmentResources: vi.fn(),
  applySystemEnvironment: vi.fn(), applySystemEnvironments: vi.fn(), previewEnvironmentScope: vi.fn(), commitEnvironmentScope: vi.fn(),
  checkEnvironmentAccess: vi.fn(), previewEnvironmentDetach: vi.fn(), detachSystemEnvironment: vi.fn(),
}));
const permissions = { canManageEnvironments: true, canCheckAccess: true, canRunAssessments: false, canManageMonitoring: false, canRegisterSubscriptions: false };
const registration: api.EnvironmentRegistration = { registrationId: 'registration-a', ownerTenantId: 'org-a', subscriptionId: 'subscription-a',
  directoryTenantId: 'azure-directory', cloud: 'AzureUSGovernment', displayName: 'Mission production', status: 'Selected', lastVerifiedAt: '2026-09-29' };
const provenance: api.EnvironmentProvenance = { source: 'ManualVerified', externalId: null, sourceRevision: null, reconciliationState: 'Verified', evidenceReference: null, recordedAt: '2026-09-29' };
const choice: api.EnvironmentChoice = { choiceId: 'choice-a', source: 'OrganizationOwned', registration, allocationId: null,
  allocationVersion: null, offeringId: null, offeringName: null, hostingScopeRevisionId: null, allocationState: null,
  startsAt: null, expiresAt: null, provenance, eligible: true, ineligibleReason: null };
const empty: api.SystemEnvironmentsResponse = { systemId: 'system-a', version: 0, permissions, attachments: [], legacyReferences: [] };
const attached: api.SystemEnvironmentAttachment = {
  attachmentId: 'attachment-a', systemId: 'system-a', version: 2, source: 'OrganizationOwned', registration,
  allocationId: null, allocationVersion: null, offeringId: null, offeringName: null,
  hostingAssignmentId: null, hostingReviewState: 'Undetermined', attachmentState: 'Attached',
  scope: { revisionId: 'scope-a', version: 1, reviewState: 'PendingReview',
    resourceIds: ['/subscriptions/subscription-a/resourceGroups/rg-app/providers/Microsoft.Web/sites/api'],
    exclusions: [], sharedDependencyResourceIds: [], discoveredAt: '2026-09-29' },
  assessmentAccess: { state: 'NotChecked', checkedAt: null, reason: null },
  monitoringAccess: { state: 'NotChecked', checkedAt: null, reason: null },
  monitoring: { configured: false, enabled: false, health: 'NotEvaluated', evaluatedAt: null, reason: null },
  readiness: { state: 'Blocked', checkedAt: null, reason: 'Scope review required' }, provenance, updatedAt: '2026-09-29',
};
const impact: api.EnvironmentImpactPreview = {
  previewId: 'preview-a', systemId: 'system-a', allocationId: null, expectedVersion: 4,
  expiresAt: '2099-01-01', systems: [], warnings: ['Retain historical evidence'], requiresScopeReview: false,
};
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getSystemEnvironments).mockResolvedValue(empty);
  vi.mocked(api.getEnvironmentChoices).mockResolvedValue({ ...empty, choices: [choice], registrationHref: '' });
  vi.mocked(api.discoverEnvironmentResources).mockResolvedValue({ systemId: 'system-a', discoveryToken: 'discovery-a', expiresAt: '2099-01-01',
    discoveredAt: '2026-09-29', selection: { source: 'OrganizationOwned', registrationId: 'registration-a', allocationId: null, expectedAllocationVersion: null },
    resources: [{ resourceId: '/subscriptions/subscription-a/resourceGroups/rg-app/providers/Microsoft.Web/sites/api', name: 'Mission API',
      resourceType: 'Microsoft.Web/sites', resourceGroup: 'rg-app', location: 'usgovvirginia' }] });
});
const mount = () => render(<MemoryRouter><ConnectedSystemEnvironments systemId="system-a" busy={false} /></MemoryRouter>);
describe('Canonical system environments', () => {
  it.each(['2000-01-01', 'not-a-date'])('rejects an expired or invalid detachment preview %s without losing rationale', async expiresAt => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...empty, version: 4, attachments: [attached] });
    vi.mocked(api.previewEnvironmentDetach).mockResolvedValue({ ...impact, expiresAt });
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Manage Mission production' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Detachment rationale' }), { target: { value: 'Keep this intent' } });
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Preview detachment impact' })); });
    fireEvent.click(screen.getByRole('checkbox', { name: /reviewed the affected assessment/ }));
    // Act
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Confirm detachment' })); });
    // Assert
    expect(api.detachSystemEnvironment).not.toHaveBeenCalled();
    expect(within(screen.getByRole('dialog')).getByRole('alert')).toHaveTextContent('impact preview expired');
    expect(screen.getByRole('textbox', { name: 'Detachment rationale' })).toHaveValue('Keep this intent');
  });
  it('keeps Manage readable while every mutation follows the current server permission flags', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...empty, version: 4,
      permissions: { ...permissions, canManageEnvironments: false, canCheckAccess: false }, attachments: [attached] });
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Manage Mission production' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Manage system scope' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Review pending scope' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Check access' })).toBeDisabled();
    expect(screen.queryByRole('textbox', { name: 'Detachment rationale' })).not.toBeInTheDocument();
    expect(screen.getByRole('dialog')).toHaveTextContent('server does not permit changing');
  });
  it.each([
    { ...impact, canCommit: false, blockers: ['Review the current resource source.'] },
    { ...impact, expiresAt: '2000-01-01' },
  ])('does not commit an unavailable or expired system-scope preview $expiresAt/$canCommit', async preview => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...empty, version: 4, attachments: [attached] });
    vi.mocked(api.getEnvironmentChoices).mockResolvedValue({ ...empty, version: 4, choices: [choice], registrationHref: '' });
    vi.mocked(api.previewEnvironmentScope).mockResolvedValue(preview);
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Manage Mission production' }));
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Review pending scope' })); });
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Continue to system scope' })); });
    fireEvent.change(screen.getByRole('textbox', { name: 'Reason for scope change' }), { target: { value: 'Retained scope review' } });
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Review attachment' })); });
    // Act / Assert
    if (preview.canCommit === false) {
      expect(screen.getByRole('checkbox', { name: /reviewed this exact system scope/ })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Accept reviewed environment scope' })).toBeDisabled();
      expect(screen.getByRole('alert')).toHaveTextContent('Review the current resource source.');
    } else {
      fireEvent.click(screen.getByRole('checkbox', { name: /reviewed this exact system scope/ }));
      await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Accept reviewed environment scope' })); });
      expect(screen.getByRole('alert')).toHaveTextContent('impact preview expired');
      expect(screen.getByRole('textbox', { name: 'Reason for scope change' })).toHaveValue('Retained scope review');
    }
    expect(api.commitEnvironmentScope).not.toHaveBeenCalled();
  });
  it('updates actual assessment access inside Manage without claiming monitoring connectivity', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...empty, version: 4, attachments: [attached] });
    vi.mocked(api.checkEnvironmentAccess).mockResolvedValue({ systemId: 'system-a', version: 5, attachments: [{
      ...attached, assessmentAccess: { state: 'Denied', checkedAt: '2026-10-06', reason: 'Assessment source permission denied' },
    }] });
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Manage Mission production' }));
    // Act
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Check access' })); });
    // Assert
    expect(api.checkEnvironmentAccess).toHaveBeenCalledWith('system-a', { expectedVersion: 4, purpose: 'Assessment' });
    expect(screen.getByRole('dialog')).toHaveTextContent('Denied');
    expect(screen.getByRole('dialog')).toHaveTextContent('Monitoring access: Not Checked · Collection: Not enabled');
    expect(screen.getByRole('link', { name: 'View monitoring (opens in a new tab)' })).toHaveAttribute('target', '_blank');
  });
  it('opens pending scope review through Manage and keeps input after a stale commit', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...empty, version: 4, attachments: [attached] });
    vi.mocked(api.getEnvironmentChoices).mockResolvedValue({ ...empty, version: 4, choices: [choice], registrationHref: '' });
    vi.mocked(api.previewEnvironmentScope).mockResolvedValue(impact);
    vi.mocked(api.commitEnvironmentScope).mockRejectedValue(new Error('The current scope version changed.'));
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Manage Mission production' }));
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Review pending scope' })); });
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Continue to system scope' })); });
    fireEvent.change(screen.getByRole('textbox', { name: 'Reason for scope change' }), { target: { value: 'Retained scope review rationale' } });
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Review attachment' })); });
    fireEvent.click(screen.getByRole('checkbox', { name: /reviewed this exact system scope/ }));
    // Act
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Accept reviewed environment scope' })); });
    // Assert
    expect(api.previewEnvironmentScope).toHaveBeenCalledWith('system-a', 'attachment-a', expect.objectContaining({
      expectedVersion: 4, expectedAttachmentVersion: 2, reviewPendingScope: true,
      resourceIds: attached.scope.resourceIds, rationale: 'Retained scope review rationale',
    }));
    expect(screen.getByRole('alert')).toHaveTextContent('current scope version changed');
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(screen.getByRole('textbox', { name: 'Reason for scope change' })).toHaveValue('Retained scope review rationale');
  });
  it('retains detachment rationale and does not claim success when the server still reports attachment', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...empty, version: 4, attachments: [attached] });
    vi.mocked(api.previewEnvironmentDetach).mockResolvedValue(impact);
    vi.mocked(api.detachSystemEnvironment).mockResolvedValue({ ...empty, version: 4, attachments: [attached] });
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Manage Mission production' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Detachment rationale' }), { target: { value: 'Retained removal rationale' } });
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Preview detachment impact' })); });
    fireEvent.click(screen.getByRole('checkbox', { name: /reviewed the affected assessment/ }));
    // Act
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Confirm detachment' })); });
    // Assert
    expect(within(screen.getByRole('dialog')).getByRole('alert')).toHaveTextContent('did not confirm detachment');
    expect(screen.getByRole('textbox', { name: 'Detachment rationale' })).toHaveValue('Retained removal rationale');
    expect(screen.queryByText(/Environment detached/)).not.toBeInTheDocument();
  });
  it('blocks detachment when its impact preview cannot be committed', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...empty, version: 4, attachments: [attached] });
    vi.mocked(api.previewEnvironmentDetach).mockResolvedValue({ ...impact, canCommit: false, blockers: ['Current source must be reviewed.'] });
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Manage Mission production' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Detachment rationale' }), { target: { value: 'Source cleanup' } });
    // Act
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Preview detachment impact' })); });
    // Assert
    expect(screen.getByRole('checkbox', { name: /reviewed the affected assessment/ })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Confirm detachment' })).toBeDisabled();
    expect(screen.getByRole('dialog')).toHaveTextContent('Current source must be reviewed.');
    expect(api.detachSystemEnvironment).not.toHaveBeenCalled();
  });
  it('retains the selected subscription when wizard Cancel is canceled', async () => {
    // Arrange
    mount();
    await screen.findByText('No subscriptions attached.');
    fireEvent.click(screen.getByRole('button', { name: 'Attach subscription' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: /Mission production/ }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    fireEvent.click(screen.getByRole('button', { name: 'Keep editing' }));
    // Assert
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    expect(screen.getByRole('checkbox', { name: /Mission production/ })).toBeChecked();
    expect(api.applySystemEnvironments).not.toHaveBeenCalled();
  });
  it('has no empty table or access/monitoring primary actions before attachment', async () => {
    // Arrange
    mount();
    // Act
    await screen.findByText('No subscriptions attached.');
    // Assert
    const register = within(screen.getByRole('region', { name: 'System subscriptions' }));
    expect(register.queryByRole('table')).not.toBeInTheDocument();
    expect(register.getAllByRole('button')).toHaveLength(1);
    expect(register.getByRole('button', { name: 'Attach subscription' })).toBeEnabled();
    expect(register.queryByRole('link', { name: /monitoring/i })).not.toBeInTheDocument();
    expect(register.getByText('Provider scopes can be recorded without a subscription.')).toBeVisible();
  });
  it('keeps the named subscription register, source and independent access/monitoring states truthful', async () => {
    // Arrange
    const attachment: api.SystemEnvironmentAttachment = {
      attachmentId: 'attachment-a', systemId: 'system-a', version: 2, source: 'ProviderAllocation', registration,
      allocationId: 'allocation-a', allocationVersion: 3, offeringId: 'offering-a', offeringName: 'Provider service',
      hostingAssignmentId: null, hostingReviewState: 'Undetermined', attachmentState: 'Attached', allocationState: 'Active',
      scope: { revisionId: 'scope-a', version: 1, reviewState: 'PendingReview', resourceIds: ['resource-a'],
        exclusions: [], sharedDependencyResourceIds: [], discoveredAt: '2026-09-29' },
      assessmentAccess: { state: 'Denied', checkedAt: '2026-09-29', reason: 'Assessment permission denied' },
      monitoringAccess: { state: 'NotChecked', checkedAt: null, reason: null },
      monitoring: { configured: true, enabled: true, health: 'Degraded', evaluatedAt: null, reason: 'Telemetry incomplete' },
      readiness: { state: 'Blocked', checkedAt: null, reason: 'Scope review required' }, provenance, updatedAt: '2026-09-29',
    };
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...empty, version: 4, attachments: [attachment] });
    mount();
    // Act
    const table = await screen.findByRole('table', { name: 'System subscriptions' });
    // Assert
    expect(within(table).getAllByRole('columnheader').map(cell => cell.textContent)).toEqual([
      'Subscription name and identifier', 'System resource scope', 'Assessment-access status', 'Monitoring status', 'Action',
    ]);
    expect(within(table).getAllByRole('button')).toHaveLength(1);
    expect(table).toHaveTextContent('subscription-a');
    expect(table).toHaveTextContent('Pending Review');
    expect(table).toHaveTextContent('Denied');
    expect(table).toHaveTextContent('Degraded');
    expect(screen.getByRole('region', { name: 'Subscription register' })).toHaveAttribute('tabindex', '0');
    expect(screen.getByText(/Attachment and access checks are separate operations/)).toBeVisible();
    // Act
    fireEvent.click(within(table).getByRole('button', { name: 'Manage Mission production' }));
    fireEvent.click(screen.getByText('Subscription source and technical details'));
    // Assert
    expect(screen.getByRole('dialog')).toHaveTextContent('Allocation statusActive');
    expect(screen.getByRole('dialog')).toHaveTextContent('Assessment permission denied');
    expect(screen.getByRole('dialog')).toHaveTextContent('Telemetry incomplete');
    expect(screen.getByRole('dialog')).toHaveTextContent('ManualVerified');
    expect(api.applySystemEnvironments).not.toHaveBeenCalled();
    expect(api.checkEnvironmentAccess).not.toHaveBeenCalled();
  });
  it('supports organization-owned setup without fabricating provider prerequisites or whole-subscription selection', async () => {
    // Arrange
    mount(); await screen.findByText('No subscriptions attached.');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Attach subscription' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: /Mission production/ }));
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Select system resource scope' })); });
    // Assert
    expect(screen.getByRole('checkbox', { name: 'Include Mission API' })).not.toBeChecked();
    expect(screen.getByText(/Future resources are not automatically included/)).toBeVisible();
    expect(screen.getByRole('button', { name: 'Review and attach' })).toBeDisabled();
    expect(api.applySystemEnvironments).not.toHaveBeenCalled();
  });
  it('surfaces failed discovery instead of treating it as an empty resource set', async () => {
    // Arrange
    vi.mocked(api.discoverEnvironmentResources).mockRejectedValue(new Error('Discovery denied'));
    mount(); await screen.findByText('No subscriptions attached.');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Attach subscription' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: /Mission production/ }));
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Select system resource scope' })); });
    // Assert
    expect(within(screen.getByRole('dialog')).getByRole('alert')).toHaveTextContent('Discovery denied');
    expect(screen.queryByText('No resources were discovered.')).not.toBeInTheDocument();
  });
  it('gates environment mutation on its own server capability, not profile authoring or local role', async () => {
    // Arrange
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...empty, permissions: { ...permissions, canManageEnvironments: false } });
    mount();
    // Act / Assert
    await screen.findByText('No subscriptions attached.');
    expect(screen.getByRole('button', { name: 'Attach subscription' })).toBeDisabled();
    expect(screen.getByText(/Your system permissions do not allow subscription attachment/)).toBeVisible();
  });
  it('selects organization and provider subscriptions together without requiring a provider relationship', async () => {
    // Arrange
    const allocated: api.EnvironmentChoice = { ...choice, choiceId: 'allocated', source: 'ProviderAllocation',
      registration: { ...registration, registrationId: 'provider-registration', displayName: 'Provider subscription' },
      allocationId: 'allocation-a', allocationVersion: 3 };
    vi.mocked(api.getEnvironmentChoices).mockResolvedValue({ ...empty, choices: [choice, allocated], registrationHref: '' });
    vi.mocked(api.discoverEnvironmentResources).mockImplementation(async (_systemId, request) => ({
      systemId: 'system-a', discoveryToken: request.selection.registrationId, expiresAt: '2099-01-01', discoveredAt: '2026-09-29',
      selection: request.selection, resources: [{ resourceId: `/subscriptions/${request.selection.registrationId}/resourceGroups/mission/providers/Microsoft.Web/sites/api`,
        name: 'Mission API', resourceType: 'Microsoft.Web/sites', resourceGroup: 'mission', location: null }],
    }));
    vi.mocked(api.applySystemEnvironments).mockRejectedValue(new Error('Version changed; refresh the current records.'));
    mount(); await screen.findByText('No subscriptions attached.');
    fireEvent.click(screen.getByRole('button', { name: 'Attach subscription' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: /Mission production/ }));
    fireEvent.click(screen.getByRole('checkbox', { name: /Provider subscription/ }));
    // Act
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Select system resource scope' })); });
    const resources = screen.getAllByRole('checkbox', { name: 'Include Mission API' });
    resources.forEach(resource => { expect(resource).not.toBeChecked(); fireEvent.click(resource); });
    expect(screen.getAllByLabelText('Related provider scope — optional')).toHaveLength(2);
    fireEvent.click(screen.getByRole('button', { name: 'Review and attach' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'I reviewed this exact system scope and the remaining prerequisites.' }));
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Attach selected subscriptions' })); });
    // Assert
    const request = vi.mocked(api.applySystemEnvironments).mock.calls[0]?.[1];
    expect(request?.items).toHaveLength(2);
    expect(request?.items.map(item => item.selection.source)).toEqual(['OrganizationOwned', 'ProviderAllocation']);
    expect(request?.items.every(item => item.reuseHostingAssignmentId === null)).toBe(true);
    expect(within(screen.getByRole('dialog')).getByRole('alert')).toHaveTextContent('Version changed');
    expect(screen.getByText('No subscriptions attached.')).toBeVisible();
  });
  it('optionally relates an organization subscription to an existing independent provider scope', async () => {
    // Arrange
    const scope: api.SystemProviderScope = { providerId: 'provider-a', assignmentId: 'scope-assignment', assignmentVersion: 2,
      relationshipId: 'relationship-a', offeringId: 'offering-a', offeringName: 'Shared service', providerName: 'Provider A',
      hostingScopeRevisionId: 'released-scope', hostingScopeName: 'Reviewed hosting scope', state: 'Active',
      relationshipState: 'Undetermined', reviewRequired: true, assignedScopes: [], selectionVersion: 1 };
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...empty, providerScopes: [scope] });
    vi.mocked(api.applySystemEnvironments).mockRejectedValue(new Error('Test response unavailable'));
    mount(); await screen.findByText('No subscriptions attached.');
    fireEvent.click(screen.getByRole('button', { name: 'Attach subscription' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: /Mission production/ }));
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Select system resource scope' })); });
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: 'Include Mission API' }));
    fireEvent.change(screen.getByLabelText('Related provider scope — optional'), { target: { value: 'scope-assignment' } });
    fireEvent.click(screen.getByRole('button', { name: 'Review and attach' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'I reviewed this exact system scope and the remaining prerequisites.' }));
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Attach selected subscriptions' })); });
    // Assert
    expect(vi.mocked(api.applySystemEnvironments).mock.calls[0]?.[1].items[0]).toEqual(expect.objectContaining({
      reuseHostingAssignmentId: 'scope-assignment', selection: expect.objectContaining({ source: 'OrganizationOwned', allocationId: null }),
    }));
  });
  it('does not report success if the response omits the explicitly requested provider link', async () => {
    // Arrange
    const scope: api.SystemProviderScope = { providerId: 'provider-a', assignmentId: 'scope-assignment', assignmentVersion: 2,
      relationshipId: 'relationship-a', offeringId: 'offering-a', offeringName: 'Shared service', providerName: 'Provider A',
      hostingScopeRevisionId: 'released-scope', hostingScopeName: 'Reviewed hosting scope', state: 'Active',
      relationshipState: 'Undetermined', reviewRequired: true, assignedScopes: [], selectionVersion: 1 };
    vi.mocked(api.getSystemEnvironments).mockResolvedValue({ ...empty, providerScopes: [scope] });
    vi.mocked(api.applySystemEnvironments).mockImplementation(async (_id, body) => ({
      ...empty, version: 1, providerScopes: [scope], hostingLinks: [], attachments: body.items.map(item => ({
        attachmentId: 'saved-attachment', systemId: 'system-a', version: 1, source: item.selection.source,
        registration, allocationId: null, allocationVersion: null, offeringId: null, offeringName: null,
        hostingAssignmentId: null, hostingReviewState: 'NotApplicable', attachmentState: 'Attached',
        scope: { revisionId: 'revision-a', version: 1, reviewState: 'PendingReview', resourceIds: item.resourceIds,
          exclusions: [], sharedDependencyResourceIds: [], discoveredAt: '2026-09-29' },
        assessmentAccess: { state: 'NotChecked', checkedAt: null, reason: null },
        monitoringAccess: { state: 'NotChecked', checkedAt: null, reason: null },
        readiness: { state: 'Blocked', checkedAt: null, reason: 'Scope needs review' },
        monitoring: { configured: false, enabled: false, health: 'NotEvaluated', evaluatedAt: null, reason: null },
        provenance, updatedAt: '2026-09-29',
      })),
    }));
    mount(); await screen.findByText('No subscriptions attached.');
    fireEvent.click(screen.getByRole('button', { name: 'Attach subscription' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: /Mission production/ }));
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Select system resource scope' })); });
    fireEvent.click(screen.getByRole('checkbox', { name: 'Include Mission API' }));
    fireEvent.change(screen.getByLabelText('Related provider scope — optional'), { target: { value: 'scope-assignment' } });
    fireEvent.click(screen.getByRole('button', { name: 'Review and attach' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'I reviewed this exact system scope and the remaining prerequisites.' }));
    // Act
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Attach selected subscriptions' })); });
    // Assert
    expect(within(screen.getByRole('dialog')).getByRole('alert')).toHaveTextContent('requested provider relationship');
    expect(screen.queryByText(/Subscriptions attached. Scope review/)).not.toBeInTheDocument();
  });
  it('retains the same apply intent and replay key after an unconfirmed response', async () => {
    // Arrange
    vi.mocked(api.applySystemEnvironments).mockRejectedValue(new Error('Response interrupted'));
    mount(); await screen.findByText('No subscriptions attached.');
    fireEvent.click(screen.getByRole('button', { name: 'Attach subscription' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: /Mission production/ }));
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Select system resource scope' })); });
    fireEvent.click(screen.getByRole('checkbox', { name: 'Include Mission API' }));
    fireEvent.click(screen.getByRole('button', { name: 'Review and attach' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'I reviewed this exact system scope and the remaining prerequisites.' }));
    // Act
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Attach selected subscriptions' })); });
    expect(within(screen.getByRole('dialog')).getByRole('alert')).toHaveTextContent('Response interrupted');
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Retry remaining attachments' })); });
    // Assert
    expect(api.applySystemEnvironments).toHaveBeenCalledTimes(2);
    const calls = vi.mocked(api.applySystemEnvironments).mock.calls;
    const first = calls[0];
    if (!first) throw new Error('Expected an apply request before retry.');
    expect(calls[1]).toEqual(first);
    expect(first[2]).toBeTruthy();
    expect(first[1].items[0]?.resourceIds).toHaveLength(1);
    expect(first[1].items[0]?.reuseHostingAssignmentId).toBeNull();
    expect(screen.getByRole('dialog')).toBeVisible();
    expect(screen.queryByText(/Environment attached. Scope review/)).not.toBeInTheDocument();
  });
});
