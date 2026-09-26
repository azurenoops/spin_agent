import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CandidateReview } from '../../features/package-imports/CandidateReview';
import { PackageSourcesPanel } from '../../features/package-imports/PackageSourcesPanel';
import * as api from '../../features/package-imports/api';
import { PackageImportError } from '../../features/package-imports/request';
import { candidate, packageStatus, page } from './fixtures';

vi.mock('../../features/package-imports/api', async importOriginal => ({
  ...await importOriginal<typeof import('../../features/package-imports/api')>(),
  listPackages: vi.fn(), getPackageCandidates: vi.fn(), editPackageCandidate: vi.fn(),
}));
vi.mock('../../components/AuthenticatedDownload', () => ({
  default: ({ url, children }: { url: string; children: React.ReactNode }) => <button data-url={url}>{children}</button>,
}));

const referenceCandidate = () => ({
  ...candidate({ type: 'AuthorizationReference', name: 'Source authorization reference', controlDuties: {} }),
  authorizationReference: {
    reference: 'Synthetic source reference', issuer: 'Stated source issuer',
    issuedAt: '2026-01-01T00:00:00Z', expiresAt: '2027-01-01T00:00:00Z',
  },
});
const reviewProps = () => ({
  packageId: 'package-1', candidate: referenceCandidate(), onSaved: vi.fn(), onCancel: vi.fn(), onReload: vi.fn(),
});
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.listPackages).mockResolvedValue(page([packageStatus()]));
  vi.mocked(api.getPackageCandidates).mockResolvedValue(page([{ ...referenceCandidate(), reviewState: 'Reviewed' }]));
  vi.mocked(api.editPackageCandidate).mockResolvedValue(referenceCandidate());
});

