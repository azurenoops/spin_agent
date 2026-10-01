import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
const getState = vi.hoisted(() => vi.fn());
vi.mock('../../features/onboarding/TenantWizard/api', () => ({ tenantWizard: { getState } }));
import TenantOnboardingGuard from '../../features/onboarding/TenantWizard/TenantOnboardingGuard';
function Route() { return <output aria-label="route">{useLocation().pathname}</output>; }
function page(route = '/systems') {
  return render(<MemoryRouter initialEntries={[route]}><Route /><TenantOnboardingGuard><p>Authorized content</p></TenantOnboardingGuard></MemoryRouter>);
}
beforeEach(() => vi.resetAllMocks());
describe('tenant activation guard', () => {
  it.each(['/onboarding/tenant', '/setup', '/setup/resume', '/onboarding'])('permits pending tenant setup path %s without a loop', async path => {
    // Arrange
    getState.mockResolvedValue({ onboardingState: 'Pending' });
    // Act
    page(path);
    // Assert
    await waitFor(() => expect(getState).toHaveBeenCalledOnce());
    expect(screen.getByLabelText('route')).toHaveTextContent(path);
  });
  it('redirects a pending tenant from ordinary application content', async () => {
    // Arrange
    getState.mockResolvedValue({ onboardingState: 'Pending' });
    // Act
    page();
    // Assert
    await waitFor(() => expect(screen.getByLabelText('route')).toHaveTextContent('/onboarding/tenant'));
  });
  it('leaves active tenants in their current authorized route', async () => {
    // Arrange
    getState.mockResolvedValue({ onboardingState: 'Active' });
    // Act
    page();
    // Assert
    await waitFor(() => expect(getState).toHaveBeenCalledOnce());
    expect(screen.getByText('Authorized content')).toBeInTheDocument();
    expect(screen.getByLabelText('route')).toHaveTextContent('/systems');
  });
  it('announces an unavailable probe instead of claiming tenant activation', async () => {
    // Arrange
    getState.mockRejectedValue(new Error('State unavailable'));
    // Act
    page();
    // Assert
    expect(await screen.findByText(/Tenant activation status is unavailable: State unavailable/)).toBeInTheDocument();
    expect(screen.getByLabelText('route')).toHaveTextContent('/systems');
  });
  it('aborts the state probe on unmount', () => {
    // Arrange
    getState.mockImplementation(() => new Promise(() => {}));
    const view = page();
    // Act
    view.unmount();
    // Assert
    expect(getState.mock.calls[0]?.[0].aborted).toBe(true);
  });
});
