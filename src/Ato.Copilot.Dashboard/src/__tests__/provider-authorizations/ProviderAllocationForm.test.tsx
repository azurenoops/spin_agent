import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { ProviderAllocationForm } from '../../features/provider-authorizations/ProviderAllocationForm';
import * as directory from '../../features/csp-dashboard/api';
import * as hosting from '../../features/provider-authorizations/hostingApi';
import { fixtureHosting, fixtureOffering } from '../../../e2e/fixtures/provider-presentation-data';

vi.mock('../../features/csp-dashboard/api', async original => ({
  ...await original<typeof directory>(), getCspDashboardSystems: vi.fn(),
}));
vi.mock('../../features/provider-authorizations/hostingApi', async original => ({
  ...await original<typeof hosting>(), getHostingScope: vi.fn(), createHostingAssignment: vi.fn(),
}));
const system: directory.SystemRow = {
  systemId: 'mission-a', name: 'Mission Alpha', tenantId: 'tenant-a', orgDisplayName: 'Organization Alpha',
  acronym: null, impactLevel: 'Moderate', currentRmfPhase: 'Prepare', complianceScore: 0,
  atoExpirationDate: null, atoStatus: 'None', atoDaysRemaining: null, atoSeverity: 'none', openPoamCount: 0, overduePoamCount: 0,
};
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(directory.getCspDashboardSystems).mockResolvedValue({ items: [system], page: 1, pageSize: 100, totalCount: 1 });
  vi.mocked(hosting.getHostingScope).mockResolvedValue(fixtureHosting);
  vi.mocked(hosting.createHostingAssignment).mockResolvedValue({
    assignmentId: 'assignment-new', revision: 1, offeringId: fixtureOffering.offeringId, systemId: system.systemId,
    hostingScope: fixtureHosting.snapshot, assignedScopes: fixtureHosting.permittedScopes, relationshipState: 'Undetermined',
  });
});

it('selects named customer/system and exact allowed scope without raw ID entry or implicit writes', async () => {
  // Arrange
  const saved = vi.fn();
  render(<ProviderAllocationForm offering={fixtureOffering} onSaved={saved} onPendingChange={vi.fn()} />);
  await screen.findByRole('option', { name: 'Organization Alpha' });
  // Act
  fireEvent.change(screen.getByLabelText('Customer organization'), { target: { value: system.tenantId } });
  fireEvent.change(screen.getByLabelText('Mission system'), { target: { value: system.systemId } });
  fireEvent.click(await screen.findByRole('checkbox', { name: /synthetic-shared/ }));
  // Assert
  expect(screen.queryByRole('textbox', { name: 'Customer tenant ID' })).not.toBeInTheDocument();
  expect(hosting.createHostingAssignment).not.toHaveBeenCalled();
  expect(screen.getByRole('button', { name: 'Save service assignment' })).toBeDisabled();
  // Act
  fireEvent.click(screen.getByLabelText('I confirm this allocation grants no permissions or authorization coverage.'));
  fireEvent.click(screen.getByRole('button', { name: 'Save service assignment' }));
  // Assert
  await waitFor(() => expect(hosting.createHostingAssignment).toHaveBeenCalledWith(fixtureOffering.offeringId, {
    targetTenantId: system.tenantId, systemId: system.systemId, hostingScopeRevisionId: fixtureHosting.snapshot.revisionId,
    assignedScopes: fixtureHosting.permittedScopes, references: [],
  }, expect.any(String)));
  expect(saved).toHaveBeenCalled();
});

it('makes an unavailable directory explicit and blocks assignment', async () => {
  // Arrange
  vi.mocked(directory.getCspDashboardSystems).mockResolvedValue({ unavailable: true, reason: 'NOT_CSP_ADMIN' });
  // Act
  render(<ProviderAllocationForm offering={fixtureOffering} onSaved={vi.fn()} onPendingChange={vi.fn()} />);
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('NOT_CSP_ADMIN');
  expect(screen.getByRole('button', { name: 'Save service assignment' })).toBeDisabled();
  expect(hosting.createHostingAssignment).not.toHaveBeenCalled();
});

it('preserves manual service scopes without inventing Azure resource identifiers', async () => {
  // Arrange
  const service = { kind: 'Service' as const, serviceId: 'service-a', serviceName: 'Demo collaboration',
    environment: 'Microsoft365DoD' as const, tenantReference: 'Demo tenant' };
  vi.mocked(hosting.getHostingScope).mockResolvedValue({ ...fixtureHosting, permittedScopes: [service] });
  vi.mocked(hosting.createHostingAssignment).mockResolvedValue({
    assignmentId: 'manual-assignment', revision: 1, offeringId: fixtureOffering.offeringId, systemId: system.systemId,
    hostingScope: fixtureHosting.snapshot, assignedScopes: [service], relationshipState: 'Undetermined',
  });
  render(<ProviderAllocationForm offering={fixtureOffering} onSaved={vi.fn()} onPendingChange={vi.fn()} />);
  await screen.findByRole('option', { name: 'Organization Alpha' });
  // Act
  fireEvent.change(screen.getByLabelText('Customer organization'), { target: { value: system.tenantId } });
  fireEvent.change(screen.getByLabelText('Mission system'), { target: { value: system.systemId } });
  fireEvent.click(await screen.findByRole('checkbox', { name: 'Demo collaboration' }));
  fireEvent.click(screen.getByLabelText('I confirm this allocation grants no permissions or authorization coverage.'));
  fireEvent.click(screen.getByRole('button', { name: 'Save service assignment' }));
  // Assert
  await waitFor(() => expect(hosting.createHostingAssignment).toHaveBeenCalledWith(fixtureOffering.offeringId,
    expect.objectContaining({ assignedScopes: [service] }), expect.any(String)));
  expect(screen.queryByLabelText('Subscription ID')).not.toBeInTheDocument();
});
