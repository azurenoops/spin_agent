import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import * as api from '../../api/capabilityResponsibilities';
import CapabilityResponsibilityReview from '../../pages/CapabilityResponsibilityReview';
import { responsibilityItem, responsibilitySnapshotJson } from '../helpers/capabilityResponsibilityFixture';
import '../helpers/dialog';

vi.mock('../../api/capabilityResponsibilities', async importOriginal => ({
  ...await importOriginal<typeof import('../../api/capabilityResponsibilities')>(),
  getCapabilityResponsibilities: vi.fn(), confirmCapabilityResponsibilities: vi.fn(),
  reconcileCapabilityResponsibilities: vi.fn(), dispatchCapabilityResponsibilityImpacts: vi.fn(),
}));
const item = () => ({
  ...responsibilityItem('MissingAllocation', 'AU-11'),
  sourceSnapshotJson: responsibilitySnapshotJson({ Controls: ['AU-11'] }),
});
const preview = (): api.CapabilityResponsibilityResponse => ({
  systemId: 'system-a', baselineId: 'baseline-a', baselineName: 'Moderate',
  canConfirm: true, items: [item()], pendingImpacts: [],
});
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(preview());
  vi.mocked(api.confirmCapabilityResponsibilities).mockImplementation(async (_system, _capability, body) => ({
    ...preview(), items: [{ ...item(), allocation: body.allocations[0]!, reviewNotes: body.reviewNotes,
      providerCoverageVerified: true, customerDutiesReviewed: true, state: 'Applied' }],
  }));
});
async function open() {
  render(<MemoryRouter initialEntries={['/systems/system-a/inheritance/subscriptions']}>
    <Routes><Route path="/systems/:id/inheritance/subscriptions" element={<CapabilityResponsibilityReview />} /></Routes>
  </MemoryRouter>);
  fireEvent.click(await screen.findByRole('button', { name: 'Open AU-11 responsibility' }));
  return screen.findByRole('dialog', { name: 'Review responsibility AU-11' });
}
function fill(drawer: HTMLElement, name: string, value: string) {
  fireEvent.change(within(drawer).getByRole('textbox', { name }), { target: { value } });
}
function shared(drawer: HTMLElement) {
  fireEvent.click(within(drawer).getByRole('radio', { name: /Provider and my team/ }));
  fill(drawer, 'Provider duties', 'Operate archive.');
  fill(drawer, 'Customer duties', 'Review local retention.');
  fill(drawer, 'Basis for this allocation', 'Reviewed original evidence for this system.');
}
function review(drawer: HTMLElement) {
  fireEvent.click(within(drawer).getByRole('button', { name: 'Review allocation' }));
  fireEvent.click(within(drawer).getByRole('checkbox'));
}

it('starts unconfirmed with a concise provider summary and separate saved state', async () => {
  // Arrange / Act
  const drawer = await open();
  // Assert
  expect(within(drawer).getByText('Decide what the provider covers and what your team must do.')).toBeVisible();
  expect(within(drawer).getByRole('radio', { name: /I need more information/ })).toBeChecked();
  expect(within(drawer).queryByRole('combobox')).not.toBeInTheDocument();
  expect(within(drawer).getByText('Last verified saved allocation: Not confirmed')).toBeVisible();
  expect(within(drawer).getByText('Review provider scope & evidence')).toBeVisible();
  expect(within(drawer).getByText('Published provider access controls.')).not.toBeVisible();
  expect(within(drawer).getByText('source-1')).not.toBeVisible();
  expect(api.confirmCapabilityResponsibilities).not.toHaveBeenCalled();
});

it('offers a first pass when the API supports persisted responsibility drafts', async () => {
  // Arrange
  vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(Object.assign(preview(), {
    supportsResponsibilityDrafts: true, baselineControlIds: ['AU-11'],
  }));
  // Act
  const drawer = await open();
  // Assert
  expect(within(drawer).getByText('Prepared first pass')).toBeVisible();
});

