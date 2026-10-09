import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import ProviderOnboardingPrototype, {
  PROTOTYPE_STORAGE_KEY,
} from '../../features/csp-onboarding/prototype/ProviderOnboardingPrototype';

beforeEach(() => localStorage.clear());

function chooseScenario(name: string) {
  fireEvent.click(screen.getByRole('button', { name }));
}

describe('provider onboarding interactive prototype', () => {
  it('routes only the new authorized administrator into provider registration', () => {
    // Arrange
    render(<ProviderOnboardingPrototype />);

    // Act
    chooseScenario('Invited user joining an existing provider');

    // Assert
    expect(screen.getByRole('heading', { name: 'Confirm your invitation' })).toBeInTheDocument();
    expect(screen.queryByText('Confirm provider identity')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Confirm membership and continue' }));
    expect(screen.getByRole('heading', { name: 'Membership confirmed' })).toBeInTheDocument();
    expect(screen.getByText(/Provider registration was not repeated\./)).toBeInTheDocument();
    expect(screen.queryByText('ISSO workspace')).not.toBeInTheDocument();
  });

  it('validates the six-stage setup and preserves entered values across navigation', () => {
    // Arrange
    render(<ProviderOnboardingPrototype />);
    chooseScenario('New authorized provider administrator');
    fireEvent.click(screen.getByRole('button', { name: 'Start provider setup' }));

    // Act
    fireEvent.change(screen.getByLabelText('Operating organization'), { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save & continue' }));

    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Operating organization is required');
    fireEvent.change(screen.getByLabelText('Operating organization'), { target: { value: 'PEO Digital' } });
    fireEvent.change(screen.getByLabelText('Provider workspace display name'), { target: { value: 'PEO Digital Provider workspace' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save & continue' }));
    expect(screen.getByRole('heading', { name: 'Confirm access and contacts' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(screen.getByLabelText('Operating organization')).toHaveValue('PEO Digital');
  });

  it('persists save-and-resume state in isolated browser storage', () => {
    // Arrange
    const first = render(<ProviderOnboardingPrototype />);
    chooseScenario('New authorized provider administrator');
    fireEvent.click(screen.getByRole('button', { name: 'Start provider setup' }));
    fireEvent.change(screen.getByLabelText('Service contact email'), { target: { value: 'provider@example.invalid' } });

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save & finish later' }));

    // Assert
    expect(screen.getByRole('heading', { name: 'Setup saved' })).toBeInTheDocument();
    expect(localStorage.getItem(PROTOTYPE_STORAGE_KEY)).toContain('provider@example.invalid');
    first.unmount();
    render(<ProviderOnboardingPrototype />);
    chooseScenario('Administrator resuming unfinished setup');
    fireEvent.click(screen.getByRole('button', { name: 'Resume saved setup' }));
    expect(screen.getByLabelText('Service contact email')).toHaveValue('provider@example.invalid');
  });

  it('keeps an uncertain upload tied to one receipt until reconciliation', () => {
    // Arrange
    render(<ProviderOnboardingPrototype />);
    chooseScenario('Administrator resuming unfinished setup');
    fireEvent.click(screen.getByRole('button', { name: 'Resume saved setup' }));
    fireEvent.click(screen.getByRole('button', { name: '5 Add available records' }));
    fireEvent.click(screen.getByLabelText('Upload supporting material'));
    const file = new File(['synthetic'], 'existing-ato.pdf', { type: 'application/pdf' });
    fireEvent.change(screen.getByLabelText('Choose synthetic source file'), { target: { files: [file] } });

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Simulate uncertain upload response' }));

    // Assert
    expect(screen.getByRole('status')).toHaveTextContent('Receipt uncertain');
    expect(screen.getByText('Original request retained')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Simulate uncertain upload response' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Reconcile original receipt' }));
    expect(screen.getByText('Source received')).toBeInTheDocument();
    expect(screen.getByText('Processing')).toBeInTheDocument();
    expect(screen.getByText('Awaiting review')).toBeInTheDocument();
    expect(screen.getByText('1 receipt · no duplicate created')).toBeInTheDocument();
  });

  it('analyzes a synthetic eMASS package during authorization starting point without verifying it', () => {
    // Arrange
    render(<ProviderOnboardingPrototype />);
    chooseScenario('New authorized provider administrator');
    fireEvent.click(screen.getByRole('button', { name: 'Start provider setup' }));
    fireEvent.click(screen.getByRole('button', { name: '4 Choose authorization starting point' }));
    fireEvent.click(screen.getByText('We have an existing authorization'));
    const file = new File(['synthetic emass export'], 'flank-speed-emass.zip', { type: 'application/zip' });
    fireEvent.change(screen.getByLabelText('Choose synthetic eMASS package'), { target: { files: [file] } });

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Analyze synthetic eMASS package' }));

    // Assert
    expect(screen.getByRole('heading', { name: 'Package understanding' })).toBeInTheDocument();
    expect(screen.getByText('System Security Plan')).toBeInTheDocument();
    expect(screen.getByText('Security Assessment Report')).toBeInTheDocument();
    expect(screen.getByText('POA&M')).toBeInTheDocument();
    expect(screen.getByText('Exact authorization boundary mapping remains unresolved')).toBeInTheDocument();
    expect(screen.getByText(/does not verify the authorization/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Use proposed facts' }));
    expect(screen.getByLabelText('Decision reference')).toHaveValue('eMASS-ATO-FS-2025-017');
    expect(screen.getByLabelText('System or boundary name')).toHaveValue('Flank Speed Azure (as stated in package)');
  });

  it('keeps active members in an onboarding status view without exposing a workspace', () => {
    // Arrange
    render(<ProviderOnboardingPrototype />);

    // Act
    chooseScenario('Existing user with an active provider');

    // Assert
    expect(screen.getByRole('heading', { name: 'Provider onboarding already complete' })).toBeInTheDocument();
    expect(screen.getByText(/This onboarding prototype stops here\./)).toBeInTheDocument();
    expect(screen.queryByText('Administrator workspace')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Prototype persona')).not.toBeInTheDocument();
  });
});
