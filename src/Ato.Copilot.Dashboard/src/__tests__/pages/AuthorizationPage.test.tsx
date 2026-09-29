import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import AuthorizationPage from '../../pages/AuthorizationPage';
import '../helpers/dialog';

const workspace = vi.hoisted(() => ({
  value: null as { systemAccess: { permissions: { canDecideAuthorization: boolean } } } | null,
}));
const polling = vi.hoisted(() => ({ error: null as Error | null, retry: vi.fn() }));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => workspace.value }));

vi.mock('../../api/client', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}));

vi.mock('../../hooks/usePolling', () => ({
  usePolling: vi.fn(() => ({ data: null, refresh: polling.retry, error: polling.error, loading: false })),
}));

vi.mock('../../hooks/useSettings', () => ({
  useSettings: vi.fn(() => ({ settings: { role: 'AO' } })),
}));
vi.mock('../../features/systems/ExternalDecisionRecords', () => ({
  default: ({ systemId }: { systemId: string }) => <section aria-label="Externally issued decision records">{systemId}</section>,
}));

describe('AuthorizationPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    workspace.value = null;
    polling.error = null;
    vi.mocked(apiClient.post).mockResolvedValue({ data: {} });
  });

  it('distinguishes unavailable decision records from a legitimate not-yet-recorded decision', () => {
    // Arrange
    polling.error = new Error('Decision records unavailable');
    // Act
    render(<MemoryRouter initialEntries={['/systems/system-1/authorization']}><Routes>
      <Route path="/systems/:id/authorization" element={<AuthorizationPage />} /></Routes></MemoryRouter>);
    // Assert
    expect(screen.getByRole('heading', { name: 'Recorded authorization decisions' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Retry decision records' })).toBeVisible();
    expect(screen.queryByText('Prepare now, record the decision when received')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry decision records' }));
    expect(polling.retry).toHaveBeenCalled();
  });

  it('does not infer AO authority from browser settings in a workspace', () => {
    // Arrange
    workspace.value = { systemAccess: { permissions: { canDecideAuthorization: false } } };

    // Act
    render(
      <MemoryRouter initialEntries={['/systems/system-1/authorization']}>
        <Routes><Route path="/systems/:id/authorization" element={<AuthorizationPage />} /></Routes>
      </MemoryRouter>,
    );

    // Assert
    expect(screen.queryByRole('button', { name: 'Issue Authorization' })).not.toBeInTheDocument();
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it('attributes a decision to the authenticated AO instead of request fields', async () => {
    // Arrange
    render(
      <MemoryRouter initialEntries={['/systems/system-1/authorization']}>
        <Routes>
          <Route path="/systems/:id/authorization" element={<AuthorizationPage />} />
        </Routes>
      </MemoryRouter>,
    );

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Issue Authorization' }));

    // Assert
    expect(screen.getByRole('dialog', { name: 'Issue Authorization Decision' })).toBeVisible();
    expect(screen.queryByLabelText(/Issued By/)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/Authorizing Official Name/)).not.toBeInTheDocument();

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Confirm authorization decision' }));

    // Assert
    await waitFor(() => expect(apiClient.post).toHaveBeenCalledWith(
      '/systems/system-1/authorization',
      expect.not.objectContaining({ issuedBy: expect.anything(), issuedByName: expect.anything() }),
    ));
  });

  it('keeps failed decision input in its dialog and cancels without another write', async () => {
    // Arrange
    vi.mocked(apiClient.post).mockRejectedValue({ error: 'Decision context changed' });
    render(<MemoryRouter initialEntries={['/systems/system-1/authorization']}><Routes>
      <Route path="/systems/:id/authorization" element={<AuthorizationPage />} /></Routes></MemoryRouter>);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Issue Authorization' }));
    fireEvent.change(screen.getByLabelText('Terms and Conditions'), { target: { value: 'Keep this draft' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirm authorization decision' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Decision context changed');
    expect(screen.getByRole('dialog')).toContainElement(screen.getByRole('alert'));
    expect(screen.getByLabelText('Terms and Conditions')).toHaveValue('Keep this draft');
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(apiClient.post).toHaveBeenCalledTimes(1);
  });
  it('blocks dismissal while the decision is being recorded', async () => {
    // Arrange
    let finish!: (value: { data: object }) => void;
    vi.mocked(apiClient.post).mockReturnValue(new Promise(resolve => { finish = resolve; }));
    render(<MemoryRouter initialEntries={['/systems/system-1/authorization']}><Routes>
      <Route path="/systems/:id/authorization" element={<AuthorizationPage />} /></Routes></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: 'Issue Authorization' }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Confirm authorization decision' }));
    fireEvent(screen.getByRole('dialog'), new Event('cancel', { cancelable: true }));
    // Assert
    expect(screen.getByRole('dialog')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Close dialog' })).toBeDisabled();
    await act(async () => finish({ data: {} }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});