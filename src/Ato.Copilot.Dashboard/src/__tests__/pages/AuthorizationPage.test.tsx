import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import AuthorizationPage from '../../pages/AuthorizationPage';

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
    vi.mocked(apiClient.post).mockResolvedValue({ data: {} });
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