import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import NotificationSettingsPanel from '../../features/notifications/NotificationSettingsPanel';

const mocks = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn(), useMe: vi.fn() }));
vi.mock('../../api/client', () => ({ default: { get: mocks.get, put: mocks.put } }));
vi.mock('../../features/auth/useMe', () => ({ useMe: mocks.useMe }));
vi.mock('../../features/auth/msalInstance', () => ({
  getMsalInstance: () => ({ getAllAccounts: () => [] }),
}));
const preferences = {
  poamOverdueAlerts: true, atoExpirationAlerts: false, complianceDriftAlerts: true, alertDaysBefore: 14,
};

describe('notification preferences contract', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.useMe.mockReturnValue({
      data: { oid: 'actor', directoryTenantId: 'directory', workspace: { kind: 'organization', tenantId: 'org-a', mode: 'ordinary' } },
      isLoading: false, error: null, refetch: vi.fn(),
    });
    mocks.get.mockResolvedValue({ data: preferences });
    mocks.put.mockResolvedValue({ data: preferences });
  });
  afterEach(cleanup);

  it('loads and saves only the server-supported preferences without any account requirement', async () => {
    // Arrange
    render(<NotificationSettingsPanel />);
    const overdue = await screen.findByRole('checkbox', { name: 'POA&M overdue alerts' });
    // Act
    fireEvent.click(overdue);
    fireEvent.change(screen.getByRole('spinbutton', { name: 'Warning days before expiration' }), { target: { value: '7' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save preferences' }));
    // Assert
    await waitFor(() => expect(mocks.put).toHaveBeenCalledWith('/notifications/preferences',
      { ...preferences, poamOverdueAlerts: false, alertDaysBefore: 7 },
      expect.objectContaining({ signal: expect.any(AbortSignal) })));
    expect(screen.queryByText('Microsoft Teams')).not.toBeInTheDocument();
  });

  it('does not manufacture editable defaults after a denied read', async () => {
    // Arrange
    mocks.get.mockRejectedValue(new Error('Membership revoked'));
    render(<NotificationSettingsPanel />);
    // Act
    await screen.findByRole('alert');
    // Assert
    expect(screen.queryByRole('button', { name: 'Save preferences' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
    expect(mocks.put).not.toHaveBeenCalled();
  });

  it('rejects a malformed successful payload instead of saving undefined fields', async () => {
    // Arrange
    mocks.get.mockResolvedValue({ data: { emailEnabled: true } });
    // Act
    render(<NotificationSettingsPanel />);
    // Assert
    await screen.findByRole('alert');
    expect(screen.queryByRole('button', { name: 'Save preferences' })).not.toBeInTheDocument();
  });

  it('reports a failed save without a success indicator', async () => {
    // Arrange
    mocks.put.mockRejectedValue(new Error('Save denied'));
    render(<NotificationSettingsPanel />);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Save preferences' }));
    // Assert
    await screen.findByRole('alert');
    expect(screen.queryByText('Preferences saved.')).not.toBeInTheDocument();
  });

  it('aborts pending loads on unmount', async () => {
    // Arrange
    let resolve!: (value: { data: typeof preferences }) => void;
    mocks.get.mockReturnValue(new Promise(r => { resolve = r; }));
    const { unmount } = render(<NotificationSettingsPanel />);
    const signal = mocks.get.mock.calls[0]?.[1]?.signal as AbortSignal | undefined;
    // Act
    unmount();
    await act(async () => { resolve({ data: preferences }); });
    // Assert
    expect(signal?.aborted).toBe(true);
  });

  it('waits for identity resolution and offers identity recovery instead of fetching preferences', () => {
    // Arrange
    const refetch = vi.fn();
    mocks.useMe.mockReturnValue({ data: null, isLoading: true, error: null, refetch });
    const { rerender } = render(<NotificationSettingsPanel />);
    expect(screen.getByRole('status')).toHaveTextContent('Resolving');
    // Act
    mocks.useMe.mockReturnValue({ data: null, isLoading: false, error: new Error('Denied'), refetch });
    rerender(<NotificationSettingsPanel />);
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    // Assert
    expect(refetch).toHaveBeenCalledOnce();
    expect(mocks.get).not.toHaveBeenCalled();
  });

  it('retries a failed load and exposes successful saving', async () => {
    // Arrange
    mocks.get.mockRejectedValueOnce(new Error('Unavailable'));
    render(<NotificationSettingsPanel />);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Retry' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Save preferences' }));
    // Assert
    await screen.findByText('Preferences saved.');
    expect(mocks.get).toHaveBeenCalledTimes(2);
  });

  it('invalidates the form on organization change before a pending save completes', async () => {
    // Arrange
    let resolve!: (value: { data: typeof preferences }) => void;
    mocks.put.mockReturnValue(new Promise(r => { resolve = r; }));
    const { rerender } = render(<NotificationSettingsPanel />);
    fireEvent.click(await screen.findByRole('button', { name: 'Save preferences' }));
    await waitFor(() => expect(mocks.put).toHaveBeenCalledOnce());
    const signal = mocks.put.mock.calls[0]?.[2].signal as AbortSignal;
    // Act
    mocks.useMe.mockReturnValue({
      data: { oid: 'actor', directoryTenantId: 'directory', workspace: { kind: 'organization', tenantId: 'org-b', mode: 'ordinary' } },
      isLoading: false, error: null, refetch: vi.fn(),
    });
    rerender(<NotificationSettingsPanel />);
    await act(async () => { resolve({ data: preferences }); });
    // Assert
    expect(signal.aborted).toBe(true);
    expect(screen.queryByText('Preferences saved.')).not.toBeInTheDocument();
    expect(mocks.get).toHaveBeenCalledTimes(2);
  });
});
