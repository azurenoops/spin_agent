import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, useLocation } from 'react-router-dom';
import type { ReactNode } from 'react';
import WorkspaceOperationsPage from '../../features/workspace-operations/WorkspaceOperationsPage';
import * as api from '../../features/workspace-operations/api';
import { emptyOrganization, validateOrganization } from '../../features/workspace-operations/OrganizationSetupPresentation';

vi.mock('../../features/workspace-operations/api', async importOriginal => ({
  ...(await importOriginal<typeof api>()),
  createOrganization: vi.fn(), getOrganizationCreation: vi.fn(),
  getOrganization: vi.fn(), getOrganizationProvisioning: vi.fn(),
  getCurrentOrganizationProvisioning: vi.fn(), beginOrganizationProvisioning: vi.fn(),
  resumeOrganizationProvisioning: vi.fn(),
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
function Route() { const location = useLocation(); return <output aria-label="Route">{location.pathname}{location.search}</output>; }
function page(route = '/organizations/new') {
  return render(<MemoryRouter initialEntries={[route]}><Route /><WorkspaceOperationsPage /></MemoryRouter>);
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
  fireEvent.change(screen.getByLabelText('Directory tenant ID'), { target: { value: directory } });
  fireEvent.change(screen.getByLabelText('User object ID'), { target: { value: object } });
  fireEvent.change(screen.getByLabelText('Administrator name'), { target: { value: 'Separate administrator' } });
  fireEvent.change(screen.getByLabelText('Administrator email'), { target: { value: 'admin@example.mil' } });
}
beforeEach(() => {
  vi.resetAllMocks();
  permissions.canAccessCsp = true;
  vi.mocked(api.createOrganization).mockResolvedValue(created);
  vi.mocked(api.getOrganizationCreation).mockResolvedValue(null);
  vi.mocked(api.getOrganization).mockResolvedValue({
    id: 'org-new', displayName: 'Mission Operations', lifecycle: 'Active', onboarding: 'Pending',
    systems: [], subscriptions: [], activity: [],
  });
  vi.mocked(api.getOrganizationProvisioning).mockResolvedValue(pending);
  vi.mocked(api.getCurrentOrganizationProvisioning).mockResolvedValue(pending);
});

describe('CSP Add Organization approved flow', () => {
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
