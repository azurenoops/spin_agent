import { act, render, screen } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import CspOnboardingGuard from '../../features/csp-onboarding/CspOnboardingGuard';

vi.mock('../../features/csp-onboarding/api', () => ({
  getCspOnboardingState: async () => ({ onboardingState: 'InWizard' }),
  isUnavailable: () => false,
}));

function Location() { return <output>{useLocation().pathname}</output>; }

describe('unfinished provider saved-exit guard', () => {
  it.each(['/setup', '/setup/resume'])('allows %s without sending a saved draft back into the wizard', async path => {
    // Arrange / Act
    await act(async () => { render(<MemoryRouter initialEntries={[path]}><CspOnboardingGuard><Location /></CspOnboardingGuard></MemoryRouter>); });
    // Assert
    expect(screen.getByText(path)).toBeInTheDocument();
  });

  it('retains the activation redirect for unrelated provider routes', async () => {
    // Arrange / Act
    render(<MemoryRouter initialEntries={['/authorizations']}><CspOnboardingGuard><Location /></CspOnboardingGuard></MemoryRouter>);
    // Assert
    expect(await screen.findByText('/onboarding/csp')).toBeInTheDocument();
  });
});
