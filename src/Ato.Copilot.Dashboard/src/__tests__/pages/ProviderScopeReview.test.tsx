import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import ProviderScopeReview from '../../features/provider-relationships/ProviderScopeReview';
import * as api from '../../features/provider-relationships/api';
import { allocationResponse } from '../provider-relationships/fixtures';

vi.mock('../../features/provider-relationships/api', () => ({
  listAllProviderRelationships: vi.fn(), previewProviderRelationship: vi.fn(), reviewProviderRelationship: vi.fn(),
}));
const row = { ...allocationResponse, relationshipId: 'relationship-a', revision: 7, canReviewRelationship: true };
const reviewed = { ...row, state: 'SeparateBoundaryConsumer' as const, revision: 9, reviewRequired: false,
  reviewedBy: 'reviewer-a', reviewedAt: '2026-09-28T13:00:00Z' };
const preview = { previewId: 'preview-a', previewHash: 'hash-a', revision: 8, contextSnapshotHash: 'context-a', blockers: [], canReview: true };
const recorded = vi.fn(), busy = vi.fn();
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.listAllProviderRelationships).mockResolvedValue([row]);
  vi.mocked(api.previewProviderRelationship).mockResolvedValue(preview);
  vi.mocked(api.reviewProviderRelationship).mockResolvedValue(reviewed);
});
function mount() {
  render(<ProviderScopeReview systemId="system-a" item={row} onRecorded={recorded} onBusyChange={busy} onCancel={vi.fn()} />);
}
describe('persisted scope relationship review', () => {
  it('previews the explicit determination then records the exact revision without authorizing the mission', async () => {
    // Arrange
    mount();
    // Act
    fireEvent.change(screen.getByLabelText('Relationship determination'), { target: { value: 'SeparateBoundaryConsumer' } });
    fireEvent.change(screen.getByLabelText('Review rationale'), { target: { value: 'The mission has its own authorization boundary.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Prepare review' }));
    // Assert
    await waitFor(() => expect(api.previewProviderRelationship).toHaveBeenCalledWith('system-a', 'relationship-a', {
      expectedRevision: 7, expectedAssignmentRevision: row.assignmentRevision, relationshipState: 'SeparateBoundaryConsumer',
      evidence: [], rationale: 'The mission has its own authorization boundary.',
    }));
    expect(api.reviewProviderRelationship).not.toHaveBeenCalled();
    // Act
    fireEvent.click(await screen.findByRole('checkbox'));
    fireEvent.click(screen.getByRole('button', { name: 'Record relationship review' }));
    // Assert
    await waitFor(() => expect(api.reviewProviderRelationship).toHaveBeenCalledWith('system-a', 'relationship-a', {
      expectedRevision: 8, previewId: 'preview-a', previewHash: 'hash-a', rationale: 'The mission has its own authorization boundary.',
    }));
    expect(recorded).toHaveBeenCalledWith(reviewed);
  });
  it('keeps a failed review unrecorded and requires a fresh preview', async () => {
    // Arrange
    vi.mocked(api.reviewProviderRelationship).mockRejectedValue(new Error('The relationship context changed.'));
    mount();
    fireEvent.change(screen.getByLabelText('Relationship determination'), { target: { value: 'SeparateBoundaryConsumer' } });
    fireEvent.change(screen.getByLabelText('Review rationale'), { target: { value: 'Recorded rationale.' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Prepare review' }));
    fireEvent.click(await screen.findByRole('checkbox'));
    fireEvent.click(screen.getByRole('button', { name: 'Record relationship review' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('relationship context changed');
    expect(screen.getByLabelText('Review rationale')).toHaveValue('Recorded rationale.');
    expect(screen.queryByRole('button', { name: 'Record relationship review' })).not.toBeInTheDocument();
    expect(recorded).not.toHaveBeenCalled();
  });
  it('fails closed when current authority is withdrawn', async () => {
    // Arrange
    vi.mocked(api.listAllProviderRelationships).mockResolvedValue([{ ...row, canReviewRelationship: false }]);
    mount();
    fireEvent.change(screen.getByLabelText('Relationship determination'), { target: { value: 'SeparateBoundaryConsumer' } });
    fireEvent.change(screen.getByLabelText('Review rationale'), { target: { value: 'Recorded rationale.' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Prepare review' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('permission');
    expect(api.previewProviderRelationship).not.toHaveBeenCalled();
  });
  it('keeps an unresolved determination review-required rather than showing it as approved', async () => {
    // Arrange
    vi.mocked(api.reviewProviderRelationship).mockResolvedValue({ ...reviewed, state: 'Undetermined', reviewRequired: true });
    mount();
    fireEvent.change(screen.getByLabelText('Relationship determination'), { target: { value: 'Undetermined' } });
    fireEvent.change(screen.getByLabelText('Review rationale'), { target: { value: 'Scope evidence still needed.' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Prepare review' }));
    fireEvent.click(await screen.findByRole('checkbox'));
    fireEvent.click(screen.getByRole('button', { name: 'Record relationship review' }));
    // Assert
    await waitFor(() => expect(recorded).toHaveBeenCalledWith(expect.objectContaining({ state: 'Undetermined', reviewRequired: true })));
  });
  it('prevents confirmation when the server reports blockers', async () => {
    // Arrange
    vi.mocked(api.previewProviderRelationship).mockResolvedValue({ ...preview, canReview: false,
      blockers: [{ code: 'STALE', message: 'Current scope required' }] });
    mount();
    fireEvent.change(screen.getByLabelText('Relationship determination'), { target: { value: 'SeparateBoundaryConsumer' } });
    fireEvent.change(screen.getByLabelText('Review rationale'), { target: { value: 'Recorded rationale.' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Prepare review' }));
    // Assert
    expect(await screen.findByRole('checkbox')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Record relationship review' })).toBeDisabled();
    expect(api.reviewProviderRelationship).not.toHaveBeenCalled();
  });
});
