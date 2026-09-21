import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import LoginCallbackPage from '../../features/auth/LoginCallbackPage';

// ─── Mocks ──────────────────────────────────────────────────────────────

const handleRedirectPromise = vi.fn();
const navigate = vi.fn();

vi.mock('axios', () => ({
  default: { get: vi.fn().mockResolvedValue({ data: { status: 'success', data: { tenantMemberships: [], isCspAdmin: false } } }) },
}));

vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({
    instance: {
      handleRedirectPromise,
      getAllAccounts: () => [],
    },
  }),
}));

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return {
    ...actual,
    useNavigate: () => navigate,
  };
});

function renderPage() {
  return render(
    <MemoryRouter>
      <LoginCallbackPage />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  handleRedirectPromise.mockReset();
  navigate.mockReset();
});

// ─── Tests ──────────────────────────────────────────────────────────────

describe('LoginCallbackPage', () => {
  it('calls instance.handleRedirectPromise on mount', async () => {
    handleRedirectPromise.mockResolvedValueOnce(null);

    renderPage();

    await waitFor(() => {
      expect(handleRedirectPromise).toHaveBeenCalledTimes(1);
    });
  });

  it('navigates to the resolved state path when state is present', async () => {
    handleRedirectPromise.mockResolvedValueOnce({
      state: '/systems/abc',
      account: { homeAccountId: 'oid-1' },
    });

    renderPage();

    await waitFor(() => {
      expect(navigate).toHaveBeenCalledWith('/systems/abc', { replace: true });
    });
  });

  it('navigates to "/" when handleRedirectPromise resolves with no state', async () => {
    handleRedirectPromise.mockResolvedValueOnce({
      state: null,
      account: { homeAccountId: 'oid-1' },
    });

    renderPage();

    await waitFor(() => {
      expect(navigate).toHaveBeenCalledWith('/', { replace: true });
    });
  });

  it('navigates to "/" when handleRedirectPromise resolves null (no redirect)', async () => {
    handleRedirectPromise.mockResolvedValueOnce(null);

    renderPage();

    await waitFor(() => {
      expect(navigate).toHaveBeenCalledWith('/', { replace: true });
    });
  });

  it('navigates to /login/error with errorClass on rejection', async () => {
    handleRedirectPromise.mockRejectedValueOnce(
      new Error('AADSTS50105: account disabled'),
    );

    renderPage();

    await waitFor(() => {
      expect(navigate).toHaveBeenCalled();
    });
    const target = navigate.mock.calls[0]?.[0] as string;
    expect(target).toMatch(/^\/login\/error\?errorClass=/);
  });

  it('renders a loading spinner placeholder while resolving', () => {
    handleRedirectPromise.mockReturnValueOnce(new Promise(() => undefined));

    const { getByText } = renderPage();

    expect(getByText(/signing you in/i)).toBeInTheDocument();
  });
});
