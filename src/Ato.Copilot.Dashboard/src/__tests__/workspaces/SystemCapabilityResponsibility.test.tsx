import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import SystemCapabilityResponsibility from '../../features/workspace-operations/system-capabilities/SystemCapabilityResponsibility';
import { responsibilityItem } from '../helpers/capabilityResponsibilityFixture';
import type { CapabilityResponsibilityResponse } from '../../api/capabilityResponsibilities';
import * as draftApi from '../../api/responsibilityDrafts';
import type { ResponsibilityDraft, ResponsibilityDraftContext, ResponsibilityValue } from '../../api/responsibilityDrafts';

const confirm = vi.fn();
vi.mock('../../features/workspace-operations/system-capabilities/systemCapabilityResponsibilityRequests', () => ({
  confirmSystemCapabilityResponsibilities: (...args: unknown[]) => confirm(...args),
}));
vi.mock('../../api/responsibilityDrafts', async original => ({
  ...await original<typeof import('../../api/responsibilityDrafts')>(),
  getResponsibilityDraft: vi.fn(),
  prepareResponsibilityDraft: vi.fn(),
  saveResponsibilityDraft: vi.fn(),
  confirmResponsibilityDraft: vi.fn(),
}));

const draftValue = (value: string): ResponsibilityValue => ({
  value, origin: 'From system records', sourceIds: [], explanation: '', userEdited: false, sourceHash: 'source-1',
});
const draftValues = {
  allocation: draftValue('NeedsConfirmation'), provider: draftValue(''), providerDuties: draftValue(''),
  customer: draftValue('Recorded customer duty'), scope: draftValue(''), exclusions: draftValue(''),
  source: draftValue(''), basis: draftValue('Recorded basis'), information: draftValue(''),
};
const reviewableValues = {
  ...draftValues, allocation: draftValue('Customer'),
};
const draftContext = (): ResponsibilityDraftContext => ({
  systemId: 'system-a', controlId: 'AC-1', baselineId: 'baseline-a', scopeId: null, canPrepare: true,
  sourceHash: 'source-1', scopes: [], sources: [], sourceValues: draftValues, questions: [], conflicts: [], draft: null,
});
const savedDraft = (): ResponsibilityDraft => ({
  id: 'draft-a', revision: 1, status: 'Proposed', sourceHash: 'source-1', isStale: false,
  generationState: 'NotRequested', generationError: null, preparedAt: '2026-10-01', generatedAt: null,
  preparedBy: 'reviewer', reviewedBy: null, reviewedAt: null, values: draftValues,
  suggestion: { values: draftValues, questions: [], conflicts: [] }, sources: [], history: [],
});
const reviewableDraft = (): ResponsibilityDraft => ({
  ...savedDraft(), values: reviewableValues, suggestion: { values: reviewableValues, questions: [], conflicts: [] },
});
const reviewableContext = (): ResponsibilityDraftContext => ({
  ...draftContext(), sourceValues: reviewableValues, draft: reviewableDraft(),
});

function preview(canConfirm = true): CapabilityResponsibilityResponse {
  const item = responsibilityItem('PendingReview', 'AC-1');
  item.allocation = { controlId: 'AC-1', inheritanceType: 'Shared', provider: 'Collect logs', customerResponsibility: 'Review logs' };
  return {
    systemId: 'system-a', baselineId: 'baseline-a', baselineName: 'Moderate baseline',
    canConfirm, items: [item], pendingImpacts: [],
  };
}

function mount(data = preview(), onChanged = vi.fn()) {
  return render(<MemoryRouter><SystemCapabilityResponsibility tenantId="tenant-a" systemId="system-a" source="provider" capabilityId="capability-a"
    sourceRevision={data.items[0]!.sourceRevision} reviewRevision={data.items[0]!.reviewRevision}
    controlId="AC-1" preview={data} onChanged={onChanged} /></MemoryRouter>);
}

