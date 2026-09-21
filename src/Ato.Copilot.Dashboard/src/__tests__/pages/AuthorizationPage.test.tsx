import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import AuthorizationPage from '../../pages/AuthorizationPage';

const workspace = vi.hoisted(() => ({
  value: null as { systemAccess: { permissions: { canDecideAuthorization: boolean } } } | null,
}));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => workspace.value }));

vi.mock('../../api/client', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}));

vi.mock('../../hooks/usePolling', () => ({
  usePolling: vi.fn(() => ({ data: null, refresh: vi.fn() })),
}));

vi.mock('../../hooks/useSettings', () => ({
  useSettings: vi.fn(() => ({ settings: { role: 'AO' } })),
}));

describe('AuthorizationPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    workspace.value = null;
    vi.mocked(apiClient.post).mockResolvedValue({ data: {} });
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
    expect(screen.queryByLabelText(/Issued By/)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/Authorizing Official Name/)).not.toBeInTheDocument();

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Issue Authorization' }));

    // Assert
    await waitFor(() => expect(apiClient.post).toHaveBeenCalledWith(
      '/systems/system-1/authorization',
      expect.not.objectContaining({ issuedBy: expect.anything(), issuedByName: expect.anything() }),
    ));
  });
});