import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, useLocation, useNavigate } from 'react-router-dom';
import type { ReactNode } from 'react';
import WorkspaceOperationsPage from '../../features/workspace-operations/WorkspaceOperationsPage';
import * as api from '../../features/workspace-operations/api';
import * as onboardingApi from '../../features/workspace-operations/organizationOnboardingApi';
import AddOrganizationPage from '../../features/workspace-operations/AddOrganizationPage';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import { emptyOrganization, validateOrganization } from '../../features/workspace-operations/OrganizationSetupPresentation';

vi.mock('../../features/workspace-operations/api', async importOriginal => ({
  ...(await importOriginal<typeof api>()),
  getDirectoryConnections: vi.fn(),
  createOrganization: vi.fn(), getOrganizationCreation: vi.fn(),
  getOrganization: vi.fn(), getOrganizationProvisioning: vi.fn(),
  getCurrentOrganizationProvisioning: vi.fn(), beginOrganizationProvisioning: vi.fn(),
  resumeOrganizationProvisioning: vi.fn(),
  listOrganizations: vi.fn(),
  searchDirectoryUsers: vi.fn(),
}));
vi.mock('../../features/workspace-operations/organizationOnboardingApi', () => ({
  saveOrganizationDraft: vi.fn(), getOrganizationDraft: vi.fn(), confirmOrganizationDraft: vi.fn(),
  getOrganizationSetupSummary: vi.fn(async (tenantId: string) => ({
    tenant: { id: tenantId, displayName: 'Mission Operations', lifecycle: 'Active', onboardingState: 'Pending' },
    liveAccess: { state: 'Missing', activeMemberCount: 0, administrators: { items: [], page: 1, pageSize: 25, total: 0 } },
    requestedOperation: null, reconciliation: 'None',
    actorActions: { canManageMemberships: true, canResumeEnrollment: true, canEnterOrganization: false },
  })),
}));
const permissions = { canAccessCsp: true, canManageMemberships: true };
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => ({ target: { kind: 'csp' }, workspace: { permissions } }),
}));
vi.mock('../../components/layout/PageLayout', () => ({
  default: ({ children }: { children: ReactNode }) => <main>{children}</main>,
}));
vi.mock('../../components/layout/PageHero', () => ({
  default: ({ title, actions }: { title: string; actions?: ReactNode }) => <header><h1>{title}</h1>{actions}</header>,
}));
vi.mock('../../features/workspaces/SupportWorkspaceButton', () => ({ default: () => <button>Audited support</button> }));

const directory = '11111111-1111-1111-1111-111111111111';
const object = '22222222-2222-2222-2222-222222222222';
const person = '33333333-3333-3333-3333-333333333333';
const pending = {
  tenantId: 'org-new', operationId: 'operation-1', tenantState: 'Completed',
  administratorState: 'Pending', membershipState: 'Pending', personState: 'NotRequested',
  initialAdministrator: null, canEditAdministrator: true, lastError: null, idempotencyKey: 'create-key',
};
const created = {
  tenantId: 'org-new', operationId: 'operation-1', displayName: 'Mission Operations',
  status: 'Active', onboardingState: 'Pending', existing: false,
};
const draftFixture: onboardingApi.OrganizationOnboardingDraft = {
  draftId: 'draft-1', revision: 1, schemaVersion: 1, state: 'Draft', savedAt: '2026-09-30T12:00:00Z',
  displayName: 'Retained organization', currentStep: 'details', creationKey: 'draft-key',
  tenantId: null, operationId: null, resumeUrl: '/organizations/new?draft=draft-1',
  values: { organizationChoice: 'create', displayName: 'Retained organization', administratorChoice: 'deferred' },
};
const liveSummary: onboardingApi.OrganizationSetupSummary = {
  tenant: { id: 'org-new', displayName: 'Existing organization', lifecycle: 'Active', onboardingState: 'Pending' },
  observedAt: '2026-09-30T12:00:00Z', reconciliation: 'Unbound', requestedOperation: null,
  liveAccess: { state: 'Available', activeMemberCount: 1, administrators: { items: [{
    personId: person, displayName: 'Existing admin', membershipId: 'member', directoryTenantId: directory, objectId: object, assignmentId: 'assignment',
  }], page: 1, pageSize: 25, total: 1 } },
  actorActions: { canManageMemberships: true, canResumeEnrollment: false, canEnterOrganization: false },
};
function Route() { const location = useLocation(); return <output aria-label="Route">{location.pathname}{location.search}</output>; }
function OrganizationRoute() { return useLocation().pathname.startsWith('/organizations') ? <WorkspaceOperationsPage /> : null; }
function page(route = '/organizations/new') {
  return render(<MemoryRouter initialEntries={[route]}><Route /><OrganizationRoute /></MemoryRouter>);
}
function details() {
  fireEvent.change(screen.getByLabelText('Organization name'), { target: { value: 'Mission Operations' } });
  fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
}
function defer() {
  details();
  fireEvent.click(screen.getByLabelText('Complete enrollment later'));
  fireEvent.click(screen.getByRole('button', { name: 'Review setup' }));
}
function identity() {
  fireEvent.click(screen.getByRole('button', { name: 'Enter manually' }));
  fireEvent.change(screen.getByLabelText('Directory tenant ID'), { target: { value: directory } });
  fireEvent.change(screen.getByLabelText('User object ID'), { target: { value: object } });
  fireEvent.change(screen.getByLabelText('Administrator name'), { target: { value: 'Separate administrator' } });
  fireEvent.change(screen.getByLabelText('Administrator email'), { target: { value: 'admin@example.mil' } });
}
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.getDirectoryConnections).mockResolvedValue([]);
  vi.mocked(api.listOrganizations).mockResolvedValue({ items: [], page: 1, pageSize: 25, total: 0 });
  permissions.canAccessCsp = true;
  vi.mocked(api.createOrganization).mockResolvedValue(created);
  vi.mocked(api.getOrganizationCreation).mockResolvedValue(null);
  vi.mocked(api.getOrganization).mockResolvedValue({
    id: 'org-new', displayName: 'Mission Operations', lifecycle: 'Active', onboarding: 'Pending',
    systems: [], subscriptions: [], activity: [],
  });
  vi.mocked(api.getOrganizationProvisioning).mockResolvedValue(pending);
  vi.mocked(api.getCurrentOrganizationProvisioning).mockResolvedValue(pending);
  vi.mocked(onboardingApi.saveOrganizationDraft).mockImplementation(async id => ({ ...draftFixture, draftId: id }));
  vi.mocked(onboardingApi.getOrganizationDraft).mockResolvedValue(draftFixture);
});

