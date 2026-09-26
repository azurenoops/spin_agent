import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PackageEntries } from '../../features/package-imports/PackageEntries';
import * as api from '../../features/package-imports/api';
import { entry, page } from './fixtures';

vi.mock('../../features/package-imports/api', async importOriginal => ({
  ...await importOriginal<typeof import('../../features/package-imports/api')>(),
  getPackageEntries: vi.fn(), excludePackageEntry: vi.fn(),
}));
vi.mock('../../components/AuthenticatedDownload', () => ({ default: ({ children }: { children: React.ReactNode }) => <button>{children}</button> }));
beforeEach(() => vi.clearAllMocks());

describe('complete source manifest', () => {
  it.each(['app.xml', 'theme1.xml'])('shows one neutral exclusion notice for workbook metadata %s', async fileName => {
    // Arrange
    const reason = 'Package formatting, relationship or metadata part; retained without executing its contents.';
    vi.mocked(api.getPackageEntries).mockResolvedValue(page([entry({
      fileName, status: 'Excluded', reason, exclusionReason: reason, candidateCount: 0,
    })]));
    const changed = vi.fn();
    // Act
    render(<PackageEntries packageId="package-1" revision={4} disabled={false} onChanged={changed} />);
    const notices = await screen.findAllByText(/Package formatting, relationship or metadata part/);
    // Assert
    expect(notices).toHaveLength(1);
    expect(notices[0]).toHaveTextContent(`Excluded from analysis: ${reason}`);
    expect(notices[0]).toHaveClass('bg-white', 'dark:bg-gray-900');
    expect(notices[0]).not.toHaveClass('bg-amber-50');
    expect(screen.getByText('Excluded', { exact: true })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Download source' })).toBeInTheDocument();
    expect(screen.queryByText('Exclude source entry')).not.toBeInTheDocument();
    expect(api.excludePackageEntry).not.toHaveBeenCalled();
    expect(changed).not.toHaveBeenCalled();
  });

  it.each(['reason', 'exclusionReason'] as const)('retains an excluded explanation provided only as %s', async field => {
    // Arrange
    const reason = 'Outside the approved scope.';
    vi.mocked(api.getPackageEntries).mockResolvedValue(page([entry({ status: 'Excluded', [field]: reason })]));
    // Act
    render(<PackageEntries packageId="package-1" revision={4} disabled={false} onChanged={vi.fn()} />);
    // Assert
    expect(await screen.findByText(`Excluded from analysis: ${reason}`)).toHaveClass('bg-white', 'dark:bg-gray-900');
    expect(screen.getAllByText(/Outside the approved scope/)).toHaveLength(1);
  });

  it('preserves a distinct processing warning alongside a neutral explicit exclusion', async () => {
    // Arrange
    vi.mocked(api.getPackageEntries).mockResolvedValue(page([entry({
      status: 'Excluded', reason: 'Image-only PDF requires OCR.', exclusionReason: 'Outside the approved scope.',
      familyCoverage: [{ family: 'Inventory', status: 'Unavailable', reason: 'Inventory was not analyzed.' }],
    })]));
    // Act
    render(<PackageEntries packageId="package-1" revision={4} disabled={false} onChanged={vi.fn()} />);
    // Assert
    expect(await screen.findByText('Image-only PDF requires OCR.')).toHaveClass('bg-amber-50');
    expect(screen.getByText('Excluded from analysis: Outside the approved scope.')).toHaveClass('bg-white', 'dark:bg-gray-900');
    expect(screen.getByText('Inventory was not analyzed.').closest('li')).toHaveClass('bg-amber-50');
  });

  it.each(['Unsupported', 'Unreadable', 'Failed'])('keeps %s analysis failures visually flagged', async status => {
    // Arrange
    const reason = 'Model output was incomplete, unsupported, or not grounded in its supplied source quotes.';
    vi.mocked(api.getPackageEntries).mockResolvedValue(page([entry({ status, reason })]));
    // Act
    render(<PackageEntries packageId="package-1" revision={4} disabled={false} onChanged={vi.fn()} />);
    // Assert
    expect(await screen.findByText(reason)).toHaveClass('bg-amber-50');
    expect(screen.queryByText(/Excluded from analysis:/)).not.toBeInTheDocument();
    expect(screen.getByText('Exclude source entry')).toBeInTheDocument();
  });

  it('does not downgrade a failure to neutral information unless the entry is excluded', async () => {
    // Arrange
    const reason = 'Analysis could not be completed.';
    vi.mocked(api.getPackageEntries).mockResolvedValue(page([entry({
      status: 'Failed', reason, exclusionReason: reason,
    })]));
    // Act
    render(<PackageEntries packageId="package-1" revision={4} disabled={false} onChanged={vi.fn()} />);
    // Assert
    const notices = await screen.findAllByText(/Analysis could not be completed/);
    expect(notices).toHaveLength(1);
    expect(notices[0]).toHaveClass('bg-amber-50');
    expect(screen.getByText('Failed', { exact: true })).toBeInTheDocument();
  });

  it('keeps excluded entries without a supplied reason visible without inventing an explanation', async () => {
    // Arrange
    vi.mocked(api.getPackageEntries).mockResolvedValue(page([entry({ status: 'Excluded' })]));
    // Act
    render(<PackageEntries packageId="package-1" revision={4} disabled={false} onChanged={vi.fn()} />);
    // Assert
    expect(await screen.findByText('Excluded', { exact: true })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Download source' })).toBeInTheDocument();
    expect(screen.queryByText(/Excluded from analysis:/)).not.toBeInTheDocument();
  });

  it('does not equate extracted text with complete authorization-family analysis', async () => {
    // Arrange
    vi.mocked(api.getPackageEntries).mockResolvedValue(page([entry({ status: 'Processed', familyCoverage: [
      { family: 'Inventory', status: 'Analyzed', reason: null },
      { family: 'AssessmentFinding', status: 'Unavailable', reason: 'Finding semantics were not analyzed.' },
    ] })]));
    // Act
    render(<PackageEntries packageId="package-1" revision={4} disabled={false} onChanged={vi.fn()} />);
    // Assert
    expect(await screen.findByText('Finding semantics were not analyzed.')).toBeInTheDocument();
    expect(screen.getByText(/AssessmentFinding.*Unavailable/)).toBeInTheDocument();
    expect(screen.getByText(/Successful extraction does not establish complete family coverage/)).toBeInTheDocument();
  });
  it('requires an explicit reason and retains failed evidence as excluded rather than processed', async () => {
    // Arrange
    const changed = vi.fn();
    vi.mocked(api.getPackageEntries).mockResolvedValue(page([entry({ status: 'Unreadable', reason: 'Image-only PDF requires OCR.' })]));
    vi.mocked(api.excludePackageEntry).mockResolvedValue(entry({ status: 'Excluded', exclusionReason: 'Outside the approved scope.' }));
    render(<PackageEntries packageId="package-1" revision={4} disabled={false} onChanged={changed} />);
    // Act
    await screen.findByText('Image-only PDF requires OCR.');
    fireEvent.click(screen.getByRole('button', { name: 'Exclude entry' }));
    expect(screen.getByRole('alert')).toHaveTextContent('rationale');
    fireEvent.change(screen.getByLabelText('Exclusion rationale for package.json'), { target: { value: 'Outside the approved scope.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Exclude entry' }));
    // Assert
    await waitFor(() => expect(api.excludePackageEntry).toHaveBeenCalledWith('package-1', 'entry-1', 1, 'Outside the approved scope.'));
    expect(changed).toHaveBeenCalledOnce();
  });

  it('preserves exclusion input when the server rejects a stale entry revision', async () => {
    // Arrange
    vi.mocked(api.getPackageEntries).mockResolvedValue(page([entry({ status: 'Failed' })]));
    vi.mocked(api.excludePackageEntry).mockRejectedValue(new Error('Entry revision is stale.'));
    render(<PackageEntries packageId="package-1" revision={4} disabled={false} onChanged={vi.fn()} />);
    const input = await screen.findByLabelText('Exclusion rationale for package.json');
    // Act
    fireEvent.change(input, { target: { value: 'Keep this reason.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Exclude entry' }));
    // Assert
    expect(await screen.findByText('Entry revision is stale.')).toBeInTheDocument();
    expect(input).toHaveValue('Keep this reason.');
  });
});
