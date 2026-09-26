import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PackageDetail, PackageImportsPage } from '../../features/package-imports/PackageImportsPage';
import { PackageSourcesPanel } from '../../features/package-imports/PackageSourcesPanel';
import * as api from '../../features/package-imports/api';
import * as authorizationApi from '../../features/provider-authorizations/api';
import { acceptedImpact, emptyImpactPage } from '../provider-authorizations/impactFixtures';
import { PackageImportError } from '../../features/package-imports/request';
import { candidate, entry, packageStatus, page, preview, reviewState } from './fixtures';
import type { PackagePreview } from '../../features/package-imports/types';
import './crypto';

vi.mock('../../components/layout/PageLayout', () => ({ default: ({ children }: { children: React.ReactNode }) => <main>{children}</main> }));
vi.mock('../../components/layout/PageHero', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../components/AuthenticatedDownload', () => ({ default: ({ children }: { children: React.ReactNode }) => <button>{children}</button> }));
vi.mock('../../features/package-imports/api', async importOriginal => ({
  ...await importOriginal<typeof import('../../features/package-imports/api')>(),
  listPackages: vi.fn(), getPackageStatus: vi.fn(), getPackageEntries: vi.fn(), getPackageCandidates: vi.fn(),
  previewPackage: vi.fn(), approvePackage: vi.fn(), publishPackage: vi.fn(), retryPackage: vi.fn(), excludePackageEntry: vi.fn(),
  receivePackage: vi.fn(), getPackageReviewState: vi.fn(),
}));
vi.mock('../../features/provider-authorizations/api', async importOriginal => ({
  ...await importOriginal<typeof import('../../features/provider-authorizations/api')>(), listImpactReviews: vi.fn(),
}));

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(authorizationApi.listImpactReviews).mockResolvedValue({ ...emptyImpactPage, total: 2, items: [
    acceptedImpact, { ...acceptedImpact, reviewId: 'impact-2', title: 'Boundary coverage change' },
  ] });
  sessionStorage.clear();
  vi.mocked(api.listPackages).mockResolvedValue(page([packageStatus()]));
  vi.mocked(api.getPackageStatus).mockResolvedValue(packageStatus());
  vi.mocked(api.getPackageReviewState).mockResolvedValue(reviewState());
  vi.mocked(api.getPackageEntries).mockResolvedValue(page([entry()]));
  vi.mocked(api.getPackageCandidates).mockResolvedValue(page([candidate({ reviewState: 'Reviewed' })]));
  vi.mocked(api.previewPackage).mockResolvedValue(preview());
  vi.mocked(api.approvePackage).mockResolvedValue(preview({ state: 'Approved' }));
  vi.mocked(api.publishPackage).mockResolvedValue({ packageId: 'package-1', publicationState: 'Published', existing: false,
    records: [{ candidateId: 'candidate-1', recordId: 'published-1', releaseId: null, type: 'Component' }] });
});
const renderDetail = () => render(<MemoryRouter><PackageImportsPage packageId="package-1" /></MemoryRouter>);
const selectAndPreview = async () => {
  fireEvent.click(await screen.findByRole('checkbox', { name: /Select Synthetic source component/ }));
  fireEvent.click(screen.getByRole('button', { name: 'Preview selected revisions' }));
  await screen.findByRole('heading', { name: 'Approval preview' });
};

