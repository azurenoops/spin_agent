import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PackageAssociationRedirect } from '../../features/provider-authorizations/AuthorizationsPage';
import * as packages from '../../features/package-imports/api';
import * as api from '../../features/provider-authorizations/api';
import { receipt } from './testData';
import { page } from '../package-imports/fixtures';
import { PackageImportError } from '../../features/package-imports/request';

vi.mock('../../features/provider-authorizations/api', async original => ({
  ...await original<typeof api>(), getAssociatedPackage: vi.fn(), listOfferings: vi.fn(), associatePackage: vi.fn(),
}));
vi.mock('../../features/package-imports/api', async original => ({ ...await original<typeof packages>(), getPackageCandidates: vi.fn() }));
function Location() { const location = useLocation(); return <output>{location.pathname}{location.search}{location.hash}</output>; }
beforeEach(() => { vi.clearAllMocks(); vi.mocked(packages.getPackageCandidates).mockResolvedValue(page([])); vi.mocked(api.listOfferings).mockResolvedValue(page([])); });
describe('retained package association recovery', () => {
  it('does not silently switch away from the selected offering for an already-associated receipt', async () => {
    // Arrange
    vi.mocked(api.getAssociatedPackage).mockResolvedValue(receipt.package);
    // Act
    render(<MemoryRouter><PackageAssociationRedirect packageId={receipt.package.packageId} initialOfferingId="another-offering" /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('This package is associated with a different offering');
    expect(screen.queryByLabelText('Offering')).not.toBeInTheDocument();
    expect(api.associatePackage).not.toHaveBeenCalled();
  });
  it('resolves the stored association while preserving the full source-review bookmark', async () => {
    // Arrange
    vi.mocked(api.getAssociatedPackage).mockResolvedValue(receipt.package);
    render(<MemoryRouter initialEntries={['/import?type=AssessmentFinding&page=2&candidate=source-1#citation']}>
      <Routes><Route path="/import" element={<PackageAssociationRedirect packageId={receipt.package.packageId} />} />
        <Route path="*" element={<Location />} /></Routes>
    </MemoryRouter>);
    // Act
    const destination = await screen.findByText(`/workspaces/csp/authorizations/offerings/offering-1/packages/${receipt.package.packageId}?type=AssessmentFinding&page=2&candidate=source-1#citation`);
    // Assert
    expect(destination).toHaveTextContent(`/workspaces/csp/authorizations/offerings/offering-1/packages/${receipt.package.packageId}?type=AssessmentFinding&page=2&candidate=source-1#citation`);
    expect(api.associatePackage).not.toHaveBeenCalled();
  });
  it('keeps unassociated receipts explicit and never auto-selects a convenient offering', async () => {
    // Arrange
    vi.mocked(api.getAssociatedPackage).mockResolvedValue({ ...receipt.package, association: null, processingState: 'ReadyForReview' });
    // Act
    render(<MemoryRouter><PackageAssociationRedirect packageId={receipt.package.packageId} /></MemoryRouter>);
    // Assert
    expect(await screen.findByText('Review extracted scope')).toBeInTheDocument();
    expect(screen.queryByLabelText('Offering')).not.toBeInTheDocument();
    const manual = await screen.findByText('Enter offering and boundary manually');
    await act(async () => { fireEvent.click(manual); });
    expect(screen.getByLabelText('Offering')).toHaveValue('');
    expect(api.associatePackage).not.toHaveBeenCalled();
  });
  it('does not turn a denied association lookup into an unassociated editable package', async () => {
    // Arrange
    vi.mocked(api.getAssociatedPackage).mockRejectedValueOnce(new PackageImportError('Package access denied', 403))
      .mockResolvedValue({ ...receipt.package, association: null, processingState: 'ReadyForReview' });
    render(<MemoryRouter><PackageAssociationRedirect packageId={receipt.package.packageId} /></MemoryRouter>);
    // Act
    expect(await screen.findByRole('alert')).toHaveTextContent('Package access denied');
    expect(screen.queryByLabelText('Offering')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    // Assert
    const manual = await screen.findByText('Enter offering and boundary manually');
    await act(async () => { fireEvent.click(manual); });
    expect(await screen.findByLabelText('Offering')).toHaveValue('');
    expect(api.associatePackage).not.toHaveBeenCalled();
  });
});
