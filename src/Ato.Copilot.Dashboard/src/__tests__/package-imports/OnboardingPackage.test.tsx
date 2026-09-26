import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import CspWizard from '../../features/csp-onboarding/CspWizard';
import AtoDocumentsStep from '../../features/csp-onboarding/steps/AtoDocumentsStep';
import ReviewStep from '../../features/csp-onboarding/steps/ReviewStep';
import * as onboardingApi from '../../features/csp-onboarding/api';
import * as packageApi from '../../features/package-imports/api';
import * as offeringApi from '../../features/provider-authorizations/api';
import { offering, boundary, receipt } from '../provider-authorizations/testData';
import { page, packageStatus } from './fixtures';
import './crypto';

vi.mock('../../features/csp-onboarding/api', () => ({
  getCspOnboardingState: vi.fn(),
  isUnavailable: vi.fn(() => false),
  getCspOnboardingAtosState: vi.fn(),
  postCspOnboardingAtosUpload: vi.fn(),
}));
vi.mock('../../features/package-imports/api', () => ({ listPackages: vi.fn(), getPackageStatus: vi.fn() }));
vi.mock('../../features/provider-authorizations/api', async original => ({
  ...await original<typeof offeringApi>(), listOfferings: vi.fn(), getOffering: vi.fn(), listBoundaries: vi.fn(), uploadPackage: vi.fn(),
}));

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(onboardingApi.getCspOnboardingState).mockResolvedValue({
    cspProfileId: 'provider', currentStep: 'Identity', onboardingState: 'InWizard',
  });
  vi.mocked(packageApi.listPackages).mockResolvedValue(page([]));
  vi.mocked(offeringApi.listOfferings).mockResolvedValue(page([offering]));
  vi.mocked(offeringApi.getOffering).mockResolvedValue(offering);
  vi.mocked(offeringApi.listBoundaries).mockResolvedValue(page([boundary]));
  vi.mocked(offeringApi.uploadPackage).mockResolvedValue(receipt);
  vi.mocked(packageApi.getPackageStatus).mockResolvedValue(receipt.package);
  vi.mocked(onboardingApi.getCspOnboardingAtosState).mockResolvedValue({
    documentsUploaded: 2, componentsExtracted: 987, capabilitiesMapped: 876,
    capabilitiesNeedsReview: 765, aiMappingAvailable: true,
  });
});

describe('onboarding does not perform package record review', () => {
  it('labels the wizard upload navigation optional without implying inheritance', async () => {
    // Arrange
    render(<MemoryRouter><CspWizard /></MemoryRouter>);
    // Act
    await screen.findByRole('heading', { name: 'Step 1 — CSP identity' });
    // Assert
    expect(screen.getByRole('button', { name: /Step 4 Optional Import an existing authorization package/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Step 4 Required/ })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Step 4 Optional Import an existing authorization package/ }));
    expect(screen.queryByText(/artifacts you inherit from/i)).not.toBeInTheDocument();
    await screen.findByText('No source packages have been received. Upload is optional.');
  });

  it('keeps upload optional without displaying an extracted inventory or tally', async () => {
    // Arrange
    const continueOnboarding = vi.fn();
    render(<AtoDocumentsStep saving={false} errorMessage={null} onContinue={continueOnboarding} onBack={vi.fn()} />);
    // Act
    await waitFor(() => expect(screen.getByText(/Optional/)).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: /continue/i }));
    // Assert
    expect(continueOnboarding).toHaveBeenCalledOnce();
    expect(screen.queryByText(/987|876|765/)).not.toBeInTheDocument();
  });

  it('states that finalizing the profile never approves or publishes imports', async () => {
    // Arrange
    const submit = vi.fn();
    render(<ReviewStep state={{ cspProfileId: 'provider', currentStep: 'Review', onboardingState: 'InWizard' }}
      saving={false} errorMessage={null} onSubmit={submit} onBack={vi.fn()} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: /finalize onboarding/i }));
    // Assert
    expect(submit).toHaveBeenCalledOnce();
    expect(screen.getByText(/does not approve or publish/i)).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByTestId('review-ato-tally')).not.toBeInTheDocument());
  });

  it('continues after a persisted receipt while analysis is still processing', async () => {
    // Arrange
    const next = vi.fn();
    render(<MemoryRouter><AtoDocumentsStep saving={false} errorMessage={null} onContinue={next} onBack={vi.fn()} /></MemoryRouter>);
    // Act
    fireEvent.change(await screen.findByLabelText('Offering'), { target: { value: offering.offeringId } });
    await screen.findByRole('option', { name: /Test service boundary/ });
    fireEvent.change(screen.getByLabelText('Boundary revision'), { target: { value: boundary.boundaryRevisionId } });
    fireEvent.change(screen.getByLabelText('Package name'), { target: { value: 'Synthetic authorization package' } });
    fireEvent.change(screen.getByLabelText('Select source files'), { target: { files: [new File(['source'], 'package.json')] } });
    expect(screen.getByRole('button', { name: /continue/i })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Upload package' }));
    await screen.findByText(/Receipt confirmed/i);
    await waitFor(() => expect(screen.getByRole('button', { name: /continue/i })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: /continue/i }));
    // Assert
    expect(next).toHaveBeenCalledOnce();
    expect(screen.getByText('Processing')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /approve|publish/i })).not.toBeInTheDocument();
  });

  it('recovers saved packages after a remount and explicitly retries unavailable status', async () => {
    // Arrange
    vi.mocked(packageApi.listPackages).mockRejectedValueOnce(new Error('Access denied.')).mockResolvedValue(page([packageStatus()]));
    render(<AtoDocumentsStep saving={false} errorMessage={null} onContinue={vi.fn()} onBack={vi.fn()} />);
    // Act
    await screen.findByText('Access denied.');
    expect(screen.queryByText(/No source packages/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    // Assert
    expect(await screen.findByText('Synthetic package')).toBeInTheDocument();
    expect(onboardingApi.postCspOnboardingAtosUpload).not.toHaveBeenCalled();
  });

  it('polls persisted processing status and stops on an explicit lookup failure', async () => {
    // Arrange
    vi.useFakeTimers();
    vi.mocked(packageApi.listPackages).mockResolvedValueOnce(page([packageStatus({ processingState: 'Processing' })]))
      .mockRejectedValueOnce(new Error('Status temporarily unavailable.'));
    const view = render(<AtoDocumentsStep saving={false} errorMessage={null} onContinue={vi.fn()} onBack={vi.fn()} />);
    // Act
    await act(async () => { await Promise.resolve(); });
    await act(async () => { await vi.advanceTimersByTimeAsync(5000); });
    // Assert
    expect(screen.getByText('Status temporarily unavailable.')).toBeInTheDocument();
    expect(packageApi.listPackages).toHaveBeenCalledTimes(2);
    view.unmount();
    vi.useRealTimers();
  });
});