describe('private source-backed authorization references', () => {
  it('reviews a complete 2000-character reference name without inventory classification or truncation', async () => {
    // Arrange
    const name = 'R'.repeat(2000);
    const props = reviewProps();
    const source = {
      ...props.candidate, name, componentType: '', classification: '', serviceCategory: '',
      authorizationReference: { ...props.candidate.authorizationReference, reference: name },
    };
    render(<CandidateReview {...props} candidate={source} />);
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: /reviewed the sources/i }));
    fireEvent.click(screen.getByRole('button', { name: 'Mark reviewed' }));
    // Assert
    expect(screen.getByLabelText('Name')).toHaveAttribute('maxlength', '2000');
    expect(screen.getByLabelText('Name')).toHaveValue(name);
    await waitFor(() => expect(api.editPackageCandidate).toHaveBeenCalledWith('package-1', 'candidate-1', expect.objectContaining({
      name, componentType: null, classification: '', serviceCategory: '', reviewAction: 'Reviewed',
      authorizationReference: { ...source.authorizationReference, reference: name },
    })));
    expect(screen.queryByLabelText('Component type')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Classification')).not.toBeInTheDocument();
  });

  it('rejects a 2001-character reference name without truncating or sending it', async () => {
    // Arrange
    const name = 'R'.repeat(2001);
    render(<CandidateReview {...reviewProps()} />);
    // Act
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: name } });
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Candidate name must be at most 2000 characters');
    expect(screen.getByLabelText('Name')).toHaveValue(name);
    expect(api.editPackageCandidate).not.toHaveBeenCalled();
  });

  it('edits reference metadata with evidence acknowledgement, without component authoring fields', async () => {
    // Arrange
    render(<CandidateReview {...reviewProps()} />);
    // Act
    fireEvent.change(screen.getByLabelText('Authorization reference'), { target: { value: ' Revised source reference ' } });
    fireEvent.change(screen.getByLabelText('Issuing authority'), { target: { value: ' Revised stated issuer ' } });
    fireEvent.click(screen.getByRole('checkbox', { name: /reviewed the sources/i }));
    fireEvent.click(screen.getByRole('button', { name: 'Mark reviewed' }));
    // Assert
    await waitFor(() => expect(api.editPackageCandidate).toHaveBeenCalledWith('package-1', 'candidate-1', expect.objectContaining({
      reviewAction: 'Reviewed', componentType: null, authorizationReference: expect.objectContaining({
        reference: 'Revised source reference', issuer: 'Revised stated issuer',
        issuedAt: '2026-01-01T00:00:00Z', expiresAt: '2027-01-01T00:00:00Z',
      }),
    })));
    expect(screen.queryByLabelText('Component type')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Contributor IDs (one per line)')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Download cited source' })).toBeEnabled();
    expect(screen.getByText(/not verified authorization decisions/)).toBeInTheDocument();
  });

  it('rejects a blank reference and dates in reverse order without sending a mutation', async () => {
    // Arrange
    render(<CandidateReview {...reviewProps()} />);
    fireEvent.change(screen.getByLabelText('Authorization reference'), { target: { value: ' ' } });
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: /reviewed the sources/i }));
    fireEvent.click(screen.getByRole('button', { name: 'Mark reviewed' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Authorization reference is required');
    fireEvent.change(screen.getByLabelText('Authorization reference'), { target: { value: 'Source reference' } });
    fireEvent.change(screen.getByLabelText('Expires at (ISO-8601)'), { target: { value: '2025-01-01T00:00:00Z' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Expiration cannot be earlier than issuance');
    expect(api.editPackageCandidate).not.toHaveBeenCalled();
  });

  it('retains reference edits after revision conflict and requires explicit reload', async () => {
    // Arrange
    const props = reviewProps();
    vi.mocked(api.editPackageCandidate).mockRejectedValue(new PackageImportError('Reference revision changed.', 409));
    render(<CandidateReview {...props} />);
    fireEvent.change(screen.getByLabelText('Authorization reference'), { target: { value: 'Keep local reference edit' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    // Assert
    expect(await screen.findByText('Reference revision changed.')).toBeInTheDocument();
    expect(screen.getByLabelText('Authorization reference')).toHaveValue('Keep local reference edit');
    expect(props.onSaved).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Reload current revision' }));
    expect(props.onReload).toHaveBeenCalledOnce();
  });

  it('preserves nullable source metadata and rejects a reference only with explicit rationale', async () => {
    // Arrange
    render(<CandidateReview {...reviewProps()} />);
    fireEvent.change(screen.getByLabelText('Issuing authority'), { target: { value: '' } });
    fireEvent.change(screen.getByLabelText('Issued at (ISO-8601)'), { target: { value: '' } });
    fireEvent.change(screen.getByLabelText('Expires at (ISO-8601)'), { target: { value: '' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Reject candidate' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('rationale');
    fireEvent.change(screen.getByLabelText('Review rationale'), { target: { value: 'Source does not support the proposed scope.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Reject candidate' }));
    // Assert
    await waitFor(() => expect(api.editPackageCandidate).toHaveBeenCalledOnce());
    expect(api.editPackageCandidate).toHaveBeenCalledWith('package-1', 'candidate-1', expect.objectContaining({
      reviewAction: 'Rejected', componentType: null, rationale: 'Source does not support the proposed scope.',
      authorizationReference: { reference: 'Synthetic source reference', issuer: null, issuedAt: null, expiresAt: null },
    }));
  });

  it.each([
    ['Authorization reference', 'r'.repeat(2001), 'at most 2,000'],
    ['Issuing authority', 'i'.repeat(501), 'at most 500'],
    ['Issued at (ISO-8601)', 'not-a-source-date', 'ISO-8601'],
  ])('validates the %s boundary instead of dispatching invalid metadata', async (field, value, error) => {
    // Arrange
    render(<CandidateReview {...reviewProps()} />);
    // Act
    fireEvent.change(screen.getByLabelText(field), { target: { value } });
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(error);
    expect(api.editPackageCandidate).not.toHaveBeenCalled();
  });

  it('pages reviewed references separately and links to their package review context', async () => {
    // Arrange
    vi.mocked(api.getPackageCandidates).mockResolvedValue(page([{ ...referenceCandidate(), reviewState: 'Reviewed' }], 1, 30));
    render(<MemoryRouter><PackageSourcesPanel /></MemoryRouter>);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Show reviewed references for Synthetic package' }));
    // Assert
    expect(await screen.findByText('Synthetic source reference')).toBeInTheDocument();
    expect(api.getPackageCandidates).toHaveBeenCalledWith('package-1',
      { page: 1, pageSize: 25, type: 'AuthorizationReference', reviewState: 'Reviewed' }, expect.any(AbortSignal));
    expect(screen.getByRole('link', { name: 'Review source authorization reference' })).toHaveAttribute('href',
      '/workspaces/csp/security-capabilities/imports/package-1?type=AuthorizationReference&reviewState=Reviewed&page=1&candidate=candidate-1');
    expect(screen.getByRole('button', { name: 'Download cited source' })).toHaveAttribute('data-url',
      '/api/csp/package-imports/package-1/artifacts/artifact-1/content');
    fireEvent.click(within(screen.getByRole('region', { name: 'Reviewed authorization references' })).getByRole('button', { name: 'Next' }));
    await waitFor(() => expect(api.getPackageCandidates).toHaveBeenLastCalledWith('package-1',
      { page: 2, pageSize: 25, type: 'AuthorizationReference', reviewState: 'Reviewed' }, expect.any(AbortSignal)));
  });

  it('distinguishes a reference lookup failure from no reviewed references and retries it', async () => {
    // Arrange
    vi.mocked(api.getPackageCandidates).mockRejectedValueOnce(new PackageImportError('References unavailable.', 403))
      .mockResolvedValue(page([]));
    render(<MemoryRouter><PackageSourcesPanel /></MemoryRouter>);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Show reviewed references for Synthetic package' }));
    await screen.findByText('References unavailable.');
    // Assert
    expect(screen.queryByText(/No reviewed authorization references/)).not.toBeInTheDocument();
    fireEvent.click(within(screen.getByRole('region', { name: 'Reviewed authorization references' })).getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText(/No reviewed authorization references/)).toBeInTheDocument();
  });

  it('shows unstated fields and missing reviewed metadata explicitly without inventing reference values', async () => {
    // Arrange
    vi.mocked(api.getPackageCandidates).mockResolvedValue(page([
      { ...referenceCandidate(), reviewState: 'Reviewed', authorizationReference: { reference: 'Reference without dates', issuer: null, issuedAt: null, expiresAt: null } },
      candidate({ candidateId: 'reference-missing', type: 'AuthorizationReference', reviewState: 'Reviewed', authorizationReference: null }),
    ]));
    render(<MemoryRouter><PackageSourcesPanel /></MemoryRouter>);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Show reviewed references for Synthetic package' }));
    // Assert
    expect(await screen.findByText('Reference without dates')).toBeInTheDocument();
    expect(screen.getByText('Issuing authority: Not stated in the reference')).toBeInTheDocument();
    expect(screen.getByText('Issued: Not stated · Expires: Not stated')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('Reviewed reference metadata is unavailable');
    fireEvent.click(screen.getByRole('button', { name: 'Hide reviewed references for Synthetic package' }));
    expect(screen.queryByRole('region', { name: 'Reviewed authorization references' })).not.toBeInTheDocument();
  });
});