describe('private package portal', () => {
  it('binds offering impact reviews to a fresh package preview and invalidates it on changed review selection', async () => {
    // Arrange
    vi.mocked(api.previewPackage).mockResolvedValue(preview({
      impactReviewIds: ['impact-1', 'impact-2'], contextSnapshotHash: 'server-package-context',
    }));
    render(<MemoryRouter><PackageDetail packageId="package-1" offeringId="offering-1"
      packageVersionId="version-1" boundaryRevisionId="boundary-1" /></MemoryRouter>);
    await screen.findByRole('checkbox', { name: /Select Synthetic source component/ });
    // Act
    fireEvent.click(await screen.findByRole('checkbox', { name: /^Logging coverage change/ }));
    fireEvent.click(screen.getByRole('checkbox', { name: /^Boundary coverage change/ }));
    await selectAndPreview();
    // Assert
    expect(api.previewPackage).toHaveBeenCalledWith('package-1', {
      expectedRevision: 4, candidates: [{ candidateId: 'candidate-1', revision: 1 }], impactReviewIds: ['impact-1', 'impact-2'],
    });
    expect(screen.getByRole('link', { name: 'Review changes' })).toHaveAttribute('href',
      '/workspaces/csp/authorizations/offerings/offering-1/impact?packageVersionId=version-1&packageId=package-1&boundaryRevisionId=boundary-1');
    const contextDetails = screen.getByText('server-package-context').closest('details');
    expect(contextDetails).not.toHaveAttribute('open');
    if (!contextDetails) throw new Error('Publication context Details missing.');
    fireEvent.click(within(contextDetails).getByText('Details'));
    expect(within(contextDetails).getByText('impact-2')).toBeVisible();
    fireEvent.click(screen.getByRole('checkbox', { name: /^Boundary coverage change/ }));
    expect(screen.getByRole('button', { name: 'Approve exact preview' })).toBeDisabled();
    expect(screen.queryByText('server-package-context')).not.toBeInTheDocument();
    expect(api.publishPackage).not.toHaveBeenCalled();
  });
  it('keeps a publication gate aside next to review records with compact metrics', async () => {
    // Arrange
    renderDetail();
    // Act
    const gate = await screen.findByRole('complementary', { name: 'Approval and publication' });
    // Assert
    expect(gate.parentElement).toHaveClass('lg:grid-cols-[minmax(0,1fr)_22rem]');
    expect(gate).toHaveClass('lg:sticky');
    expect(screen.getByRole('region', { name: 'Package summary' })).toHaveTextContent('1 of 1 source entries processed');
  });
  it('preserves selected records when switching to source files and back', async () => {
    // Arrange
    renderDetail();
    const record = await screen.findByRole('checkbox', { name: /Select Synthetic source component/ });
    // Act
    fireEvent.click(record);
    fireEvent.click(screen.getByRole('button', { name: 'Source files' }));
    // Assert
    expect(screen.queryByRole('heading', { name: 'Review extracted records' })).not.toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Source entries' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Extracted records' }));
    expect(screen.getByRole('checkbox', { name: /Select Synthetic source component/ })).toBeChecked();
  });
  it('pages stored packages and uploads outside onboarding without publishing', async () => {
    // Arrange
    vi.mocked(api.listPackages).mockResolvedValue(page([packageStatus()], 1, 30));
    vi.mocked(api.receivePackage).mockResolvedValue(packageStatus({ name: 'New package', packageId: 'new-package', processingState: 'Received' }));
    render(<MemoryRouter><PackageImportsPage /></MemoryRouter>);
    await screen.findByText('Synthetic package');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    await waitFor(() => expect(api.listPackages).toHaveBeenLastCalledWith(2, expect.any(AbortSignal)));
    fireEvent.change(screen.getByLabelText('Select source files'), { target: { files: [new File(['source'], 'source.json')] } });
    fireEvent.click(screen.getByRole('button', { name: 'Upload package' }));
    // Assert
    expect(await screen.findByText('New package')).toBeInTheDocument();
    expect(api.receivePackage).toHaveBeenCalledOnce();
    expect(api.approvePackage).not.toHaveBeenCalled();
    expect(api.publishPackage).not.toHaveBeenCalled();
  });

  it('retains a retry key until unfinished processing is durably accepted', async () => {
    // Arrange
    vi.mocked(api.getPackageStatus).mockResolvedValue(packageStatus({ processingState: 'NeedsAttention', lastError: 'Analysis unavailable.' }));
    vi.mocked(api.retryPackage).mockRejectedValueOnce(new Error('Retry receipt unknown.')).mockResolvedValue(packageStatus({ processingState: 'Received' }));
    renderDetail();
    const retry = await screen.findByRole('button', { name: 'Retry unfinished analysis' });
    // Act
    fireEvent.click(retry);
    await screen.findByText('Retry receipt unknown.');
    fireEvent.click(retry);
    // Assert
    await waitFor(() => expect(api.retryPackage).toHaveBeenCalledTimes(2));
    expect(vi.mocked(api.retryPackage).mock.calls[0]).toEqual(vi.mocked(api.retryPackage).mock.calls[1]);
  });
  it('shows loading, access denial and explicit retry rather than an empty success state', async () => {
    // Arrange
    let reject!: (reason: Error) => void;
    vi.mocked(api.getPackageStatus).mockImplementationOnce(() => new Promise((_, fail) => { reject = fail; }));
    renderDetail();
    expect(screen.getAllByRole('status').every(element => element.textContent?.includes('Loading'))).toBe(true);
    // Act
    await act(async () => reject(new PackageImportError('Provider access denied.', 403)));
    expect(screen.getByRole('alert')).toHaveTextContent('Provider access denied.');
    expect(screen.queryByText(/No source packages/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    // Assert
    expect(await screen.findByRole('heading', { name: 'Synthetic package', level: 2 })).toBeInTheDocument();
  });

  it('keeps approval and publication separate and uses exact preview revisions', async () => {
    // Arrange
    renderDetail();
    // Act
    await selectAndPreview();
    expect(screen.getByText('Server preview returned no blockers. Approval is still required before publication.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Publish approved set' })).toBeDisabled();
    expect(api.approvePackage).not.toHaveBeenCalled();
    expect(screen.getByRole('list', { name: 'Exact preview selection' })).toHaveTextContent('candidate-1');
    fireEvent.click(screen.getByRole('button', { name: 'Approve exact preview' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Publish approved set' })).toBeEnabled());
    expect(screen.getByText('Server approval is recorded for this exact preview. Publication is a separate action.')).toBeInTheDocument();
    expect(screen.queryByText(/Approval is still required before publication/)).not.toBeInTheDocument();
    expect(api.publishPackage).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Publish approved set' }));
    // Assert
    expect(await screen.findByText('published-1')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Publication recorded: Published' })).toBeInTheDocument();
    expect(screen.queryByText(/Approval is still required before publication/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Publication is a separate action/)).not.toBeInTheDocument();
    expect(api.previewPackage).toHaveBeenCalledWith('package-1', { expectedRevision: 4, candidates: [{ candidateId: 'candidate-1', revision: 1 }] });
    expect(api.approvePackage).toHaveBeenCalledWith('package-1', { previewId: 'preview-1', previewHash: 'exact-hash', revision: 4 });
    expect(api.publishPackage).toHaveBeenCalledWith('package-1', { previewId: 'preview-1', previewHash: 'exact-hash', revision: 4 }, expect.any(String));
  });

  it.each([
    ['Published', 'The server recorded publication for this exact preview.'],
    ['Invalidated', 'This preview is not eligible for approval or publication. Refresh the package to review its current state.'],
  ])('does not prompt for approval or publication when the server decision is %s', async (state, copy) => {
    // Arrange
    vi.mocked(api.previewPackage).mockResolvedValue(preview({ state }));
    renderDetail();
    // Act
    await selectAndPreview();
    // Assert
    expect(screen.getByText(copy)).toBeInTheDocument();
    expect(screen.queryByText(/Approval is still required before publication/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Approve exact preview' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Publish approved set' })).toBeDisabled();
  });

  it('renders server blockers and cannot approve or publish a blocked set', async () => {
    // Arrange
    vi.mocked(api.previewPackage).mockResolvedValue(preview({ blockers: ['Resolve unreadable source evidence.'] }));
    renderDetail();
    // Act
    await selectAndPreview();
    // Assert
    expect(screen.getByText('Resolve unreadable source evidence.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Approve exact preview' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Publish approved set' })).toBeDisabled();
    expect(api.approvePackage).not.toHaveBeenCalled();
  });

  it('does not concurrently approve and invalidates stale publication after conflict', async () => {
    // Arrange
    let resolve!: (value: PackagePreview) => void;
    vi.mocked(api.approvePackage).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    vi.mocked(api.publishPackage).mockRejectedValue(new PackageImportError('Dependency revision changed.', 409));
    renderDetail();
    await selectAndPreview();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Approve exact preview' }));
    fireEvent.click(screen.getByRole('button', { name: 'Approve exact preview' }));
    expect(api.approvePackage).toHaveBeenCalledOnce();
    await act(async () => resolve(preview({ state: 'Approved' })));
    fireEvent.click(screen.getByRole('button', { name: 'Publish approved set' }));
    // Assert
    expect(await screen.findByText('Dependency revision changed.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Publish approved set' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Reload current package' })).toBeInTheDocument();
  });

  it('invalidates a preview when the package revision changes on refresh', async () => {
    // Arrange
    renderDetail();
    await selectAndPreview();
    vi.mocked(api.getPackageStatus).mockResolvedValue(packageStatus({ revision: 5 }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Refresh package' }));
    // Assert
    expect(await screen.findByText(/Package revision changed/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Approve exact preview' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Publish approved set' })).toBeDisabled();
  });

  it('retains the publication key on an uncertain failure for safe replay', async () => {
    // Arrange
    vi.mocked(api.publishPackage).mockRejectedValueOnce(new Error('Publication outcome unknown.')).mockResolvedValue({
      packageId: 'package-1', publicationState: 'Published', records: [], existing: true,
    });
    renderDetail();
    await selectAndPreview();
    fireEvent.click(screen.getByRole('button', { name: 'Approve exact preview' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Publish approved set' })).toBeEnabled());
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Publish approved set' }));
    await screen.findByText('Publication outcome unknown.');
    fireEvent.click(screen.getByRole('button', { name: 'Publish approved set' }));
    // Assert
    await waitFor(() => expect(api.publishPackage).toHaveBeenCalledTimes(2));
    expect(vi.mocked(api.publishPackage).mock.calls[1]).toEqual(vi.mocked(api.publishPackage).mock.calls[0]);
  });

  it('cannot replace an uncertain publication with a new preview or editable selection', async () => {
    // Arrange
    vi.mocked(api.publishPackage).mockRejectedValueOnce(new Error('Publication outcome unknown.')).mockResolvedValue({
      packageId: 'package-1', publicationState: 'Published', records: [], existing: true,
    });
    renderDetail();
    await selectAndPreview();
    fireEvent.click(screen.getByRole('button', { name: 'Approve exact preview' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Publish approved set' })).toBeEnabled());
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Publish approved set' }));
    await screen.findByText('Publication outcome unknown.');
    // Assert
    expect(screen.getByRole('button', { name: 'Preview selected revisions' })).toBeDisabled();
    expect(screen.getByRole('checkbox', { name: /Select Synthetic source component/ })).toBeDisabled();
    expect(screen.getByRole('button', { name: /Review Synthetic source component/ })).toBeDisabled();
    expect(screen.queryByRole('region', { name: 'Persisted publication outcome' })).not.toBeInTheDocument();
    vi.mocked(api.getPackageReviewState).mockResolvedValue(reviewState({ preview: preview({ state: 'Approved' }) }));
    fireEvent.click(screen.getByRole('button', { name: 'Refresh package' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Publish approved set' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Publish approved set' }));
    await waitFor(() => expect(api.publishPackage).toHaveBeenCalledTimes(2));
    expect(vi.mocked(api.publishPackage).mock.calls[1]).toEqual(vi.mocked(api.publishPackage).mock.calls[0]);
    expect(api.previewPackage).toHaveBeenCalledOnce();
  });

  it('uses server candidate filters and independent page controls', async () => {
    // Arrange
    vi.mocked(api.getPackageCandidates).mockResolvedValue(page([candidate()], 1, 30));
    renderDetail();
    await screen.findByRole('button', { name: /Review Synthetic source component/ });
    // Act
    fireEvent.change(screen.getByLabelText('Candidate type'), { target: { value: 'Capability' } });
    fireEvent.change(screen.getByLabelText('Review state'), { target: { value: 'NeedsReview' } });
    await waitFor(() => expect(api.getPackageCandidates).toHaveBeenLastCalledWith('package-1',
      { page: 1, pageSize: 25, type: 'Capability', reviewState: 'NeedsReview' }, expect.any(AbortSignal)));
    fireEvent.click(within(screen.getByRole('region', { name: 'Candidate records' })).getByRole('button', { name: 'Next' }));
    // Assert
    await waitFor(() => expect(api.getPackageCandidates).toHaveBeenLastCalledWith('package-1',
      { page: 2, pageSize: 25, type: 'Capability', reviewState: 'NeedsReview' }, expect.any(AbortSignal)));
  });

  it('links sources and import status without claiming an authorization was verified', async () => {
    // Arrange
    render(<MemoryRouter><PackageSourcesPanel /></MemoryRouter>);
    // Act
    const link = await screen.findByRole('link', { name: 'Review sources and import' });
    // Assert
    expect(link).toHaveAttribute('href', '/workspaces/csp/security-capabilities/imports/package-1');
    expect(screen.getByText(/not verified authorization decisions/i)).toBeInTheDocument();
  });

  it('filters Published records and keeps their source review read-only', async () => {
    // Arrange
    vi.mocked(api.getPackageCandidates).mockResolvedValue(page([candidate({ reviewState: 'Published', publishedRecordId: 'published-record-1' })]));
    renderDetail();
    await screen.findByRole('button', { name: /Review Synthetic source component/ });
    // Act
    fireEvent.change(screen.getByLabelText('Review state'), { target: { value: 'Published' } });
    await waitFor(() => expect(api.getPackageCandidates).toHaveBeenLastCalledWith('package-1',
      { page: 1, pageSize: 25, type: undefined, reviewState: 'Published' }, expect.any(AbortSignal)));
    // Assert
    expect(screen.getByRole('checkbox', { name: /Select Synthetic source component/ })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: /Review Synthetic source component/ }));
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Mark reviewed' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Reject candidate' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Download cited source' })).toBeEnabled();
  });
});