it('preserves entered drafts across Shared, Inherited, Customer and unconfirmed choices', async () => {
  // Arrange
  const drawer = await open();
  shared(drawer);
  // Act
  fireEvent.click(within(drawer).getByRole('radio', { name: /Provider covers the control/ }));
  fill(drawer, 'Applicable scope', 'Enrolled archive only.');
  fill(drawer, 'Exclusions', 'None after review.');
  fill(drawer, 'Supporting source', 'Reviewed evidence record 7.');
  fireEvent.click(within(drawer).getByRole('radio', { name: /My team implements/ }));
  // Assert
  expect(within(drawer).getByRole('textbox', { name: 'Customer duties' })).toHaveValue('Review local retention.');
  expect(within(drawer).queryByRole('textbox', { name: 'Provider duties' })).not.toBeInTheDocument();
  // Act
  fireEvent.click(within(drawer).getByRole('radio', { name: /I need more information/ }));
  fill(drawer, 'Information needed', 'Need approved system scope.');
  fireEvent.click(within(drawer).getByRole('radio', { name: /Provider and my team/ }));
  // Assert
  expect(within(drawer).getByRole('textbox', { name: 'Provider duties' })).toHaveValue('Operate archive.');
  expect(within(drawer).getByRole('textbox', { name: 'Customer duties' })).toHaveValue('Review local retention.');
  expect(within(drawer).getByRole('textbox', { name: 'Basis for this allocation' })).toHaveValue('Reviewed original evidence for this system.');
});

it('reviews without writing, then sends explicit allocation and existing review evidence', async () => {
  // Arrange
  const drawer = await open();
  shared(drawer);
  // Act
  fireEvent.click(within(drawer).getByRole('button', { name: 'Review allocation' }));
  // Assert
  expect(within(drawer).getByRole('heading', { name: 'Review before confirming' })).toBeVisible();
  expect(within(drawer).getByText('Operate archive.')).toBeVisible();
  expect(api.confirmCapabilityResponsibilities).not.toHaveBeenCalled();
  expect(within(drawer).getByRole('button', { name: 'Confirm responsibility' })).toBeDisabled();
  // Act
  fireEvent.click(within(drawer).getByRole('checkbox'));
  fireEvent.click(within(drawer).getByRole('button', { name: 'Confirm responsibility' }));
  // Assert
  await waitFor(() => expect(api.confirmCapabilityResponsibilities).toHaveBeenCalledWith(
    'system-a', 'capability-a', expect.objectContaining({
      sourceRevision: 'source-1', reviewRevision: 'review-1', baselineId: 'baseline-a',
      providerCoverageVerified: true, customerDutiesReviewed: true,
      reviewNotes: expect.stringContaining('Operate archive.'),
      allocations: [{ controlId: 'AU-11', inheritanceType: 'Shared', provider: 'Flankspeed', customerResponsibility: 'Review local retention.' }],
    }), expect.any(AbortSignal)));
});

it('keeps an information gap local without overwriting an existing allocation', async () => {
  // Arrange
  const data = preview();
  data.items[0]!.allocation = { controlId: 'AU-11', inheritanceType: 'Customer', provider: null, customerResponsibility: 'Saved duties' };
  vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(data);
  const drawer = await open();
  // Act
  fireEvent.click(within(drawer).getByRole('radio', { name: /I need more information/ }));
  fill(drawer, 'Information needed', 'Verify scope first.');
  fireEvent.click(within(drawer).getByRole('button', { name: 'Review information gap' }));
  // Assert
  expect(within(drawer).getByText('Last verified saved allocation: Customer')).toBeVisible();
  expect(within(drawer).getByText(/No server draft or proposal is saved/)).toBeVisible();
  expect(within(drawer).queryByRole('button', { name: 'Confirm responsibility' })).not.toBeInTheDocument();
  expect(api.confirmCapabilityResponsibilities).not.toHaveBeenCalled();
});

it.each([409, 500])('preserves drafts after HTTP %s and explicitly refreshes before another write', async status => {
  // Arrange
  vi.mocked(api.confirmCapabilityResponsibilities).mockRejectedValue(new api.ResponsibilityApiError('Request rejected.', status));
  const drawer = await open();
  shared(drawer);
  review(drawer);
  // Act
  fireEvent.click(within(drawer).getByRole('button', { name: 'Confirm responsibility' }));
  // Assert
  expect(await within(drawer).findByRole('alert')).toHaveTextContent(/Confirmation failed/);
  expect(screen.getAllByRole('alert')).toHaveLength(1);
  expect(api.getCapabilityResponsibilities).toHaveBeenCalledTimes(1);
  expect(within(drawer).getByRole('button', { name: 'Confirm responsibility' })).toBeDisabled();
  // Act
  vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue({
    ...preview(), items: [{ ...item(), sourceRevision: 'source-2', reviewRevision: 'review-2' }],
  });
  fireEvent.click(within(drawer).getByRole('button', { name: 'Refresh saved state' }));
  // Assert
  await waitFor(() => expect(within(drawer).getByRole('textbox', { name: 'Provider duties' })).toHaveValue('Operate archive.'));
  expect(within(drawer).getByRole('textbox', { name: 'Customer duties' })).toHaveValue('Review local retention.');
  expect(within(drawer).queryByRole('checkbox')).not.toBeInTheDocument();
  expect(screen.queryByText(/Selected allocation confirmed/)).not.toBeInTheDocument();
});

