import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import CspWizard from '../../features/csp-onboarding/CspWizard';
import * as setupApi from '../../features/csp-onboarding/providerSetupApi';
import { setupState, setupResult } from './testData';
import { packageStatus } from '../package-imports/fixtures';
import '../package-imports/crypto';

vi.mock('../../features/csp-onboarding/providerSetupApi', () => ({
  getSetup: vi.fn(), saveDraft: vi.fn(), completeSetup: vi.fn(), uploadSource: vi.fn(), prepareUpload: vi.fn(),
}));
function RouteProbe() { const location = useLocation(); return <output aria-label="Current route">{location.pathname}</output>; }
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(setupApi.getSetup).mockResolvedValue(setupState());
});

describe('canonical provider setup handoff', () => {
  it('preserves selected sources across in-page navigation without uploading them', async () => {
    // Arrange
    render(<MemoryRouter><CspWizard /></MemoryRouter>);
    await screen.findByRole('heading', { name: 'Confirm provider identity' });
    fireEvent.click(screen.getByRole('button', { name: /5 Add available records/ }));
    // Act
    fireEvent.change(screen.getByLabelText('Select source files'), { target: { files: [new File(['source'], 'retain.json')] } });
    fireEvent.click(screen.getByRole('button', { name: /6 Review and finish setup/ }));
    fireEvent.click(screen.getByRole('button', { name: /5 Add available records/ }));
    // Assert
    expect(screen.getByRole('button', { name: 'Remove selected files' })).toBeInTheDocument();
    expect(setupApi.uploadSource).not.toHaveBeenCalled();
    expect(setupApi.prepareUpload).not.toHaveBeenCalled();
  });

  it('offers optional receipt-only sources without requiring offering or boundary creation', async () => {
    // Arrange
    render(<MemoryRouter><CspWizard /></MemoryRouter>);
    await screen.findByRole('heading', { name: 'Confirm provider identity' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: /5 Add available records/ }));
    // Assert
    expect(screen.getByRole('heading', { name: 'Add available records' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Boundary revision')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Skip sources for now' })).toBeEnabled();
    expect(screen.queryByRole('region', { name: 'Candidate records' })).not.toBeInTheDocument();
  });

  it('finishes setup during processing and hands off separately from source review and publication', async () => {
    // Arrange
    const review = setupState('p-review');
    const id = '44444444-4444-4444-8444-444444444444';
    review.uploadIntents = [{
      intentId: id, revision: 1, intentHash: 'A'.repeat(64), savedAt: '2026-09-30T12:00:00Z',
      input: { intentId: id, schemaVersion: 1, packageName: 'Synthetic processing source', entryPoint: 'Onboarding',
        associationMode: 'Unassociated', context: null, offeringHintId: null,
        files: [{ ordinal: 0, fileName: 'source.txt', mediaType: 'text/plain', byteLength: 1, sha256: 'B'.repeat(64) }],
        handlingPolicyVersion: 'test-1', declaredContent: { classification: 'Unclassified', markings: [], containsOnlySyntheticData: true } },
      receipt: packageStatus({ processingState: 'Processing' }),
      reconciliation: { outcome: 'Confirmed', observedAt: '2026-09-30T12:00:00Z', nextAction: 'OpenReceipt' },
    }];
    review.draft!.fields.sources = { choice: 'Intents', intentIds: [id] };
    vi.mocked(setupApi.getSetup).mockResolvedValue(review);
    vi.mocked(setupApi.saveDraft).mockResolvedValue(setupResult(review));
    const ready = structuredClone(review);
    ready.profile.onboardingState = 'Active';
    ready.draft!.completion = {};
    vi.mocked(setupApi.completeSetup).mockResolvedValue(setupResult(ready));
    render(<MemoryRouter><RouteProbe /><CspWizard /></MemoryRouter>);
    // Act
    fireEvent.click(await screen.findByLabelText('Confirm this provider setup.'));
    fireEvent.click(screen.getByRole('button', { name: 'Finish provider setup' }));
    await screen.findByRole('heading', { name: 'Provider setup complete' });
    fireEvent.click(screen.getByRole('link', { name: 'Open provider workspace' }));
    // Assert
    await waitFor(() => expect(screen.getByLabelText('Current route')).toHaveTextContent('/workspaces/csp/authorizations'));
    expect(setupApi.completeSetup).toHaveBeenCalledWith(1, 1, [], expect.any(String));
    expect(setupApi.uploadSource).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: /Approve|Publish approved/ })).not.toBeInTheDocument();
  });
});
