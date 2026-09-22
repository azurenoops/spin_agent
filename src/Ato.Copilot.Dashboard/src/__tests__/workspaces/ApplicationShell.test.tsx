import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { EventType, type AccountInfo, type EventMessage } from '@azure/msal-browser';
import axios from 'axios';
import App from '../../App';

vi.mock('axios', () => ({ default: { get: vi.fn() } }));
const auth = vi.hoisted(() => ({
  accounts: [] as AccountInfo[],
  active: null as AccountInfo | null,
  callbacks: new Set<(event: EventMessage) => void>(),
}));
const msal = {
  loginRedirect: vi.fn(),
  getAllAccounts: () => auth.accounts,
  getActiveAccount: () => auth.active,
  addEventCallback: (callback: (event: EventMessage) => void) => {
    auth.callbacks.add(callback);
    return 'account-listener';
  },
  removeEventCallback: () => { auth.callbacks.clear(); },
};
vi.mock('@azure/msal-react', () => ({ useMsal: () => ({ instance: msal, accounts: auth.accounts }) }));
vi.mock('../../ApplicationFrame', () => ({ default: ({ children }: { children: React.ReactNode }) => <div data-testid="private-frame">{children}</div> }));
vi.mock('../../ApplicationRoutes', () => ({ default: () => <h1>Authorized application</h1> }));
vi.mock('../../hooks/useSettings', () => ({
  SettingsContext: { Provider: ({ children }: { children: React.ReactNode }) => children },
  useSettingsProvider: () => ({}),
}));
vi.mock('../../features/auth/LoginPage', () => ({ default: () => <h1>Public login</h1> }));
vi.mock('../../features/auth/LoginCallbackPage', () => ({ default: () => <h1>Public callback</h1> }));
vi.mock('../../features/auth/LoginErrorPage', () => ({ default: () => <h1>Public error</h1> }));
vi.mock('../../features/auth/TenantPickerPage', () => ({ default: () => <h1>Choose a workspace</h1> }));

const permissions = { canManageMemberships: false, canManageOrganization: false, canAccessCsp: false };
const me = {
  oid: 'mission-owner', displayName: 'Owner', homeTenant: null, effectiveTenant: null,
  workspace: { kind: 'organization', tenantId: 'org-a', displayName: 'Organization A', mode: 'ordinary', personId: 'person-a', roles: ['MissionOwner'], permissions },
};

function account(id: string, tenantId = 'directory-a'): AccountInfo {
  return { homeAccountId: `${id}.${tenantId}`, localAccountId: id, tenantId, environment: 'login.microsoftonline.com', username: `${id}@example.test` };
}

beforeEach(() => {
  vi.mocked(axios.get).mockReset();
  auth.accounts = [];
  auth.active = null;
  auth.callbacks.clear();
});

describe('authenticated application shell', () => {
  it.each(['/login', '/login/callback', '/login/error', '/login/', '/LOGIN/ERROR'])('keeps %s outside identity and private providers', async path => {
    // Arrange / Act
    render(<MemoryRouter initialEntries={[path]}><App /></MemoryRouter>);
    // Assert
    expect(screen.getByRole('heading')).toHaveTextContent('Public');
    expect(axios.get).not.toHaveBeenCalled();
    expect(screen.queryByTestId('private-frame')).not.toBeInTheDocument();
  });

  it('shares one identity probe and gates canonical children until it resolves', async () => {
    // Arrange
    let finish!: (value: unknown) => void;
    vi.mocked(axios.get).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
    render(<MemoryRouter initialEntries={['/workspaces/organizations/org-a']}><App /></MemoryRouter>);
    expect(screen.queryByTestId('private-frame')).not.toBeInTheDocument();
    // Act
    finish({ data: { status: 'success', data: me } });
    // Assert
    await screen.findByText('Authorized application');
    expect(axios.get).toHaveBeenCalledTimes(1);
  });

  it('denies a mismatched server context without rendering private providers', async () => {
    // Arrange
    vi.mocked(axios.get).mockResolvedValue({ data: { status: 'success', data: me } });
    // Act
    render(<MemoryRouter initialEntries={['/workspaces/organizations/org-b']}><App /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('did not authorize');
    expect(screen.queryByTestId('private-frame')).not.toBeInTheDocument();
  });

  it('shows scope recovery on scoped 403 instead of claiming no tenant assignment', async () => {
    // Arrange
    vi.mocked(axios.get).mockRejectedValue(Object.assign(new Error('Denied target'), { response: { status: 403 } }));
    // Act
    render(<MemoryRouter initialEntries={['/workspaces/organizations/org-b']}><App /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Denied target');
    fireEvent.click(screen.getByRole('link', { name: 'Choose a workspace' }));
    await waitFor(() => expect(axios.get).toHaveBeenCalledTimes(2));
    expect(screen.queryByText('Authorized application')).not.toBeInTheDocument();
  });

  it('clears private children and refetches when the primary account changes at the same URL', async () => {
    // Arrange
    auth.accounts = [account('owner-a')];
    vi.mocked(axios.get).mockResolvedValueOnce({ data: { status: 'success', data: me } });
    const tree = <MemoryRouter initialEntries={['/workspaces/organizations/org-a']}><App /></MemoryRouter>;
    const { rerender } = render(tree);
    await screen.findByText('Authorized application');
    vi.mocked(axios.get).mockReturnValue(new Promise(() => undefined));
    // Act
    auth.accounts = [account('owner-b')];
    rerender(<MemoryRouter initialEntries={['/workspaces/organizations/org-a']}><App /></MemoryRouter>);
    // Assert
    expect(screen.queryByTestId('private-frame')).not.toBeInTheDocument();
    expect(axios.get).toHaveBeenCalledTimes(2);
  });

  it('invalidates on active-account events even when the account list and URL remain unchanged', async () => {
    // Arrange
    auth.accounts = [account('owner-a'), account('owner-b')];
    auth.active = auth.accounts[0]!;
    vi.mocked(axios.get).mockResolvedValueOnce({ data: { status: 'success', data: me } });
    render(<MemoryRouter initialEntries={['/workspaces/organizations/org-a']}><App /></MemoryRouter>);
    await screen.findByText('Authorized application');
    vi.mocked(axios.get).mockReturnValue(new Promise(() => undefined));
    // Act
    act(() => {
      auth.active = auth.accounts[1]!;
      for (const callback of auth.callbacks) callback({
        eventType: EventType.ACTIVE_ACCOUNT_CHANGED, interactionType: null, payload: null, error: null, timestamp: Date.now(),
      });
    });
    // Assert
    expect(screen.queryByTestId('private-frame')).not.toBeInTheDocument();
    expect(axios.get).toHaveBeenCalledTimes(2);
  });

  it('includes the account directory even when its local object ID stays the same', async () => {
    // Arrange
    auth.accounts = [account('same-oid', 'directory-a')];
    vi.mocked(axios.get).mockResolvedValueOnce({ data: { status: 'success', data: me } });
    const { rerender } = render(<MemoryRouter initialEntries={['/workspaces/organizations/org-a']}><App /></MemoryRouter>);
    await screen.findByText('Authorized application');
    vi.mocked(axios.get).mockReturnValue(new Promise(() => undefined));
    // Act
    auth.accounts = [account('same-oid', 'directory-b')];
    rerender(<MemoryRouter initialEntries={['/workspaces/organizations/org-a']}><App /></MemoryRouter>);
    // Assert
    expect(screen.queryByTestId('private-frame')).not.toBeInTheDocument();
    expect(axios.get).toHaveBeenCalledTimes(2);
  });
});
