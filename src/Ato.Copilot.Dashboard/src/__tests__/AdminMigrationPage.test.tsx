/**
 * UF-019 — AdminMigrationPage: typed-confirmation guard tests.
 *
 * Validates that the Confirm Migration button is:
 *   - disabled until BOTH the name input matches AND the checkbox is checked
 *   - enabled only once both conditions are met
 *   - disabled again if either condition is un-satisfied
 *
 * Also validates the API call sends the X-Admin-Confirm-Migration header.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { LoginConfig } from '../features/auth/types';
import AdminMigrationPage from '../pages/AdminMigrationPage';

const DEPLOYMENT_NAME = 'ACME ATO Copilot';

// ─── Mocks ──────────────────────────────────────────────────────────────────

vi.mock('../features/auth/LoginConfigContext', () => ({
  useLoginConfig: (): LoginConfig => ({
    branding: {
      deploymentName: DEPLOYMENT_NAME,
      logoUrl: null,
      supportEmail: null,
    },
    defaultMethod: 'Cac',
    enabledMethods: [{ id: 'Cac', displayName: 'Sign in with CAC/PIV' }],
    cloud: 'AzureUSGovernment',
    idleTimeoutMinutes: 30,
    rememberTenantCookieDays: 30,
    simulation: null,
    msal: {
      clientId: 'cid',
      authority: 'https://login.microsoftonline.us/tid',
      redirectUri: 'http://localhost/login/callback',
      postLogoutRedirectUri: 'http://localhost/login',
    },
  }),
}));

vi.mock('../components/layout/PageLayout', () => ({
  default: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));
vi.mock('../components/layout/PageHero', () => ({
  default: () => <div data-testid="page-hero" />,
}));

const mockGet = vi.fn();
const mockPost = vi.fn();
vi.mock('../api/client', () => ({
  default: {
    get: (...args: unknown[]) => mockGet(...args),
    post: (...args: unknown[]) => mockPost(...args),
  },
}));

// ─── Helpers ────────────────────────────────────────────────────────────────

const PREVIEW_WITH_MISSING = {
  data: {
    tables: [
      {
        tableName: 'ControlImplementations',
        totalRows: 100,
        rowsMissingTenant: 50,
        rowsAssignedByOverride: 0,
        rowsAssignedToDefault: 0,
      },
    ],
  },
};

async function renderAndOpenConfirm() {
  mockGet.mockResolvedValue(PREVIEW_WITH_MISSING);
  render(<AdminMigrationPage />);

  // Wait for preview to load.
  await waitFor(() => expect(screen.getByText('ControlImplementations')).toBeDefined());

  // Open the confirmation panel.
  const migrateBtn = screen.getByRole('button', { name: /migrate to multitenant/i });
  fireEvent.click(migrateBtn);

  return screen.getByRole('button', { name: /confirm migration/i }) as HTMLButtonElement;
}

// ─── Tests ───────────────────────────────────────────────────────────────────

describe('AdminMigrationPage — UF-019 typed-confirmation guard', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows the Confirm button as disabled when the panel first opens', async () => {
    const confirmBtn = await renderAndOpenConfirm();
    expect(confirmBtn.disabled).toBe(true);
    expect(confirmBtn.getAttribute('aria-disabled')).toBe('true');
  });

  it('keeps the Confirm button disabled when ONLY the name is typed correctly', async () => {
    const confirmBtn = await renderAndOpenConfirm();
    const nameInput = screen.getByPlaceholderText(DEPLOYMENT_NAME) as HTMLInputElement;

    fireEvent.change(nameInput, { target: { value: DEPLOYMENT_NAME } });

    expect(confirmBtn.disabled).toBe(true);
  });

  it('keeps the Confirm button disabled when ONLY the checkbox is checked', async () => {
    const confirmBtn = await renderAndOpenConfirm();
    const checkbox = screen.getByRole('checkbox') as HTMLInputElement;

    fireEvent.click(checkbox);

    expect(confirmBtn.disabled).toBe(true);
  });

  it('enables the Confirm button when BOTH name matches AND checkbox is checked', async () => {
    const confirmBtn = await renderAndOpenConfirm();
    const nameInput = screen.getByPlaceholderText(DEPLOYMENT_NAME) as HTMLInputElement;
    const checkbox = screen.getByRole('checkbox') as HTMLInputElement;

    fireEvent.change(nameInput, { target: { value: DEPLOYMENT_NAME } });
    fireEvent.click(checkbox);

    expect(confirmBtn.disabled).toBe(false);
    expect(confirmBtn.getAttribute('aria-disabled')).toBe('false');
  });

  it('is case-insensitive: accepts all-lowercase deployment name', async () => {
    const confirmBtn = await renderAndOpenConfirm();
    const nameInput = screen.getByPlaceholderText(DEPLOYMENT_NAME) as HTMLInputElement;
    const checkbox = screen.getByRole('checkbox') as HTMLInputElement;

    fireEvent.change(nameInput, { target: { value: DEPLOYMENT_NAME.toLowerCase() } });
    fireEvent.click(checkbox);

    expect(confirmBtn.disabled).toBe(false);
  });

  it('disables the Confirm button again when the name is cleared after being correct', async () => {
    const confirmBtn = await renderAndOpenConfirm();
    const nameInput = screen.getByPlaceholderText(DEPLOYMENT_NAME) as HTMLInputElement;
    const checkbox = screen.getByRole('checkbox') as HTMLInputElement;

    fireEvent.change(nameInput, { target: { value: DEPLOYMENT_NAME } });
    fireEvent.click(checkbox);
    expect(confirmBtn.disabled).toBe(false);

    fireEvent.change(nameInput, { target: { value: '' } });
    expect(confirmBtn.disabled).toBe(true);
  });

  it('sends the X-Admin-Confirm-Migration header with the typed value on POST', async () => {
    mockPost.mockResolvedValue({
      data: {
        startedAt: new Date().toISOString(),
        completedAt: new Date().toISOString(),
        defaultTenantId: '00000000-0000-0000-0000-000000000001',
        tables: [],
        rlsInstalled: false,
        error: null,
      },
    });

    const confirmBtn = await renderAndOpenConfirm();
    const nameInput = screen.getByPlaceholderText(DEPLOYMENT_NAME) as HTMLInputElement;
    const checkbox = screen.getByRole('checkbox') as HTMLInputElement;

    fireEvent.change(nameInput, { target: { value: DEPLOYMENT_NAME } });
    fireEvent.click(checkbox);
    fireEvent.click(confirmBtn);

    await waitFor(() => expect(mockPost).toHaveBeenCalledOnce());

    const [, , config] = mockPost.mock.calls[0] as [unknown, unknown, { headers: Record<string, string> }];
    expect(config.headers['X-Admin-Confirm-Migration']).toBe(DEPLOYMENT_NAME);
  });

  it('Cancel button closes the confirmation panel and resets state', async () => {
    await renderAndOpenConfirm();
    const nameInput = screen.getByPlaceholderText(DEPLOYMENT_NAME) as HTMLInputElement;
    const checkbox = screen.getByRole('checkbox') as HTMLInputElement;

    fireEvent.change(nameInput, { target: { value: DEPLOYMENT_NAME } });
    fireEvent.click(checkbox);

    const cancelBtn = screen.getByRole('button', { name: /cancel/i });
    fireEvent.click(cancelBtn);

    // Confirmation panel should be gone.
    expect(screen.queryByPlaceholderText(DEPLOYMENT_NAME)).toBeNull();
  });
});
