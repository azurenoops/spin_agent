import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import CspWizard from '../../features/csp-onboarding/CspWizard';
import AtoDocumentsStep from '../../features/csp-onboarding/steps/AtoDocumentsStep';
import ReviewStep from '../../features/csp-onboarding/steps/ReviewStep';
import * as onboardingApi from '../../features/csp-onboarding/api';
import * as packageApi from '../../features/package-imports/api';
import * as offeringApi from '../../features/provider-authorizations/api';
import { offering, boundary, receipt } from './testData';
import { page, packageStatus } from '../package-imports/fixtures';
import '../package-imports/crypto';

vi.mock('../../features/csp-onboarding/api', () => ({
  getCspOnboardingState: vi.fn(), isUnavailable: vi.fn(() => false),
  postCspOnboardingAtosUpload: vi.fn(), postCspOnboardingSubmit: vi.fn(),
}));
vi.mock('../../features/package-imports/api', async importOriginal => ({
  ...await importOriginal<typeof packageApi>(),
  listPackages: vi.fn(), getPackageStatus: vi.fn(), getPackageCandidates: vi.fn(),
  previewPackage: vi.fn(), approvePackage: vi.fn(), publishPackage: vi.fn(),
}));
vi.mock('../../features/provider-authorizations/api', async original => ({
  ...await original<typeof offeringApi>(), listOfferings: vi.fn(), getOffering: vi.fn(), listBoundaries: vi.fn(), uploadPackage: vi.fn(),
}));

function RouteProbe() {
  const location = useLocation();
  return <output aria-label="Current route">{location.pathname}</output>;
}

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
});

