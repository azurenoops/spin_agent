import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import '../helpers/dialog';
import ProviderSubscriptionAllocations from '../../features/provider-authorizations/ProviderSubscriptionAllocations';
import * as api from '../../api/systemEnvironments';
vi.mock('../../api/systemEnvironments', () => ({
  listProviderEnvironmentAllocations: vi.fn(), getProviderAllocationChoices: vi.fn(), recordProviderEnvironmentAllocation: vi.fn(),
  getProviderAllocationUsage: vi.fn(), previewProviderAllocationChange: vi.fn(), commitProviderAllocationChange: vi.fn(),
}));
const subscription = '11111111-1111-1111-1111-111111111111';
const registration: api.EnvironmentRegistration = { registrationId: 'registration-a', ownerTenantId: 'provider-a',
  subscriptionId: subscription, directoryTenantId: 'directory-a', cloud: 'AzureUSGovernment', displayName: 'Mission subscription', status: 'Selected', lastVerifiedAt: '2026-09-29' };
const allocation: api.ProviderEnvironmentAllocation = { allocationId: 'allocation-a', version: 1, offeringId: 'offering-a',
  offeringName: 'Provider service', consumerTenantId: 'org-a', consumerName: 'Mission organization', registration,
  hostingScopeRevisionId: 'scope-a', permittedResourceScopes: [`/subscriptions/${subscription}/resourceGroups/mission`],
  state: 'Active', startsAt: '2026-09-29', expiresAt: null, systemCount: 1,
  provenance: { source: 'ProviderRecorded', externalId: null, sourceRevision: null, reconciliationState: 'Verified', evidenceReference: null, recordedAt: '2026-09-29' } };
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.listProviderEnvironmentAllocations).mockResolvedValue({ offeringId: 'offering-a', canManage: true, allocations: [allocation] });
  vi.mocked(api.getProviderAllocationChoices).mockResolvedValue({ offeringId: 'offering-a', offeringVersion: 2, canManage: true,
    registrations: [registration], consumers: [{ tenantId: 'org-a', name: 'Mission organization' }],
    releasedScopes: [{ revisionId: 'scope-a', name: 'Released scope', permittedResourceScopes: allocation.permittedResourceScopes, excludedResourceScopes: [] }],
    registrationHref: '' });
  vi.mocked(api.getProviderAllocationUsage).mockResolvedValue({ allocationId: 'allocation-a', version: 1, systems: [{
    systemId: 'system-a', systemName: 'Mission system', attachmentId: 'attachment-a', attachmentVersion: 1, selectedResourceCount: 2,
    assessmentAffected: true, monitoringAffected: true,
  }] });
});
describe('Provider subscription allocation management', () => {
  it('records a provider-verified allocation using the canonical manual provenance contract', async () => {
    // Arrange
    vi.mocked(api.recordProviderEnvironmentAllocation).mockResolvedValue(allocation);
    render(<MemoryRouter><ProviderSubscriptionAllocations offeringId="offering-a" /></MemoryRouter>);
    await screen.findByText('Mission subscription');
    fireEvent.click(screen.getByRole('button', { name: 'Record subscription allocation' }));
    fireEvent.change(screen.getByLabelText('Actual Azure subscription'), { target: { value: 'registration-a' } });
    fireEvent.change(screen.getByLabelText('Consuming organization'), { target: { value: 'org-a' } });
    fireEvent.change(screen.getByLabelText('Released hosting scope'), { target: { value: 'scope-a' } });
    fireEvent.click(screen.getByRole('checkbox', { name: allocation.permittedResourceScopes[0] }));
    fireEvent.change(screen.getByLabelText('Effective from'), { target: { value: '2026-09-29T12:00' } });
    fireEvent.click(screen.getByRole('checkbox', { name: 'I verified the exact subscription, consumer and external source provenance.' }));
    // Act
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Record allocation' })); });
    // Assert
    expect(api.recordProviderEnvironmentAllocation).toHaveBeenCalledWith('offering-a', expect.objectContaining({
      registrationId: 'registration-a', consumerTenantId: 'org-a', hostingScopeRevisionId: 'scope-a',
      provenance: expect.objectContaining({ source: 'ProviderRecorded', reconciliationState: 'Verified', externalId: null, sourceRevision: null }),
    }), expect.any(String));
  });
  it('requires an explicit impact preview and confirmation before withdrawing allocation eligibility', async () => {
    // Arrange
    vi.mocked(api.previewProviderAllocationChange).mockResolvedValue({ previewId: 'preview-a', systemId: null, allocationId: 'allocation-a',
      expectedVersion: 1, expiresAt: '2099-01-01', systems: [], requiresScopeReview: true, warnings: ['Access will be reevaluated'] });
    vi.mocked(api.commitProviderAllocationChange).mockResolvedValue({ ...allocation, state: 'Withdrawn', version: 2 });
    render(<MemoryRouter><ProviderSubscriptionAllocations offeringId="offering-a" /></MemoryRouter>);
    fireEvent.click(await screen.findByRole('button', { name: 'Review allocation' }));
    await screen.findByText('Mission system · 2 resources');
    // Act
    fireEvent.change(screen.getByLabelText('Change rationale'), { target: { value: 'Allocation contract ended' } });
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Preview allocation impact' })); });
    // Assert
    expect(api.commitProviderAllocationChange).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Confirm allocation change' })).toBeDisabled();
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: 'I reviewed affected systems and the access impact.' }));
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Confirm allocation change' })); });
    // Assert
    expect(api.commitProviderAllocationChange).toHaveBeenCalledWith('offering-a', 'allocation-a', {
      expectedVersion: 1, previewId: 'preview-a', rationale: 'Allocation contract ended', acknowledgeImpact: true,
    }, expect.any(String));
  });
  it('does not show recording authority when the server denies provider management', async () => {
    // Arrange
    vi.mocked(api.getProviderAllocationChoices).mockResolvedValue({ offeringId: 'offering-a', offeringVersion: 2,
      canManage: false, registrations: [], consumers: [], releasedScopes: [], registrationHref: '' });
    // Act
    render(<MemoryRouter><ProviderSubscriptionAllocations offeringId="offering-a" /></MemoryRouter>);
    await screen.findByText('Mission subscription');
    // Assert
    expect(screen.getByRole('button', { name: 'Record subscription allocation' })).toBeDisabled();
    expect(api.recordProviderEnvironmentAllocation).not.toHaveBeenCalled();
  });
});
