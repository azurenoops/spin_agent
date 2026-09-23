import { beforeEach, describe, expect, it, vi } from 'vitest';
import axios from 'axios';
import { createOrganization, getOrganizationCreation, getCurrentOrganizationProvisioning, resumeOrganizationProvisioning } from '../../features/workspace-operations/api';

vi.mock('axios', async importOriginal => {
  const actual = await importOriginal<typeof import('axios')>();
  return { ...actual, default: { ...actual.default, request: vi.fn() } };
});
beforeEach(() => vi.resetAllMocks());
describe('organization setup transport', () => {
  it('persists explicit administrator intent with the original creation key', async () => {
    // Arrange
    const body = { displayName: 'Org', initialAdministrator: {
      directoryTenantId: 'directory', objectId: 'object', newPerson: { displayName: 'Admin', email: 'admin@example.mil' },
    } };
    vi.mocked(axios.request).mockResolvedValue({ data: { data: { tenantId: 'org' } } });
    // Act
    await createOrganization(body, 'original-key');
    // Assert
    expect(axios.request).toHaveBeenCalledWith({ method: 'POST', url: '/api/csp/dashboard/tenants', data: body, headers: { 'Idempotency-Key': 'original-key' } });
  });
  it('uses an encoded creation key for read-only recovery', async () => {
    // Arrange
    const controller = new AbortController();
    vi.mocked(axios.request).mockResolvedValue({ data: { data: { tenantId: 'org' } } });
    // Act
    await getOrganizationCreation('key/with slash', controller.signal);
    // Assert
    expect(axios.request).toHaveBeenCalledWith({ method: 'GET', url: '/api/csp/organization-creations/key%2Fwith%20slash', signal: controller.signal });
  });
  it.each([
    [getOrganizationCreation, 'ORGANIZATION_CREATION_NOT_FOUND'],
    [getCurrentOrganizationProvisioning, 'PROVISIONING_NOT_FOUND'],
  ] as const)('distinguishes an absent record from authorization errors for %s', async (read, code) => {
    // Arrange
    vi.mocked(axios.request).mockRejectedValueOnce({ isAxiosError: true,
      response: { status: 404, data: { error: { code, message: 'Not found.' } } } });
    // Act
    const absent = await read('key');
    // Assert
    expect(absent).toBeNull();
    vi.mocked(axios.request).mockRejectedValueOnce({ isAxiosError: true,
      response: { status: 404, data: { error: { code: 'ACCESS_DENIED', message: 'Not available.' } } } });
    await expect(read('key')).rejects.toMatchObject({ code: 'ACCESS_DENIED', status: 404 });
  });
  it('resumes an existing operation with the authored identity, never creation', async () => {
    // Arrange
    const body = { directoryTenantId: 'directory', objectId: 'object', personId: 'person' };
    const controller = new AbortController();
    vi.mocked(axios.request).mockResolvedValue({ data: { data: { administratorState: 'Completed' } } });
    // Act
    await resumeOrganizationProvisioning('org', 'op', body, controller.signal);
    // Assert
    expect(axios.request).toHaveBeenCalledExactlyOnceWith({
      method: 'PATCH', url: '/api/csp/organizations/org/provisioning/op', data: body, signal: controller.signal,
    });
  });
});
