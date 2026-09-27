import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import EmassStatusPage from '../../pages/EmassStatus';
import * as emassApi from '../../api/emass-status';
import * as exchangeApi from '../../api/emass-exchanges';
import { MemoryRouter } from 'react-router-dom';

vi.mock('../../api/emass-status');
vi.mock('../../api/emass-exchanges');
vi.mock('../../components/layout/SystemLayout', () => ({
  useSystemContext: () => ({ detail: { systemId: 'system-1', name: 'Mission Analytics' } }),
}));

describe('EmassStatusPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(exchangeApi.getExchangeHistory).mockResolvedValue({ version: 0, canRecord: true, items: [] });
    vi.mocked(exchangeApi.getExchangeExports).mockResolvedValue([]);
    vi.mocked(emassApi.getEmassStatus).mockResolvedValue({
      systemId: 'system-1',
      overallStatus: 'HasConflicts',
      lastExportedAt: '2026-03-01T12:00:00Z',
      lastSyncedAt: '2026-03-02T12:00:00Z',
      unresolvedConflictCount: 1,
      exportSummary: [{ category: 'Controls', exportedCount: 100, pendingCount: 4, lastExportedAt: '2026-03-01T12:00:00Z' }],
      readinessStatus: { isReady: false, blockingGapCount: 1, advisoryGapCount: 0 },
    });
    vi.mocked(emassApi.getEmassReadiness).mockResolvedValue({
      systemId: 'system-1',
      isReady: false,
      checkedAt: '2026-03-03T12:00:00Z',
      gaps: [{ fieldName: 'EmassSystemId', description: 'Register the eMASS ID.', severity: 'Blocking', fixUrl: '/settings' }],
    });
    vi.mocked(emassApi.getEmassConflicts).mockResolvedValue({
      items: [{
        id: 'conflict-1',
        entityType: 'ControlImplementation',
        entityId: 'AC-2',
        fieldName: 'implementationStatus',
        spinValue: 'Implemented',
        emassValue: 'Partially Implemented',
        conflictStatus: 'Unresolved',
        detectedAt: '2026-03-02T12:00:00Z',
        resolvedAt: null,
        resolvedBy: null,
      }],
      total: 1,
      limit: 50,
      offset: 0,
    });
    vi.mocked(emassApi.uploadEmassSync).mockResolvedValue({
      batchId: 'batch-1', systemId: 'system-1', conflictsCreated: 1,
      identicalFields: 20, skippedUnresolved: 0, syncedAt: '2026-03-03T12:00:00Z',
    });
    vi.mocked(emassApi.resolveConflict).mockResolvedValue({
      id: 'conflict-1', entityType: 'ControlImplementation', entityId: 'AC-2',
      fieldName: 'implementationStatus', spinValue: 'Implemented', emassValue: 'Partially Implemented',
      conflictStatus: 'KeepSpin', detectedAt: '2026-03-02T12:00:00Z',
      resolvedAt: '2026-03-03T12:00:00Z', resolvedBy: 'isso-user',
    });
  });

  it('shows workflow state and completes upload and conflict resolution', async () => {
    // Arrange
    render(<MemoryRouter><EmassStatusPage /></MemoryRouter>);

    // Act
    await screen.findByText('eMASS round-trip review');

    // Assert
    expect(screen.getByText('Has conflicts')).toBeInTheDocument();
    expect(screen.getByText('Register the eMASS ID.')).toBeInTheDocument();
    expect(screen.getByText('Partially Implemented')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Current package validation' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'eMASS identifier and exchange advisories' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'SPIN exported value' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'Returned eMASS value' })).toBeVisible();
    expect(await screen.findByRole('heading', { name: 'Manual exchange history' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'Sync eMASS workbook' })
      .compareDocumentPosition(screen.getByRole('heading', { name: 'Manual exchange history' })))
      .toBe(Node.DOCUMENT_POSITION_FOLLOWING);

    // Act
    const file = new File(['workbook'], 'controls.xlsx', {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    });
    fireEvent.change(screen.getByLabelText('eMASS workbook'), { target: { files: [file] } });
    fireEvent.click(screen.getByRole('button', { name: 'Sync workbook' }));

    // Assert
    await waitFor(() => expect(emassApi.uploadEmassSync).toHaveBeenCalledWith('system-1', file, false));
    await waitFor(() => expect(emassApi.getEmassConflicts).toHaveBeenCalledTimes(2));
    await screen.findByRole('button', { name: 'Sync workbook' });

    // Act
    fireEvent.change(await screen.findByLabelText('Resolution'), { target: { value: 'KeepSpin' } });
    fireEvent.change(await screen.findByLabelText('Resolution rationale'), { target: { value: 'Local reviewed narrative remains authoritative.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Record resolution' }));

    // Assert
    await waitFor(() => expect(emassApi.resolveConflict).toHaveBeenCalledWith(
      'system-1', 'conflict-1', 'KeepSpin', 'Local reviewed narrative remains authoritative.',
    ));
  });
});