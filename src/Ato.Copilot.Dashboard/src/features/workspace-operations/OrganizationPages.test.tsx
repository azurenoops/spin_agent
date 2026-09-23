import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, useLocation, useNavigate } from 'react-router-dom';
import type { ReactNode } from 'react';
import { Organizations, OrganizationDetailView } from './OrganizationPages';
import { WorkspaceNavigationProvider, useLocation as useWorkspaceLocation } from '../workspaces/workspaceNavigation';
import { startImpersonation } from '../tenancy/api';
import * as api from './api';
import type { OrganizationCatalogItem, OrganizationDetail } from './types';

vi.mock('./api', () => ({
  listOrganizations: vi.fn(),
  getOrganization: vi.fn(),
  getCurrentOrganizationProvisioning: vi.fn(),
}));
vi.mock('../tenancy/api', () => ({ startImpersonation: vi.fn() }));
vi.mock('../../hooks/useOrganizationContext', () => ({
  useOrganizationContext: () => ({ displayName: null }),
}));
vi.mock('../../components/layout/PageLayout', () => ({
  default: ({ children }: { children: ReactNode }) => <main>{children}</main>,
}));

const session = {
  target: { kind: 'csp' },
  workspace: { permissions: { canAccessCsp: true, canManageMemberships: true } },
};
vi.mock('../workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => session,
}));

const organizations: OrganizationCatalogItem[] = [
  { id: 'org-1', displayName: 'Coastal Watch', lifecycle: 'Active', onboarding: 'Active',
    reviewState: 'Pending', systemCount: 2, distinctAdoptionCount: 3 },
  { id: 'org-2', displayName: 'Logistics Office', lifecycle: 'Active', onboarding: 'InWizard',
    reviewState: 'NotRequired', systemCount: 0, distinctAdoptionCount: 0 },
];
const detail: OrganizationDetail = {
  id: 'org-1', displayName: 'Coastal Watch', lifecycle: 'Active', onboarding: 'Pending',
  systems: [{ id: 'sys-1', name: 'Watch operations', rmfPhase: 'Assess', isActive: true }],
  subscriptions: [{ id: 'sub-1', systemId: 'sys-1', capabilityId: 'capability-1',
    sourceRevision: 'release-3', isActive: true }],
  activity: [{ action: 'Provider release delivered', occurredAt: '2026-09-23T12:00:00Z', outcome: 'Success' }],
};

function HistoryControls() {
  const location = useLocation();
  const navigate = useNavigate();
  return <><button onClick={() => navigate(-1)}>History back</button>
    <button onClick={() => navigate('/organizations/org-2')}>Change organization</button>
    <output aria-label="Current route">{location.pathname}{location.search}</output></>;
}

function Subject() {
  const location = useWorkspaceLocation();
  const tenantId = location.pathname.split('/')[2];
  return tenantId ? <OrganizationDetailView tenantId={tenantId} /> : <Organizations />;
}

function page(route = '/organizations', entries = [route], scoped = false) {
  const content = <><HistoryControls /><Subject /></>;
  return render(<MemoryRouter initialEntries={entries} initialIndex={entries.length - 1}>
    {scoped ? <WorkspaceNavigationProvider workspace={{ kind: 'csp' }}>{content}</WorkspaceNavigationProvider> : content}
  </MemoryRouter>);
}

beforeEach(() => {
  vi.resetAllMocks();
  session.target.kind = 'csp';
  session.workspace.permissions.canAccessCsp = true;
  session.workspace.permissions.canManageMemberships = true;
  vi.mocked(api.listOrganizations).mockResolvedValue({
    items: organizations, page: 1, pageSize: 25, total: 42, aggregateState: 'Available',
  });
  vi.mocked(api.getOrganization).mockResolvedValue(detail);
  vi.mocked(api.getCurrentOrganizationProvisioning).mockResolvedValue(null);
});

