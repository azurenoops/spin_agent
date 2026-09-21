import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  createMembershipPerson,
  getMembershipPersons,
  getWorkspaceMemberships,
  getWorkspaceOptions,
  grantWorkspaceMembership,
  revokeWorkspaceMembership,
  workspaceErrorMessage,
} from '../../features/workspaces/api';

const http = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), delete: vi.fn() }));
vi.mock('axios', () => ({ default: http }));

beforeEach(() => vi.clearAllMocks());

describe('workspace administration API contract', () => {
  it('loads paginated authorized workspace options', async () => {
    // Arrange
    const data = { items: [], total: 0 };
    http.get.mockResolvedValue({ data: { status: 'success', data } });

    // Act
    const result = await getWorkspaceOptions(2);

    // Assert
    expect(result).toEqual(data);
    expect(http.get).toHaveBeenCalledWith('/api/auth/workspaces', { params: { page: 2, pageSize: 50 } });
  });

  it('keeps contact creation separate from membership granting', async () => {
    // Arrange
    const person = { id: 'person-1', displayName: 'Synthetic Member', email: 'member@example.invalid' };
    http.post.mockResolvedValue({ data: { status: 'success', data: person } });

    // Act
    const result = await createMembershipPerson('org-alpha', { displayName: person.displayName, email: person.email });

    // Assert
    expect(result).toEqual(person);
    expect(http.post).toHaveBeenCalledOnce();
    expect(http.post).toHaveBeenCalledWith('/api/tenants/org-alpha/membership-persons',
      { displayName: person.displayName, email: person.email });
  });

  it('searches organization contacts', async () => {
    // Arrange
    http.get.mockResolvedValue({ data: { status: 'success', data: [] } });

    // Act
    await getMembershipPersons('org-alpha', 'member');

    // Assert
    expect(http.get).toHaveBeenCalledWith('/api/tenants/org-alpha/membership-persons', { params: { query: 'member' } });
  });

  it('lists memberships and grants only the explicit directory/object/person association', async () => {
    // Arrange
    const grant = { directoryTenantId: 'directory-a', objectId: 'object-a', personId: 'person-a' };
    http.get.mockResolvedValue({ data: { status: 'success', data: { items: [], total: 0 } } });
    http.post.mockResolvedValue({ data: { status: 'success', data: { id: 'membership-a', ...grant } } });

    // Act
    await getWorkspaceMemberships('org-alpha');
    const result = await grantWorkspaceMembership('org-alpha', grant);

    // Assert
    expect(http.get).toHaveBeenCalledWith('/api/tenants/org-alpha/memberships', { params: { page: 1, pageSize: 50 } });
    expect(http.post).toHaveBeenCalledWith('/api/tenants/org-alpha/memberships', grant);
    expect(result.id).toBe('membership-a');
  });

  it('accepts only the documented revocation response', async () => {
    // Arrange
    http.delete.mockResolvedValueOnce({ status: 204 }).mockResolvedValueOnce({ status: 200 });

    // Act
    await revokeWorkspaceMembership('org-alpha', 'membership-a');
    const invalid = revokeWorkspaceMembership('org-alpha', 'membership-a');

    // Assert
    expect(http.delete).toHaveBeenCalledWith('/api/tenants/org-alpha/memberships/membership-a');
    await expect(invalid).rejects.toThrow('Unexpected membership revocation response.');
  });

  it.each([{ status: 'success' }, { data: [] }, { status: 'success', data: null }])(
    'rejects a malformed success envelope',
    async body => {
      // Arrange
      http.get.mockResolvedValue({ data: body });

      // Act
      const request = getWorkspaceOptions();

      // Assert
      await expect(request).rejects.toThrow('Unexpected workspace API response.');
    },
  );

  it('surfaces a structured API rejection instead of returning an empty successful result', async () => {
    // Arrange
    http.get.mockResolvedValue({ data: {
      status: 'error', error: { errorCode: 'FORBIDDEN', message: 'Membership administration is not permitted.' },
    } });

    // Act
    const request = getWorkspaceMemberships('org-alpha');

    // Assert
    await expect(request).rejects.toThrow('Membership administration is not permitted.');
  });

  it.each([
    [{ response: { data: { status: 'error', error: { message: 'Access revoked.' } } } }, 'Access revoked.'],
    [new Error('Connection unavailable.'), 'Connection unavailable.'],
    [null, 'Unable to complete the workspace request.'],
  ])('formats workspace request errors for explicit UI feedback', (error, expected) => {
    // Arrange
    const failure = error;

    // Act
    const message = workspaceErrorMessage(failure);

    // Assert
    expect(message).toBe(expected);
  });
});
