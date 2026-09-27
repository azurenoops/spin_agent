import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import '../helpers/dialog';
import ExternalDecisionRecords from '../../features/systems/ExternalDecisionRecords';
import SapDraftEditor from '../../features/systems/SapDraftEditor';
import * as api from '../../features/systems/systemDecisionDraftApi';
vi.mock('../../features/systems/systemDecisionDraftApi', async importOriginal => ({
  ...await importOriginal<typeof import('../../features/systems/systemDecisionDraftApi')>(),
  getExternalDecisionContext: vi.fn(), recordExternalDecision: vi.fn(), getSapDraft: vi.fn(), updateSapDraft: vi.fn(),
}));
vi.mock('../../api/sap', () => ({ generateSap: vi.fn() }));
const hash = 'a'.repeat(64);
const context = { systemId: 'a', canRecord: true, activeDecisionId: null, page: 1, pageSize: 50, sourceTotal: 1, packageTotal: 1, recordTotal: 0,
  sourceEvidence: [{ id: 'source-a', fileName: 'Recorded AO decision.pdf', contentHash: hash }],
  completedPackages: [{ id: 'package-a', contentHash: hash, generatedAt: '2026-09-01', purpose: 1 }], records: [] };
const draft = { sapId: 'sap-a', systemId: 'a', title: 'Initial assessment', assessmentLead: 'Lead A',
  scopeNotes: 'Current scope', assessmentApproach: 'Examine', status: 'Draft', draftHash: hash, canEdit: true, canCreate: true };

describe('Remaining Systems document fields', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.getExternalDecisionContext).mockResolvedValue(context);
    vi.mocked(api.recordExternalDecision).mockResolvedValue({ id: 'record-a' });
    vi.mocked(api.getSapDraft).mockResolvedValue(draft);
    vi.mocked(api.updateSapDraft).mockResolvedValue({ ...draft, title: 'Manual assessment', draftHash: 'b'.repeat(64) });
  });
  it('records exact external authority/date/source/baseline through a distinct AO action', async () => {
    // Arrange
    const recorded = vi.fn();
    render(<MemoryRouter><ExternalDecisionRecords systemId="a" onRecorded={recorded} /></MemoryRouter>);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Record external decision' }));
    fireEvent.change(screen.getByLabelText('Decision source'), { target: { value: 'source-a' } });
    fireEvent.change(screen.getByLabelText('Issuing authority as recorded'), { target: { value: 'External AO Office' } });
    fireEvent.change(screen.getByLabelText('Decision date'), { target: { value: '2026-01-01' } });
    fireEvent.change(screen.getByLabelText('Expiration date'), { target: { value: '2027-01-01' } });
    fireEvent.change(screen.getByLabelText('Applicable retained package'), { target: { value: 'package-a' } });
    fireEvent.click(screen.getByLabelText('I reviewed the source and exact retained package.'));
    fireEvent.click(screen.getByRole('button', { name: 'Save recorded decision' }));
    // Assert
    await waitFor(() => expect(api.recordExternalDecision).toHaveBeenCalledWith('a', expect.objectContaining({
      issuingAuthority: 'External AO Office', decisionDate: '2026-01-01', sourceEvidenceId: 'source-a',
      expectedSourceHash: hash, baselinePackageId: 'package-a', expectedPackageHash: hash, makeCurrent: false,
    })));
    expect(recorded).toHaveBeenCalled();
  });
  it('hides record authority when the server grants read only', async () => {
    // Arrange
    vi.mocked(api.getExternalDecisionContext).mockResolvedValue({ ...context, canRecord: false });
    // Act
    render(<MemoryRouter><ExternalDecisionRecords systemId="a" onRecorded={vi.fn()} /></MemoryRouter>);
    // Assert
    expect(await screen.findByText(/AO permission is required/)).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Record external decision' })).not.toBeInTheDocument();
  });
  it('saves all manual assessment fields with the exact draft content fence', async () => {
    // Arrange
    render(<SapDraftEditor systemId="a" onSaved={vi.fn()} />);
    // Act
    fireEvent.change(await screen.findByLabelText('Assessment title'), { target: { value: 'Manual assessment' } });
    fireEvent.change(screen.getByLabelText('Assessment lead'), { target: { value: 'Assigned SCA' } });
    fireEvent.change(screen.getByLabelText('Assessment scope'), { target: { value: 'Reviewed boundary and baseline' } });
    fireEvent.change(screen.getByLabelText('Assessment approach'), { target: { value: 'Examine, interview and test' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save assessment draft' }));
    // Assert
    await waitFor(() => expect(api.updateSapDraft).toHaveBeenCalledWith('a', 'sap-a', {
      title: 'Manual assessment', assessmentLead: 'Assigned SCA', scopeNotes: 'Reviewed boundary and baseline',
      assessmentApproach: 'Examine, interview and test', expectedContentHash: hash,
    }));
  });
  it('keeps finalized assessment fields immutable', async () => {
    // Arrange
    vi.mocked(api.getSapDraft).mockResolvedValue({ ...draft, status: 'Finalized', canEdit: false });
    // Act
    render(<SapDraftEditor systemId="a" onSaved={vi.fn()} />);
    // Assert
    expect(await screen.findByLabelText('Assessment title')).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Save assessment draft' })).not.toBeInTheDocument();
  });
  it('reloads the current SAP after a conflict and never claims stale edits were saved', async () => {
    // Arrange
    vi.mocked(api.updateSapDraft).mockRejectedValue(new Error('The assessment draft changed.'));
    render(<SapDraftEditor systemId="a" onSaved={vi.fn()} />);
    // Act
    fireEvent.change(await screen.findByLabelText('Assessment title'), { target: { value: 'Stale title' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save assessment draft' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('draft changed');
    await waitFor(() => expect(screen.getByLabelText('Assessment title')).toHaveValue('Initial assessment'));
    expect(screen.queryByText('Assessment draft saved. Review and finalization remain separate.')).not.toBeInTheDocument();
  });
});
