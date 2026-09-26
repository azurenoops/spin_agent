import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PackageDetail } from '../../features/package-imports/PackageImportsPage';
import * as api from '../../features/package-imports/api';
import { candidate, page, packageStatus, reviewState } from './fixtures';
import { PackageImportError } from '../../features/package-imports/request';
import type { PackageCandidate } from '../../features/package-imports/types';
import './crypto';

vi.mock('../../components/AuthenticatedDownload', () => ({ default: () => <button>Download cited source</button> }));
vi.mock('../../features/package-imports/api', async original => ({
  ...await original<typeof api>(), getPackageStatus: vi.fn(), getPackageReviewState: vi.fn(),
  getPackageCandidates: vi.fn(), getPackageEntries: vi.fn(), reviewPackageClaim: vi.fn(),
  editPackageCandidate: vi.fn(), previewPackage: vi.fn(), publishPackage: vi.fn(),
}));
const finding = (): PackageCandidate => ({
  ...candidate({ type: 'AssessmentFinding', name: 'Source-stated finding', reviewState: 'NeedsReview' }),
  claim: {
    authorizationDecision: null, boundary: null, poamItem: null,
    assessmentFinding: { sourceFindingId: 'F-1', observation: 'Source observation', severityAsStated: 'High',
      statusAsStated: 'closed', assessor: null, assessmentDate: null, controlIds: ['AC-2'], evidenceReferences: [] },
    fieldSources: [{ field: 'assessmentFinding.observation', citationIndexes: [0] }],
    relationships: [], sourceAliases: ['F-1'], qualifications: ['A source closure assertion is not workflow closure.'],
  },
});
beforeEach(() => {
  vi.clearAllMocks(); sessionStorage.clear();
  vi.mocked(api.getPackageStatus).mockResolvedValue(packageStatus());
  vi.mocked(api.getPackageReviewState).mockResolvedValue(reviewState());
  vi.mocked(api.getPackageCandidates).mockResolvedValue(page([finding()]));
  vi.mocked(api.getPackageEntries).mockResolvedValue(page([]));
  vi.mocked(api.reviewPackageClaim).mockResolvedValue({
    reviewId: 'review-1', candidateId: 'candidate-1', candidateRevision: 2, reviewState: 'Reviewed',
    unresolvedRelationships: [], recordedObject: null,
  });
});
describe('profile-2 source claims remain distinct from published inventory', () => {
  it('renders source assertions and citations without treating claimed closure as workflow closure', async () => {
    // Arrange
    render(<MemoryRouter><PackageDetail packageId="package-1" /></MemoryRouter>);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Review Source-stated finding' }));
    // Assert
    expect(await screen.findByRole('heading', { name: 'Review source claim' })).toBeInTheDocument();
    expect(screen.getByText('closed')).toBeInTheDocument();
    expect(screen.getByText('A source closure assertion is not workflow closure.')).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: /Select Source-stated finding/ })).toBeDisabled();
    expect(screen.queryByLabelText('Component type')).not.toBeInTheDocument();
    expect(api.publishPackage).not.toHaveBeenCalled();
  });
  it('records explicit human claim review through its dedicated endpoint, not inventory edit or object creation', async () => {
    // Arrange
    render(<MemoryRouter><PackageDetail packageId="package-1" /></MemoryRouter>);
    fireEvent.click(await screen.findByRole('button', { name: 'Review Source-stated finding' }));
    // Act
    expect(await screen.findByLabelText('Claim review rationale')).toBeEnabled();
    fireEvent.change(await screen.findByLabelText('Claim review rationale'), { target: { value: 'Checked original cited source.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Record claim review' }));
    // Assert
    await waitFor(() => expect(api.reviewPackageClaim).toHaveBeenCalledWith('package-1', 'candidate-1', {
      expectedCandidateRevision: 1, action: 'Reviewed', rationale: 'Checked original cited source.', resolutions: [],
    }, expect.any(String)));
    expect(api.editPackageCandidate).not.toHaveBeenCalled();
    expect(api.previewPackage).not.toHaveBeenCalled();
    expect(api.publishPackage).not.toHaveBeenCalled();
  });
  it('retains review rationale and exact candidate revision after a stale conflict', async () => {
    // Arrange
    vi.mocked(api.reviewPackageClaim).mockRejectedValue(new PackageImportError('Candidate changed; reload exact sources.', 409));
    render(<MemoryRouter><PackageDetail packageId="package-1" /></MemoryRouter>);
    fireEvent.click(await screen.findByRole('button', { name: 'Review Source-stated finding' }));
    fireEvent.change(await screen.findByLabelText('Claim review rationale'), { target: { value: 'Retain my review.' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Record claim review' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Candidate changed');
    expect(screen.getByLabelText('Claim review rationale')).toHaveValue('Retain my review.');
    expect(api.editPackageCandidate).not.toHaveBeenCalled();
  });
});
