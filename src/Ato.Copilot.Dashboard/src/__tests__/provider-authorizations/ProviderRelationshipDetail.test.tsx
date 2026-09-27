import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { AuthorizationsPage } from '../../features/provider-authorizations/AuthorizationsPage';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import * as api from '../../features/provider-authorizations/api';
import * as hosting from '../../features/provider-authorizations/hostingApi';
import { offering } from './testData';
import { PackageImportError } from '../../features/package-imports/request';

vi.mock('../../components/layout/PageLayout', () => ({ default: ({ children }: { children: ReactNode }) => <main>{children}</main> }));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => ({
  target: { kind: 'csp' }, workspace: { permissions: { canAccessCsp: true } },
}) }));
vi.mock('../../features/provider-authorizations/api', async original => ({
  ...await original<typeof api>(), getOffering: vi.fn(), getBoundaryOverview: vi.fn(),
}));
vi.mock('../../features/provider-authorizations/hostingApi', async original => ({
  ...await original<typeof hosting>(), getHostingAssignment: vi.fn(), getHostingScope: vi.fn(),
}));
const scope = { kind: 'Service' as const, serviceId: 'service-1', serviceName: 'Synthetic service',
  environment: 'ManualService' as const, tenantReference: 'Synthetic service tenant' };
const projection = {
  offeringId: offering.offeringId, offeringRevision: offering.revision,
  capabilities: { items: [{ capabilityId: 'capability-1', candidateId: null, packageId: null, name: 'Recorded protection',
    reviewState: 'Reviewed', publicationState: 'Published', releaseId: 'release-1', boundaryRevisionId: 'boundary-1' }],
    page: 1, pageSize: 10, total: 1, awaitingReview: 0, published: 1 },
  missionSystems: { items: [{ assignmentId: 'assignment-1', systemId: 'system-1', systemName: 'Synthetic mission',
    relationshipState: 'ReviewRequired', associated: true, adoptedCapabilityCount: 2, assignedScopes: [scope] }],
    page: 3, pageSize: 10, total: 21 },
};
function mount(extraQuery = '') {
  return render(<MemoryRouter initialEntries={[api.authorizationHref(offering.offeringId, `missions/assignment-1?missionPage=3&offeringPage=2${extraQuery ? `&${extraQuery}` : ''}`)]}>
    <WorkspaceNavigationProvider workspace={{ kind: 'csp' }}><AuthorizationsPage /></WorkspaceNavigationProvider>
  </MemoryRouter>);
}
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getOffering).mockResolvedValue({ ...offering, currentHostingScopeRevisionId: 'newer-scope' });
  vi.mocked(api.getBoundaryOverview).mockResolvedValue(projection);
  vi.mocked(hosting.getHostingAssignment).mockResolvedValue({
    assignmentId: 'assignment-1', offeringId: offering.offeringId, systemId: 'system-1', revision: 4,
    systemName: 'Synthetic mission', targetTenantName: 'Synthetic customer',
    relationshipState: 'ReviewRequired', assignedScopes: [scope],
    hostingScope: { revisionId: 'retained-scope', revision: 2, snapshotHash: 'retained-hash' },
  });
  vi.mocked(hosting.getHostingScope).mockResolvedValue({
    offeringId: offering.offeringId, offeringRevision: offering.revision, name: 'Retained service allocation',
    snapshot: { revisionId: 'retained-scope', revision: 2, snapshotHash: 'retained-hash' },
    impactReviewId: null, predecessorRevisionId: null, permittedScopes: [scope], exclusions: [], citations: [],
  });
});
it('opens the provider relationship with its exact retained allocation and real workflow links', async () => {
  // Arrange / Act
  mount();
  // Assert
  expect(await screen.findByRole('heading', { name: 'Synthetic mission' })).toBeInTheDocument();
  expect(await screen.findByText('Retained service allocation')).toBeInTheDocument();
  expect(hosting.getHostingScope).toHaveBeenCalledWith(offering.offeringId, 'retained-scope', expect.any(AbortSignal));
  expect(api.getBoundaryOverview).toHaveBeenCalledWith(offering.offeringId, 1, 3, expect.any(AbortSignal));
  expect(screen.getByRole('link', { name: 'Manage service allocations' })).toHaveAttribute('href', api.authorizationHref(offering.offeringId, 'inherited-coverage?task=allocations'));
  expect(screen.getByRole('link', { name: 'Open Recorded protection' })).toHaveAttribute('href', '/workspaces/csp/security-capabilities/capability-1');
  expect(screen.getByRole('link', { name: 'Review current publication for Recorded protection' })).toHaveAttribute('href', '/workspaces/csp/security-capabilities/capability-1?tab=review');
  expect(screen.getByRole('link', { name: 'Back to mission systems' })).toHaveAttribute('href', `/workspaces/csp/systems?offeringId=${offering.offeringId}&offeringPage=2&missionPage=3`);
  expect(screen.queryByText(/duties completed/i)).not.toBeInTheDocument();
});
it('retains failure/retry without inventing a relationship or zero adoptions', async () => {
  // Arrange
  vi.mocked(hosting.getHostingAssignment).mockRejectedValueOnce(new PackageImportError('Allocation access denied', 403));
  mount();
  // Act
  expect(await screen.findByRole('alert')).toHaveTextContent('Allocation access denied');
  expect(screen.queryByText('Recorded capability adoptions')).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
  // Assert
  expect(await screen.findByRole('heading', { name: 'Synthetic mission' })).toBeInTheDocument();
});
it('locates the exact relationship after its listing page changes', async () => {
  // Arrange
  vi.mocked(api.getBoundaryOverview).mockImplementation(async (_id, _capabilityPage, missionPage) => ({
    ...projection, missionSystems: { ...projection.missionSystems, page: missionPage ?? 1,
      items: missionPage === 2 ? projection.missionSystems.items : [] },
  }));
  // Act
  mount();
  // Assert
  expect(await screen.findByText('Recorded capability adoptions')).toBeInTheDocument();
  expect(api.getBoundaryOverview).toHaveBeenCalledWith(offering.offeringId, 1, 2, expect.any(AbortSignal));
});
it('refuses mismatched projection ownership instead of showing another offering’s adoptions', async () => {
  // Arrange
  vi.mocked(api.getBoundaryOverview).mockResolvedValue({ ...projection, offeringId: 'other-offering' });
  // Act
  mount();
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('does not match this offering');
  expect(screen.queryByText('Recorded capability adoptions')).not.toBeInTheDocument();
});
it('restores the capability catalog page without losing the mission listing context', async () => {
  // Arrange
  vi.mocked(api.getBoundaryOverview).mockImplementation(async (_id, capabilityPage) => ({
    ...projection, capabilities: { ...projection.capabilities, page: capabilityPage ?? 1, total: 11 },
  }));
  // Act
  mount('capabilityPage=2');
  // Assert
  await screen.findByRole('link', { name: 'Open Recorded protection' });
  expect(api.getBoundaryOverview).toHaveBeenCalledWith(offering.offeringId, 2, 3, expect.any(AbortSignal));
  expect(screen.getByRole('link', { name: 'Back to mission systems' })).toHaveAttribute('href',
    `/workspaces/csp/systems?offeringId=${offering.offeringId}&offeringPage=2&missionPage=3`);
});
