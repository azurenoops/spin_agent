import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import SetupFrame, { SetupGuidance, SetupPanel } from '../../features/onboarding/shared/SetupFrame';

const steps = [
  { id: 'p-details', label: 'Provider details', status: 'saved' as const },
  { id: 'p-sources', label: 'Source package', status: 'deferred' as const },
  { id: 'p-review', label: 'Review setup', disabled: true },
];

describe('mock-aligned shared setup frame', () => {
  it('renders production branding, numbered progress and supporting guidance without prototype controls', () => {
    // Arrange
    const changeStep = vi.fn();
    // Act
    render(<SetupFrame journey="Provider" title="Add source material" description="Review after setup."
      currentStep="p-sources" steps={steps} onStepChange={changeStep}
      guidance={<SetupGuidance title="Source context">Association remains a later task.</SetupGuidance>}>
      <SetupPanel title="Source receipt">No source received.</SetupPanel>
    </SetupFrame>);
    // Assert
    expect(screen.getByRole('img', { name: 'SPIN Agent' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'Add source material' })).toHaveFocus();
    expect(screen.getByRole('navigation', { name: 'Provider setup steps' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /2 Source package Deferred/ })).toHaveAttribute('aria-current', 'step');
    expect(screen.getByRole('button', { name: /3 Review setup/ })).toBeDisabled();
    expect(screen.getByRole('heading', { name: 'Source context' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Source receipt' })).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Choose screen' })).not.toBeInTheDocument();
    expect(screen.queryByText('Setup complete')).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: /1 Provider details Saved/ }));
    // Assert
    expect(changeStep).toHaveBeenCalledWith('p-details');
  });

  it('invokes distinct explicit save, back and continue actions without assuming saved state', () => {
    // Arrange
    const save = vi.fn();
    const back = vi.fn();
    const next = vi.fn();
    const choosePath = vi.fn();
    // Act
    render(<SetupFrame journey="System" title="Review system setup" description="Review retained facts."
      currentStep="s-review" steps={[]} onSaveLater={save} onBack={back} onChoosePath={choosePath}
      saveStatus="Unsaved changes" primaryAction={{ label: 'Confirm setup', onClick: next }}>
      <p>Authorization not recorded.</p>
    </SetupFrame>);
    fireEvent.click(screen.getByRole('button', { name: 'Save & finish later' }));
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm setup' }));
    fireEvent.click(screen.getByRole('button', { name: 'Choose a different path' }));
    // Assert
    expect(save).toHaveBeenCalledOnce();
    expect(back).toHaveBeenCalledOnce();
    expect(next).toHaveBeenCalledOnce();
    expect(choosePath).toHaveBeenCalledOnce();
    expect(screen.getByRole('status')).toHaveTextContent('Unsaved changes');
  });

  it('blocks duplicate actions and step navigation while the domain reports a pending write', () => {
    // Arrange
    const action = vi.fn();
    // Act
    render(<SetupFrame journey="Provider" title="Saving" description="Waiting for the server."
      currentStep="p-details" steps={steps} busy onStepChange={action} onSaveLater={action}
      onBack={action} onChoosePath={action} primaryAction={{ label: 'Continue', onClick: action }}>
      <input aria-label="Provider name" defaultValue="Synthetic provider" />
    </SetupFrame>);
    for (const button of screen.getAllByRole('button')) fireEvent.click(button);
    // Assert
    for (const button of screen.getAllByRole('button')) expect(button).toBeDisabled();
    expect(action).not.toHaveBeenCalled();
    expect(screen.getByRole('main')).toHaveAttribute('aria-busy', 'true');
  });

  it('announces and focuses failures without discarding fields or inventing success', () => {
    // Arrange
    const props = { journey: 'Organization' as const, title: 'Organization details', description: 'Save a draft.',
      currentStep: 'o-details', steps: [] };
    const view = render(<SetupFrame {...props}><input aria-label="Name" defaultValue="My draft" /></SetupFrame>);
    // Act
    view.rerender(<SetupFrame {...props} error="Save failed. Your changes remain unsaved." saveStatus="Unsaved changes">
      <input aria-label="Name" defaultValue="My draft" />
    </SetupFrame>);
    // Assert
    expect(screen.getByRole('alert')).toHaveFocus();
    expect(screen.getByRole('alert')).toHaveTextContent('Save failed');
    expect(screen.getByRole('textbox', { name: 'Name' })).toHaveValue('My draft');
    expect(screen.getByRole('status')).toHaveTextContent('Unsaved changes');
  });

  it('focuses the new heading on step change and retains explicit action availability', () => {
    // Arrange
    const props = { journey: 'Provider' as const, description: 'Saved details.', steps,
      primaryAction: { label: 'Finish', onClick: vi.fn(), disabled: true } };
    const view = render(<SetupFrame {...props} title="Details" currentStep="p-details">First</SetupFrame>);
    // Act
    view.rerender(<SetupFrame {...props} title="Review" currentStep="p-review" stepLabel="Review required">Second</SetupFrame>);
    // Assert
    expect(screen.getByRole('heading', { level: 1, name: 'Review' })).toHaveFocus();
    expect(screen.getByRole('button', { name: 'Finish' })).toBeDisabled();
    expect(screen.getByText('Review required')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save & finish later' })).not.toBeInTheDocument();
  });

  it('keeps footer guidance separate from the explicit save status', () => {
    // Arrange
    const props = { journey: 'Tenant' as const, title: 'Tenant setup', description: 'Review saved values.',
      currentStep: 'details', steps: [], footerHelp: 'Activation requires submitted values.' };
    const view = render(<SetupFrame {...props}>Fields</SetupFrame>);
    // Assert
    expect(screen.getByText('Activation requires submitted values.')).not.toHaveClass('mt-1');
    // Act
    view.rerender(<SetupFrame {...props} saveStatus="Draft saved">Fields</SetupFrame>);
    // Assert
    expect(screen.getByText('Activation requires submitted values.')).toHaveClass('mt-1');
    expect(screen.getByRole('status')).toHaveTextContent('Draft saved');
  });
});
