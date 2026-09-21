import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { endImpersonation, readImpersonation } from '../../features/tenancy/api';

const client = vi.hoisted(() => ({
  delete: vi.fn(),
  interceptors: { request: { use: vi.fn() } },
}));
vi.mock('axios', () => ({ default: { create: () => client } }));
vi.mock('../../features/auth/interceptors', () => ({ attachAuthInterceptor: vi.fn() }));

const state = { tenantId: 'org-alpha', displayName: 'Organization Alpha', expiresAt: '2099-01-01T00:00:00Z' };

beforeEach(() => {
  client.delete.mockReset();
  sessionStorage.setItem('ato-impersonation', JSON.stringify(state));
});
afterEach(() => {
  sessionStorage.clear();
  vi.restoreAllMocks();
});

describe('support exit state', () => {
  it('retains the local mirror and does not refetch away the retry error when DELETE fails', async () => {
    // Arrange
    const failure = new Error('Support exit unavailable');
    client.delete.mockRejectedValue(failure);
    const dispatch = vi.spyOn(window, 'dispatchEvent');

    // Act
    const exit = endImpersonation();

    // Assert
    await expect(exit).rejects.toBe(failure);
    expect(readImpersonation()).toEqual(state);
    expect(dispatch).not.toHaveBeenCalled();
  });

  it('clears the local mirror and notifies identity consumers after a successful DELETE', async () => {
    // Arrange
    client.delete.mockResolvedValue({ status: 204 });
    const dispatch = vi.spyOn(window, 'dispatchEvent');

    // Act
    await endImpersonation();

    // Assert
    expect(client.delete).toHaveBeenCalledWith('/tenants/impersonation');
    expect(readImpersonation()).toBeNull();
    expect(dispatch).toHaveBeenCalledWith(expect.objectContaining({ type: 'ato:tenant-changed' }));
  });

  it('does not clear support state for an unexpected success-shaped response', async () => {
    // Arrange
    client.delete.mockResolvedValue({ status: 200, data: { status: 'error' } });
    const dispatch = vi.spyOn(window, 'dispatchEvent');

    // Act
    const exit = endImpersonation();

    // Assert
    await expect(exit).rejects.toThrow('Unexpected support exit response.');
    expect(readImpersonation()).toEqual(state);
    expect(dispatch).not.toHaveBeenCalled();
  });
});