describe('CSP Add Organization approved flow', () => {
  it('starts a clean draft after deliberately leaving a saved-draft URL', async () => {
    // Arrange
    function NewDraft() { const navigate = useNavigate(); return <button onClick={() => navigate('/organizations/new')}>Start fresh organization</button>; }
    render(<MemoryRouter initialEntries={['/organizations/new?draft=draft-1']}><NewDraft /><AddOrganizationPage /></MemoryRouter>);
    await screen.findByDisplayValue('Retained organization');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Start fresh organization' }));
    // Assert
    expect(screen.getByLabelText('Organization name')).toHaveValue('');
    expect(screen.getByRole('button', { name: 'Continue' })).toBeEnabled();
    expect(screen.queryByText('Server draft saved · revision 1')).not.toBeInTheDocument();
  });
  it('reconciles only an exact same-request uncertain draft save without adopting foreign edits', async () => {
    // Arrange
    vi.mocked(onboardingApi.saveOrganizationDraft).mockRejectedValue(new Error('Response interrupted'));
    vi.mocked(onboardingApi.getOrganizationDraft).mockImplementation(async id => ({
      ...draftFixture, draftId: id, values: vi.mocked(onboardingApi.saveOrganizationDraft).mock.calls[0]![1],
    }));
    page();
    fireEvent.change(screen.getByLabelText('Organization name'), { target: { value: 'Retained exact request' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save & finish later' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Check saved draft' }));
    // Assert
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Check saved draft' })).not.toBeInTheDocument());
    expect(screen.getByLabelText('Organization name')).toHaveValue('Retained exact request');
    expect(screen.getByText('Server draft saved · revision 1')).toBeInTheDocument();
    expect(api.createOrganization).not.toHaveBeenCalled();
  });
  it.each([404, 503])('retains edits when uncertain draft read returns %s', async status => {
    // Arrange
    vi.mocked(onboardingApi.saveOrganizationDraft).mockRejectedValue(new Error('Write response missing'));
    vi.mocked(onboardingApi.getOrganizationDraft).mockRejectedValue(new api.WorkspaceOperationError('Read unavailable', status, 'NOT_FOUND'));
    page();
    fireEvent.change(screen.getByLabelText('Organization name'), { target: { value: 'Keep while uncertain' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save & finish later' }));
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Check saved draft' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(status === 404 ? 'No saved draft was found' : 'Saved outcome is not yet confirmed');
    expect(screen.getByLabelText('Organization name')).toHaveValue('Keep while uncertain');
    expect(api.createOrganization).not.toHaveBeenCalled();
  });
  it('recovers an uncertain confirmation of the same saved intent without repeating creation', async () => {
    // Arrange
    vi.mocked(onboardingApi.getOrganizationDraft).mockResolvedValueOnce({ ...draftFixture, currentStep: 'review' })
      .mockImplementation(async id => ({ ...draftFixture, draftId: id, state: 'Confirmed',
        values: vi.mocked(onboardingApi.saveOrganizationDraft).mock.calls[0]![1], resumeUrl: '/organizations/org-new/provisioning?key=draft-key' }));
    vi.mocked(onboardingApi.confirmOrganizationDraft).mockRejectedValue(new Error('Confirmation response interrupted'));
    page('/organizations/new?draft=draft-1');
    fireEvent.click(await screen.findByRole('button', { name: 'Create organization' }));
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Check saved draft' }));
    // Assert
    await waitFor(() => expect(screen.getByLabelText('Route')).toHaveTextContent('/organizations/org-new/provisioning?key=draft-key'));
    expect(onboardingApi.confirmOrganizationDraft).toHaveBeenCalledOnce();
    expect(api.createOrganization).not.toHaveBeenCalled();
  });
  it('blocks a discarded draft returned while reconciling an interrupted write', async () => {
    // Arrange
    vi.mocked(onboardingApi.saveOrganizationDraft).mockRejectedValue(new Error('Response interrupted'));
    vi.mocked(onboardingApi.getOrganizationDraft).mockImplementation(async id => ({ ...draftFixture, draftId: id, state: 'Discarded' }));
    page();
    fireEvent.click(screen.getByRole('button', { name: 'Save & finish later' }));
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Check saved draft' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('discarded');
    expect(screen.getByRole('button', { name: 'Continue' })).toBeDisabled();
  });
  it('supports Enter submission through the same validation/review commands', async () => {
    // Arrange
    page();
    fireEvent.change(screen.getByLabelText('Organization name'), { target: { value: 'Keyboard organization' } });
    // Act
    fireEvent.submit(screen.getByLabelText('Organization name').closest('form')!);
    fireEvent.click(screen.getByLabelText('Complete enrollment later'));
    fireEvent.submit(screen.getByLabelText('Complete enrollment later').closest('form')!);
    fireEvent.submit(screen.getByRole('button', { name: 'Edit organization details' }).closest('form')!);
    // Assert
    await waitFor(() => expect(api.createOrganization).toHaveBeenCalledOnce());
    expect(vi.mocked(api.createOrganization).mock.calls[0]![0].displayName).toBe('Keyboard organization');
  });
  it('distinguishes failure to start enrollment from a created enrollment operation', async () => {
    // Arrange
    vi.mocked(api.getCurrentOrganizationProvisioning).mockResolvedValue(null);
    vi.mocked(api.beginOrganizationProvisioning).mockRejectedValue(new Error('Start response unavailable'));
    page('/organizations/org-new/provisioning');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Start enrollment' }));
    // Assert
    expect(await screen.findByText('The enrollment start outcome is uncertain. Reload saved status before retrying.')).toBeInTheDocument();
    expect(api.createOrganization).not.toHaveBeenCalled();
  });
  it('reports failed saved-state reload after enrollment failure instead of displaying success', async () => {
    // Arrange
    vi.mocked(api.getOrganizationProvisioning).mockResolvedValueOnce({ ...pending, initialAdministrator: { directoryTenantId: directory, objectId: object, personId: person } })
      .mockRejectedValue(new Error('Saved state unavailable'));
    vi.mocked(api.resumeOrganizationProvisioning).mockRejectedValue(new Error('Enrollment failed'));
    page('/organizations/org-new/provisioning?key=stable-key');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Continue enrollment' }));
    // Assert
    expect(await screen.findByText('Cannot confirm saved setup outcomes: Saved state unavailable')).toBeInTheDocument();
    expect(screen.queryByText('Organization setup complete')).not.toBeInTheDocument();
  });
  it('exposes customer navigation only when summary proves the current actor can enter', async () => {
    // Arrange
    vi.mocked(onboardingApi.getOrganizationSetupSummary).mockResolvedValue({ ...liveSummary, actorActions: {
      ...liveSummary.actorActions, canEnterOrganization: true,
    } });
    // Act
    page('/organizations/org-new/provisioning');
    // Assert
    expect(await screen.findByRole('link', { name: 'Choose authorized organization workspace' })).toHaveAttribute('href', '/workspaces/organizations/org-new');
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Back' })); });
    expect(screen.getByLabelText('Route')).toHaveTextContent('/organizations');
  });
  it('distinguishes missing creation from failing creation-status reads on a recovery link', async () => {
    // Arrange
    page('/organizations/new?key=missing-key');
    expect(await screen.findByText(/No saved organization was found for this request yet/)).toBeInTheDocument();
    vi.mocked(api.getOrganizationCreation).mockRejectedValue(new Error('Creation status unavailable'));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Check creation status' }));
    // Assert
    expect(await screen.findByText('Creation status unavailable')).toBeInTheDocument();
    expect(api.createOrganization).not.toHaveBeenCalled();
  });
  it('does not treat a different keyed provisioning error as enrollment-not-started', async () => {
    // Arrange
    vi.mocked(api.getOrganizationProvisioning).mockRejectedValue(
      new api.WorkspaceOperationError('Organization access unavailable', 404, 'NOT_FOUND'));
    // Act
    page('/organizations/org-new/provisioning?key=foreign-key');
    // Assert
    expect(await screen.findByText('Organization access unavailable')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Start enrollment' })).not.toBeInTheDocument();
  });
  it('reloads a keyless saved enrollment after failure without starting another operation', async () => {
    // Arrange
    vi.mocked(api.getCurrentOrganizationProvisioning).mockResolvedValue({
      ...pending, idempotencyKey: '', initialAdministrator: { directoryTenantId: directory, objectId: object, personId: person },
    });
    vi.mocked(api.resumeOrganizationProvisioning).mockRejectedValue(new Error('Enrollment response lost'));
    page('/organizations/org-new/provisioning');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Continue enrollment' }));
    // Assert
    await waitFor(() => expect(api.getCurrentOrganizationProvisioning).toHaveBeenCalledTimes(2));
    expect(api.beginOrganizationProvisioning).not.toHaveBeenCalled();
  });
  it('searches and explicitly reuses the existing administrator without another organization create', async () => {
    // Arrange
    vi.mocked(api.listOrganizations).mockResolvedValue({ items: [{ id: 'org-new', displayName: 'Existing organization',
      lifecycle: 'Active', onboarding: 'Pending', reviewState: 'NotRequired', systemCount: 0, distinctAdoptionCount: 0 }], page: 1, pageSize: 25, total: 1 });
    vi.mocked(onboardingApi.getOrganizationSetupSummary).mockResolvedValue(liveSummary);
    vi.mocked(onboardingApi.confirmOrganizationDraft).mockImplementation(async id => ({ ...draftFixture, draftId: id,
      state: 'Confirmed', tenantId: 'org-new', resumeUrl: '/organizations/org-new/provisioning' }));
    page();
    // Act
    fireEvent.change(screen.getByLabelText('Find an authorized organization'), { target: { value: 'Existing' } });
    fireEvent.click(screen.getByRole('button', { name: 'Search organizations' }));
    fireEvent.click(await screen.findByRole('button', { name: /Existing organization · Use existing organization/ }));
    await waitFor(() => expect(onboardingApi.getOrganizationSetupSummary).toHaveBeenCalledWith('org-new', null, expect.any(AbortSignal)));
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
    fireEvent.click(await screen.findByLabelText('Use the existing administrator'));
    fireEvent.click(screen.getByRole('button', { name: 'Review setup' }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm organization reuse' }));
    // Assert
    await waitFor(() => expect(onboardingApi.saveOrganizationDraft).toHaveBeenCalledWith(expect.any(String),
      expect.objectContaining({ organizationChoice: 'existing', existingTenantId: 'org-new', administratorChoice: 'existing' }), 'review', 0));
    expect(api.createOrganization).not.toHaveBeenCalled();
    expect(api.resumeOrganizationProvisioning).not.toHaveBeenCalled();
  });
  it('offers existing-admin reuse from directory fallback and preserves deliberate fresh selection', async () => {
    // Arrange
    vi.mocked(onboardingApi.getOrganizationDraft).mockResolvedValue({ ...draftFixture, currentStep: 'administrator',
      values: { ...draftFixture.values, organizationChoice: 'existing', existingTenantId: 'org-new' } });
    vi.mocked(onboardingApi.getOrganizationSetupSummary).mockResolvedValue(liveSummary);
    page('/organizations/new?draft=draft-1');
    await screen.findByText(/Current administrator: Existing admin/);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Directory lookup is unavailable' }));
    fireEvent.click(screen.getByRole('button', { name: 'Use the existing administrator' }));
    expect(screen.getByLabelText('Use the existing administrator')).toBeChecked();
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    fireEvent.click(screen.getByRole('button', { name: 'Create a different organization' }));
    fireEvent.click(screen.getByRole('button', { name: '2 Administrator access' }));
    // Assert
    expect(screen.queryByLabelText('Use the existing administrator')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Directory lookup is unavailable' }));
    fireEvent.click(screen.getByRole('button', { name: 'Return to administrator setup' }));
    fireEvent.click(screen.getByRole('button', { name: 'Directory lookup is unavailable' }));
    fireEvent.click(screen.getByRole('button', { name: 'Complete enrollment later' }));
    expect(screen.getByLabelText('Complete enrollment later')).toBeChecked();
  });
  it('surfaces search and existing-scope lookup failures instead of inventing organization matches', async () => {
    // Arrange
    vi.mocked(api.listOrganizations).mockRejectedValue(new Error('Organization search unavailable'));
    page();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Search organizations' }));
    // Assert
    expect(await screen.findByText('Organization search unavailable')).toBeInTheDocument();
    expect(onboardingApi.confirmOrganizationDraft).not.toHaveBeenCalled();
  });
  it.each([
    ['discarded', { ...draftFixture, state: 'Discarded' as const }, 'This draft was discarded.'],
    ['wrong identity', { ...draftFixture, draftId: 'wrong-draft' }, 'does not match'],
  ])('rejects a %s hydration response without enabling confirmation', async (_name, saved, message) => {
    // Arrange
    vi.mocked(onboardingApi.getOrganizationDraft).mockResolvedValue(saved);
    // Act
    page('/organizations/new?draft=draft-1');
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(message);
    expect(screen.getByRole('button', { name: 'Continue' })).toBeDisabled();
  });
  it('recovers a confirmed draft through its retained destination using reads only', async () => {
    // Arrange
    vi.mocked(onboardingApi.getOrganizationDraft).mockResolvedValue({ ...draftFixture, state: 'Confirmed',
      resumeUrl: '/organizations/org-new/provisioning?key=draft-key' });
    // Act
    page('/organizations/new?draft=draft-1');
    // Assert
    await waitFor(() => expect(screen.getByLabelText('Route')).toHaveTextContent('/organizations/org-new/provisioning?key=draft-key'));
    expect(api.createOrganization).not.toHaveBeenCalled();
    expect(api.resumeOrganizationProvisioning).not.toHaveBeenCalled();
  });
  it('keeps saved administrator identifiers editable only in the hydrated unbound draft', async () => {
    // Arrange
    vi.mocked(onboardingApi.getOrganizationDraft).mockResolvedValue({ ...draftFixture, currentStep: 'administrator',
      values: { ...draftFixture.values, administratorChoice: 'other', administrator: { directoryTenantId: directory, objectId: object, personId: person } } });
    page('/organizations/new?draft=draft-1');
    // Assert
    expect(await screen.findByLabelText('Person record ID')).toHaveValue(person);
    expect(screen.getByLabelText('Directory tenant ID')).toHaveValue(directory);
    expect(api.resumeOrganizationProvisioning).not.toHaveBeenCalled();
  });
  it('does not treat a failed existing-organization summary as usable administrator access', async () => {
    // Arrange
    vi.mocked(onboardingApi.getOrganizationDraft).mockResolvedValue({ ...draftFixture, currentStep: 'review',
      values: { ...draftFixture.values, organizationChoice: 'existing', existingTenantId: 'org-new' } });
    vi.mocked(onboardingApi.getOrganizationSetupSummary).mockRejectedValue(new Error('Live access unavailable'));
    // Act
    page('/organizations/new?draft=draft-1');
    // Assert
    expect(await screen.findByText('Live access unavailable')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Confirm organization reuse' })).toBeDisabled();
  });
  it('lets directory discovery populate explicit intent, clear it, and switch to a manual existing Person', async () => {
    // Arrange
    vi.mocked(api.getDirectoryConnections).mockResolvedValue([{ id: 'connection', name: 'Authorized directory', directoryTenantId: directory, cloud: 'Public', configured: true }]);
    vi.mocked(api.searchDirectoryUsers).mockResolvedValue({ users: [{ directoryTenantId: directory, objectId: object,
      displayName: 'Discovered administrator', email: 'admin@example.invalid', userPrincipalName: 'admin@example.invalid' }], hasMore: false });
    page(); details();
    // Act
    await screen.findByLabelText('Find a person');
    fireEvent.change(screen.getByLabelText('Find a person'), { target: { value: 'Admin' } });
    fireEvent.click(screen.getByRole('button', { name: 'Search Entra' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Select Discovered administrator (admin@example.invalid)' }));
    // Assert
    expect(screen.getByDisplayValue('Discovered administrator')).toBeInTheDocument();
    expect(api.resumeOrganizationProvisioning).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Choose another person' }));
    fireEvent.click(screen.getByRole('button', { name: 'Enter manually' }));
    expect(screen.getByLabelText('User object ID')).toHaveValue('');
    fireEvent.click(screen.getByLabelText('Use an existing Person record'));
    expect(screen.getByLabelText('Person record ID')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Find in Entra' }));
    expect(await screen.findByLabelText('Find a person')).toBeInTheDocument();
  });
  it('saves unbound enrollment edits as a private draft instead of executing a grant', async () => {
    // Arrange
    page('/organizations/org-new/provisioning?key=old-key');
    await screen.findByText('Administrator: Pending');
    identity();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save & finish later' }));
    // Assert
    await waitFor(() => expect(onboardingApi.saveOrganizationDraft).toHaveBeenCalledWith(expect.any(String),
      expect.objectContaining({ organizationChoice: 'existing', existingTenantId: 'org-new',
        administrator: expect.objectContaining({ directoryTenantId: directory, objectId: object }) }), 'administrator', 0));
    await waitFor(() => expect(screen.getByLabelText('Route')).toHaveTextContent('/setup/resume'));
    expect(api.resumeOrganizationProvisioning).not.toHaveBeenCalled();
  });
  it('retains unbound enrollment edits when a draft save fails', async () => {
    // Arrange
    vi.mocked(onboardingApi.saveOrganizationDraft).mockRejectedValue(new Error('Enrollment draft unavailable'));
    page('/organizations/org-new/provisioning?key=old-key');
    await screen.findByText('Administrator: Pending');
    identity();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Finish later' }));
    // Assert
    expect(await screen.findByText('Enrollment draft unavailable')).toBeInTheDocument();
    expect(screen.getByLabelText('User object ID')).toHaveValue(object);
    expect(api.resumeOrganizationProvisioning).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Refresh setup status' }));
    await waitFor(() => expect(api.getOrganizationProvisioning).toHaveBeenCalledTimes(2));
  });
  it('requires explicit server-value reconciliation after a stale draft save before adopting its newer revision', async () => {
    // Arrange
    vi.mocked(onboardingApi.getOrganizationDraft).mockResolvedValueOnce(draftFixture)
      .mockResolvedValue({ ...draftFixture, revision: 2,
        values: { ...draftFixture.values, displayName: 'Other tab saved name', primaryPocName: 'Other tab contact' } });
    vi.mocked(onboardingApi.saveOrganizationDraft).mockRejectedValueOnce(
      new api.WorkspaceOperationError('Draft changed in another tab', 409, 'STALE_REVISION'));
    page('/organizations/new?draft=draft-1');
    await screen.findByDisplayValue('Retained organization');
    fireEvent.change(screen.getByLabelText('Organization name'), { target: { value: 'Stale local name' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save & finish later' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Check saved draft' }));
    // Assert
    expect(await screen.findByRole('button', { name: 'Use saved server version' })).toBeInTheDocument();
    expect(screen.getByLabelText('Organization name')).toHaveValue('Stale local name');
    fireEvent.click(screen.getByRole('button', { name: 'Save & finish later' }));
    expect(onboardingApi.saveOrganizationDraft).toHaveBeenCalledTimes(1);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Use saved server version' }));
    // Assert
    expect(screen.getByLabelText('Organization name')).toHaveValue('Other tab saved name');
    expect(screen.getByLabelText('Primary contact name (optional)')).toHaveValue('Other tab contact');
    vi.mocked(onboardingApi.saveOrganizationDraft).mockResolvedValue({ ...draftFixture, revision: 3 });
    fireEvent.click(screen.getByRole('button', { name: 'Save & finish later' }));
    await waitFor(() => expect(onboardingApi.saveOrganizationDraft).toHaveBeenLastCalledWith('draft-1',
      expect.objectContaining({ displayName: 'Other tab saved name', primaryPocName: 'Other tab contact' }), 'details', 2));
  });
  it('does not rehydrate a locally edited draft when workspace navigation identity refreshes', async () => {
    // Arrange
    let resolveLateRead: ((value: onboardingApi.OrganizationOnboardingDraft) => void) | undefined;
    vi.mocked(onboardingApi.getOrganizationDraft).mockResolvedValueOnce(draftFixture)
      .mockImplementation(() => new Promise(resolve => { resolveLateRead = resolve; }));
    const tree = () => <MemoryRouter initialEntries={['/workspaces/csp/organizations/new?draft=draft-1']}>
      <WorkspaceNavigationProvider workspace={{ kind: 'csp' }}><AddOrganizationPage /></WorkspaceNavigationProvider>
    </MemoryRouter>;
    const view = render(tree());
    await screen.findByDisplayValue('Retained organization');
    fireEvent.change(screen.getByLabelText('Organization name'), { target: { value: 'Locally reviewed organization' } });
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
    fireEvent.click(screen.getByRole('button', { name: 'Review setup' }));
    // Act
    view.rerender(tree());
    await act(async () => { resolveLateRead?.(draftFixture); });
    // Assert
    expect(screen.getByRole('heading', { name: 'Review organization setup', level: 1 })).toBeInTheDocument();
    expect(screen.getByText('Locally reviewed organization', { exact: true })).toBeInTheDocument();
    expect(onboardingApi.getOrganizationDraft).toHaveBeenCalledOnce();
    expect(api.createOrganization).not.toHaveBeenCalled();
  });
  it('cancels a prior draft read and ignores its response after an actual draft identity change', async () => {
    // Arrange
    let finishFirst!: (value: onboardingApi.OrganizationOnboardingDraft) => void;
    let finishSecond!: (value: onboardingApi.OrganizationOnboardingDraft) => void;
    vi.mocked(onboardingApi.getOrganizationDraft).mockImplementation(id => new Promise(resolve => {
      if (id === 'draft-1') finishFirst = resolve; else finishSecond = resolve;
    }));
    function SwitchDraft() {
      const navigate = useNavigate();
      return <button onClick={() => navigate('/organizations/new?draft=draft-2')}>Choose another saved draft</button>;
    }
    render(<MemoryRouter initialEntries={['/organizations/new?draft=draft-1']}><SwitchDraft /><AddOrganizationPage /></MemoryRouter>);
    const firstSignal = vi.mocked(onboardingApi.getOrganizationDraft).mock.calls[0]![1]!;
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Choose another saved draft' }));
    await act(async () => finishSecond({ ...draftFixture, draftId: 'draft-2',
      values: { ...draftFixture.values, displayName: 'Second organization' } }));
    await act(async () => finishFirst(draftFixture));
    // Assert
    expect(firstSignal.aborted).toBe(true);
    expect(screen.getByLabelText('Organization name')).toHaveValue('Second organization');
    expect(screen.queryByDisplayValue('Retained organization')).not.toBeInTheDocument();
  });
  it('offers an explicit server-saved exit before organization creation', () => {
    // Arrange
    page();
    // Act
    fireEvent.change(screen.getByLabelText('Organization name'), { target: { value: 'Partial organization' } });
    // Assert
    expect(screen.getByRole('button', { name: 'Save & finish later' })).toBeInTheDocument();
    expect(api.createOrganization).not.toHaveBeenCalled();
  });
  it('saves a partial draft and navigates only after server confirmation without creating access', async () => {
    // Arrange
    page();
    fireEvent.change(screen.getByLabelText('Organization name'), { target: { value: 'Partial organization' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save & finish later' }));
    // Assert
    await waitFor(() => expect(onboardingApi.saveOrganizationDraft).toHaveBeenCalledWith(expect.any(String),
      expect.objectContaining({ displayName: 'Partial organization', organizationChoice: 'create' }), 'details', 0));
    await waitFor(() => expect(screen.getByLabelText('Route')).toHaveTextContent('/setup/resume'));
    expect(api.createOrganization).not.toHaveBeenCalled();
    expect(api.resumeOrganizationProvisioning).not.toHaveBeenCalled();
  });
  it('keeps partial edits and the stable draft URL when saving fails', async () => {
    // Arrange
    vi.mocked(onboardingApi.saveOrganizationDraft).mockRejectedValue(new Error('Draft storage unavailable'));
    page();
    fireEvent.change(screen.getByLabelText('Organization name'), { target: { value: 'Preserve this name' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save & finish later' }));
    // Assert
    expect(await screen.findByText('Draft storage unavailable')).toBeInTheDocument();
    expect(screen.getByDisplayValue('Preserve this name')).toBeInTheDocument();
    expect(screen.getByLabelText('Route')).toHaveTextContent('/organizations/new?draft=');
    expect(api.createOrganization).not.toHaveBeenCalled();
  });
  it('hydrates a saved review and confirms the same draft without another legacy creation request', async () => {
    // Arrange
    vi.mocked(onboardingApi.getOrganizationDraft).mockResolvedValue({ ...draftFixture, currentStep: 'review' });
    vi.mocked(onboardingApi.saveOrganizationDraft).mockResolvedValue({ ...draftFixture, revision: 2 });
    vi.mocked(onboardingApi.confirmOrganizationDraft).mockResolvedValue({ ...draftFixture, state: 'Confirmed',
      tenantId: 'org-new', operationId: 'operation-1', resumeUrl: '/organizations/org-new/provisioning?key=draft-key' });
    page('/organizations/new?draft=draft-1');
    // Act
    const confirm = await screen.findByRole('button', { name: 'Create organization' });
    await waitFor(() => expect(confirm).toBeEnabled());
    fireEvent.click(confirm);
    // Assert
    await waitFor(() => expect(onboardingApi.confirmOrganizationDraft).toHaveBeenCalledWith('draft-1', 2));
    expect(api.createOrganization).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.getByLabelText('Route')).toHaveTextContent('/organizations/org-new/provisioning?key=draft-key'));
  });
  it('shows directory fallback without discarding the organization details', async () => {
    // Arrange
    page(); details();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Directory lookup is unavailable' }));
    fireEvent.click(screen.getByRole('button', { name: 'Enter identity details manually' }));
    // Assert
    expect(screen.getByLabelText('Directory tenant ID')).toBeInTheDocument();
    expect(screen.getByText('Organization: Mission Operations')).toBeInTheDocument();
    expect(api.createOrganization).not.toHaveBeenCalled();
  });
  it('shows another current administrator without completing or retrying the requested identity', async () => {
    // Arrange
    vi.mocked(onboardingApi.getOrganizationSetupSummary).mockResolvedValue({
      tenant: { id: 'org-new', displayName: 'Mission Operations', lifecycle: 'Active', onboardingState: 'Pending' },
      observedAt: '2026-09-30T12:00:00Z', reconciliation: 'DifferentIdentity', requestedOperation: pending,
      liveAccess: { state: 'Available', activeMemberCount: 1, administrators: { items: [{
        personId: person, displayName: 'Current administrator', membershipId: 'membership', directoryTenantId: directory,
        objectId: object, assignmentId: 'role',
      }], page: 1, pageSize: 25, total: 1 } },
      actorActions: { canManageMemberships: true, canResumeEnrollment: false, canEnterOrganization: false },
    });
    // Act
    page('/organizations/org-new/provisioning?key=old-key');
    // Assert
    expect(await screen.findByRole('heading', { name: 'Review administrator setup status' })).toBeInTheDocument();
    expect(screen.getByText('Current administrator')).toBeInTheDocument();
    expect(screen.getByText('Administrator: Pending')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Resume incomplete enrollment' })).toBeDisabled();
    expect(api.resumeOrganizationProvisioning).not.toHaveBeenCalled();
    expect(screen.queryByRole('link', { name: 'Choose authorized organization workspace' })).not.toBeInTheDocument();
  });
  it('reuses live administrator access without inventing a missing enrollment operation', async () => {
    // Arrange
    vi.mocked(api.getCurrentOrganizationProvisioning).mockResolvedValue(null);
    vi.mocked(onboardingApi.getOrganizationSetupSummary).mockResolvedValue({
      tenant: { id: 'org-new', displayName: 'Mission Operations', lifecycle: 'Active', onboardingState: 'Pending' },
      observedAt: '2026-09-30T12:00:00Z', reconciliation: 'Unbound', requestedOperation: null,
      liveAccess: { state: 'Available', activeMemberCount: 1, administrators: { items: [{
        personId: person, displayName: 'Current administrator', membershipId: 'membership', directoryTenantId: directory,
        objectId: object, assignmentId: 'role',
      }], page: 1, pageSize: 25, total: 1 } },
      actorActions: { canManageMemberships: true, canResumeEnrollment: false, canEnterOrganization: false },
    });
    // Act
    page('/organizations/org-new/provisioning');
    // Assert
    expect(await screen.findByRole('heading', { name: 'Administrator access is ready' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Start enrollment' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Review administrator setup status' })).not.toBeInTheDocument();
    expect(api.beginOrganizationProvisioning).not.toHaveBeenCalled();
    expect(api.resumeOrganizationProvisioning).not.toHaveBeenCalled();
  });
  it.each([
    ['displayName', 200], ['legalEntityName', 300], ['primaryPocName', 200], ['primaryPocEmail', 254],
  ] as const)('enforces the persisted %s length boundary of %s', (field, limit) => {
    // Arrange
    const value = (length: number) => field === 'primaryPocEmail' ? `${'a'.repeat(length - 6)}@x.mil` : 'a'.repeat(length);
    const fields = { ...emptyOrganization, displayName: 'Organization', [field]: value(limit) };
    // Act
    const atLimit = validateOrganization(fields);
    const tooLong = validateOrganization({ ...fields, [field]: value(limit + 1) });
    // Assert
    expect(atLimit[field]).toBeUndefined();
    expect(tooLong[field]).toBeTruthy();
  });
  it('validates only invalid fields and clears errors when corrected', async () => {
    // Arrange
    page();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
    // Assert
    expect(screen.getByText('Enter an organization name.')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Organization name'), { target: { value: 'Mission Operations' } });
    expect(screen.queryByText('Enter an organization name.')).not.toBeInTheDocument();
    expect(api.createOrganization).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText('Primary contact email (optional)'), { target: { value: 'invalid' } });
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(screen.getByText('Enter a valid contact email.')).toBeInTheDocument();
  });

  it('reviews deferred enrollment and creates nothing until confirmation', async () => {
    // Arrange
    page();
    // Act
    defer();
    // Assert
    expect(screen.getByRole('heading', { name: 'Review organization setup' })).toBeInTheDocument();
    expect(screen.getByText('Enrollment deferred')).toBeInTheDocument();
    expect(api.createOrganization).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Create organization' }));
    await waitFor(() => expect(api.createOrganization).toHaveBeenCalledOnce());
    expect(vi.mocked(api.createOrganization).mock.calls[0]?.[0]).toEqual({ displayName: 'Mission Operations' });
    await screen.findByText('Organization created · Enrollment pending');
    expect(api.resumeOrganizationProvisioning).not.toHaveBeenCalled();
    expect(screen.queryByText('Organization setup complete')).not.toBeInTheDocument();
  });

  it('collects separate administrator details and reviews local Person creation explicitly', async () => {
    // Arrange
    page();
    fireEvent.change(screen.getByLabelText('Primary contact email (optional)'), { target: { value: 'contact@example.mil' } });
    details();
    // Act
    identity();
    fireEvent.click(screen.getByRole('button', { name: 'Review setup' }));
    // Assert
    expect(screen.getByText('Organization-local Person record')).toBeInTheDocument();
    expect(screen.getByText('admin@example.mil')).toBeInTheDocument();
    expect(screen.getByText('contact@example.mil')).toBeInTheDocument();
    expect(screen.queryByText(/Identity confirmed|Verified directory identity/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Create organization' }));
    await waitFor(() => expect(api.createOrganization).toHaveBeenCalledWith(expect.objectContaining({
      initialAdministrator: { directoryTenantId: directory, objectId: object,
        newPerson: { displayName: 'Separate administrator', email: 'admin@example.mil' } },
    }), expect.any(String)));
  });

  it('retains existing Person identifiers without accepting invalid GUIDs', () => {
    // Arrange
    page(); details();
    fireEvent.click(screen.getByRole('button', { name: 'Enter manually' }));
    fireEvent.click(screen.getByLabelText('Use an existing Person record'));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Review setup' }));
    // Assert
    expect(screen.getByText('Enter a valid directory tenant ID.')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Directory tenant ID'), { target: { value: directory } });
    fireEvent.change(screen.getByLabelText('User object ID'), { target: { value: object } });
    fireEvent.change(screen.getByLabelText('Person record ID'), { target: { value: person } });
    fireEvent.click(screen.getByRole('button', { name: 'Review setup' }));
    expect(screen.getByText(person)).toBeInTheDocument();
    expect(api.createOrganization).not.toHaveBeenCalled();
  });

  it('allows editing review details without any writes', () => {
    // Arrange
    page(); defer();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Edit organization details' }));
    // Assert
    expect(screen.getByLabelText('Organization name')).toHaveValue('Mission Operations');
    expect(api.createOrganization).not.toHaveBeenCalled();
  });

  it('uses named radio groups for native keyboard navigation', () => {
    // Arrange
    page(); details();
    // Act
    const now = screen.getByLabelText('Enroll administrator now');
    const later = screen.getByLabelText('Complete enrollment later');
    fireEvent.click(screen.getByRole('button', { name: 'Enter manually' }));
    const create = screen.getByLabelText('Create a Person record for this administrator');
    const existing = screen.getByLabelText('Use an existing Person record');
    // Assert
    expect(now.getAttribute('name')).toBeTruthy();
    expect(now.getAttribute('name')).toBe(later.getAttribute('name'));
    expect(create.getAttribute('name')).toBeTruthy();
    expect(create.getAttribute('name')).toBe(existing.getAttribute('name'));
    expect(create.getAttribute('name')).not.toBe(now.getAttribute('name'));
  });

  it('locks concurrent creation and freezes intent after an uncertain response', async () => {
    // Arrange
    let reject!: (reason: Error) => void;
    vi.mocked(api.createOrganization).mockImplementation(() => new Promise((_, fail) => { reject = fail; }));
    page(); defer();
    // Act
    const button = screen.getByRole('button', { name: 'Create organization' });
    fireEvent.click(button); fireEvent.click(button);
    // Assert
    expect(api.createOrganization).toHaveBeenCalledOnce();
    expect(screen.getByLabelText('Route')).toHaveTextContent('?key=');
    await act(async () => reject(new Error('Connection interrupted')));
    expect(await screen.findByRole('button', { name: 'Check creation status' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Edit organization details' })).not.toBeInTheDocument();
    expect(screen.getByText('Mission Operations')).toBeInTheDocument();
  });

  it('restores an uncertain creation by key after refresh without posting again', async () => {
    // Arrange
    vi.mocked(api.getOrganizationCreation).mockResolvedValue(created);
    // Act
    page('/organizations/new?key=known-key');
    // Assert
    await waitFor(() => expect(screen.getByLabelText('Route')).toHaveTextContent('/organizations/org-new/provisioning?key=known-key'));
    expect(api.createOrganization).not.toHaveBeenCalled();
  });

  it('does not treat recovery permission rejection as a missing operation', async () => {
    // Arrange
    vi.mocked(api.getOrganizationCreation).mockRejectedValue(new api.WorkspaceOperationError('Provider permission denied.', 403));
    // Act
    page('/organizations/new?key=known-key');
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Provider permission denied.');
    expect(api.createOrganization).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: 'Retry creation' })).not.toBeInTheDocument();
  });

  it('rejects provider pages without permission', () => {
    // Arrange
    permissions.canAccessCsp = false;
    // Act
    page();
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Provider access is required.');
    expect(api.createOrganization).not.toHaveBeenCalled();
  });

  it('retries an uncertain create with the identical key and frozen intent after checking status', async () => {
    // Arrange
    vi.mocked(api.createOrganization).mockRejectedValueOnce(new Error('Connection interrupted')).mockResolvedValueOnce(created);
    page(); defer();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Create organization' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Check creation status' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Retry creation' }));
    // Assert
    await waitFor(() => expect(api.createOrganization).toHaveBeenCalledTimes(2));
    expect(vi.mocked(api.createOrganization).mock.calls[0]).toEqual(vi.mocked(api.createOrganization).mock.calls[1]);
    await screen.findByText('Organization created · Enrollment pending');
  });

  it('retains details and permits correction after a rejected creation', async () => {
    // Arrange
    vi.mocked(api.createOrganization).mockRejectedValue(new api.WorkspaceOperationError('An organization with this name already exists.', 422));
    page(); defer();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Create organization' }));
    await screen.findByRole('alert');
    fireEvent.click(screen.getByRole('button', { name: 'Edit organization details' }));
    // Assert
    expect(screen.getByLabelText('Organization name')).toHaveValue('Mission Operations');
    expect(api.createOrganization).toHaveBeenCalledOnce();
  });

  it('continues confirmed administrator intent once and reports completion only after saved stages', async () => {
    // Arrange
    const administrator = { directoryTenantId: directory, objectId: object, newPerson: { displayName: 'Separate administrator', email: 'admin@example.mil' } };
    vi.mocked(api.getOrganizationProvisioning).mockResolvedValue({ ...pending, initialAdministrator: administrator, personState: 'Pending' });
    vi.mocked(api.resumeOrganizationProvisioning).mockResolvedValue({
      ...pending, initialAdministrator: administrator, personState: 'Completed', membershipState: 'Completed', administratorState: 'Completed',
    });
    page(); details(); identity();
    fireEvent.click(screen.getByRole('button', { name: 'Review setup' }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Create organization' }));
    // Assert
    await screen.findByText('Organization setup complete');
    expect(api.resumeOrganizationProvisioning).toHaveBeenCalledExactlyOnceWith('org-new', 'operation-1', administrator, expect.any(AbortSignal));
  });

  it('does not mark setup complete when a requested Person is still pending', async () => {
    // Arrange
    vi.mocked(api.getOrganizationProvisioning).mockResolvedValue({
      ...pending, personState: 'Pending', membershipState: 'Completed', administratorState: 'Completed',
      initialAdministrator: { directoryTenantId: directory, objectId: object, personId: person },
    });
    // Act
    page('/organizations/org-new/provisioning?key=create-key');
    // Assert
    await screen.findByText('Person: Pending');
    expect(screen.queryByText('Organization setup complete')).not.toBeInTheDocument();
  });

  it('blocks concurrent enrollment retries', async () => {
    // Arrange
    const administrator = { directoryTenantId: directory, objectId: object, personId: person };
    vi.mocked(api.getOrganizationProvisioning).mockResolvedValue({ ...pending, initialAdministrator: administrator });
    let finish!: (value: apiResult) => void;
    type apiResult = Awaited<ReturnType<typeof api.resumeOrganizationProvisioning>>;
    vi.mocked(api.resumeOrganizationProvisioning).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
    page('/organizations/org-new/provisioning?key=create-key');
    // Act
    const retry = await screen.findByRole('button', { name: 'Continue enrollment' });
    fireEvent.click(retry); fireEvent.click(retry);
    // Assert
    expect(api.resumeOrganizationProvisioning).toHaveBeenCalledOnce();
    expect(screen.getByRole('button', { name: 'Enrollment in progress...' })).toBeDisabled();
    await act(async () => finish({ ...pending, administratorState: 'Completed', membershipState: 'Completed' }));
  });

  it('starts a missing enrollment operation with one stable key', async () => {
    // Arrange
    vi.mocked(api.getCurrentOrganizationProvisioning).mockResolvedValue(null);
    vi.mocked(api.beginOrganizationProvisioning).mockResolvedValue(pending);
    page('/organizations/org-new/provisioning');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Start enrollment' }));
    // Assert
    await screen.findByText('Organization: Completed');
    expect(api.beginOrganizationProvisioning).toHaveBeenCalledOnce();
    expect(screen.getByLabelText('Route')).toHaveTextContent('?key=');
    expect(api.createOrganization).not.toHaveBeenCalled();
  });

  it('allows same-key enrollment recovery only for a verified missing operation', async () => {
    // Arrange
    vi.mocked(api.getOrganizationProvisioning).mockRejectedValueOnce(new api.WorkspaceOperationError('No enrollment exists.', 404, 'PROVISIONING_NOT_FOUND'));
    vi.mocked(api.beginOrganizationProvisioning).mockResolvedValue(pending);
    page('/organizations/org-new/provisioning?key=stable-key');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Start enrollment' }));
    // Assert
    await screen.findByText('Organization: Completed');
    expect(api.beginOrganizationProvisioning).toHaveBeenCalledWith('org-new', 'stable-key', expect.any(AbortSignal));
  });

  it('does not show setup from a mismatched organization response', async () => {
    // Arrange
    vi.mocked(api.getOrganizationProvisioning).mockResolvedValue({ ...pending, tenantId: 'other-org' });
    // Act
    page('/organizations/org-new/provisioning?key=stable-key');
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('does not belong to this organization');
    expect(screen.queryByText('Organization: Completed')).not.toBeInTheDocument();
  });

  it('edits an unbound rejected administrator and preserves required field validation', async () => {
    // Arrange
    vi.mocked(api.getOrganizationProvisioning).mockResolvedValue({
      ...pending, initialAdministrator: { directoryTenantId: directory, objectId: object, personId: person }, canEditAdministrator: true,
    });
    vi.mocked(api.resumeOrganizationProvisioning).mockResolvedValue(pending);
    page('/organizations/org-new/provisioning?key=stable-key');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Correct administrator details' }));
    fireEvent.change(screen.getByLabelText('Person record ID'), { target: { value: 'invalid' } });
    fireEvent.click(screen.getByRole('button', { name: 'Resume incomplete enrollment' }));
    // Assert
    expect(screen.getByText('Enter a valid Person record ID.')).toBeInTheDocument();
    expect(api.resumeOrganizationProvisioning).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText('Person record ID'), { target: { value: person } });
    fireEvent.click(screen.getByRole('button', { name: 'Resume incomplete enrollment' }));
    await waitFor(() => expect(api.resumeOrganizationProvisioning).toHaveBeenCalledOnce());
  });

  it('recovers persisted identity and completed work instead of asking to re-enter it', async () => {
    // Arrange
    const administrator = { directoryTenantId: directory, objectId: object, personId: person };
    vi.mocked(api.getOrganizationProvisioning).mockResolvedValue({
      ...pending, initialAdministrator: administrator, canEditAdministrator: false,
      membershipState: 'Completed', lastError: 'Administrator enrollment failed.',
    });
    vi.mocked(api.resumeOrganizationProvisioning).mockResolvedValue({
      ...pending, initialAdministrator: administrator, administratorState: 'Completed', membershipState: 'Completed',
    });
    // Act
    page('/organizations/org-new/provisioning?key=create-key');
    fireEvent.click(await screen.findByRole('button', { name: 'Retry administrator enrollment' }));
    // Assert
    await screen.findByText('Organization setup complete');
    expect(api.resumeOrganizationProvisioning).toHaveBeenCalledWith('org-new', 'operation-1', administrator, expect.any(AbortSignal));
    expect(api.createOrganization).not.toHaveBeenCalled();
    expect(api.beginOrganizationProvisioning).not.toHaveBeenCalled();
  });

  it('reloads real outcomes after a lost resume response without inventing success', async () => {
    // Arrange
    const administrator = { directoryTenantId: directory, objectId: object, personId: person };
    vi.mocked(api.getOrganizationProvisioning).mockResolvedValueOnce({ ...pending, initialAdministrator: administrator })
      .mockResolvedValueOnce({ ...pending, initialAdministrator: administrator, membershipState: 'Completed', administratorState: 'Pending' });
    vi.mocked(api.resumeOrganizationProvisioning).mockRejectedValue(new Error('Connection interrupted'));
    page('/organizations/org-new/provisioning?key=create-key');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Continue enrollment' }));
    // Assert
    await waitFor(() => expect(api.getOrganizationProvisioning).toHaveBeenCalledTimes(2));
    expect(screen.queryByText('Organization setup complete')).not.toBeInTheDocument();
    expect(screen.getByText('Membership: Completed')).toBeInTheDocument();
    expect(api.createOrganization).not.toHaveBeenCalled();
  });
});
