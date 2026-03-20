import { describe, it, expect } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useIntakeWizard } from '../../hooks/useIntakeWizard';
import { WizardStep } from '../../types/dashboard';

describe('useIntakeWizard', () => {
  it('starts closed with Registration step', () => {
    const { result } = renderHook(() => useIntakeWizard());

    expect(result.current.state.isOpen).toBe(false);
    expect(result.current.state.currentStep).toBe(WizardStep.Registration);
    expect(result.current.state.systemId).toBeNull();
  });

  it('opens the wizard', () => {
    const { result } = renderHook(() => useIntakeWizard());

    act(() => result.current.open());

    expect(result.current.state.isOpen).toBe(true);
    expect(result.current.state.currentStep).toBe(WizardStep.Registration);
  });

  it('cancel closes and resets wizard', () => {
    const { result } = renderHook(() => useIntakeWizard());

    act(() => result.current.open());
    act(() => result.current.cancel());

    expect(result.current.state.isOpen).toBe(false);
    expect(result.current.state.currentStep).toBe(WizardStep.Registration);
  });

  it('nextStep advances and marks current step completed', () => {
    const { result } = renderHook(() => useIntakeWizard());

    act(() => result.current.open());
    act(() => result.current.nextStep());

    expect(result.current.state.currentStep).toBe(WizardStep.SecurityCapabilities);
    expect(result.current.state.completedSteps[0]).toBe(true);
  });

  it('nextStep merges data when provided', () => {
    const { result } = renderHook(() => useIntakeWizard());

    act(() => result.current.open());
    act(() =>
      result.current.nextStep({
        registration: {
          name: 'Test System',
          acronym: 'TS',
          systemType: 'MajorApplication',
          missionCriticality: 'MissionCritical',
          hostingEnvironment: 'Azure',
          description: 'Test',
        },
      }),
    );

    expect(result.current.state.stepData.registration.name).toBe('Test System');
  });

  it('prevStep navigates backward', () => {
    const { result } = renderHook(() => useIntakeWizard());

    act(() => result.current.open());
    act(() => result.current.nextStep());
    act(() => result.current.prevStep());

    expect(result.current.state.currentStep).toBe(WizardStep.Registration);
  });

  it('prevStep does nothing on first step', () => {
    const { result } = renderHook(() => useIntakeWizard());

    act(() => result.current.open());
    act(() => result.current.prevStep());

    expect(result.current.state.currentStep).toBe(WizardStep.Registration);
  });

  it('skipStep advances without data and marks completed', () => {
    const { result } = renderHook(() => useIntakeWizard());

    act(() => result.current.open());
    act(() => result.current.nextStep()); // go to step 2
    act(() => result.current.skipStep()); // skip step 2

    expect(result.current.state.currentStep).toBe(WizardStep.SystemComponents);
    expect(result.current.state.completedSteps[1]).toBe(true);
  });

  it('skipStep does nothing on Registration step', () => {
    const { result } = renderHook(() => useIntakeWizard());

    act(() => result.current.open());
    act(() => result.current.skipStep());

    expect(result.current.state.currentStep).toBe(WizardStep.Registration);
  });

  it('goToStep navigates backward to completed step', () => {
    const { result } = renderHook(() => useIntakeWizard());

    act(() => result.current.open());
    act(() => result.current.nextStep()); // step 2
    act(() => result.current.nextStep()); // step 3
    act(() => result.current.goToStep(WizardStep.Registration));

    expect(result.current.state.currentStep).toBe(WizardStep.Registration);
  });

  it('goToStep rejects forward navigation', () => {
    const { result } = renderHook(() => useIntakeWizard());

    act(() => result.current.open());
    act(() => result.current.goToStep(WizardStep.SystemComponents));

    expect(result.current.state.currentStep).toBe(WizardStep.Registration);
  });

  it('setSystemId stores the system ID', () => {
    const { result } = renderHook(() => useIntakeWizard());

    act(() => result.current.open());
    act(() => result.current.setSystemId('sys-123'));

    expect(result.current.state.systemId).toBe('sys-123');
  });

  it('setValidationErrors stores errors', () => {
    const { result } = renderHook(() => useIntakeWizard());

    act(() => result.current.open());
    act(() => result.current.setValidationErrors({ name: ['Required'] }));

    expect(result.current.state.validationErrors).toEqual({ name: ['Required'] });
  });

  it('clearValidationErrors clears errors', () => {
    const { result } = renderHook(() => useIntakeWizard());

    act(() => result.current.open());
    act(() => result.current.setValidationErrors({ name: ['Required'] }));
    act(() => result.current.clearValidationErrors());

    expect(result.current.state.validationErrors).toEqual({});
  });

  it('nextStep clears validation errors', () => {
    const { result } = renderHook(() => useIntakeWizard());

    act(() => result.current.open());
    act(() => result.current.setValidationErrors({ name: ['Required'] }));
    act(() => result.current.nextStep());

    expect(result.current.state.validationErrors).toEqual({});
  });

  it('reset returns to initial state', () => {
    const { result } = renderHook(() => useIntakeWizard());

    act(() => result.current.open());
    act(() => result.current.nextStep());
    act(() => result.current.setSystemId('sys-456'));
    act(() => result.current.reset());

    expect(result.current.state.isOpen).toBe(false);
    expect(result.current.state.currentStep).toBe(WizardStep.Registration);
    expect(result.current.state.systemId).toBeNull();
  });
});
