import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import EmassImportWizard from '../../../features/admin/imported-documents/EmassImportWizard';
import SspPdfImportWizard from '../../../features/admin/imported-documents/SspPdfImportWizard';
import Step4SspPdfImport from '../../../features/onboarding/steps/Step4SspPdfImport';

const api = vi.hoisted(() => ({
  uploadSspPdfBatch: vi.fn(), getSspPdfBatch: vi.fn(), getSspPdfExtraction: vi.fn(),
  putSspPdfCorrections: vi.fn(), importSspPdfSystem: vi.fn(),
}));
vi.mock('../../../features/onboarding/api/onboardingApi', () => ({ onboarding: api }));
vi.mock('../../../features/onboarding/components/BackgroundJobProgress', () => ({ default: () => null }));
vi.mock('../../../features/onboarding/steps/Step3EmassImport', () => ({
  default: () => <p>Canonical eMASS receipt and review</p>,
}));

afterEach(() => { vi.useRealTimers(); vi.clearAllMocks(); });

describe('mission imports share the canonical transport', () => {
  it('uses the canonical eMASS review component in the admin entry', () => {
    // Arrange
    render(<MemoryRouter><EmassImportWizard onClose={vi.fn()} /></MemoryRouter>);
    // Act / Assert
    expect(screen.getByText('Canonical eMASS receipt and review')).toBeVisible();
  });

  it('uses the canonical PDF batch component in the admin entry', () => {
    // Arrange
    render(<MemoryRouter><SspPdfImportWizard onClose={vi.fn()} /></MemoryRouter>);
    // Act / Assert
    expect(screen.getByRole('heading', { name: /Step 4 — SSP PDF batch import/ })).toBeVisible();
  });

  it('does not import old corrections when saving reviewed fields fails', async () => {
    // Arrange
    api.uploadSspPdfBatch.mockResolvedValue({ batchId: 'batch', sessions: [{ sessionId: 'session', extractJobId: 'job' }] });
    api.getSspPdfBatch.mockResolvedValue([{ sessionId: 'session', originalFileName: 'source.pdf', status: 'Extracted' }]);
    api.getSspPdfExtraction.mockResolvedValue({ fields: [{ name: 'system_name', value: 'Original', confidence: 'High' }] });
    api.putSspPdfCorrections.mockRejectedValue(new Error('Correction save failed'));
    api.importSspPdfSystem.mockResolvedValue({ sessionId: 'session', systemId: 'wrong-system' });
    const saved = vi.fn();
    const { container } = render(<Step4SspPdfImport onSaved={saved} />);
    fireEvent.change(container.querySelector('input[type=file]')!, { target: { files: [new File(['source'], 'source.pdf')] } });
    fireEvent.click(screen.getByRole('button', { name: 'Upload 1 file(s)' }));
    await screen.findByText('Batch progress');
    // Act
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 2100)); });
    fireEvent.click(await screen.findByRole('button', { name: 'Review & import' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Import as system' }));
    // Assert
    await waitFor(() => expect(api.putSspPdfCorrections).toHaveBeenCalled());
    expect(await screen.findByRole('alert')).toHaveTextContent('Correction save failed');
    expect(api.importSspPdfSystem).not.toHaveBeenCalled();
    expect(saved).not.toHaveBeenCalled();
  });
});