describe('System capability responsibility review', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    confirm.mockResolvedValue(preview());
    vi.mocked(draftApi.getResponsibilityDraft).mockResolvedValue(draftContext());
    vi.mocked(draftApi.prepareResponsibilityDraft).mockResolvedValue({ ...draftContext(), draft: savedDraft() });
    vi.mocked(draftApi.saveResponsibilityDraft).mockResolvedValue({ ...savedDraft(), revision: 2 });
  });
  afterEach(() => vi.useRealTimers());

  it('offers a separately saved responsibility draft without accepting responsibility', async () => {
    // Arrange
    mount();
    // Act
    fireEvent.click(screen.getByText('Prepare or review a saved responsibility draft'));
    expect(await screen.findByRole('heading', { name: 'Prepared responsibility · AC-1' })).toBeVisible();
    expect(screen.getByRole('textbox', { name: 'Customer responsibility draft' })).toHaveValue('Recorded customer duty');
    fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
    // Assert
    await waitFor(() => expect(draftApi.saveResponsibilityDraft).toHaveBeenCalledOnce());
    expect(draftApi.confirmResponsibilityDraft).not.toHaveBeenCalled();
    expect(await screen.findByText('Draft saved. Responsibility acceptance remains separate.')).toBeVisible();
  });

  it('requires saved-state review, both acknowledgements and notes before draft confirmation', async () => {
    // Arrange
    vi.mocked(draftApi.getResponsibilityDraft).mockResolvedValue(reviewableContext());
    vi.mocked(draftApi.confirmResponsibilityDraft).mockResolvedValue({
      ...reviewableDraft(), status: 'Accepted', reviewedBy: 'reviewer', reviewedAt: '2026-10-01',
    });
    const changed = vi.fn();
    mount(preview(), changed);
    fireEvent.click(screen.getByText('Prepare or review a saved responsibility draft'));
    // Act
    const review = await screen.findByRole('button', { name: 'Review saved responsibility' });
    expect(review).toBeEnabled();
    fireEvent.click(review);
    const confirmDraft = screen.getByRole('button', { name: 'Confirm saved responsibility draft for AC-1' });
    expect(confirmDraft).toBeDisabled();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Provider coverage or system applicability verified' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Customer duties reviewed for saved responsibility draft' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Review notes' }), { target: { value: 'Reviewed saved fields and current source versions.' } });
    fireEvent.click(confirmDraft);
    // Assert
    await waitFor(() => expect(draftApi.confirmResponsibilityDraft).toHaveBeenCalledOnce());
    expect(draftApi.confirmResponsibilityDraft).toHaveBeenCalledWith(
      'system-a', expect.objectContaining({ id: 'draft-a', revision: 1 }),
      'Reviewed saved fields and current source versions.', expect.any(AbortSignal));
    expect(confirm).not.toHaveBeenCalled();
    expect(changed).toHaveBeenCalledOnce();
  });

  it('keeps the saved-draft workflow read-only when the server denies draft permission', async () => {
    // Arrange
    vi.mocked(draftApi.getResponsibilityDraft).mockResolvedValue({ ...reviewableContext(), canPrepare: false });
    mount();
    fireEvent.click(screen.getByText('Prepare or review a saved responsibility draft'));
    // Act
    expect(await screen.findByText(/assigned ISSM or ISSO is required to save or confirm/i)).toBeVisible();
    // Assert
    expect(screen.getByRole('button', { name: 'Save draft' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Review saved responsibility' })).toBeDisabled();
    expect(draftApi.confirmResponsibilityDraft).not.toHaveBeenCalled();
  });

  it('requires both source and customer checks plus notes for exact revision confirmation', async () => {
    // Arrange
    mount();
    const submit = screen.getByRole('button', { name: 'Confirm AC-1 responsibility' });
    expect(submit).toBeDisabled();
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: 'Provider coverage verified' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Customer duties reviewed' }));
    expect(submit).toBeDisabled();
    fireEvent.change(screen.getByRole('textbox', { name: 'Review notes (required)' }), { target: { value: 'Verified published logs and customer review duties.' } });
    fireEvent.click(submit);
    // Assert
    await waitFor(() => expect(confirm).toHaveBeenCalledOnce());
    expect(confirm.mock.calls[0]?.slice(0, 3)).toEqual(['tenant-a', 'system-a', 'capability-a']);
    expect(confirm.mock.calls[0]?.[3]).toMatchObject({
      baselineId: 'baseline-a', sourceRevision: 'source-1', reviewRevision: 'review-1',
      providerCoverageVerified: true, customerDutiesReviewed: true,
      reviewNotes: 'Verified published logs and customer review duties.',
      allocations: [{ controlId: 'AC-1', inheritanceType: 'Shared', provider: 'Collect logs', customerResponsibility: 'Review logs' }],
    });
  });

  it('keeps confirmation unavailable without server permission', () => {
    // Arrange
    mount(preview(false));
    // Act
    const submit = screen.getByRole('button', { name: 'Confirm AC-1 responsibility' });
    // Assert
    expect(submit).toBeDisabled();
    expect(screen.getByText(/assigned ISSM or ISSO/i)).toBeVisible();
    expect(confirm).not.toHaveBeenCalled();
  });

  it('links to the registered system responsibility review route', () => {
    // Arrange
    mount();
    // Act
    const link = screen.getByRole('link', { name: 'Open full system responsibility review' });
    // Assert
    expect(link).toHaveAttribute('href', '/systems/system-a/inheritance/subscriptions');
  });

  it('does not turn unavailable reviewed source data into a fabricated comparison', () => {
    // Arrange
    mount();
    // Act
    const current = screen.getByRole('region', { name: 'Available source snapshot' });
    // Assert
    expect(current).toHaveTextContent('Reviewed access capability');
    expect(screen.getByText(/No confirmed source snapshot/i)).toBeVisible();
  });

  it('rejects responsibility items from a different capability or system', () => {
    // Arrange
    const foreign = preview();
    foreign.systemId = 'system-b';
    // Act
    mount(foreign);
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent(/does not match/i);
    expect(screen.queryByRole('button', { name: /Confirm/ })).not.toBeInTheDocument();
  });

  it('clears acknowledgements when the reviewed source revision changes', () => {
    // Arrange
    const data = preview();
    const view = mount(data);
    fireEvent.click(screen.getByRole('checkbox', { name: 'Provider coverage verified' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Customer duties reviewed' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Review notes (required)' }), { target: { value: 'Reviewed old revision.' } });
    const changed = structuredClone(data);
    changed.items[0]!.sourceRevision = 'source-2';
    // Act
    view.rerender(<MemoryRouter><SystemCapabilityResponsibility tenantId="tenant-a" systemId="system-a" source="provider" capabilityId="capability-a"
      sourceRevision="source-2" reviewRevision={changed.items[0]!.reviewRevision}
      controlId="AC-1" preview={changed} onChanged={vi.fn()} /></MemoryRouter>);
    // Assert
    expect(screen.getByRole('button', { name: 'Confirm AC-1 responsibility' })).toBeDisabled();
    expect(screen.getByRole('checkbox', { name: 'Provider coverage verified' })).not.toBeChecked();
    expect(screen.getByRole('textbox', { name: 'Review notes (required)' })).toHaveValue('');
  });

  it('keeps outside-baseline contributions visible without allowing confirmation', () => {
    // Arrange
    const data = preview();
    data.items[0]!.state = 'OutsideBaseline';
    // Act
    mount(data);
    // Assert
    expect(screen.getByText(/OutsideBaseline/)).toBeVisible();
    expect(screen.getByRole('button', { name: 'Confirm AC-1 responsibility' })).toBeDisabled();
  });

  it('fails closed for a malformed current snapshot', () => {
    // Arrange
    const data = preview();
    data.items[0]!.sourceSnapshotJson = '{"Name":"Untrusted partial snapshot"}';
    // Act
    mount(data);
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent(/snapshot.*malformed/i);
    expect(screen.getByRole('button', { name: 'Confirm AC-1 responsibility' })).toBeDisabled();
  });

  it('bounds confirmation even if transport never settles after cancellation', async () => {
    // Arrange
    vi.useFakeTimers();
    confirm.mockReturnValue(new Promise(() => undefined));
    mount();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Provider coverage verified' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Customer duties reviewed' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Review notes (required)' }), { target: { value: 'Verified duties.' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Confirm AC-1 responsibility' }));
    await act(async () => { await vi.advanceTimersByTimeAsync(30001); });
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent(/timed out/i);
    expect(screen.getByRole('button', { name: 'Confirm AC-1 responsibility' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Refresh responsibilities' })).toBeVisible();
  });

  it('invalidates acknowledgements after a stale confirmation and refreshes rather than retrying', async () => {
    // Arrange
    const changed = vi.fn();
    confirm.mockRejectedValue(Object.assign(new Error('Review changed.'), { status: 409 }));
    mount(preview(), changed);
    fireEvent.click(screen.getByRole('checkbox', { name: 'Provider coverage verified' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Customer duties reviewed' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Review notes (required)' }), { target: { value: 'Verified duties.' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Confirm AC-1 responsibility' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/source, baseline or review changed/);
    fireEvent.click(screen.getByRole('button', { name: 'Refresh responsibilities' }));
    // Assert
    expect(changed).toHaveBeenCalledWith();
    expect(screen.getByRole('checkbox', { name: 'Provider coverage verified' })).not.toBeChecked();
    expect(confirm).toHaveBeenCalledOnce();
  });

  it('does not confirm when the control detail and responsibility snapshot revisions disagree', () => {
    // Arrange
    const data = preview();
    // Act
    render(<MemoryRouter><SystemCapabilityResponsibility tenantId="tenant-a" systemId="system-a" source="provider" capabilityId="capability-a"
      controlId="AC-1" sourceRevision="new-source" reviewRevision="review-1" preview={data} onChanged={vi.fn()} /></MemoryRouter>);
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent(/revision.*changed|revisions.*match/i);
    expect(screen.queryByRole('button', { name: 'Confirm AC-1 responsibility' })).not.toBeInTheDocument();
  });

  it('shows persisted review evidence without fabricating historical acknowledgement fields', () => {
    // Arrange
    const data = preview();
    Object.assign(data.items[0]!, { providerCoverageVerified: true, customerDutiesReviewed: true, reviewNotes: 'Saved audit note.' });
    // Act
    mount(data);
    // Assert
    expect(screen.getByText('Saved audit note.')).toBeVisible();
    expect(screen.getByText(/Provider coverage: Verified/)).toBeVisible();
    expect(screen.getByRole('textbox', { name: 'Review notes (required)' })).toHaveAttribute('maxLength', '2000');
  });

  it('requires renewed acknowledgements after allocation or duty edits and saves the edited allocation', async () => {
    // Arrange
    mount();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Provider coverage verified' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Customer duties reviewed' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Review notes (required)' }), { target: { value: 'Rechecked updated duties.' } });
    // Act
    fireEvent.change(screen.getByRole('combobox', { name: 'Responsibility allocation' }), { target: { value: 'Customer' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Provider responsibility' }), { target: { value: '' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Customer responsibility' }), { target: { value: 'Operate and review system logging.' } });
    // Assert
    expect(screen.getByRole('button', { name: 'Confirm AC-1 responsibility' })).toBeDisabled();
    expect(screen.getByRole('checkbox', { name: 'Provider coverage verified' })).not.toBeChecked();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Provider coverage verified' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Customer duties reviewed' }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm AC-1 responsibility' }));
    await waitFor(() => expect(confirm).toHaveBeenCalledOnce());
    expect(confirm.mock.calls[0]?.[3].allocations).toEqual([{
      controlId: 'AC-1', inheritanceType: 'Customer', provider: null, customerResponsibility: 'Operate and review system logging.',
    }]);
  });
});