describe('provider organization mock-aligned pages', () => {
  it('returns unfinished setup from the organization list without creating anything', async () => {
    // Arrange
    vi.mocked(api.listOrganizations).mockResolvedValue({
      items: [{ ...organizations[0]!, setupState: 'Pending', memberCount: 0 }], page: 1, pageSize: 25, total: 1,
    });
    // Act
    page();
    // Assert
    expect(await screen.findByText('Enrollment pending')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Resume setup' })).toHaveAttribute('href', '/organizations/org-1/provisioning');
  });

  it('shows saved organization details and permitted next steps without opening customer scope', async () => {
    // Arrange
    vi.mocked(api.getOrganization).mockResolvedValue({
      ...detail, legalEntityName: 'Coastal Directorate', primaryPocName: 'Contact Person',
      primaryPocEmail: 'contact@example.mil', memberCount: 1, setupState: 'Completed',
    });
    // Act
    page('/organizations/org-1');
    // Assert
    expect(await screen.findByText('Coastal Directorate')).toBeInTheDocument();
    expect(screen.getByText('contact@example.mil')).toBeInTheDocument();
    expect(screen.getByText('Setup complete')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Manage members' })).toHaveAttribute('href', '/organizations/org-1/memberships');
    expect(screen.getByRole('link', { name: 'View provider catalog' })).toHaveAttribute('href', '/security-capabilities');
    expect(startImpersonation).not.toHaveBeenCalled();
  });

  it('does not offer membership mutation without its permission', async () => {
    // Arrange
    session.workspace.permissions.canManageMemberships = false;
    // Act
    page('/organizations/org-1');
    // Assert
    await screen.findByRole('heading', { name: 'Organization setup' });
    expect(screen.queryByRole('link', { name: 'Manage members' })).not.toBeInTheDocument();
  });

  it('renders the five-column organization table instead of per-row support actions', async () => {
    // Arrange
    page();
    // Act
    const table = await screen.findByRole('table', { name: 'Organizations' });
    // Assert
    expect(within(table).getAllByRole('columnheader').map(cell => cell.textContent)).toEqual([
      'Organization', 'Account', 'Systems', 'Provider adoption', 'Action',
    ]);
    const row = within(table).getByRole('row', { name: /Coastal Watch/ });
    expect(within(row).getByText('CW')).toBeInTheDocument();
    expect(within(row).getByText('org-1')).toBeInTheDocument();
    expect(within(row).getByText('2 registered')).toBeInTheDocument();
    expect(within(row).getByText('3 capabilities')).toBeInTheDocument();
    expect(within(row).getByText('Awaiting source review')).toBeInTheDocument();
    expect(within(row).getByRole('link', { name: 'View organization' })).toHaveAttribute('href', '/organizations/org-1');
    expect(screen.queryByRole('button', { name: /support/i })).not.toBeInTheDocument();
    expect(startImpersonation).not.toHaveBeenCalled();
    expect(screen.getByRole('link', { name: 'Add organization' })).toHaveAttribute('href', '/organizations/new');
  });

  it('distinguishes server totals from page-scoped metrics and never invents system review counts', async () => {
    // Arrange
    page();
    // Act
    const summary = await screen.findByRole('region', { name: 'Organization summary' });
    // Assert
    expect(within(summary).getByLabelText('Organizations total')).toHaveTextContent('42');
    expect(within(summary).getByLabelText('Active on this page')).toHaveTextContent('2');
    expect(within(summary).getByLabelText('Awaiting source review on this page')).toHaveTextContent('1');
    expect(within(summary).getByLabelText('Onboarding on this page')).toHaveTextContent('1');
    expect(screen.queryByText(/systems need review/)).not.toBeInTheDocument();
    expect(screen.getByText('Account status is separate from system authorization.')).toBeInTheDocument();
  });

  it('shows unavailable adoption and partial total states without presenting unknown counts as zero', async () => {
    // Arrange
    vi.mocked(api.listOrganizations).mockResolvedValue({
      items: [{ ...organizations[0]!, distinctAdoptionCount: null }],
      page: 1, pageSize: 25, total: 42, aggregateState: 'Partial',
    });
    page();
    // Act
    await screen.findByRole('alert');
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent(/unavailable or partial.*Partial/);
    expect(screen.getByLabelText('Organizations total')).toHaveTextContent('Unavailable');
    expect(screen.getByText('Adoption unavailable')).toBeInTheDocument();
    expect(screen.queryByText('0 capabilities')).not.toBeInTheDocument();
  });

  it('retains lifecycle, onboarding and review bookmarks while search resets paging', async () => {
    // Arrange
    page('/organizations?search=Watch&lifecycle=Active&onboarding=InWizard&review=Pending&page=2');
    await screen.findByRole('table', { name: 'Organizations' });
    // Act
    fireEvent.change(screen.getByLabelText('Search'), { target: { value: ' Logistics ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }));
    // Assert
    await waitFor(() => expect(api.listOrganizations).toHaveBeenLastCalledWith({
      page: 1, pageSize: 25, search: 'Logistics', lifecycle: 'Active', onboarding: 'InWizard', review: 'Pending',
    }, expect.any(AbortSignal)));
    expect(screen.getByLabelText('Current route')).toHaveTextContent('page=1');
    expect(screen.getByLabelText('onboarding')).toHaveValue('InWizard');
    expect(screen.getByLabelText('review')).toHaveValue('Pending');
  });

  it('synchronizes legacy filter values and search with browser history', async () => {
    // Arrange
    page('/organizations?search=second&lifecycle=Active', [
      '/organizations?search=first&lifecycle=Draft&onboarding=Pending&review=awaitingreview',
      '/organizations?search=second&lifecycle=Active',
    ]);
    await screen.findByRole('table', { name: 'Organizations' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'History back' }));
    // Assert
    await waitFor(() => expect(screen.getByLabelText('Search')).toHaveValue('first'));
    expect(screen.getByLabelText('lifecycle')).toHaveValue('Draft');
    expect(screen.getByLabelText('review')).toHaveValue('awaitingreview');
    expect(api.listOrganizations).toHaveBeenLastCalledWith(expect.objectContaining({
      page: 1, search: 'first', lifecycle: 'Draft', onboarding: 'Pending', review: 'awaitingreview',
    }), expect.any(AbortSignal));
  });

  it('pages server data without dropping filters', async () => {
    // Arrange
    page('/organizations?lifecycle=Suspended&review=Completed');
    await screen.findByRole('table', { name: 'Organizations' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    // Assert
    await waitFor(() => expect(api.listOrganizations).toHaveBeenLastCalledWith(expect.objectContaining({
      page: 2, lifecycle: 'Suspended', review: 'Completed',
    }), expect.any(AbortSignal)));
  });

  it('keeps all navigation in the current provider workspace', async () => {
    // Arrange
    page('/workspaces/csp/organizations', undefined, true);
    // Act
    await screen.findByRole('table', { name: 'Organizations' });
    // Assert
    expect(screen.getByRole('link', { name: 'Coastal Watch' })).toHaveAttribute('href', '/workspaces/csp/organizations/org-1');
    expect(screen.getByRole('link', { name: 'Add organization' })).toHaveAttribute('href', '/workspaces/csp/organizations/new');
  });

  it('shows loading before data and retries a failed collection request', async () => {
    // Arrange
    let reject!: (error: Error) => void;
    vi.mocked(api.listOrganizations).mockImplementationOnce(() => new Promise((_resolve, failure) => { reject = failure; }));
    page();
    expect(screen.getByText('Loading workspace data…')).toHaveAttribute('role', 'status');
    expect(screen.queryByRole('region', { name: 'Organization summary' })).not.toBeInTheDocument();
    // Act
    await act(async () => reject(new Error('Organization data unavailable')));
    expect(screen.getByRole('alert')).toHaveTextContent('Organization data unavailable');
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    // Assert
    expect(await screen.findByRole('table', { name: 'Organizations' })).toBeInTheDocument();
    expect(api.listOrganizations).toHaveBeenCalledTimes(2);
  });

  it('distinguishes a filtered empty result from an unavailable collection', async () => {
    // Arrange
    vi.mocked(api.listOrganizations).mockResolvedValue({ items: [], page: 1, pageSize: 25, total: 0, aggregateState: 'Available' });
    page('/organizations?search=missing');
    // Act
    await screen.findByText('No organizations match the current filters.');
    // Assert
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Organizations total')).toHaveTextContent('0');
  });

  it('renders overview systems and profile/support cards without fabricating authorization or review decisions', async () => {
    // Arrange
    page('/organizations/org-1');
    // Act
    await screen.findByRole('heading', { name: 'Coastal Watch' });
    // Assert
    expect(screen.getByRole('heading', { name: 'Systems' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Provider changes to review' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Organization profile' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Support access' })).toBeInTheDocument();
    const systems = screen.getByRole('table', { name: 'Organization systems' });
    expect(within(systems).getByRole('columnheader', { name: 'System authorization' })).toBeInTheDocument();
    expect(within(systems).getByText('Watch operations')).toBeInTheDocument();
    expect(within(systems).getByText('Assess')).toBeInTheDocument();
    expect(within(systems).getByText('Not provided')).toBeInTheDocument();
    expect(screen.getByText('Review details unavailable')).toBeInTheDocument();
    expect(screen.queryByText('No pending reviews')).not.toBeInTheDocument();
    expect(screen.queryByText(/Administrator assignment pending/)).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'All organizations' })).toHaveAttribute('href', '/organizations');
  });

  it('uses three underline tabs with keyboard navigation and URL state', async () => {
    // Arrange
    page('/organizations/org-1');
    await screen.findByRole('heading', { name: 'Coastal Watch' });
    const overview = screen.getByRole('tab', { name: 'Overview & systems' });
    expect(screen.getAllByRole('tab')).toHaveLength(3);
    // Act
    overview.focus();
    fireEvent.keyDown(overview, { key: 'ArrowRight' });
    // Assert
    const subscriptions = screen.getByRole('tab', { name: 'Provider subscriptions' });
    expect(subscriptions).toHaveFocus();
    expect(subscriptions).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByLabelText('Current route')).toHaveTextContent('tab=subscriptions');
    expect(screen.getByRole('tabpanel')).toHaveAccessibleName('Provider subscriptions');
    expect(screen.getByText('release-3')).toBeInTheDocument();
    expect(screen.getByText('Watch operations')).toBeInTheDocument();
    fireEvent.keyDown(subscriptions, { key: 'End' });
    expect(screen.getByRole('tab', { name: 'Provider activity' })).toHaveFocus();
    expect(screen.getByRole('tabpanel')).toHaveAccessibleName('Provider activity');
    expect(screen.getByText('Provider release delivered')).toBeInTheDocument();
    expect(screen.getByText(/latest.*50/i)).toBeInTheDocument();
    fireEvent.keyDown(screen.getByRole('tab', { name: 'Provider activity' }), { key: 'Home' });
    expect(overview).toHaveFocus();
    expect(screen.getByRole('tabpanel')).toHaveAccessibleName('Overview & systems');
  });

  it.each(['systems', 'unexpected'])('resolves the %s bookmark to overview and systems', async tab => {
    // Arrange
    page(`/organizations/org-1?tab=${tab}`);
    // Act
    await screen.findByRole('heading', { name: 'Systems' });
    // Assert
    expect(screen.getByRole('tab', { name: 'Overview & systems' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByText('Watch operations')).toBeInTheDocument();
  });

  it('retains an existing enrollment operation and its exact recovery key', async () => {
    // Arrange
    vi.mocked(api.getCurrentOrganizationProvisioning).mockResolvedValue({
      operationId: 'operation-1', tenantId: 'org-1', tenantState: 'Created',
      administratorState: 'Pending', membershipState: 'Pending', lastError: null,
      idempotencyKey: 'enroll +/key',
    });
    page('/organizations/org-1');
    // Act
    const enrollment = await screen.findByRole('link', { name: 'Enrollment status' });
    // Assert
    expect(enrollment).toHaveAttribute('href', '/organizations/org-1/provisioning?key=enroll%20%2B%2Fkey');
    expect(screen.queryByRole('link', { name: 'Start enrollment' })).not.toBeInTheDocument();
    expect(api.getCurrentOrganizationProvisioning).toHaveBeenCalledWith('org-1', expect.any(AbortSignal));
  });

  it('does not offer fresh enrollment while the existing operation cannot be loaded', async () => {
    // Arrange
    vi.mocked(api.getCurrentOrganizationProvisioning).mockRejectedValue(new Error('Enrollment service unavailable'));
    page('/organizations/org-1');
    // Act
    await screen.findByRole('alert');
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Enrollment service unavailable');
    expect(screen.queryByRole('link', { name: 'Start enrollment' })).not.toBeInTheDocument();
  });

  it('allows a fresh enrollment only after confirming no current operation exists', async () => {
    // Arrange
    page('/organizations/org-1');
    // Act
    const link = await screen.findByRole('link', { name: 'Start enrollment' });
    // Assert
    expect(link).toHaveAttribute('href', '/organizations/org-1/provisioning');
  });

  it('does not replace an existing enrollment when its stable recovery key is missing', async () => {
    // Arrange
    vi.mocked(api.getCurrentOrganizationProvisioning).mockResolvedValue({
      operationId: 'operation-1', tenantId: 'org-1', tenantState: 'Created',
      administratorState: 'Pending', membershipState: 'Pending', lastError: null,
    });
    page('/organizations/org-1');
    // Act
    await screen.findByRole('heading', { name: 'Coastal Watch' });
    // Assert
    expect(screen.queryByRole('link', { name: 'Start enrollment' })).not.toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('The current enrollment operation has no stable recovery key.');
  });

  it('uses light surfaces and preserves the shared gradient hero', async () => {
    // Arrange
    page('/organizations/org-1');
    // Act
    const heading = await screen.findByRole('heading', { name: 'Coastal Watch' });
    // Assert
    expect(heading.closest('.bg-gradient-to-r')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Organization profile' }).closest('section')).toHaveClass('bg-white', 'border-slate-200');
    expect(screen.getByRole('heading', { name: 'Systems' }).closest('section')).toHaveClass('bg-white');
  });

  it('requires explicit support consent from the hero without entering customer scope on viewing', async () => {
    // Arrange
    page('/organizations/org-1');
    await screen.findByRole('heading', { name: 'Coastal Watch' });
    expect(startImpersonation).not.toHaveBeenCalled();
    // Act
    fireEvent.click(screen.getAllByRole('button', { name: 'Audited support' })[0]!);
    // Assert
    const dialog = screen.getByRole('dialog', { name: 'Audited support for Coastal Watch' });
    expect(within(dialog).getByRole('button', { name: 'Start audited support' })).toBeDisabled();
    expect(startImpersonation).not.toHaveBeenCalled();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('shows explicit empty states without converting absent authorization and review data into approval', async () => {
    // Arrange
    vi.mocked(api.getOrganization).mockResolvedValue({ ...detail, systems: [], subscriptions: [], activity: [] });
    page('/organizations/org-1');
    // Act
    await screen.findByText('No systems are registered for this organization.');
    // Assert
    expect(screen.getByText('Review details unavailable')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: 'Provider subscriptions' }));
    expect(screen.getByText('No provider subscriptions are available.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: 'Provider activity' }));
    expect(screen.getByText('No provider activity is available.')).toBeInTheDocument();
  });

  it('shows a detail failure and does not expose actions for an unknown organization', async () => {
    // Arrange
    vi.mocked(api.getOrganization).mockRejectedValue(new Error('Organization was not found.'));
    page('/organizations/missing');
    // Act
    await screen.findByRole('alert');
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Organization was not found.');
    expect(screen.queryByRole('button', { name: 'Audited support' })).not.toBeInTheDocument();
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'All organizations' })).toBeInTheDocument();
  });

  it('clears stale organization identity and enrollment when the target changes', async () => {
    // Arrange
    let finish!: (value: OrganizationDetail) => void;
    vi.mocked(api.getOrganization).mockImplementation(tenantId => tenantId === 'org-1'
      ? Promise.resolve(detail) : new Promise(resolve => { finish = resolve; }));
    page('/organizations/org-1');
    await screen.findByRole('heading', { name: 'Coastal Watch' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Change organization' }));
    // Assert
    expect(screen.queryByRole('heading', { name: 'Coastal Watch' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Audited support' })).not.toBeInTheDocument();
    await act(async () => finish({ ...detail, id: 'org-2', displayName: 'Logistics Office' }));
    expect(await screen.findByRole('heading', { name: 'Logistics Office' })).toBeInTheDocument();
  });

  it.each(['/organizations', '/organizations/org-1'])('preserves provider access checks for %s', route => {
    // Arrange
    session.workspace.permissions.canAccessCsp = false;
    // Act
    page(route);
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Provider access is required.');
    expect(api.listOrganizations).not.toHaveBeenCalled();
    expect(api.getOrganization).not.toHaveBeenCalled();
  });
});
