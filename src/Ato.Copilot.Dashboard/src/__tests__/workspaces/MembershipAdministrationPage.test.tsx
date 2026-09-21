import type { ReactNode } from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { axe } from 'vitest-axe';
import MembershipAdministrationPage from '../../features/workspaces/MembershipAdministrationPage';
import {
  createMembershipPerson,
  getMembershipPersons,
  getWorkspaceMemberships,
  grantWorkspaceMembership,
  revokeWorkspaceMembership,
} from '../../features/workspaces/api';
import type { MembershipPerson, WorkspaceMembership } from '../../features/workspaces/types';

vi.mock('../../components/layout/PageLayout', () => ({
  default: ({ title, children }: { title: string; children: ReactNode }) => (
    <main aria-label={title}>{children}</main>
  ),
}));
vi.mock('../../features/workspaces/api', async importOriginal => ({
  ...await importOriginal<typeof import('../../features/workspaces/api')>(),
  createMembershipPerson: vi.fn(),
  getMembershipPersons: vi.fn(),
  getWorkspaceMemberships: vi.fn(),
  grantWorkspaceMembership: vi.fn(),
  revokeWorkspaceMembership: vi.fn(),
}));

const tenantId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const otherTenantId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const directoryTenantId = '11111111-1111-1111-1111-111111111111';
const objectId = '22222222-2222-2222-2222-222222222222';
const person: MembershipPerson = { id: 'person-a', displayName: 'Synthetic Contact', email: 'contact@example.test' };
const membership: WorkspaceMembership = {
  id: 'membership-a', tenantId, directoryTenantId, objectId, personId: person.id,
  grantedAt: '2026-09-01T12:00:00Z', grantedBy: 'synthetic-admin',
  revokedAt: null, revokedBy: null,
};
const props = { tenantId, tenantName: 'Organization Alpha', canManageMemberships: true };
const forbidden = { response: { status: 403, data: { error: { message: 'Membership administration is forbidden.' } } } };

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej; });
  return { promise, resolve, reject };
}

async function loaded() {
  await screen.findByRole('option', { name: /Synthetic Contact/ });
  await waitFor(() => expect(screen.queryByText('Loading memberships…')).not.toBeInTheDocument());
}

function fillGrant(directory = directoryTenantId, object = objectId) {
  fireEvent.change(screen.getByLabelText('Directory tenant ID'), { target: { value: directory } });
  fireEvent.change(screen.getByLabelText('Object ID'), { target: { value: object } });
  fireEvent.change(screen.getByLabelText('Organization-local Person'), { target: { value: person.id } });
}

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(getMembershipPersons).mockResolvedValue([person]);
  vi.mocked(getWorkspaceMemberships).mockResolvedValue({ items: [membership], total: 1 });
  vi.mocked(createMembershipPerson).mockResolvedValue({ id: 'person-new', displayName: 'New Contact', email: 'new@example.test' });
  vi.mocked(grantWorkspaceMembership).mockResolvedValue(membership);
  vi.mocked(revokeWorkspaceMembership).mockResolvedValue(undefined);
});

