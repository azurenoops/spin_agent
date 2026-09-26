import { cleanup, render, waitFor } from '@testing-library/react';
import axios, { AxiosError, AxiosHeaders } from 'axios';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import RequireAuth from '../../features/auth/RequireAuth';
import { MeContext } from '../../features/auth/useMe';

const { navigate, loginRedirect } = vi.hoisted(() => ({
  navigate: vi.fn(),
  loginRedirect: vi.fn().mockResolvedValue(undefined),
}));

vi.mock('react-router-dom', () => ({ useNavigate: () => navigate }));
vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({ instance: { loginRedirect } }),
}));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  WorkspaceStatus: () => null,
}));

function authError(errorCode: string, status = 401): AxiosError {
  return new AxiosError('Authentication failed', 'ERR_BAD_REQUEST', undefined, undefined, {
    status, statusText: 'Unauthorized', headers: {}, config: { headers: new AxiosHeaders() },
    data: { status: 'error', data: { errorCode } },
  });
}

function renderGuard(shared: boolean, error: AxiosError) {
  vi.spyOn(axios, 'get').mockRejectedValue(error);
  const guard = <RequireAuth><div>Protected content</div></RequireAuth>;
  return render(shared
    ? <MeContext.Provider value={{ data: null, isLoading: false, error, refetch: vi.fn() }}>
      {guard}
    </MeContext.Provider>
    : guard);
}

beforeEach(() => vi.clearAllMocks());
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

describe.each([true, false])('stale simulation recovery (shared identity: %s)', shared => {
  it('returns an obsolete simulation session to explicit login selection', async () => {
    // Arrange
    const error = authError('SIMULATED_IDENTITY_NOT_FOUND');

    // Act
    const view = renderGuard(shared, error);

    // Assert
    await waitFor(() => expect(navigate).toHaveBeenCalledWith(
      '/login?reason=simulation_identity_changed', { replace: true }));
    expect(loginRedirect).not.toHaveBeenCalled();
    expect(view.queryByText('Protected content')).not.toBeInTheDocument();
  });

  it('preserves Entra redirect behavior for other unauthorized sessions', async () => {
    // Arrange
    const error = authError('AUTH_REQUIRED');

    // Act
    renderGuard(shared, error);

    // Assert
    await waitFor(() => expect(loginRedirect).toHaveBeenCalled());
    expect(navigate).not.toHaveBeenCalled();
  });

  it('does not treat an authorization rejection as stale-session recovery', async () => {
    // Arrange
    const error = authError('SIMULATED_IDENTITY_NOT_FOUND', 403);

    // Act
    renderGuard(shared, error);

    // Assert
    await waitFor(() => expect(navigate).toHaveBeenCalledWith(
      '/login/error?errorClass=NoTenantAssignment', { replace: true }));
    expect(loginRedirect).not.toHaveBeenCalled();
  });
});
