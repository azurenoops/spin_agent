import { stubbedPublicClientApplication } from '@azure/msal-browser';
import { describe, expect, it, vi } from 'vitest';
import { acquireBearer, setMsalInstance } from '../../features/auth/msalInstance';

const first = {
  homeAccountId: 'first', localAccountId: 'object-first', tenantId: 'directory-a',
  username: 'first@example.invalid', environment: 'login.microsoftonline.com',
};
const selected = { ...first, homeAccountId: 'selected', localAccountId: 'object-selected' };

describe('active account token selection', () => {
  it.each([true, false])('uses active account when present (active=%s)', async active => {
    // Arrange
    const acquireTokenSilent = vi.fn().mockResolvedValue({ accessToken: 'synthetic-token' });
    setMsalInstance({
      ...stubbedPublicClientApplication,
      getAllAccounts: () => [first, selected],
      getActiveAccount: () => active ? selected : null,
      acquireTokenSilent,
    });

    // Act
    const token = await acquireBearer();

    // Assert
    expect(token).toBe('synthetic-token');
    expect(acquireTokenSilent).toHaveBeenCalledWith(expect.objectContaining({ account: active ? selected : first }));
  });

  it('does not manufacture an account for a cookie-only session', async () => {
    // Arrange
    const acquireTokenSilent = vi.fn();
    setMsalInstance({
      ...stubbedPublicClientApplication, getAllAccounts: () => [], getActiveAccount: () => null, acquireTokenSilent,
    });

    // Act
    const token = await acquireBearer();

    // Assert
    expect(token).toBe('');
    expect(acquireTokenSilent).not.toHaveBeenCalled();
  });
});