describe('MembershipAdministrationPage', () => {
  it.each([false, undefined, 'true'])('denies permission %s without reading memberships or persons', permission => {
    // Arrange
    const canManageMemberships = permission as boolean;

    // Act
    render(<MembershipAdministrationPage {...props} canManageMemberships={canManageMemberships} />);

    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent(/access denied/i);
    expect(getMembershipPersons).not.toHaveBeenCalled();
    expect(getWorkspaceMemberships).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: 'Grant membership' })).not.toBeInTheDocument();
  });

  it('loads the target organization and explains that membership, contact details, and RMF roles are separate', async () => {
    // Arrange
    render(<MembershipAdministrationPage {...props} />);

    // Act
    await loaded();

    // Assert
    expect(getMembershipPersons).toHaveBeenCalledWith(tenantId, '');
    expect(getWorkspaceMemberships).toHaveBeenCalledWith(tenantId, 1);
    expect(screen.getByRole('heading', { name: 'Organization Alpha — Membership administration' })).toBeInTheDocument();
    expect(screen.getByText(/Email is contact information only/)).toBeInTheDocument();
    expect(screen.getByText(/Granting membership does not assign any RMF role/)).toBeInTheDocument();
    expect(screen.getByLabelText('Directory tenant ID')).toHaveValue('');
    expect(screen.getByLabelText('Object ID')).toHaveValue('');
    expect(within(screen.getByRole('table')).getByText('Synthetic Contact')).toBeInTheDocument();
    expect(within(screen.getByRole('table')).getByText(person.id)).toBeInTheDocument();
    expect(screen.getByText('Active')).toBeInTheDocument();
    expect(screen.getByText('Granted')).toBeInTheDocument();
  });

  it('shows revoked membership history without offering another revocation', async () => {
    // Arrange
    vi.mocked(getWorkspaceMemberships).mockResolvedValue({
      items: [{ ...membership, revokedAt: '2026-09-03T12:00:00Z', revokedBy: 'synthetic-admin' }], total: 1,
    });
    render(<MembershipAdministrationPage {...props} />);

    // Act
    await loaded();

    // Assert
    expect(within(screen.getByRole('table')).getByText('Revoked', { selector: 'span' })).toBeInTheDocument();
    expect(screen.getByText('2026-09-03T12:00:00Z')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Revoke membership for/ })).not.toBeInTheDocument();
  });

  it('shows loading and empty membership/person states', async () => {
    // Arrange
    const members = deferred<{ items: WorkspaceMembership[]; total: number }>();
    vi.mocked(getWorkspaceMemberships).mockReturnValue(members.promise);
    vi.mocked(getMembershipPersons).mockResolvedValue([]);
    render(<MembershipAdministrationPage {...props} />);

    // Act
    expect(screen.getByText('Loading memberships…')).toHaveAttribute('role', 'status');
    await act(async () => members.resolve({ items: [], total: 0 }));

    // Assert
    expect(screen.getByText('No memberships found.')).toBeInTheDocument();
    expect(screen.getByText(/No matching organization-local people/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Grant membership' })).toBeDisabled();
  });

  it('creates a contact without granting membership or selecting an identity', async () => {
    // Arrange
    render(<MembershipAdministrationPage {...props} />);
    await loaded();
    fireEvent.change(screen.getByLabelText('Display name'), { target: { value: '  New Contact  ' } });
    fireEvent.change(screen.getByLabelText('Contact email'), { target: { value: ' new@example.test ' } });

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Create contact' }));

    // Assert
    await waitFor(() => expect(createMembershipPerson).toHaveBeenCalledWith(tenantId, {
      displayName: 'New Contact', email: 'new@example.test',
    }));
    expect(await screen.findByText(/Contact created. No membership or RMF role was granted/)).toBeInTheDocument();
    expect(screen.getByRole('option', { name: /New Contact/ })).toBeInTheDocument();
    expect(screen.getByLabelText('Organization-local Person')).toHaveValue('');
    expect(grantWorkspaceMembership).not.toHaveBeenCalled();
  });

  it('preserves contact input on failure without reporting a grant or success', async () => {
    // Arrange
    vi.mocked(createMembershipPerson).mockRejectedValue(new Error('Contact creation failed.'));
    render(<MembershipAdministrationPage {...props} />);
    await loaded();
    fireEvent.change(screen.getByLabelText('Display name'), { target: { value: 'New Contact' } });
    fireEvent.change(screen.getByLabelText('Contact email'), { target: { value: 'new@example.test' } });

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Create contact' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Contact creation failed.');
    expect(screen.getByLabelText('Display name')).toHaveValue('New Contact');
    expect(screen.getByLabelText('Contact email')).toHaveValue('new@example.test');
    expect(screen.queryByRole('option', { name: /New Contact/ })).not.toBeInTheDocument();
    expect(screen.queryByText(/Contact created/)).not.toBeInTheDocument();
    expect(grantWorkspaceMembership).not.toHaveBeenCalled();
  });

  it.each([
    ['', 'bad-email', /Display name is required/],
    ['New Contact', 'bad-email', /Enter a valid contact email/],
  ])('validates contact creation without making a request', async (displayName, email, error) => {
    // Arrange
    render(<MembershipAdministrationPage {...props} />);
    await loaded();
    fireEvent.change(screen.getByLabelText('Display name'), { target: { value: displayName } });
    fireEvent.change(screen.getByLabelText('Contact email'), { target: { value: email } });

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Create contact' }));

    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent(error);
    expect(createMembershipPerson).not.toHaveBeenCalled();
  });

  it.each([
    ['not-a-guid', objectId, /Directory tenant ID must be a non-empty GUID/],
    [directoryTenantId, 'email@example.test', /Object ID must be a non-empty GUID/],
    ['00000000-0000-0000-0000-000000000000', objectId, /Directory tenant ID must be a non-empty GUID/],
    [directoryTenantId, '00000000-0000-0000-0000-000000000000', /Object ID must be a non-empty GUID/],
  ])('rejects malformed identity IDs with visible feedback', async (directory, object, error) => {
    // Arrange
    render(<MembershipAdministrationPage {...props} />);
    await loaded();
    fillGrant(directory, object);

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Grant membership' }));

    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent(error);
    expect(grantWorkspaceMembership).not.toHaveBeenCalled();
  });

  it('requires explicit Person selection', async () => {
    // Arrange
    render(<MembershipAdministrationPage {...props} />);
    await loaded();
    fillGrant();
    fireEvent.change(screen.getByLabelText('Organization-local Person'), { target: { value: '' } });

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Grant membership' }));

    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent(/Select an organization-local Person/);
    expect(grantWorkspaceMembership).not.toHaveBeenCalled();
  });

  it('grants only the explicitly supplied trusted identity and existing Person, then refreshes', async () => {
    // Arrange
    render(<MembershipAdministrationPage {...props} />);
    await loaded();
    fillGrant(` ${directoryTenantId} `, ` ${objectId} `);

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Grant membership' }));

    // Assert
    expect(await screen.findByText('Membership granted. No RMF role was assigned.')).toBeInTheDocument();
    expect(grantWorkspaceMembership).toHaveBeenCalledExactlyOnceWith(tenantId, {
      directoryTenantId, objectId, personId: person.id,
    });
    await waitFor(() => expect(getWorkspaceMemberships).toHaveBeenCalledTimes(2));
    expect(createMembershipPerson).not.toHaveBeenCalled();
    expect(screen.getByLabelText('Object ID')).toHaveValue('');
  });

  it('surfaces a duplicate grant response without reporting success or clearing the form', async () => {
    // Arrange
    vi.mocked(grantWorkspaceMembership).mockRejectedValue({
      response: { status: 409, data: { error: { message: 'An active membership already exists.' } } },
    });
    render(<MembershipAdministrationPage {...props} />);
    await loaded();
    fillGrant();

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Grant membership' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('An active membership already exists.');
    expect(screen.queryByText('Membership granted. No RMF role was assigned.')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Object ID')).toHaveValue(objectId);
    expect(getWorkspaceMemberships).toHaveBeenCalledTimes(1);
  });

  it('distinguishes a successful grant from a failed subsequent membership refresh', async () => {
    // Arrange
    render(<MembershipAdministrationPage {...props} />);
    await loaded();
    vi.mocked(getWorkspaceMemberships).mockRejectedValue(new Error('Membership refresh failed.'));
    fillGrant();

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Grant membership' }));

    // Assert
    expect(await screen.findByText('Membership granted. No RMF role was assigned.')).toBeInTheDocument();
    expect(await screen.findByRole('alert')).toHaveTextContent('Membership refresh failed.');
    expect(screen.getByRole('button', { name: 'Retry memberships' })).toBeInTheDocument();
    expect(grantWorkspaceMembership).toHaveBeenCalledTimes(1);
  });

  it('confirms revocation explicitly and supports cancellation', async () => {
    // Arrange
    render(<MembershipAdministrationPage {...props} />);
    await loaded();
    fireEvent.click(screen.getByRole('button', { name: /Revoke membership for Synthetic Contact/ }));

    // Act
    expect(revokeWorkspaceMembership).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel revocation' }));
    expect(screen.queryByRole('button', { name: 'Confirm revocation' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Revoke membership for Synthetic Contact/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' }));

    // Assert
    expect(await screen.findByText('Membership revoked.')).toBeInTheDocument();
    expect(revokeWorkspaceMembership).toHaveBeenCalledExactlyOnceWith(tenantId, membership.id);
    await waitFor(() => expect(getWorkspaceMemberships).toHaveBeenCalledTimes(2));
  });

  it('keeps failed revocation visible and does not claim success', async () => {
    // Arrange
    vi.mocked(revokeWorkspaceMembership).mockRejectedValue(new Error('Revocation could not be completed.'));
    render(<MembershipAdministrationPage {...props} />);
    await loaded();
    fireEvent.click(screen.getByRole('button', { name: /Revoke membership for/ }));

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Revocation could not be completed.');
    expect(screen.queryByText('Membership revoked.')).not.toBeInTheDocument();
    expect(screen.getByText('Active')).toBeInTheDocument();
  });

  it('paginates memberships with the 50-row contract', async () => {
    // Arrange
    vi.mocked(getWorkspaceMemberships).mockResolvedValue({ items: [membership], total: 51 });
    render(<MembershipAdministrationPage {...props} />);
    await loaded();
    expect(screen.getByRole('button', { name: 'Previous memberships page' })).toBeDisabled();

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Next memberships page' }));

    // Assert
    await waitFor(() => expect(getWorkspaceMemberships).toHaveBeenLastCalledWith(tenantId, 2));
    expect(await screen.findByText('Page 2 of 2 · 51 memberships')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Next memberships page' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Previous memberships page' }));
    await waitFor(() => expect(getWorkspaceMemberships).toHaveBeenLastCalledWith(tenantId, 1));
  });

  it('searches organization-local contacts explicitly and preserves association labels', async () => {
    // Arrange
    render(<MembershipAdministrationPage {...props} />);
    await loaded();
    vi.mocked(getMembershipPersons).mockResolvedValue([{ id: 'person-b', displayName: 'Contact Beta', email: 'b@example.test' }]);
    fireEvent.change(screen.getByLabelText('Search organization-local people'), { target: { value: '  Beta  ' } });

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Search people' }));

    // Assert
    expect(await screen.findByRole('option', { name: /Contact Beta/ })).toBeInTheDocument();
    expect(getMembershipPersons).toHaveBeenLastCalledWith(tenantId, 'Beta');
    expect(within(screen.getByRole('table')).getByText('Synthetic Contact')).toBeInTheDocument();
  });

  it('shows Person ID when an associated contact is absent from the search result', async () => {
    // Arrange
    vi.mocked(getMembershipPersons).mockResolvedValue([]);
    render(<MembershipAdministrationPage {...props} />);

    // Act
    await screen.findByRole('table');

    // Assert
    expect(within(screen.getByRole('table')).getByText(person.id)).toBeInTheDocument();
    expect(screen.getByText('Person not in loaded contacts')).toBeInTheDocument();
  });

  it.each(['memberships', 'persons'] as const)('surfaces failed %s reads and allows retry', async kind => {
    // Arrange
    const request = kind === 'memberships' ? vi.mocked(getWorkspaceMemberships) : vi.mocked(getMembershipPersons);
    request.mockRejectedValueOnce(new Error(`Cannot load ${kind}.`));
    render(<MembershipAdministrationPage {...props} />);

    // Act
    expect(await screen.findByRole('alert')).toHaveTextContent(`Cannot load ${kind}.`);
    fireEvent.click(screen.getByRole('button', { name: kind === 'memberships' ? 'Retry memberships' : 'Retry people' }));

    // Assert
    await loaded();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(request).toHaveBeenCalledTimes(2);
  });

  it.each(['memberships', 'persons', 'grant', 'contact', 'revoke'] as const)('fails closed on a 403 from %s', async source => {
    // Arrange
    if (source === 'memberships') vi.mocked(getWorkspaceMemberships).mockRejectedValue(forbidden);
    if (source === 'persons') vi.mocked(getMembershipPersons).mockRejectedValue(forbidden);
    if (source === 'grant') vi.mocked(grantWorkspaceMembership).mockRejectedValue(forbidden);
    if (source === 'contact') vi.mocked(createMembershipPerson).mockRejectedValue(forbidden);
    if (source === 'revoke') vi.mocked(revokeWorkspaceMembership).mockRejectedValue(forbidden);
    render(<MembershipAdministrationPage {...props} />);

    // Act
    if (source === 'grant') {
      await loaded();
      fillGrant();
      fireEvent.click(screen.getByRole('button', { name: 'Grant membership' }));
    }
    if (source === 'contact') {
      await loaded();
      fireEvent.change(screen.getByLabelText('Display name'), { target: { value: 'New Contact' } });
      fireEvent.change(screen.getByLabelText('Contact email'), { target: { value: 'new@example.test' } });
      fireEvent.click(screen.getByRole('button', { name: 'Create contact' }));
    }
    if (source === 'revoke') {
      await loaded();
      fireEvent.click(screen.getByRole('button', { name: /Revoke membership for/ }));
      fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' }));
    }

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(/Access denied.*Membership administration is forbidden/s);
    expect(screen.queryByRole('button', { name: 'Grant membership' })).not.toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('clears previous-tenant contacts, members, and form state synchronously when the target changes', async () => {
    // Arrange
    const { rerender } = render(<MembershipAdministrationPage {...props} />);
    await loaded();
    fillGrant();
    vi.mocked(getMembershipPersons).mockReturnValue(new Promise(() => {}));
    vi.mocked(getWorkspaceMemberships).mockReturnValue(new Promise(() => {}));

    // Act
    rerender(<MembershipAdministrationPage {...props} tenantId={otherTenantId} tenantName="Organization Beta" />);

    // Assert
    expect(screen.getByRole('heading', { name: /Organization Beta/ })).toBeInTheDocument();
    expect(screen.queryByText('Synthetic Contact')).not.toBeInTheDocument();
    expect(screen.queryByRole('option', { name: /Synthetic Contact/ })).not.toBeInTheDocument();
    expect(screen.queryByText(person.id)).not.toBeInTheDocument();
    expect(screen.getByLabelText('Object ID')).toHaveValue('');
    expect(getWorkspaceMemberships).toHaveBeenLastCalledWith(otherTenantId, 1);
  });

  it('ignores stale read responses and errors after switching tenants', async () => {
    // Arrange
    const oldPeople = deferred<MembershipPerson[]>();
    const oldMembers = deferred<{ items: WorkspaceMembership[]; total: number }>();
    vi.mocked(getMembershipPersons).mockReturnValueOnce(oldPeople.promise);
    vi.mocked(getWorkspaceMemberships).mockReturnValueOnce(oldMembers.promise);
    const { rerender } = render(<MembershipAdministrationPage {...props} />);
    vi.mocked(getMembershipPersons).mockResolvedValue([]);
    vi.mocked(getWorkspaceMemberships).mockResolvedValue({ items: [], total: 0 });

    // Act
    rerender(<MembershipAdministrationPage {...props} tenantId={otherTenantId} tenantName="Organization Beta" />);
    await act(async () => {
      oldPeople.resolve([person]);
      oldMembers.reject(forbidden);
    });

    // Assert
    expect(await screen.findByText('No memberships found.')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.queryByRole('option', { name: /Synthetic Contact/ })).not.toBeInTheDocument();
  });

  it('does not refresh or announce an old-tenant mutation after switching tenants', async () => {
    // Arrange
    const grant = deferred<WorkspaceMembership>();
    vi.mocked(grantWorkspaceMembership).mockReturnValue(grant.promise);
    const { rerender } = render(<MembershipAdministrationPage {...props} />);
    await loaded();
    fillGrant();
    fireEvent.click(screen.getByRole('button', { name: 'Grant membership' }));
    expect(screen.getByRole('button', { name: 'Grant membership' })).toBeDisabled();
    vi.mocked(getMembershipPersons).mockResolvedValue([]);
    vi.mocked(getWorkspaceMemberships).mockResolvedValue({ items: [], total: 0 });

    // Act
    rerender(<MembershipAdministrationPage {...props} tenantId={otherTenantId} tenantName="Organization Beta" />);
    await act(async () => grant.resolve(membership));

    // Assert
    expect(await screen.findByText('No memberships found.')).toBeInTheDocument();
    expect(screen.queryByText('Membership granted. No RMF role was assigned.')).not.toBeInTheDocument();
    expect(getWorkspaceMemberships).toHaveBeenCalledTimes(2);
    expect(getWorkspaceMemberships).toHaveBeenLastCalledWith(otherTenantId, 1);
  });

  it('unmounts administration when permission is withdrawn and ignores pending data', async () => {
    // Arrange
    const people = deferred<MembershipPerson[]>();
    vi.mocked(getMembershipPersons).mockReturnValue(people.promise);
    const { rerender } = render(<MembershipAdministrationPage {...props} />);

    // Act
    rerender(<MembershipAdministrationPage {...props} canManageMemberships={false} />);
    await act(async () => people.resolve([person]));

    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent(/Access denied/);
    expect(screen.queryByRole('option', { name: /Synthetic Contact/ })).not.toBeInTheDocument();
    expect(getMembershipPersons).toHaveBeenCalledTimes(1);
  });

  it('ignores a late mutation denial from the previous organization', async () => {
    // Arrange
    const grant = deferred<WorkspaceMembership>();
    vi.mocked(grantWorkspaceMembership).mockReturnValue(grant.promise);
    const { rerender } = render(<MembershipAdministrationPage {...props} />);
    await loaded();
    fillGrant();
    fireEvent.click(screen.getByRole('button', { name: 'Grant membership' }));
    vi.mocked(getMembershipPersons).mockResolvedValue([]);
    vi.mocked(getWorkspaceMemberships).mockResolvedValue({ items: [], total: 0 });

    // Act
    rerender(<MembershipAdministrationPage {...props} tenantId={otherTenantId} tenantName="Organization Beta" />);
    await act(async () => grant.reject(forbidden));

    // Assert
    expect(await screen.findByText('No memberships found.')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /Organization Beta/ })).toBeInTheDocument();
  });

  describe('onRevoked', () => {
    it('notifies exactly once with the selected membership only after the revoke succeeds', async () => {
      // Arrange
      const revoke = deferred<void>();
      const onRevoked = vi.fn();
      vi.mocked(revokeWorkspaceMembership).mockReturnValue(revoke.promise);
      render(<MembershipAdministrationPage {...props} onRevoked={onRevoked} />);
      await loaded();
      fireEvent.click(screen.getByRole('button', { name: /Revoke membership for/ }));

      // Act
      fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' }));
      expect(onRevoked).not.toHaveBeenCalled();
      await act(async () => revoke.resolve(undefined));

      // Assert
      expect(onRevoked).toHaveBeenCalledExactlyOnceWith(membership);
      expect(revokeWorkspaceMembership).toHaveBeenCalledExactlyOnceWith(tenantId, membership.id);
      expect(screen.getByText('Membership revoked.')).toBeInTheDocument();
    });

    it('does not notify when confirmation is cancelled', async () => {
      // Arrange
      const onRevoked = vi.fn();
      render(<MembershipAdministrationPage {...props} onRevoked={onRevoked} />);
      await loaded();
      fireEvent.click(screen.getByRole('button', { name: /Revoke membership for/ }));

      // Act
      fireEvent.click(screen.getByRole('button', { name: 'Cancel revocation' }));

      // Assert
      expect(revokeWorkspaceMembership).not.toHaveBeenCalled();
      expect(onRevoked).not.toHaveBeenCalled();
    });

    it.each([new Error('Revoke failed.'), forbidden])('does not notify on a failed or denied revoke', async error => {
      // Arrange
      const onRevoked = vi.fn();
      vi.mocked(revokeWorkspaceMembership).mockRejectedValue(error);
      render(<MembershipAdministrationPage {...props} onRevoked={onRevoked} />);
      await loaded();
      fireEvent.click(screen.getByRole('button', { name: /Revoke membership for/ }));

      // Act
      fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' }));
      await screen.findByRole('alert');

      // Assert
      expect(onRevoked).not.toHaveBeenCalled();
      expect(screen.queryByText('Membership revoked.')).not.toBeInTheDocument();
    });

    it('does not notify when administration is denied before mounting', () => {
      // Arrange
      const onRevoked = vi.fn();

      // Act
      render(<MembershipAdministrationPage {...props} canManageMemberships={false} onRevoked={onRevoked} />);

      // Assert
      expect(screen.getByRole('alert')).toHaveTextContent('Access denied');
      expect(revokeWorkspaceMembership).not.toHaveBeenCalled();
      expect(onRevoked).not.toHaveBeenCalled();
    });

    it.each(['unmount', 'permission withdrawn', 'tenant changed'] as const)(
      'does not notify for a pending revoke after %s',
      async transition => {
        // Arrange
        const revoke = deferred<void>();
        const onRevoked = vi.fn();
        const nextOnRevoked = vi.fn();
        vi.mocked(revokeWorkspaceMembership).mockReturnValue(revoke.promise);
        const { rerender, unmount } = render(<MembershipAdministrationPage {...props} onRevoked={onRevoked} />);
        await loaded();
        fireEvent.click(screen.getByRole('button', { name: /Revoke membership for/ }));
        fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' }));

        // Act
        if (transition === 'unmount') unmount();
        if (transition === 'permission withdrawn') {
          rerender(<MembershipAdministrationPage {...props} canManageMemberships={false} onRevoked={onRevoked} />);
        }
        if (transition === 'tenant changed') {
          vi.mocked(getMembershipPersons).mockResolvedValue([]);
          vi.mocked(getWorkspaceMemberships).mockResolvedValue({ items: [], total: 0 });
          rerender(<MembershipAdministrationPage {...props} tenantId={otherTenantId} tenantName="Organization Beta"
            onRevoked={nextOnRevoked} />);
        }
        await act(async () => revoke.resolve(undefined));

        // Assert
        expect(revokeWorkspaceMembership).toHaveBeenCalledExactlyOnceWith(tenantId, membership.id);
        expect(onRevoked).not.toHaveBeenCalled();
        expect(nextOnRevoked).not.toHaveBeenCalled();
        expect(screen.queryByText('Membership revoked.')).not.toBeInTheDocument();
        expect(getWorkspaceMemberships).toHaveBeenCalledTimes(transition === 'tenant changed' ? 2 : 1);
      },
    );

    it('notifies after successful revoke even if refreshing the list subsequently returns 403', async () => {
      // Arrange
      const onRevoked = vi.fn();
      render(<MembershipAdministrationPage {...props} onRevoked={onRevoked} />);
      await loaded();
      vi.mocked(getWorkspaceMemberships).mockRejectedValue(forbidden);
      fireEvent.click(screen.getByRole('button', { name: /Revoke membership for/ }));

      // Act
      fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' }));
      await screen.findByRole('alert');

      // Assert
      expect(onRevoked).toHaveBeenCalledExactlyOnceWith(membership);
      expect(screen.getByRole('alert')).toHaveTextContent('Access denied');
    });

    it('uses the latest callback when it changes while the same tenant revoke is pending', async () => {
      // Arrange
      const revoke = deferred<void>();
      const onRevoked = vi.fn();
      const nextOnRevoked = vi.fn();
      vi.mocked(revokeWorkspaceMembership).mockReturnValue(revoke.promise);
      const { rerender } = render(<MembershipAdministrationPage {...props} onRevoked={onRevoked} />);
      await loaded();
      fireEvent.click(screen.getByRole('button', { name: /Revoke membership for/ }));
      fireEvent.click(screen.getByRole('button', { name: 'Confirm revocation' }));

      // Act
      rerender(<MembershipAdministrationPage {...props} onRevoked={nextOnRevoked} />);
      await act(async () => revoke.resolve(undefined));

      // Assert
      expect(onRevoked).not.toHaveBeenCalled();
      expect(nextOnRevoked).toHaveBeenCalledExactlyOnceWith(membership);
    });
  });

  it('provides accessible form controls and inline revocation confirmation', async () => {
    // Arrange
    const { container } = render(<MembershipAdministrationPage {...props} />);
    await loaded();
    fireEvent.click(screen.getByRole('button', { name: /Revoke membership for/ }));

    // Act
    // Layout is isolated by the unit fixture; visual contrast still requires the real-browser check.
    const result = await axe(container, { rules: { 'color-contrast': { enabled: false } } });

    // Assert
    expect(result.violations).toEqual([]);
    expect(screen.getByRole('button', { name: 'Confirm revocation' })).toHaveFocus();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel revocation' }));
    expect(screen.getByRole('button', { name: /Revoke membership for/ })).toHaveFocus();
  });
});
