import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PackageImportsPage } from '../../features/package-imports/PackageImportsPage';
import * as api from '../../features/package-imports/api';
import type { PackagePreview, PackagePublication } from '../../features/package-imports/types';
import { PackageImportError } from '../../features/package-imports/request';
import { candidate, entry, packageStatus, page, preview } from './fixtures';

const recovery = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock('../../components/layout/PageLayout', () => ({ default: ({ children }: { children: React.ReactNode }) => <main>{children}</main> }));
vi.mock('../../components/layout/PageHero', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../components/AuthenticatedDownload', () => ({ default: ({ children }: { children: React.ReactNode }) => <button>{children}</button> }));
vi.mock('../../features/package-imports/api', async importOriginal => ({
  ...await importOriginal<typeof import('../../features/package-imports/api')>(),
  getPackageReviewState: recovery.get,
  getPackageStatus: vi.fn(), getPackageCandidates: vi.fn(), getPackageEntries: vi.fn(),
  previewPackage: vi.fn(), approvePackage: vi.fn(), publishPackage: vi.fn(),
}));

interface RecoveryFixture {
  packageId: string; revision: number; preview: PackagePreview | null;
  previewIsStale: boolean; publication: PackagePublication | null;
}
const outcome: PackagePublication = {
  packageId: 'package-1', publicationState: 'PartiallyPublished', existing: true,
  records: [{ candidateId: 'previous-candidate', recordId: 'canonical-prior-record', releaseId: 'canonical-prior-release', type: 'Capability' }],
};
const snapshot = (overrides: Partial<RecoveryFixture> = {}): RecoveryFixture => ({
  packageId: 'package-1', revision: 4, preview: null, previewIsStale: false, publication: null, ...overrides,
});
const mount = () => render(<MemoryRouter><PackageImportsPage packageId="package-1" /></MemoryRouter>);

beforeEach(() => {
  vi.clearAllMocks();
  sessionStorage.clear();
  recovery.get.mockResolvedValue(snapshot());
  vi.mocked(api.getPackageStatus).mockResolvedValue(packageStatus());
  vi.mocked(api.getPackageEntries).mockResolvedValue(page([entry()]));
  vi.mocked(api.getPackageCandidates).mockResolvedValue(page([candidate({ reviewState: 'Reviewed' })]));
  vi.mocked(api.previewPackage).mockResolvedValue(preview());
  vi.mocked(api.approvePackage).mockResolvedValue(preview({ state: 'Approved' }));
  vi.mocked(api.publishPackage).mockResolvedValue(outcome);
});

describe('persisted package decision recovery', () => {
  it('restores an approved exact selection on remount without generating or approving a replacement', async () => {
    // Arrange
    recovery.get.mockResolvedValue(snapshot({ preview: preview({ state: 'Approved',
      impactReviewIds: ['retained-impact'], contextSnapshotHash: 'retained-package-context' }) }));
    mount();
    // Act
    await waitFor(() => expect(screen.getByRole('button', { name: 'Publish approved set' })).toBeEnabled());
    expect(screen.getByText('retained-package-context')).toBeInTheDocument();
    expect(screen.getByText('retained-impact')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Publish approved set' }));
    // Assert
    await screen.findByText('canonical-prior-record');
    expect(api.previewPackage).not.toHaveBeenCalled();
    expect(api.approvePackage).not.toHaveBeenCalled();
    expect(api.publishPackage).toHaveBeenCalledWith('package-1',
      { previewId: 'preview-1', previewHash: 'exact-hash', revision: 4 }, 'package-publication-preview-1');
  });

  it('restores an unapproved preview without silently approving it', async () => {
    // Arrange
    recovery.get.mockResolvedValue(snapshot({ preview: preview() }));
    mount();
    // Act
    await screen.findByRole('heading', { name: 'Approval preview' });
    // Assert
    expect(screen.getByRole('button', { name: 'Approve exact preview' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Publish approved set' })).toBeDisabled();
    expect(api.previewPackage).not.toHaveBeenCalled();
    expect(api.approvePackage).not.toHaveBeenCalled();
  });

  it('keeps prior canonical outcomes visible independently of a later approved preview', async () => {
    // Arrange
    recovery.get.mockResolvedValue(snapshot({ preview: preview({ state: 'Approved' }), publication: outcome }));
    mount();
    // Act
    await screen.findByText(/Release canonical-prior-release/);
    // Assert
    expect(screen.getByRole('heading', { name: 'Publication recorded: Partially Published' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Publish approved set' })).toBeEnabled();
    expect(screen.getByRole('list', { name: 'Exact preview selection' })).toHaveTextContent('candidate-1');
  });

  it('retains stale decision evidence but disables approval and publication', async () => {
    // Arrange
    recovery.get.mockResolvedValue(snapshot({ preview: preview({ revision: 3, state: 'Approved' }), previewIsStale: true }));
    mount();
    // Act
    await screen.findByText(/This saved preview is stale/);
    // Assert
    expect(screen.getByText(/Hash exact-hash/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Approve exact preview' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Publish approved set' })).toBeDisabled();
  });

  it('does not allow mutations before the decision lookup resolves', async () => {
    // Arrange
    let resolve!: (value: RecoveryFixture) => void;
    recovery.get.mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    mount();
    await screen.findByText('Synthetic source component');
    // Act
    const selection = screen.getByRole('checkbox', { name: /Select Synthetic source component/ });
    // Assert
    expect(selection).toBeDisabled();
    await act(async () => resolve(snapshot()));
    await waitFor(() => expect(selection).toBeEnabled());
  });

  it('shows denied recovery explicitly and retries without treating it as an empty decision', async () => {
    // Arrange
    recovery.get.mockRejectedValueOnce(new PackageImportError('Decision access denied.', 403)).mockResolvedValue(snapshot({ preview: preview() }));
    mount();
    // Act
    await screen.findByText('Decision access denied.');
    // Assert
    expect(await screen.findByRole('checkbox', { name: /Select Synthetic source component/ })).toBeDisabled();
    fireEvent.click(within(screen.getByRole('region', { name: 'Persisted review state' })).getByRole('button', { name: 'Retry' }));
    expect(await screen.findByRole('heading', { name: 'Approval preview' })).toBeInTheDocument();
  });

  it('recovers a committed publication after a lost response without replaying another set', async () => {
    // Arrange
    recovery.get.mockResolvedValueOnce(snapshot({ preview: preview({ state: 'Approved' }) }));
    vi.mocked(api.publishPackage).mockRejectedValueOnce(new Error('Publication response was lost.'));
    mount();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Publish approved set' })).toBeEnabled());
    recovery.get.mockResolvedValue(snapshot({ preview: preview({ state: 'Published' }), publication: outcome }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Publish approved set' }));
    await screen.findByText('Publication response was lost.');
    fireEvent.click(screen.getByRole('button', { name: 'Refresh package' }));
    // Assert
    expect(await screen.findByText(/Release canonical-prior-release/)).toBeInTheDocument();
    expect(screen.queryByText(/Publication outcome is unknown/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Publish approved set' })).toBeDisabled();
    expect(api.publishPackage).toHaveBeenCalledOnce();
    expect(api.approvePackage).not.toHaveBeenCalled();
  });

  it('preserves exact-key replay after a lost response and component remount', async () => {
    // Arrange
    recovery.get.mockResolvedValue(snapshot({ preview: preview({ state: 'Approved' }) }));
    vi.mocked(api.publishPackage).mockRejectedValueOnce(new Error('Unknown publication result.')).mockResolvedValue(outcome);
    const first = mount();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Publish approved set' })).toBeEnabled());
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Publish approved set' }));
    await screen.findByText('Unknown publication result.');
    first.unmount();
    mount();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Publish approved set' })).toBeEnabled());
    expect(screen.getByRole('button', { name: 'Preview selected revisions' })).toBeDisabled();
    expect(screen.getByRole('checkbox', { name: /Select Synthetic source component/ })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Publish approved set' }));
    // Assert
    await screen.findByText('canonical-prior-record');
    expect(vi.mocked(api.publishPackage).mock.calls[0]).toEqual(vi.mocked(api.publishPackage).mock.calls[1]);
    expect(api.previewPackage).not.toHaveBeenCalled();
  });

  it('opens a reviewed reference from its exact paged source-panel link without enabling publication', async () => {
    // Arrange
    vi.mocked(api.getPackageCandidates).mockResolvedValue(page([candidate({
      type: 'AuthorizationReference', name: 'Reviewed source reference', reviewState: 'Reviewed',
      authorizationReference: { reference: 'Stated source decision', issuer: null, issuedAt: null, expiresAt: null },
    })], 2, 30));
    render(<MemoryRouter initialEntries={['/workspaces/csp/security-capabilities/imports/package-1?type=AuthorizationReference&reviewState=Reviewed&page=2&candidate=candidate-1']}>
      <PackageImportsPage packageId="package-1" />
    </MemoryRouter>);
    // Act
    await screen.findByLabelText('Authorization reference');
    // Assert
    expect(api.getPackageCandidates).toHaveBeenCalledWith('package-1',
      { page: 2, pageSize: 25, type: 'AuthorizationReference', reviewState: 'Reviewed' }, expect.any(AbortSignal));
    expect(screen.getByLabelText('Authorization reference')).toHaveValue('Stated source decision');
    expect(screen.getByRole('checkbox', { name: /Select Reviewed source reference/ })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Publish approved set' })).toBeDisabled();
  });

  it('recovers a lost approval response without submitting approval again', async () => {
    // Arrange
    recovery.get.mockResolvedValueOnce(snapshot({ preview: preview() }));
    vi.mocked(api.approvePackage).mockRejectedValueOnce(new Error('Approval response was lost.'));
    mount();
    await screen.findByRole('heading', { name: 'Approval preview' });
    recovery.get.mockResolvedValue(snapshot({ preview: preview({ state: 'Approved' }) }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Approve exact preview' }));
    await screen.findByText('Approval response was lost.');
    fireEvent.click(screen.getByRole('button', { name: 'Refresh package' }));
    // Assert
    await waitFor(() => expect(screen.getByRole('button', { name: 'Publish approved set' })).toBeEnabled());
    expect(api.approvePackage).toHaveBeenCalledOnce();
    expect(api.previewPackage).not.toHaveBeenCalled();
  });

  it('does not erase an earlier publication when changing selection and generating another preview', async () => {
    // Arrange
    recovery.get.mockResolvedValue(snapshot({ publication: outcome }));
    mount();
    await screen.findByText('canonical-prior-record');
    // Act
    fireEvent.click(await screen.findByRole('checkbox', { name: /Select Synthetic source component/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Preview selected revisions' }));
    await screen.findByRole('heading', { name: 'Approval preview' });
    // Assert
    expect(screen.getByText('canonical-prior-record')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Approve exact preview' })).toBeEnabled();
  });
});
