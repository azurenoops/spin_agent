import { expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { HostingContextSummary, HostingScopeIdentity } from '../../features/provider-authorizations/HostingContextSummary';
import type { HostingScopeRevision } from '../../features/provider-authorizations/hostingTypes';
import type { ProviderScope } from '../../features/provider-authorizations/types';

const scope: HostingScopeRevision = {
  offeringId: 'offering', offeringRevision: 45, name: 'SYNTHETIC service · recovery:retained-hash',
  snapshot: { revisionId: 'scope-exact', revision: 2, snapshotHash: 'scope-hash' },
  purpose: 'Retained shared-service purpose', predecessorRevisionId: null, impactReviewId: null,
  permittedScopes: [], exclusions: [], citations: [],
};

it('preserves the explicit synthetic designation and distinguishes scope revision from offering revision', () => {
  // Arrange / Act
  render(<HostingContextSummary offeringName="Shared services" scope={scope} />);
  // Assert
  expect(screen.getByRole('heading', { name: 'Shared services · Scope revision 2' })).toBeInTheDocument();
  expect(screen.getByText('Synthetic source')).toBeVisible();
  expect(screen.getByText(scope.purpose!)).toBeVisible();
  expect(screen.getByText(scope.name)).not.toBeVisible();
  expect(screen.queryByText(/revision 45/)).not.toBeInTheDocument();
  fireEvent.click(screen.getByText('Hosting provenance'));
  expect(screen.getByText(scope.name)).toBeVisible();
  expect(screen.getByText('scope-exact')).toBeVisible();
  expect(screen.getByText('scope-hash')).toBeVisible();
});

it('does not invent purpose or duplicate an existing synthetic business label', () => {
  // Arrange / Act
  render(<HostingContextSummary offeringName="SYNTHETIC collaboration" scope={{ ...scope, purpose: null }} />);
  // Assert
  expect(screen.getByRole('heading')).toHaveTextContent('SYNTHETIC collaboration');
  expect(screen.queryByText('Synthetic source')).not.toBeInTheDocument();
  expect(screen.queryByText('Retained shared-service purpose')).not.toBeInTheDocument();
});

it.each([
  ['/subscriptions/sub-id', 'Subscription scope'],
  ['/subscriptions/sub-id/resourceGroups/synthetic-shared', 'Resource group · synthetic-shared'],
  ['/subscriptions/sub-id/resourceGroups/synthetic-shared/providers/Microsoft.Compute/virtualMachines/synthetic-vm', 'Resource · synthetic-vm'],
])('keeps technical Azure identifiers in expandable details for %s', (resourceId, label) => {
  // Arrange
  const azure: ProviderScope = { cloud: 'AzureUSGovernment', directoryTenantId: 'directory-id', subscriptionId: 'sub-id', resourceId };
  // Act
  render(<HostingScopeIdentity scope={azure} />);
  // Assert
  expect(screen.getByText(label)).toBeVisible();
  expect(screen.getByText(resourceId)).not.toBeVisible();
  fireEvent.click(screen.getByText('Technical scope details'));
  expect(screen.getByText(resourceId)).toBeVisible();
  expect(screen.getByText('directory-id')).toBeVisible();
});

it('uses a manual service name without inventing Azure scope', () => {
  // Arrange
  const service: ProviderScope = { kind: 'Service', serviceId: 'service-exact', serviceName: 'SYNTHETIC collaboration',
    environment: 'Microsoft365DoD', tenantReference: 'tenant-exact' };
  // Act
  render(<HostingScopeIdentity scope={service} />);
  // Assert
  expect(screen.getByText(service.serviceName)).toBeVisible();
  expect(screen.queryByText('Subscription')).not.toBeInTheDocument();
  expect(screen.getByText(/service-exact/)).not.toBeVisible();
  fireEvent.click(screen.getByText('Technical scope details'));
  expect(screen.getByText(/service-exact/)).toBeVisible();
});
