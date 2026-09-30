import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import RetainedPackageContext from '../../components/RetainedPackageContext';
import * as api from '../../api/package';

vi.mock('../../api/package', () => ({ getPackageContextOptions: vi.fn() }));

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getPackageContextOptions).mockResolvedValue({
    baselines: [{ id: 'package-a', purpose: 'InitialSubmission', generatedAt: '2026-09-01T00:00:00Z', contentHash: 'baseline-hash' }],
    decisions: [{ id: 'decision-a', decisionType: 'ATO', decisionDate: '2026-09-02T00:00:00Z', issuer: 'Recorded authority', snapshotHash: 'decision-hash' }],
    previews: [{ id: 'preview-a', generatedAt: '2026-09-03T00:00:00Z', contentHash: 'preview-hash' }],
  });
});

describe('retained package context selection', () => {
  it('requires explicit baseline and decision selections and passes exact hashes', async () => {
    // Arrange
    const change = vi.fn();
    render(<RetainedPackageContext systemId="system-a" purpose="AuthorizedBaselineArchive" onChange={change} />);
    await screen.findByRole('combobox', { name: 'Retained baseline package' });
    expect(change).not.toHaveBeenCalled();
    // Act
    fireEvent.change(screen.getByRole('combobox', { name: 'Retained baseline package' }), { target: { value: 'package-a' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Recorded authorization decision' }), { target: { value: 'decision-a' } });
    // Assert
    expect(change).toHaveBeenLastCalledWith({
      baselinePackageId: 'package-a', baselineContentHash: 'baseline-hash',
      authorizationDecisionId: 'decision-a', expectedDecisionSnapshotHash: 'decision-hash',
    });
  });

  it('pins the explicitly selected retained change preview rather than current working data', async () => {
    // Arrange
    const change = vi.fn();
    render(<RetainedPackageContext systemId="system-a" purpose="ChangeSubmission" onChange={change} />);
    await screen.findByRole('combobox', { name: 'Retained baseline package' });
    // Act
    fireEvent.change(screen.getByRole('combobox', { name: 'Retained baseline package' }), { target: { value: 'package-a' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Recorded authorization decision' }), { target: { value: 'decision-a' } });
    expect(change).toHaveBeenLastCalledWith(null);
    fireEvent.change(screen.getByRole('combobox', { name: 'Retained SSP change preview' }), { target: { value: 'preview-a' } });
    // Assert
    expect(change).toHaveBeenLastCalledWith(expect.objectContaining({ changePreviewId: 'preview-a', changeContentHash: 'preview-hash' }));
  });

  it('surfaces unavailable source options without manufacturing choices', async () => {
    // Arrange
    vi.mocked(api.getPackageContextOptions).mockRejectedValue(new Error('Retained sources unavailable'));
    // Act
    render(<RetainedPackageContext systemId="system-a" purpose="AuthorizedBaselineArchive" onChange={vi.fn()} />);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Retained sources unavailable');
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
  });
});