it('preserves the draft and last verified allocation when recovery loading fails', async () => {
  // Arrange
  vi.mocked(api.confirmCapabilityResponsibilities).mockRejectedValue(new api.ResponsibilityApiError('Save failed.', 500));
  const drawer = await open();
  shared(drawer); review(drawer);
  fireEvent.click(within(drawer).getByRole('button', { name: 'Confirm responsibility' }));
  await within(drawer).findByRole('alert');
  vi.mocked(api.getCapabilityResponsibilities).mockRejectedValue(new Error('Preview offline.'));
  // Act
  fireEvent.click(within(drawer).getByRole('button', { name: 'Refresh saved state' }));
  // Assert
  expect(await within(drawer).findByRole('alert')).toHaveTextContent(/Preview refresh failed.*Preview offline/);
  expect(within(drawer).getByText('Last verified saved allocation: Not confirmed')).toBeVisible();
  expect(screen.getAllByRole('alert')).toHaveLength(1);
  fireEvent.click(within(drawer).getByRole('button', { name: 'Back to edit' }));
  expect(within(drawer).getByRole('textbox', { name: 'Customer duties' })).toHaveValue('Review local retention.');
});

it('allows read-only source inspection without an enabled write action', async () => {
  // Arrange
  vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue({ ...preview(), canConfirm: false });
  // Act
  const drawer = await open();
  // Assert
  expect(within(drawer).getByRole('radio', { name: /Provider and my team/ })).toBeDisabled();
  expect(within(drawer).getByText(/Read-only/)).toBeVisible();
  expect(api.confirmCapabilityResponsibilities).not.toHaveBeenCalled();
});

it.each(['Inherited', 'Customer'] as const)('confirms %s only with its relevant fields and retains local duties', async allocation => {
  // Arrange
  const drawer = await open();
  shared(drawer);
  // Act
  fireEvent.click(within(drawer).getByRole('radio', {
    name: allocation === 'Inherited' ? /Provider covers the control/ : /My team implements/,
  }));
  if (allocation === 'Inherited') {
    expect(within(drawer).getByRole('button', { name: 'Review allocation' })).toBeDisabled();
    fill(drawer, 'Applicable scope', 'System archive.');
    fill(drawer, 'Exclusions', 'None after review.');
    fill(drawer, 'Supporting source', 'Reviewed source record 1.');
  }
  review(drawer);
  fireEvent.click(within(drawer).getByRole('button', { name: 'Confirm responsibility' }));
  // Assert
  await waitFor(() => expect(api.confirmCapabilityResponsibilities).toHaveBeenCalledWith(
    'system-a', 'capability-a', expect.objectContaining({
      allocations: [{ controlId: 'AU-11', inheritanceType: allocation,
        provider: allocation === 'Customer' ? null : 'Flankspeed', customerResponsibility: 'Review local retention.' }],
      reviewNotes: expect.stringContaining('Basis for this allocation'),
    }), expect.any(AbortSignal)));
  expect(await screen.findByText(/Selected allocation confirmed/)).toBeVisible();
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
});

it('blocks oversized review context without discarding fields', async () => {
  // Arrange
  const drawer = await open();
  shared(drawer);
  // Act
  fill(drawer, 'Provider duties', 'p'.repeat(1100));
  fill(drawer, 'Basis for this allocation', 'b'.repeat(1000));
  // Assert
  expect(within(drawer).getByRole('alert')).toHaveTextContent('2000 characters');
  expect(within(drawer).getByRole('button', { name: 'Review allocation' })).toBeDisabled();
  expect(api.confirmCapabilityResponsibilities).not.toHaveBeenCalled();
});

it('keeps stale, missing evidence and explicitly synthetic source cautions visible', async () => {
  // Arrange
  vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue({
    ...preview(), items: [{ ...item(), reviewedSourceRevision: 'older-source',
      sourceSnapshotJson: responsibilitySnapshotJson({ Controls: ['AU-11'], Description: 'Synthetic demonstration coverage.' }) }],
  });
  // Act
  const drawer = await open();
  // Assert
  expect(within(drawer).getByText(/source has changed since the saved review/)).toBeVisible();
  expect(within(drawer).getByText(/Source text describes synthetic/)).toBeVisible();
  expect(within(drawer).getByText(/System scope and evidence sufficiency/)).toBeVisible();
});
