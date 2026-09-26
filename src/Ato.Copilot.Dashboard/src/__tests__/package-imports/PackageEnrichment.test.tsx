import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PackageDetail } from '../../features/package-imports/PackageImportsPage';
import * as api from '../../features/package-imports/api';
import { page, packageStatus, reviewState } from './fixtures';
import './crypto';

vi.mock('../../features/package-imports/api', async original => ({
  ...await original<typeof api>(), getPackageStatus: vi.fn(), getPackageReviewState: vi.fn(),
  getPackageCandidates: vi.fn(), getPackageEntries: vi.fn(), enrichPackage: vi.fn(), getAnalysisOperation: vi.fn(),
}));
beforeEach(() => {
  vi.clearAllMocks(); sessionStorage.clear();
  vi.mocked(api.getPackageStatus).mockResolvedValue({ ...packageStatus(), analysisProfileVersion: 1 });
  vi.mocked(api.getPackageReviewState).mockResolvedValue(reviewState());
  vi.mocked(api.getPackageCandidates).mockResolvedValue(page([]));
  vi.mocked(api.getPackageEntries).mockResolvedValue(page([]));
  vi.mocked(api.enrichPackage).mockResolvedValue({ operationId: 'enrichment-1', packageId: 'package-1', targetAnalysisProfileVersion: 2, state: 'Processing', existing: false });
  vi.mocked(api.getAnalysisOperation).mockResolvedValue({ operationId: 'enrichment-1', packageId: 'package-1', sourceProfileVersion: 1, targetAnalysisProfileVersion: 2, state: 'Processing' });
});
describe('explicit retained-source profile enrichment', () => {
  it('does not reprocess an old package on mount and requires explicit profile-2 intent', async () => {
    // Arrange
    render(<MemoryRouter><PackageDetail packageId="package-1" /></MemoryRouter>);
    // Act
    const button = await screen.findByRole('button', { name: 'Analyze additional authorization claim families' });
    expect(api.enrichPackage).not.toHaveBeenCalled();
    fireEvent.click(button);
    // Assert
    await waitFor(() => expect(api.enrichPackage).toHaveBeenCalledWith('package-1', {
      expectedRevision: 4, targetAnalysisProfileVersion: 2,
    }, expect.any(String)));
    expect(await screen.findByText(/Enrichment operation: Processing/)).toBeInTheDocument();
  });
  it('retries an uncertain operation with its original revision and durable key', async () => {
    // Arrange
    vi.mocked(api.enrichPackage).mockRejectedValueOnce(new Error('No response')).mockResolvedValue({
      operationId: 'enrichment-1', packageId: 'package-1', targetAnalysisProfileVersion: 2, state: 'Processing', existing: true,
    });
    const view = render(<MemoryRouter><PackageDetail packageId="package-1" /></MemoryRouter>);
    fireEvent.click(await screen.findByRole('button', { name: 'Analyze additional authorization claim families' }));
    await screen.findByText(/No response/);
    const request = vi.mocked(api.enrichPackage).mock.calls[0];
    view.unmount();
    vi.mocked(api.getPackageStatus).mockResolvedValue({ ...packageStatus({ revision: 9 }), analysisProfileVersion: 1 });
    vi.mocked(api.getPackageReviewState).mockResolvedValue(reviewState({ revision: 9 }));
    // Act
    render(<MemoryRouter><PackageDetail packageId="package-1" /></MemoryRouter>);
    fireEvent.click(await screen.findByRole('button', { name: 'Recover enrichment receipt' }));
    // Assert
    await waitFor(() => expect(api.enrichPackage).toHaveBeenCalledTimes(2));
    expect(vi.mocked(api.enrichPackage).mock.calls[1]).toEqual(request);
  });
  it('recovers persisted operation status after remount without starting another analysis', async () => {
    // Arrange
    const view = render(<MemoryRouter><PackageDetail packageId="package-1" /></MemoryRouter>);
    fireEvent.click(await screen.findByRole('button', { name: 'Analyze additional authorization claim families' }));
    await screen.findByText(/Enrichment operation: Processing/);
    view.unmount();
    // Act
    render(<MemoryRouter><PackageDetail packageId="package-1" /></MemoryRouter>);
    // Assert
    await screen.findByText(/Enrichment operation: Processing/);
    expect(api.enrichPackage).toHaveBeenCalledOnce();
    expect(api.getAnalysisOperation).toHaveBeenCalledWith('package-1', 'enrichment-1', expect.any(AbortSignal));
  });
});
