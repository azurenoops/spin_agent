import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

const state = {
  tenantId: 'tenant-a', status: 'InProgress' as const, lastStep: 'NarrativeSeeds',
  startedAt: null, completedAt: null, lastReRunAt: null,
  steps: [{ step: 'OrganizationContext', status: 'Completed' as const, completedAt: '', durationMs: 0 },
    { step: 'Roles', status: 'Completed' as const, completedAt: '', durationMs: 0 }],
};
const api = vi.hoisted(() => ({ complete: vi.fn(), skipStep: vi.fn(), getState: vi.fn(), start: vi.fn() }));
vi.mock('../../../features/onboarding/api/onboardingApi', () => ({ onboarding: api }));
vi.mock('../../../features/onboarding/steps/Step7NarrativeSeeds', () => ({
  default: ({ onComplete }: { onComplete: () => void }) => <button onClick={onComplete}>Finish setup</button>,
}));
vi.mock('../../../features/onboarding/steps/Step1OrganizationContext', () => ({ default: () => null }));
vi.mock('../../../features/onboarding/steps/Step2RoleAssignments', () => ({ default: () => null }));
vi.mock('../../../features/onboarding/steps/Step3EmassImport', () => ({ default: () => null }));
vi.mock('../../../features/onboarding/steps/Step4SspPdfImport', () => ({ default: () => null }));
vi.mock('../../../features/onboarding/steps/Step5AzureSubscriptions', () => ({ default: () => null }));
vi.mock('../../../features/onboarding/steps/Step6Templates', () => ({ default: () => null }));
import OnboardingWizardModal from '../../../features/onboarding/OnboardingWizardModal';

describe('general onboarding completion safety', () => {
  it('keeps the modal open and exposes the completion error', async () => {
    // Arrange
    api.skipStep.mockResolvedValue(undefined);
    api.complete.mockRejectedValue(new Error('Completion could not be saved'));
    api.getState.mockResolvedValue(state);
    const close = vi.fn();
    render(<MemoryRouter><OnboardingWizardModal initialState={state}
      forced={false} onStateChange={vi.fn()} onClose={close} /></MemoryRouter>);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Finish setup' }));
    // Assert
    await waitFor(() => expect(api.complete).toHaveBeenCalled());
    expect(await screen.findByRole('alert')).toHaveTextContent('Completion could not be saved');
    expect(close).not.toHaveBeenCalled();
  });
});
