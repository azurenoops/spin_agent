import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import TicketingConfig from './TicketingConfig';
import * as api from '../../api/poam';

vi.mock('../../api/poam', () => ({ getTicketingConfig: vi.fn(), configureTicketing: vi.fn() }));
vi.mock('../permissions/useSystemMutationPermission', () => ({ useSystemMutationPermission: () => true }));

describe('TicketingConfig contract', () => {
  beforeEach(() => vi.resetAllMocks());
  it('loads backend projectKey and sends only credential reference fields', async () => {
    // Arrange
    vi.mocked(api.getTicketingConfig).mockResolvedValue({
      configured: true, provider: 'Jira', baseUrl: 'https://tickets.example', projectKey: 'TEST', syncEnabled: true,
    });
    vi.mocked(api.configureTicketing).mockResolvedValue({ configured: true });
    render(<TicketingConfig systemId="system" />);
    // Act
    await screen.findByDisplayValue('TEST');
    fireEvent.change(screen.getByLabelText(/Server credential reference/), { target: { value: 'jira-reference' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save connector' }));
    // Assert
    await waitFor(() => expect(api.configureTicketing).toHaveBeenCalledWith('system', {
      provider: 'jira', baseUrl: 'https://tickets.example', projectKey: 'TEST',
      apiKeySecretName: 'jira-reference', syncEnabled: true,
    }));
    expect(screen.queryByLabelText(/Auth Token/)).toBeNull();
    expect(screen.queryByText(/Auto-sync enabled/)).toBeNull();
  });
  it('does not silently hide configuration read failures', async () => {
    // Arrange
    vi.mocked(api.getTicketingConfig).mockRejectedValue(new Error('denied'));
    // Act
    render(<TicketingConfig systemId="system" />);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Could not load');
  });
});
