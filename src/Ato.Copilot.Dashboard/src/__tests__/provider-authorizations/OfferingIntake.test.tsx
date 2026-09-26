import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { OfferingIntake } from '../../features/provider-authorizations/OfferingIntake';
import * as api from '../../features/provider-authorizations/api';
import * as packageApi from '../../features/package-imports/api';
import { PackageImportError } from '../../features/package-imports/request';
import { offering, boundary, receipt } from './testData';
import { page } from '../package-imports/fixtures';
import '../package-imports/crypto';

vi.mock('../../features/provider-authorizations/api', async original => ({
  ...await original<typeof api>(), getOffering: vi.fn(), listBoundaries: vi.fn(), uploadPackage: vi.fn(),
  getAssociatedPackage: vi.fn(), associatePackage: vi.fn(),
}));
vi.mock('../../features/package-imports/api', async original => ({
  ...await original<typeof packageApi>(), getPackageStatus: vi.fn(),
}));
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getOffering).mockResolvedValue(offering);
  vi.mocked(api.listBoundaries).mockResolvedValue(page([boundary]));
  vi.mocked(api.uploadPackage).mockResolvedValue(receipt);
  vi.mocked(packageApi.getPackageStatus).mockResolvedValue(receipt.package);
  vi.mocked(api.getAssociatedPackage).mockResolvedValue({ ...receipt.package, association: null });
  vi.mocked(api.associatePackage).mockResolvedValue(receipt);
});
async function prepare() {
  await screen.findByRole('option', { name: /Test service boundary/ });
  fireEvent.change(screen.getByLabelText('Boundary revision'), { target: { value: boundary.boundaryRevisionId } });
  fireEvent.change(screen.getByLabelText('Package name'), { target: { value: 'Original package name' } });
  fireEvent.change(screen.getByLabelText('Select source files'), { target: { files: [new File(['original bytes'], 'original.txt')] } });
}
describe('versioned offering intake', () => {
  it('releases preparation controls and the wizard pending gate after a durable receipt and reload', async () => {
    // Arrange
    const pending = vi.fn();
    render(<MemoryRouter><OfferingIntake initialOfferingId={offering.offeringId} onPendingChange={pending} /></MemoryRouter>);
    await prepare();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Upload package' }));
    await screen.findByText(/Receipt confirmed/);
    // Assert
    await waitFor(() => expect(screen.getByLabelText('Boundary revision')).toBeEnabled());
    expect(pending).toHaveBeenLastCalledWith(false);
    expect(api.getOffering).toHaveBeenCalledTimes(2);
  });
  it('reloads both offering and retained package after association instead of reusing stale revisions', async () => {
    // Arrange
    vi.mocked(api.getAssociatedPackage).mockResolvedValueOnce({ ...receipt.package, association: null }).mockResolvedValue(receipt.package);
    const received = vi.fn();
    render(<MemoryRouter><OfferingIntake initialOfferingId={offering.offeringId} existingPackageId={receipt.package.packageId} onReceived={received} /></MemoryRouter>);
    await screen.findByRole('option', { name: /Test service boundary/ });
    fireEvent.change(screen.getByLabelText('Boundary revision'), { target: { value: boundary.boundaryRevisionId } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Associate retained package' }));
    // Assert
    await waitFor(() => expect(api.getOffering).toHaveBeenCalledTimes(2));
    expect(api.getAssociatedPackage).toHaveBeenCalledTimes(2);
    expect(received).toHaveBeenCalledWith(receipt);
    expect(await screen.findByRole('button', { name: 'Associate retained package' })).toBeDisabled();
  });
  it('locks association context on an uncertain outcome and retries the original exact request', async () => {
    // Arrange
    vi.mocked(api.associatePackage).mockRejectedValueOnce(new Error('Association response lost.')).mockResolvedValue(receipt);
    render(<MemoryRouter><OfferingIntake initialOfferingId={offering.offeringId} existingPackageId={receipt.package.packageId} /></MemoryRouter>);
    await screen.findByRole('option', { name: /Test service boundary/ });
    fireEvent.change(screen.getByLabelText('Boundary revision'), { target: { value: boundary.boundaryRevisionId } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Associate retained package' }));
    await screen.findByText(/Outcome uncertain/);
    // Assert
    expect(screen.getByLabelText('Boundary revision')).toBeDisabled();
    const first = vi.mocked(api.associatePackage).mock.calls[0];
    fireEvent.click(screen.getByRole('button', { name: 'Retry same operation' }));
    await screen.findByText(/Receipt confirmed/);
    expect(vi.mocked(api.associatePackage).mock.calls[1]).toEqual(first);
  });
  it('binds an explicit successor to its existing package series without replacing retained history', async () => {
    // Arrange
    render(<MemoryRouter><OfferingIntake initialOfferingId={offering.offeringId} previousVersion={receipt.packageVersion} /></MemoryRouter>);
    await prepare();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Upload package' }));
    // Assert
    await waitFor(() => expect(api.uploadPackage).toHaveBeenCalledWith(offering.offeringId, expect.objectContaining({
      seriesId: receipt.packageVersion.seriesId, previousVersionId: receipt.packageVersion.packageVersionId,
      expectedOfferingRevision: offering.revision, boundaryRevisionId: boundary.boundaryRevisionId,
    }), expect.any(Array), expect.any(String)));
  });
  it('retains exact name, revision, bytes and key when upload response is lost', async () => {
    // Arrange
    vi.mocked(api.uploadPackage).mockRejectedValueOnce(new Error('Response lost')).mockResolvedValue(receipt);
    render(<MemoryRouter><OfferingIntake initialOfferingId={offering.offeringId} /></MemoryRouter>);
    await prepare();
    fireEvent.click(screen.getByRole('button', { name: 'Upload package' }));
    await screen.findByText(/Files and the upload key are retained/);
    const first = vi.mocked(api.uploadPackage).mock.calls[0];
    // Act
    expect(screen.getByLabelText('Package name')).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Retry same upload' }));
    // Assert
    await screen.findByText(/Receipt confirmed/);
    expect(vi.mocked(api.uploadPackage).mock.calls[1]).toEqual(first);
  });
  it('allows correcting a definitely rejected request instead of replaying the previous form forever', async () => {
    // Arrange
    vi.mocked(api.uploadPackage).mockRejectedValueOnce(new PackageImportError('Name rejected', 422)).mockResolvedValue(receipt);
    render(<MemoryRouter><OfferingIntake initialOfferingId={offering.offeringId} /></MemoryRouter>);
    await prepare();
    fireEvent.click(screen.getByRole('button', { name: 'Upload package' }));
    await screen.findByText(/server rejected this request/i);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Remove original.txt' }));
    fireEvent.change(screen.getByLabelText('Package name'), { target: { value: 'Corrected package name' } });
    fireEvent.change(screen.getByLabelText('Select source files'), { target: { files: [new File(['corrected bytes'], 'corrected.txt')] } });
    fireEvent.click(screen.getByRole('button', { name: 'Upload package' }));
    // Assert
    await screen.findByText(/Receipt confirmed/);
    expect(api.uploadPackage).toHaveBeenLastCalledWith(offering.offeringId, expect.objectContaining({ name: 'Corrected package name' }), expect.arrayContaining([expect.objectContaining({ name: 'corrected.txt' })]), expect.any(String));
  });
});
