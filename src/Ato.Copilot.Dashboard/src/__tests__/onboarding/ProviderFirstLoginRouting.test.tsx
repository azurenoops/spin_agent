import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import HomeResolver from '../../features/admin/HomeResolver';
import AccessRequiredPage from '../../features/admin/AccessRequiredPage';
import ProviderInvitationPage from '../../features/csp-onboarding/ProviderInvitationPage';
import { packageRequest } from '../../features/package-imports/request';

const accessState: { value: any } = { value: null };

vi.mock('../../features/admin/access', () => ({
  useEffectiveAccess: () => accessState.value,
}));
vi.mock('../../features/package-imports/request', async original => ({
  ...await original<typeof import('../../features/package-imports/request')>(),
  packageRequest: vi.fn(),
}));
vi.mock('../../pages/PortfolioRoute', () => ({ default: () => <div>Portfolio</div> }));

function LocationProbe() {
  const location = useLocation();
  return <output aria-label="Current route">{location.pathname}</output>;
}

beforeEach(() => {
  vi.clearAllMocks();
  accessState.value = {
    access: { destinations: [], entryRoute: null },
    selectedDestination: null,
    isLoading: false,
    error: null,
    reload: vi.fn(),
    selectDestination: vi.fn(),
  };
});

describe('provider first-login routing', () => {
  it('routes an authorized unfinished administrator to the saved provider setup', () => {
    // Arrange
    accessState.value.access.entryRoute = {
      kind: 'ProviderSetupResume',
      reasonCode: 'PROVIDER_SETUP_INCOMPLETE',
      destination: '/workspaces/csp/onboarding/csp?reentry=resume',
    };

    // Act
    render(<MemoryRouter><LocationProbe /><HomeResolver /></MemoryRouter>);

    // Assert
    expect(screen.getByLabelText('Current route')).toHaveTextContent('/workspaces/csp/onboarding/csp');
  });

  it('confirms an invitation without repeating provider registration', async () => {
    // Arrange
    vi.mocked(packageRequest).mockImplementation(async request => request.method === 'POST'
      ? { destination: '/workspaces/csp/authorizations' } as never
      : {
        invitationId: 'invite-1',
        providerId: 'provider-1',
        providerName: 'PEO Digital',
        authenticatedIdentity: 'Jamie Lee',
        targetIdentity: 'jamie.lee@example.invalid',
        offeringName: 'Flank Speed Azure',
        requestedRoles: ['ISSO'],
        requestedScope: 'Offering',
        status: 'Pending',
        expiresAt: '2026-10-10T00:00:00Z',
      } as never);

    // Act
    render(<MemoryRouter initialEntries={['/access/invitations/invite-1']}>
      <Routes>
        <Route path="/access/invitations/:invitationId" element={<><LocationProbe /><ProviderInvitationPage /></>} />
        <Route path="*" element={<LocationProbe />} />
      </Routes>
    </MemoryRouter>);
    fireEvent.click(await screen.findByRole('button', { name: 'Confirm membership and assigned access' }));

    // Assert
    await waitFor(() => expect(screen.getByLabelText('Current route')).toHaveTextContent('/workspaces/csp/authorizations'));
    expect(packageRequest).toHaveBeenCalledWith(expect.objectContaining({
      method: 'POST',
      url: '/api/csp/invitations/invite-1/accept',
    }));
  });

  it('submits a durable access request without loading provider data', async () => {
    // Arrange
    vi.mocked(packageRequest).mockImplementation(async request => request.method === 'POST'
      ? {
        requestId: 'request-1',
        status: 'Pending',
        justification: 'Support Flank Speed evidence.',
        submittedAt: '2026-10-08T12:00:00Z',
      } as never
      : null as never);
    render(<MemoryRouter><AccessRequiredPage /></MemoryRouter>);
    await waitFor(() => expect(packageRequest).toHaveBeenCalledWith(expect.objectContaining({
      url: '/api/csp/access-requests/current',
    })));

    // Act
    fireEvent.change(screen.getByLabelText('Reason for access'), {
      target: { value: 'Support Flank Speed evidence.' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Submit access request' }));

    // Assert
    expect(await screen.findByRole('status')).toHaveTextContent('Provider access request: Pending');
    expect(packageRequest).toHaveBeenCalledWith(expect.objectContaining({
      method: 'POST',
      url: '/api/csp/access-requests',
    }));
  });
});
