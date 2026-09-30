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