describe('authorization-led optional onboarding handoff', () => {
  it('prevents sidebar navigation from discarding unreceipted selected sources', async () => {
    // Arrange
    render(<MemoryRouter initialEntries={['/onboarding/csp']}><CspWizard /></MemoryRouter>);
    await screen.findByRole('heading', { name: 'Step 1 — CSP identity' });
    fireEvent.click(screen.getByRole('button', { name: /Step 4 Optional Import an existing authorization package/i }));
    fireEvent.change(await screen.findByLabelText('Offering'), { target: { value: offering.offeringId } });
    await screen.findByRole('option', { name: /Test service boundary/ });
    fireEvent.change(screen.getByLabelText('Boundary revision'), { target: { value: boundary.boundaryRevisionId } });
    fireEvent.change(screen.getByLabelText('Package name'), { target: { value: 'Keep this package' } });
    // Act
    fireEvent.change(screen.getByLabelText('Select source files'), { target: { files: [new File(['source'], 'retain.json')] } });
    // Assert
    expect(screen.getByRole('button', { name: /Step 5.*Review & submit/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /Step 1.*CSP identity/i })).toBeDisabled();
    expect(offeringApi.uploadPackage).not.toHaveBeenCalled();
  });
  it('labels the optional wizard step as importing an existing authorization package', async () => {
    // Arrange
    render(<MemoryRouter initialEntries={['/onboarding/csp']}><CspWizard /></MemoryRouter>);
    await screen.findByRole('heading', { name: 'Step 1 — CSP identity' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: /Step 4 Optional Import an existing authorization package/i }));
    // Assert
    await screen.findByRole('option', { name: offering.name });
    expect(screen.getByRole('heading', { name: /Import an existing authorization package.*Optional/i })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Candidate records' })).not.toBeInTheDocument();
    expect(packageApi.getPackageCandidates).not.toHaveBeenCalled();
  });

  it('directs detailed review to Authorizations while allowing setup to continue during processing', async () => {
    // Arrange
    const next = vi.fn();
    render(<MemoryRouter><AtoDocumentsStep saving={false} errorMessage={null} onContinue={next} onBack={vi.fn()} /></MemoryRouter>);
    // Act
    fireEvent.change(await screen.findByLabelText('Offering'), { target: { value: offering.offeringId } });
    await screen.findByRole('option', { name: /Test service boundary/ });
    fireEvent.change(screen.getByLabelText('Boundary revision'), { target: { value: boundary.boundaryRevisionId } });
    fireEvent.change(screen.getByLabelText('Package name'), { target: { value: 'Synthetic authorization package' } });
    fireEvent.change(screen.getByLabelText('Select source files'), { target: { files: [new File(['synthetic source'], 'source.json')] } });
    fireEvent.click(screen.getByRole('button', { name: 'Upload package' }));
    await screen.findByText(/Receipt confirmed/i);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Continue' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
    // Assert
    expect(next).toHaveBeenCalledOnce();
    expect(screen.getByText('Processing')).toBeInTheDocument();
    expect(packageApi.getPackageCandidates).not.toHaveBeenCalled();
    expect(packageApi.previewPackage).not.toHaveBeenCalled();
    expect(packageApi.approvePackage).not.toHaveBeenCalled();
    expect(packageApi.publishPackage).not.toHaveBeenCalled();
    expect(offeringApi.uploadPackage).toHaveBeenCalledWith(offering.offeringId, expect.objectContaining({
      boundaryRevisionId: boundary.boundaryRevisionId, expectedOfferingRevision: offering.revision,
    }), expect.any(Array), expect.any(String));
    expect(onboardingApi.postCspOnboardingAtosUpload).not.toHaveBeenCalled();
    expect(screen.getByText(/review.*later.*Authorizations/i)).toBeInTheDocument();
    expect(screen.queryByText(/Security Capabilities.*Review imports/i)).not.toBeInTheDocument();
  });

  it('keeps the final profile confirmation separate from Authorizations review and publication', () => {
    // Arrange
    const submit = vi.fn();
    render(<MemoryRouter><ReviewStep state={{ cspProfileId: 'provider', currentStep: 'Review', onboardingState: 'InWizard' }}
      saving={false} errorMessage={null} onSubmit={submit} onBack={vi.fn()} /></MemoryRouter>);
    // Act
    fireEvent.click(screen.getByRole('button', { name: /finalize onboarding/i }));
    // Assert
    expect(submit).toHaveBeenCalledOnce();
    expect(screen.getByText(/does not approve or publish/i)).toHaveTextContent(/Authorizations/);
    expect(screen.queryByRole('region', { name: 'Candidate records' })).not.toBeInTheDocument();
    expect(packageApi.approvePackage).not.toHaveBeenCalled();
    expect(packageApi.publishPackage).not.toHaveBeenCalled();
  });

  it('hands completed onboarding to Authorizations with processing receipts and no inventory review prerequisite', async () => {
    // Arrange
    vi.mocked(onboardingApi.getCspOnboardingState)
      .mockResolvedValueOnce({ cspProfileId: 'provider', currentStep: 'Review', onboardingState: 'InWizard' })
      .mockResolvedValue({ cspProfileId: 'provider', currentStep: 'Complete', onboardingState: 'Active' });
    vi.mocked(onboardingApi.postCspOnboardingSubmit).mockResolvedValue({
      cspProfileId: 'provider', onboardingState: 'Active', onboardingCompletedAt: '2026-09-24T12:00:00Z',
    });
    vi.mocked(packageApi.listPackages).mockResolvedValue(page([packageStatus({ processingState: 'Processing' })]));
    render(<MemoryRouter initialEntries={['/onboarding/csp']}><RouteProbe /><CspWizard /></MemoryRouter>);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: /finalize onboarding/i }));
    await waitFor(() => expect(onboardingApi.postCspOnboardingSubmit).toHaveBeenCalledOnce());
    // Assert
    await waitFor(() => expect(screen.getByLabelText('Current route')).toHaveTextContent('/workspaces/csp/authorizations'));
    expect(packageApi.getPackageCandidates).not.toHaveBeenCalled();
    expect(packageApi.previewPackage).not.toHaveBeenCalled();
    expect(packageApi.approvePackage).not.toHaveBeenCalled();
    expect(packageApi.publishPackage).not.toHaveBeenCalled();
  });

  it('still permits skipping package upload entirely', async () => {
    // Arrange
    const next = vi.fn();
    render(<MemoryRouter><AtoDocumentsStep saving={false} errorMessage={null} onContinue={next} onBack={vi.fn()} /></MemoryRouter>);
    await screen.findByText('No source packages have been received. Upload is optional.');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
    // Assert
    expect(next).toHaveBeenCalledOnce();
    expect(onboardingApi.postCspOnboardingAtosUpload).not.toHaveBeenCalled();
    expect(packageApi.getPackageCandidates).not.toHaveBeenCalled();
  });
});
