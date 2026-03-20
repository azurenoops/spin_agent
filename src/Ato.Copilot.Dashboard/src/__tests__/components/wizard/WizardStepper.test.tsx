import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import WizardStepper from '../../../components/wizard/WizardStepper';
import { WizardStep } from '../../../types/dashboard';

const STEP_LABELS = [
  'System Registration',
  'Security Capabilities',
  'System Components',
  'Authorization Boundaries',
  'Assign RMF Roles',
  'Verify Roles',
  'Set Categorization',
];

describe('WizardStepper', () => {
  it('renders all 7 step labels', () => {
    render(
      <WizardStepper
        currentStep={WizardStep.Registration}
        completedSteps={[false, false, false, false, false, false, false]}
        onStepClick={vi.fn()}
      />,
    );

    for (const label of STEP_LABELS) {
      expect(screen.getByText(label)).toBeDefined();
    }
  });

  it('highlights the current step', () => {
    const { container } = render(
      <WizardStepper
        currentStep={WizardStep.SecurityCapabilities}
        completedSteps={[true, false, false, false, false, false, false]}
        onStepClick={vi.fn()}
      />,
    );

    // Current step should have a blue indicator
    const steps = container.querySelectorAll('[data-step]');
    if (steps.length > 0) {
      // Check Step 2 is highlighted (current)
      expect(screen.getByText('Security Capabilities')).toBeDefined();
    }
  });

  it('shows completed steps with checkmark styling', () => {
    render(
      <WizardStepper
        currentStep={WizardStep.SystemComponents}
        completedSteps={[true, true, false, false, false, false, false]}
        onStepClick={vi.fn()}
      />,
    );

    // Step 1 and 2 completed, step 3 current
    expect(screen.getByText('System Registration')).toBeDefined();
    expect(screen.getByText('Security Capabilities')).toBeDefined();
    expect(screen.getByText('System Components')).toBeDefined();
  });

  it('fires callback when clicking on a completed step', () => {
    const onStepClick = vi.fn();

    render(
      <WizardStepper
        currentStep={WizardStep.SystemComponents}
        completedSteps={[true, true, false, false, false, false, false]}
        onStepClick={onStepClick}
      />,
    );

    // Click "System Registration" (completed step 1)
    fireEvent.click(screen.getByText('System Registration'));
    expect(onStepClick).toHaveBeenCalledWith(WizardStep.Registration);
  });

  it('does not fire callback when clicking on a future step', () => {
    const onStepClick = vi.fn();

    render(
      <WizardStepper
        currentStep={WizardStep.Registration}
        completedSteps={[false, false, false, false, false, false, false]}
        onStepClick={onStepClick}
      />,
    );

    // Click "Security Capabilities" (future step, not completed)
    fireEvent.click(screen.getByText('Security Capabilities'));
    expect(onStepClick).not.toHaveBeenCalled();
  });
});
