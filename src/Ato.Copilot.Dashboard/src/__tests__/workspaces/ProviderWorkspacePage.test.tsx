import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import ProviderWorkspacePage from '../../features/provider-workspace/ProviderWorkspacePage';
import * as api from '../../features/provider-authorizations/api';
import { offering } from '../provider-authorizations/testData';
import type { OfferingOverviewData } from '../../features/provider-authorizations/types';
import '../helpers/dialog';
vi.mock('../../features/provider-authorizations/ProviderAllocationForm', () => ({
  ProviderAllocationForm: () => <div>Named customer assignment form</div>,
  allocationScopeName: (scope: { resourceId?: string; serviceName?: string; subscriptionId?: string }) => scope.serviceName ?? (scope.resourceId ? scope.resourceId.split('/').pop() : `Subscription ${scope.subscriptionId}`),
}));
vi.mock('../../features/provider-authorizations/ProviderMonitoringPage', () => ({
  default: () => <h1>Service monitoring</h1>,
  ProviderMonitoringPanel: () => <h1>Service monitoring</h1>,
}));
vi.mock('../../features/provider-authorizations/providerMonitoringApi', () => ({
  getProviderMonitoring: vi.fn(async () => ({ sources: [], rules: [], evaluations: [] })),
}));
vi.mock('../../features/workspace-operations/api', async original => ({
  ...await original<typeof import('../../features/workspace-operations/api')>(),
  getDirectoryConnections: vi.fn(async () => []),
  listOrganizations: vi.fn(async () => ({ items: [], total: 0, page: 1, pageSize: 25 })),
}));

vi.mock('../../features/provider-authorizations/api', () => ({
  listOfferings: vi.fn(), getOfferingOverview: vi.fn(), listImpactReviews: vi.fn(), getBoundaryOverview: vi.fn(),
  listFindings: vi.fn(async () => ({ items: [], total: 0, page: 1, pageSize: 25 })), listFindingEvidence: vi.fn(),
  authorizationHref: (id?: string, part?: string) => `/authorizations${id ? `/offerings/${id}` : ''}${part ? `/${part}` : ''}`,
}));
vi.mock('../../components/layout/PageLayout', () => ({
  default: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => session,
}));
let session: { target: { kind: string }; workspace: { permissions: { canAccessCsp: boolean; canManageMemberships: boolean }; displayName: string; roles: string[] } };

const overview: OfferingOverviewData = {
  offeringId: offering.offeringId, offeringRevision: 1,
  customerActionCount: 7,
  authorizations: { items: [], total: 0, page: 1, pageSize: 25, recorded: 0, unconfirmed: 0, rejected: 0 },
  packages: { items: [], total: 0, page: 1, pageSize: 25, needsAttention: 1, processing: 0, awaitingReview: 2, preferredAuthorizationReview: null },
  capabilities: { proposed: 3, awaitingReview: 2, awaitingApproval: 1, published: 4, archived: 0 },
  hosting: { name: 'Demo hosting', configured: true, scopeCount: 2, assignmentCount: 2, associatedSystemCount: 1 },
};

beforeEach(() => {
  vi.clearAllMocks();
  session = { target: { kind: 'csp' }, workspace: { permissions: { canAccessCsp: true, canManageMemberships: true }, displayName: 'Demo Provider', roles: ['CSP.Admin'] } };
  vi.mocked(api.listOfferings).mockResolvedValue({ items: [offering], total: 1, page: 1, pageSize: 25 });
  vi.mocked(api.getOfferingOverview).mockResolvedValue(overview);
  vi.mocked(api.listImpactReviews).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 25 });
  vi.mocked(api.getBoundaryOverview).mockResolvedValue({ offeringId: offering.offeringId, offeringRevision: 1,
    capabilities: { items: [], total: 0, page: 1, pageSize: 25, published: 0, awaitingReview: 0 },
    missionSystems: { items: [], total: 0, page: 1, pageSize: 25 } });
});

