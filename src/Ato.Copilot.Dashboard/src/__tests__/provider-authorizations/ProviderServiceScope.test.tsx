import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { HostingPanel } from '../../features/provider-authorizations/HostingPanel';
import { ScopeDetails } from '../../features/provider-relationships/MissionTaskPresentation';
import { isProviderScope } from '../../features/provider-authorizations/scopes';
import * as hosting from '../../features/provider-authorizations/hostingApi';
import type { Offering } from '../../features/provider-authorizations/types';
import type { HostingScopeRevision } from '../../features/provider-authorizations/hostingTypes';
import { page } from '../package-imports/fixtures';
import '../package-imports/crypto';

vi.mock('../../features/provider-authorizations/hostingApi', async original => ({
  ...await original<typeof hosting>(), listHostingScopes: vi.fn(), createHostingScope: vi.fn(),
}));
const offering: Offering = {
  offeringId: 'service-offering', providerId: 'same-provider', name: 'Synthetic M365', description: '',
  environments: ['Microsoft365DoD'], lifecycle: 'Draft', revision: 4, currentBoundaryRevisionId: null,
  currentHostingScopeRevisionId: 'scope-service', serviceModel: 'SoftwareAsAService', managementArrangement: 'SharedOperations',
};
const scope = {
  kind: 'Service' as const, serviceId: 'synthetic-m365-service', serviceName: 'Synthetic collaboration',
  environment: 'Microsoft365DoD' as const, tenantReference: 'Synthetic service tenant',
};
const revision: HostingScopeRevision = {
  offeringId: offering.offeringId, offeringRevision: 4, snapshot: { revisionId: 'scope-service', revision: 1, snapshotHash: 'service-hash' },
  predecessorRevisionId: null, impactReviewId: null, name: 'Synthetic service relationship',
  permittedScopes: [scope], exclusions: [], citations: [],
};
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(hosting.listHostingScopes).mockResolvedValue(page([revision]));
  vi.mocked(hosting.createHostingScope).mockResolvedValue({ ...revision, offeringRevision: 5,
    snapshot: { revisionId: 'service-next', revision: 2, snapshotHash: 'next-hash' } });
});
it('edits and submits an explicit service relationship without Azure input fields or fabricated identifiers', async () => {
  // Arrange
  render(<HostingPanel offering={offering} task="scope" initialScope={revision} onChanged={vi.fn()} />);
  await waitFor(() => expect(screen.getByLabelText('Hosting scope name')).toBeEnabled());
  // Act
  fireEvent.click(screen.getByLabelText('I confirm this exact technical scope revision, not authorization coverage.'));
  fireEvent.click(screen.getByRole('button', { name: 'Save hosting scope revision' }));
  // Assert
  expect(screen.getByLabelText('Service identifier 1')).toHaveValue(scope.serviceId);
  expect(screen.queryByLabelText('subscriptionId 1')).not.toBeInTheDocument();
  expect(screen.queryByLabelText('directoryTenantId 1')).not.toBeInTheDocument();
  await waitFor(() => expect(hosting.createHostingScope).toHaveBeenCalledWith(offering.offeringId, expect.objectContaining({
    expectedOfferingRevision: 4, predecessorRevisionId: 'scope-service', permittedScopes: [scope],
  }), expect.any(String)));
});
it('shows the Mission Owner the exact service identity rather than Azure placeholders', () => {
  // Arrange / Act
  render(<ScopeDetails scopes={[scope]} />);
  // Assert
  expect(screen.getByText(/synthetic-m365-service/)).toBeInTheDocument();
  expect(screen.queryByText(/Subscription:/)).not.toBeInTheDocument();
});
it('accepts legacy Azure and explicit Service responses but rejects hybrid and unknown kinds', () => {
  // Arrange
  const azure = { cloud: 'AzureCloud', directoryTenantId: 'directory', subscriptionId: 'subscription', resourceId: '/subscriptions/subscription' };
  // Act / Assert
  expect(isProviderScope(azure)).toBe(true);
  expect(isProviderScope(scope)).toBe(true);
  expect(isProviderScope({ ...scope, subscriptionId: 'invented' })).toBe(false);
  expect(isProviderScope({ ...azure, kind: 'Unknown' })).toBe(false);
});
