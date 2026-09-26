import axios from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { packageRequest } from '../../features/package-imports/request';

vi.mock('axios', async importOriginal => {
  const actual = await importOriginal<typeof import('axios')>();
  return { ...actual, default: { ...actual.default, request: vi.fn() } };
});
beforeEach(() => vi.clearAllMocks());

describe('provider package error contract', () => {
  it.each([403, 409, 422, 413])('preserves status %s, code and corrective suggestion', async status => {
    // Arrange
    vi.mocked(axios.request).mockRejectedValue({
      isAxiosError: true, message: 'Request failed',
      response: { status, data: { error: { errorCode: 'PACKAGE_CONFLICT', message: 'Source revision changed.', suggestion: 'Reload and review the current revision.' } } },
    });
    // Act
    const request = packageRequest({ method: 'GET', url: '/api/csp/package-imports/id' });
    // Assert
    await expect(request).rejects.toMatchObject({
      status, code: 'PACKAGE_CONFLICT', message: 'Source revision changed. Reload and review the current revision.',
    });
  });

  it('rejects a malformed response instead of manufacturing a receipt', async () => {
    // Arrange
    vi.mocked(axios.request).mockResolvedValue({ status: 202, data: { status: 'success' } });
    // Act
    const request = packageRequest({ method: 'POST', url: '/api/csp/onboarding/atos/upload' });
    // Assert
    await expect(request).rejects.toThrow('The server did not return package data.');
  });

  it('surfaces success-shaped envelopes that explicitly report an error', async () => {
    // Arrange
    vi.mocked(axios.request).mockResolvedValue({ status: 200, data: { status: 'error', data: {}, error: { code: 'DENIED', message: 'Access denied.' } } });
    // Act
    const request = packageRequest({ method: 'GET', url: '/api/csp/package-imports' });
    // Assert
    await expect(request).rejects.toMatchObject({ code: 'DENIED', message: 'Access denied.' });
  });
});