describe('provider task workspace', () => {
  it('opens an assignment dialog and searches customer relationships without leaving the selected offering', async () => {
    // Arrange
    vi.mocked(api.getBoundaryOverview).mockResolvedValue({
      offeringId: offering.offeringId, offeringRevision: 1,
      capabilities: { items: [], page: 1, pageSize: 25, total: 0, published: 0, awaitingReview: 0 },
      missionSystems: { items: [{ assignmentId: 'a', systemId: 'system-a', systemName: 'Harbor Logistics',
        targetTenantName: 'Maritime Operations', relationshipState: 'Undetermined', associated: false,
        adoptedCapabilityCount: 0, assignedScopes: [] }], page: 1, pageSize: 25, total: 1 },
    });
    render(<MemoryRouter><ProviderWorkspacePage view="missions" /></MemoryRouter>);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Assign service scope' }));
    // Assert
    expect(screen.getByRole('dialog', { name: 'Assign service scope' })).toHaveTextContent('Named customer assignment form');
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    // Act
    fireEvent.change(screen.getByRole('searchbox', { name: 'Search mission system or organization' }), { target: { value: 'Maritime' } });
    // Assert
    expect(await screen.findByText('Harbor Logistics')).toBeVisible();
    expect(screen.getByRole('columnheader', { name: 'Service / scope' })).toBeVisible();
    expect(screen.getByRole('columnheader', { name: 'Capability release' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Preview Systems handoff' }));
    expect(screen.getByRole('dialog', { name: 'Preview Systems handoff' })).toHaveTextContent('read-only');
  });
  it('renders the mock overview from retained offering records, not sample counts', async () => {
    // Arrange
    render(<MemoryRouter><ProviderWorkspacePage view="overview" /></MemoryRouter>);
    // Act
    await screen.findByRole('heading', { name: 'Focus for today' });
    // Assert
    expect(screen.getByRole('heading', { name: 'Your provider workspace' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Published release' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Source status' })).toBeInTheDocument();
    expect(screen.getByLabelText('Published capabilities')).toHaveTextContent('4');
    expect(screen.getByLabelText('Customer actions')).toHaveTextContent('7');
    expect(screen.getByRole('link', { name: `Review ${offering.name}` })).toHaveAttribute('href', `/authorizations/offerings/${offering.offeringId}/packages`);
    expect(screen.queryByText('Harbor Logistics')).not.toBeInTheDocument();
    expect(screen.queryByText('Release 1.2 live')).not.toBeInTheDocument();
  });

  it('shows a failed summary explicitly and retries rather than reporting zero', async () => {
    // Arrange
    vi.mocked(api.getOfferingOverview).mockRejectedValueOnce(new Error('Provider summary unavailable'));
    render(<MemoryRouter><ProviderWorkspacePage view="overview" /></MemoryRouter>);
    // Act
    expect(await screen.findByRole('alert')).toHaveTextContent('Provider summary unavailable');
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    // Assert
    expect(await screen.findByLabelText('Published capabilities')).toHaveTextContent('4');
  });

  it('keeps unavailable customer action counts distinct from zero', async () => {
    // Arrange
    vi.mocked(api.getOfferingOverview).mockResolvedValue({ ...overview, customerActionCount: null });
    // Act
    render(<MemoryRouter><ProviderWorkspacePage view="overview" /></MemoryRouter>);
    // Assert
    expect(await screen.findByLabelText('Customer actions')).toHaveTextContent('Unavailable');
  });

  it('does not request provider records from an organization workspace', () => {
    // Arrange
    session.target.kind = 'organization';
    // Act
    render(<MemoryRouter><ProviderWorkspacePage view="changes" /></MemoryRouter>);
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Provider workspace access is required');
    expect(api.listOfferings).not.toHaveBeenCalled();
  });

  it('keeps empty offerings distinct from unavailable service data', async () => {
    // Arrange
    vi.mocked(api.listOfferings).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 25 });
    // Act
    render(<MemoryRouter><ProviderWorkspacePage view="overview" /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('heading', { name: 'No service offerings recorded' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Create offering' })).toHaveAttribute('href', '/authorizations/create');
    expect(api.getOfferingOverview).not.toHaveBeenCalled();
    expect(screen.queryByLabelText('Published capabilities')).not.toBeInTheDocument();
  });

  it('gates denied provider identities before making requests', () => {
    // Arrange
    session.workspace.permissions.canAccessCsp = false;
    // Act
    render(<MemoryRouter><ProviderWorkspacePage view="administration" /></MemoryRouter>);
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Provider workspace access is required');
    expect(api.listOfferings).not.toHaveBeenCalled();
  });

  it('uses administration links without inventing identity grants or cloud connection status', async () => {
    // Arrange / Act
    render(<MemoryRouter><ProviderWorkspacePage view="administration" /></MemoryRouter>);
    // Assert
    expect(screen.getByRole('link', { name: 'Manage organizations' })).toHaveAttribute('href', '/organizations');
    expect(screen.getByRole('button', { name: 'Review provider profile' })).toBeEnabled();
    expect(api.listOfferings).not.toHaveBeenCalled();
    expect(screen.queryByText('Connected')).not.toBeInTheDocument();
  });

  it('routes provider monitoring through its owning workspace without a mission impersonation', async () => {
    // Arrange / Act
    render(<MemoryRouter initialEntries={['/provider-changes?tab=monitoring']}><ProviderWorkspacePage view="changes" /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('heading', { name: 'Service monitoring' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Change queue' })).toHaveAttribute('href', `/provider-changes?offeringId=${offering.offeringId}`);
    expect(api.listImpactReviews).not.toHaveBeenCalled();
  });

  it('loads reviewed changes using retained offering scope', async () => {
    // Arrange
    vi.mocked(api.listImpactReviews).mockResolvedValue({
      items: [{ reviewId: 'review-1', revision: 1, disposition: 'PendingReview', reviewedBy: null, reviewedAt: null, contextSnapshotHash: 'context-1', stale: true, title: 'Retention update' }],
      total: 1, page: 1, pageSize: 25,
    });
    // Act
    render(<MemoryRouter><ProviderWorkspacePage view="changes" /></MemoryRouter>);
    // Assert
    const link = await screen.findByRole('link', { name: 'Review Retention update' });
    expect(link).toHaveAttribute('href', `/authorizations/offerings/${offering.offeringId}/impact?reviewId=review-1&returnTo=changes`);
    expect(screen.getByText('New assessment required')).toBeInTheDocument();
  });

  it('shows actual service associations without treating allocation as adoption', async () => {
    // Arrange
    vi.mocked(api.getBoundaryOverview).mockResolvedValue({
      offeringId: offering.offeringId, offeringRevision: 1,
      capabilities: { items: [], total: 0, page: 1, pageSize: 25, awaitingReview: 0, published: 0 },
      missionSystems: { items: [{
        assignmentId: 'assignment-a', systemId: 'system-a', systemName: 'DEMO Mission',
        relationshipState: 'Pending', associated: false, adoptedCapabilityCount: 0, assignedScopes: [],
      }], total: 1, page: 1, pageSize: 25 },
    });
    // Act
    render(<MemoryRouter><ProviderWorkspacePage view="missions" /></MemoryRouter>);
    // Assert
    expect(await screen.findByText('DEMO Mission')).toBeInTheDocument();
    expect(screen.getByText('Awaiting MO')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Assign service scope' })).toBeEnabled();
    expect(screen.getByRole('link', { name: 'Cross-organization oversight' })).toHaveAttribute('href', '/provider-oversight/systems');
  });

  it('changes selected offering without reusing the prior service metrics', async () => {
    // Arrange
    const second = { ...offering, offeringId: 'offering-b', name: 'Second service' };
    vi.mocked(api.listOfferings).mockResolvedValue({ items: [offering, second], total: 2, page: 1, pageSize: 25 });
    vi.mocked(api.getOfferingOverview).mockImplementation(async id => ({
      ...overview, offeringId: id, capabilities: { ...overview.capabilities, published: id === second.offeringId ? 9 : 4 },
    }));
    render(<MemoryRouter><ProviderWorkspacePage view="overview" /></MemoryRouter>);
    await screen.findByLabelText('Published capabilities');
    // Act
    fireEvent.change(screen.getByRole('combobox', { name: 'Service offering' }), { target: { value: second.offeringId } });
    // Assert
    expect(await screen.findByLabelText('Published capabilities')).toHaveTextContent('9');
    expect(screen.getByRole('link', { name: 'Review Second service' })).toHaveAttribute('href', '/authorizations/offerings/offering-b/packages');
  });

  it('restores the selected offering and both listing pages after relationship navigation', async () => {
    // Arrange
    const selected = { ...offering, offeringId: 'offering-b', name: 'Selected service' };
    vi.mocked(api.listOfferings).mockResolvedValue({ items: [selected], page: 2, pageSize: 25, total: 26 });
    vi.mocked(api.getBoundaryOverview).mockResolvedValue({
      offeringId: selected.offeringId, offeringRevision: 1,
      capabilities: { items: [], total: 0, page: 1, pageSize: 10, awaitingReview: 0, published: 0 },
      missionSystems: { items: [{ assignmentId: 'assignment-b', systemId: 'system-b', systemName: 'Selected mission',
        relationshipState: 'ReviewRequired', associated: true, adoptedCapabilityCount: 1, assignedScopes: [] }],
        page: 3, pageSize: 10, total: 21 },
    });
    // Act
    render(<MemoryRouter initialEntries={['/systems?offeringId=offering-b&offeringPage=2&missionPage=3']}>
      <ProviderWorkspacePage view="missions" />
    </MemoryRouter>);
    // Assert
    await screen.findByText('Selected mission');
    expect(screen.getByRole('combobox', { name: 'Service offering' })).toHaveValue('offering-b');
    await waitFor(() => expect(api.listOfferings).toHaveBeenCalledWith(2, '', expect.any(AbortSignal)));
    expect(api.getBoundaryOverview).toHaveBeenCalledWith('offering-b', 1, 3, expect.any(AbortSignal));
    expect(screen.getByRole('link', { name: 'View relationship for Selected mission' })).toHaveAttribute('href',
      '/authorizations/offerings/offering-b/missions/assignment-b?missionPage=3&offeringPage=2');
  });

  it('shows retained scopes and relationship state for an associated mission', async () => {
    // Arrange
    vi.mocked(api.getBoundaryOverview).mockResolvedValue({
      offeringId: offering.offeringId, offeringRevision: 1,
      capabilities: { items: [], total: 0, page: 1, pageSize: 25, awaitingReview: 0, published: 0 },
      missionSystems: { items: [{
        assignmentId: 'assignment-a', systemId: 'system-a', systemName: null, relationshipState: 'SeparateBoundary',
        associated: true, adoptedCapabilityCount: 2,
        assignedScopes: [{ cloud: 'AzureCloud', directoryTenantId: 'directory-a', subscriptionId: 'subscription-a', resourceId: '/subscriptions/subscription-a/resourceGroups/demo' },
          { cloud: 'AzureCloud', directoryTenantId: 'directory-a', subscriptionId: 'subscription-b', resourceId: '' }],
      }], total: 1, page: 1, pageSize: 25 },
    });
    // Act
    render(<MemoryRouter><ProviderWorkspacePage view="missions" /></MemoryRouter>);
    // Assert
    expect(await screen.findByTitle('Recorded relationship state: SeparateBoundary')).toHaveTextContent('Associated');
    expect(screen.getByText('system-a')).toBeInTheDocument();
    expect(screen.getByText('demo, Subscription subscription-b')).toBeInTheDocument();
    expect(screen.getByText('2 adopted capabilities · Exact releases unavailable')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'View relationship for system-a' })).toHaveAttribute('href',
      `/authorizations/offerings/${offering.offeringId}/missions/assignment-a?missionPage=1&offeringPage=1`);
  });
});
