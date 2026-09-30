import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import AzureSettingsPage from '../../pages/AzureSettingsPage';
import { onboarding } from '../../features/onboarding/api/onboardingApi';
vi.mock('../../features/onboarding/api/onboardingApi', () => ({ onboarding: {
  listAzureRegistrations: vi.fn(), putAzureRegistrations: vi.fn(), removeAzureRegistration: vi.fn(),
} }));
const first = '11111111-1111-1111-1111-111111111111';
const next = '22222222-2222-2222-2222-222222222222';
beforeEach(() => vi.clearAllMocks());
describe('Organization subscription registration', () => {
  it('preserves existing registrations when adding another subscription through the replace-set API', async () => {
    // Arrange
    vi.mocked(onboarding.listAzureRegistrations).mockResolvedValue([{ id: 'existing', subscriptionId: first,
      displayName: 'Existing subscription', parentTenantId: 'directory', environment: 'AzureCloud', status: 'Selected',
      tenantId: 'org-a', lastSeenVisibleAt: '2026-09-29T16:00:00Z' }]);
    vi.mocked(onboarding.putAzureRegistrations).mockResolvedValue([]);
    render(<MemoryRouter><AzureSettingsPage /></MemoryRouter>);
    await screen.findByRole('heading', { name: 'Azure Subscription Settings' });
    // Act
    fireEvent.change(screen.getByLabelText('Subscription ID'), { target: { value: next } });
    fireEvent.click(screen.getByRole('button', { name: 'Register' }));
    // Assert
    await waitFor(() => expect(onboarding.putAzureRegistrations).toHaveBeenCalledExactlyOnceWith([first, next]));
  });
  it('cannot replace registration state after a failed initial read', async () => {
    // Arrange
    vi.mocked(onboarding.listAzureRegistrations).mockRejectedValue(new Error('Registration list unavailable'));
    render(<MemoryRouter><AzureSettingsPage /></MemoryRouter>);
    await screen.findByText('Registration list unavailable');
    // Act
    fireEvent.change(screen.getByLabelText('Subscription ID'), { target: { value: next } });
    // Assert
    expect(screen.getByRole('button', { name: 'Register' })).toBeDisabled();
    expect(onboarding.putAzureRegistrations).not.toHaveBeenCalled();
  });
});
