import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import EmassExchangeHistory from '../../features/systems/EmassExchangeHistory';
import * as api from '../../api/emass-exchanges';
import '../helpers/dialog';

vi.mock('../../api/emass-exchanges');
const exported = { packageId: 'package-1', packageHash: 'abc123', exportGeneratedAt: '2026-09-20T12:00:00Z', purpose: 'InitialSubmission' };
const receipt = { ...exported, id: 'receipt-1', version: 1, outcome: 'ReceiptRecorded' as const,
  receivingWorkflow: 'Receiving workflow', externalReference: 'REF-123', occurredAt: '2026-09-21T12:00:00Z',
  recordedAt: '2026-09-22T12:00:00Z', recordedBy: 'authenticated-person', notes: 'Checked receipt', supersedesId: null };

describe('manual eMASS exchange history', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(api.getExchangeHistory).mockResolvedValue({ version: 0, canRecord: true, items: [] });
    vi.mocked(api.getExchangeExports).mockResolvedValue([exported]);
    vi.mocked(api.recordExchange).mockResolvedValue(receipt);
  });

  it('never infers receipt from a completed export and submits exact selected identity', async () => {
    // Arrange
    render(<EmassExchangeHistory systemId="system-1" />);
    // Act
    await screen.findByText('No manual exchange observations recorded.');
    expect(screen.queryByLabelText('Retained export')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Record external observation' }));
    expect(screen.getByRole('dialog', { name: 'Record external observation' })).toBeVisible();
    fireEvent.change(screen.getByLabelText('Retained export'), { target: { value: 'package-1' } });
    fireEvent.change(screen.getByLabelText('Receiving workflow'), { target: { value: 'Receiving workflow' } });
    fireEvent.change(screen.getByLabelText('External reference'), { target: { value: 'REF-123' } });
    fireEvent.change(screen.getByLabelText('Event time'), { target: { value: '2026-09-21T12:00' } });
    fireEvent.click(screen.getByRole('button', { name: 'Record observation' }));
    // Assert
    await waitFor(() => expect(api.recordExchange).toHaveBeenCalledWith('system-1', expect.objectContaining({
      packageId: 'package-1', packageHash: 'abc123', exportGeneratedAt: exported.exportGeneratedAt,
      outcome: 'ReceiptRecorded', receivingWorkflow: 'Receiving workflow', externalReference: 'REF-123',
      expectedVersion: 0, idempotencyKey: expect.any(String),
    })));
    expect(screen.getByText(/does not connect to eMASS/)).toBeVisible();
  });

  it('shows append-only corrections and does not offer writes to a reader', async () => {
    // Arrange
    vi.mocked(api.getExchangeHistory).mockResolvedValue({ version: 2, canRecord: false, items: [
      { ...receipt, id: 'correction-1', version: 2, outcome: 'ImportRejected', supersedesId: receipt.id, notes: 'Corrected returned result' },
      receipt,
    ] });
    render(<EmassExchangeHistory systemId="system-1" />);
    // Act
    await screen.findByText('Import rejected');
    // Assert
    expect(screen.getByText('Receipt recorded', { selector: 'strong' })).toBeVisible();
    expect(screen.getByText(/Corrects receipt-1/)).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Record observation' })).not.toBeInTheDocument();
    expect(screen.getAllByText('authenticated-person')).toHaveLength(2);
  });

  it('retains observation after conflict and provides reload without automatic retry', async () => {
    // Arrange
    vi.mocked(api.recordExchange).mockRejectedValue({ errors: [{ message: 'Exchange history changed. Reload and review.' }] });
    render(<EmassExchangeHistory systemId="system-1" />);
    await screen.findByText('No manual exchange observations recorded.');
    fireEvent.click(screen.getByRole('button', { name: 'Record external observation' }));
    // Act
    fireEvent.change(screen.getByLabelText('Retained export'), { target: { value: 'package-1' } });
    fireEvent.change(screen.getByLabelText('Receiving workflow'), { target: { value: 'Workflow' } });
    fireEvent.change(screen.getByLabelText('External reference'), { target: { value: 'receipt-ref' } });
    fireEvent.change(screen.getByLabelText('Event time'), { target: { value: '2026-09-21T12:00' } });
    fireEvent.click(screen.getByRole('button', { name: 'Record observation' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Exchange history changed.');
    expect(screen.getByLabelText('External reference')).toHaveValue('receipt-ref');
    expect(api.recordExchange).toHaveBeenCalledTimes(1);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Reload exchange history' }));
    // Assert
    await waitFor(() => expect(api.getExchangeHistory).toHaveBeenCalledTimes(2));
    expect(api.recordExchange).toHaveBeenCalledTimes(1);
  });

  it('appends a correction and preserves its idempotency key after an interrupted response', async () => {
    // Arrange
    vi.mocked(api.getExchangeHistory).mockResolvedValue({ version: 1, canRecord: true, items: [receipt] });
    vi.mocked(api.recordExchange).mockRejectedValueOnce(new Error('Response interrupted'));
    render(<EmassExchangeHistory systemId="system-1" />);
    await screen.findByText('REF-123');
    fireEvent.click(screen.getByRole('button', { name: 'Record external observation' }));
    // Act
    fireEvent.change(screen.getByLabelText('Retained export'), { target: { value: 'package-1' } });
    fireEvent.change(screen.getByLabelText('Observed outcome'), { target: { value: 'ImportRejected' } });
    fireEvent.change(screen.getByLabelText('Correction of'), { target: { value: 'receipt-1' } });
    fireEvent.change(screen.getByLabelText('Receiving workflow'), { target: { value: 'Workflow' } });
    fireEvent.change(screen.getByLabelText('External reference'), { target: { value: 'rejection-ref' } });
    fireEvent.change(screen.getByLabelText('Event time'), { target: { value: '2026-09-21T12:00' } });
    fireEvent.change(screen.getByLabelText('Notes / correction reason'), { target: { value: 'Receipt was a rejection notice.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Record observation' }));
    await screen.findByText('Response interrupted');
    fireEvent.click(screen.getByRole('button', { name: 'Record observation' }));
    // Assert
    await waitFor(() => expect(api.recordExchange).toHaveBeenCalledTimes(2));
    const [firstCall, retriedCall] = vi.mocked(api.recordExchange).mock.calls;
    if (!firstCall || !retriedCall) throw new Error('Expected both initial and retried exchange requests.');
    const first = firstCall[1];
    const retried = retriedCall[1];
    expect(retried).toEqual(first);
    expect(retried).toMatchObject({ supersedesId: 'receipt-1', expectedVersion: 1, outcome: 'ImportRejected',
      notes: 'Receipt was a rejection notice.' });
    expect(screen.getByText('Receipt recorded', { selector: 'strong' })).toBeVisible();
  });
  it('cancels the dialog without recording an observation', async () => {
    // Arrange
    render(<EmassExchangeHistory systemId="system-1" />);
    const open = await screen.findByRole('button', { name: 'Record external observation' });
    // Act
    open.focus();
    fireEvent.click(open);
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(open).toHaveFocus();
    expect(api.recordExchange).not.toHaveBeenCalled();
  });
  it('blocks dismissal and duplicate observation writes while recording', async () => {
    // Arrange
    let finish!: (value: typeof receipt) => void;
    vi.mocked(api.recordExchange).mockReturnValue(new Promise(resolve => { finish = resolve; }));
    render(<EmassExchangeHistory systemId="system-1" />);
    fireEvent.click(await screen.findByRole('button', { name: 'Record external observation' }));
    fireEvent.change(screen.getByLabelText('Retained export'), { target: { value: 'package-1' } });
    fireEvent.change(screen.getByLabelText('Receiving workflow'), { target: { value: 'Workflow' } });
    fireEvent.change(screen.getByLabelText('External reference'), { target: { value: 'REF-123' } });
    fireEvent.change(screen.getByLabelText('Event time'), { target: { value: '2026-09-21T12:00' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Record observation' }));
    fireEvent(screen.getByRole('dialog'), new Event('cancel', { cancelable: true }));
    // Assert
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Close dialog' })).toBeDisabled();
    expect(screen.getByRole('dialog')).toBeVisible();
    expect(api.recordExchange).toHaveBeenCalledTimes(1);
    await act(async () => finish(receipt));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
