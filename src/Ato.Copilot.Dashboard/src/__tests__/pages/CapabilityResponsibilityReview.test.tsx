import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { Link, MemoryRouter, Route, Routes } from 'react-router-dom';
import CapabilityResponsibilityReview from '../../pages/CapabilityResponsibilityReview';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import * as api from '../../api/capabilityResponsibilities';
import type { CapabilityResponsibilityItem, CapabilityResponsibilityResponse } from '../../api/capabilityResponsibilities';

vi.mock('../../api/capabilityResponsibilities', async importOriginal => {
  const actual = await importOriginal<typeof import('../../api/capabilityResponsibilities')>();
  return { ...actual, getCapabilityResponsibilities: vi.fn(), confirmCapabilityResponsibilities: vi.fn(),
    reconcileCapabilityResponsibilities: vi.fn(), dispatchCapabilityResponsibilityImpacts: vi.fn() };
});
const item = (state = 'MissingAllocation', controlId = 'AC-1'): CapabilityResponsibilityItem => ({
  subscriptionId: 'subscription-a', capabilityId: 'capability-a', componentId: 'component-a', cspProfileId: 'provider-a',
  controlId, sourceRevision: 'source-1', reviewRevision: 'review-1', state, reviewedSourceRevision: null,
  confirmedBy: null, confirmedAt: null, allocation: null, effectiveInheritanceType: null, designationSource: null,
});
const preview = (overrides: Partial<CapabilityResponsibilityResponse> = {}): CapabilityResponsibilityResponse => ({
  systemId: 'system-a', baselineId: 'baseline-a', canConfirm: true, items: [item()], pendingImpacts: [], ...overrides,
});
function renderReview() {
  return render(<MemoryRouter initialEntries={['/workspaces/organizations/org-a/systems/system-a/inheritance/subscriptions']}>
    <WorkspaceNavigationProvider workspace={{ kind: 'organization', tenantId: 'org-a' }}>
      <Routes><Route path="/workspaces/organizations/:tenantId/systems/:id/inheritance/subscriptions" element={<CapabilityResponsibilityReview />} /></Routes>
    </WorkspaceNavigationProvider>
  </MemoryRouter>);
}
async function chooseShared() {
  const select = await screen.findByRole('combobox', { name: 'Allocation for AC-1' });
  fireEvent.change(select, { target: { value: 'Shared' } });
  fireEvent.change(screen.getByRole('textbox', { name: 'Provider for AC-1' }), { target: { value: 'Reviewed CSP' } });
  fireEvent.change(screen.getByRole('textbox', { name: 'Customer responsibility for AC-1' }), { target: { value: 'Customer reviews accounts.' } });
  fireEvent.click(screen.getByRole('checkbox', { name: 'I reviewed the provider revision and the selected allocations.' }));
}
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(preview());
  vi.mocked(api.confirmCapabilityResponsibilities).mockResolvedValue(preview({ items: [item('Applied')] }));
});

describe('system subscription responsibility review', () => {
  it('requires a baseline and preserves the organization scope on the baseline link', async () => {
    // Arrange
    vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(preview({ baselineId: null, items: [item('MissingBaseline')] }));
    // Act
    renderReview();
    // Assert
    expect(await screen.findByText('Select a baseline before confirming allocations.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Select or review baseline' })).toHaveAttribute('href', '/workspaces/organizations/org-a/systems/system-a/baseline');
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    expect(api.confirmCapabilityResponsibilities).not.toHaveBeenCalled();
  });

  it('uses only server canConfirm, never a browser ISSM role preference', async () => {
    // Arrange
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({ role: 'ISSM' }));
    vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(preview({ canConfirm: false }));
    // Act
    renderReview();
    // Assert
    expect(await screen.findByText(/Read-only: an effective assigned ISSM or ISSO/)).toBeInTheDocument();
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reconcile current baseline' })).toBeDisabled();
    expect(api.confirmCapabilityResponsibilities).not.toHaveBeenCalled();
    localStorage.removeItem('ato-dashboard-settings');
  });

  it('starts without an inferred allocation and sends exactly the displayed revisions', async () => {
    // Arrange
    renderReview();
    expect(await screen.findByRole('combobox', { name: 'Allocation for AC-1' })).toHaveValue('');
    expect(screen.getByRole('button', { name: 'Confirm selected allocations' })).toBeDisabled();
    // Act
    await chooseShared();
    fireEvent.click(screen.getByRole('button', { name: 'Confirm selected allocations' }));
    // Assert
    await waitFor(() => expect(api.confirmCapabilityResponsibilities).toHaveBeenCalledWith('system-a', 'capability-a', {
      baselineId: 'baseline-a', sourceRevision: 'source-1', reviewRevision: 'review-1',
      allocations: [{ controlId: 'AC-1', inheritanceType: 'Shared', provider: 'Reviewed CSP', customerResponsibility: 'Customer reviews accounts.' }],
    }, expect.anything()));
    expect(api.reconcileCapabilityResponsibilities).not.toHaveBeenCalled();
    expect(api.dispatchCapabilityResponsibilityImpacts).not.toHaveBeenCalled();
  });

  it('shows source, subscription and effective designation separately for every prerequisite/state', async () => {
    // Arrange
    const states = ['MissingAllocation', 'PendingReview', 'ConflictingAllocations', 'PreservedOverride', 'OutsideBaseline', 'Inactive'];
    vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(preview({ items: states.map((state, i) => ({
      ...item(state, `AC-${i + 1}`), effectiveInheritanceType: state === 'PreservedOverride' ? 'Customer' : null,
      designationSource: state === 'PreservedOverride' ? 'Manual' : null,
    })) }));
    // Act
    renderReview();
    // Assert
    await screen.findByText('Subscription subscription-a');
    for (const label of ['Missing allocation', 'Pending review', 'Conflicting allocations', 'Preserved override', 'Outside baseline', 'Inactive']) {
      expect(screen.getByText(label, { exact: true })).toBeInTheDocument();
    }
    expect(screen.getByText('component-a')).toBeInTheDocument();
    expect(screen.getByText('provider-a')).toBeInTheDocument();
    expect(screen.getByText('Customer · Manual')).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Allocation for AC-5' })).not.toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Allocation for AC-6' })).not.toBeInTheDocument();
  });

  it('refreshes after 409, clears stale edits and requires a new explicit confirmation', async () => {
    // Arrange
    vi.mocked(api.confirmCapabilityResponsibilities).mockRejectedValue(new api.ResponsibilityApiError('Review changed.', 409));
    vi.mocked(api.getCapabilityResponsibilities).mockResolvedValueOnce(preview()).mockResolvedValueOnce(
      preview({ items: [{ ...item('PendingReview'), sourceRevision: 'source-2', reviewRevision: 'review-2' }] }));
    renderReview();
    await chooseShared();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Confirm selected allocations' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(/changed.*review again/i);
    await waitFor(() => expect(api.getCapabilityResponsibilities).toHaveBeenCalledTimes(2));
    expect(await screen.findByRole('combobox', { name: 'Allocation for AC-1' })).toHaveValue('');
    expect(screen.getByText('source-2')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Confirm selected allocations' })).toBeDisabled();
  });

  it('blocks old controls after write permission is denied', async () => {
    // Arrange
    vi.mocked(api.confirmCapabilityResponsibilities).mockRejectedValue(new api.ResponsibilityApiError('ISSM or ISSO required.', 403));
    renderReview();
    await chooseShared();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Confirm selected allocations' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('ISSM or ISSO required.');
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry preview' })).toBeInTheDocument();
  });

  it('keeps reconciliation and mark-only delivery explicit and separate from narrative generation', async () => {
    // Arrange
    const data = preview({ pendingImpacts: [{
      id: 'impact-a', baselineId: 'baseline-a', controlId: 'AC-1', stateHash: 'hash-a',
      reason: 'SourceReconciled', sourcesJson: '[{"SubscriptionId":"subscription-a","IsActive":false}]', createdAt: '2026-09-21T00:00:00Z',
    }] });
    vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(data);
    vi.mocked(api.reconcileCapabilityResponsibilities).mockResolvedValue(data);
    vi.mocked(api.dispatchCapabilityResponsibilityImpacts).mockResolvedValue({ delivered: 1, pending: 0, proposalIds: [], deferred: [] });
    renderReview();
    await screen.findByText('SourceReconciled');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Reconcile current baseline' }));
    await waitFor(() => expect(api.reconcileCapabilityResponsibilities).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Deliver pending review impacts' })).toBeEnabled());
    expect(api.dispatchCapabilityResponsibilityImpacts).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Deliver pending review impacts' }));
    // Assert
    expect(await screen.findByText(/1 delivered.*0 pending/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /generate|approve/i })).not.toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: 'Pending review impacts' })).getByText(/subscription-a/)).toBeInTheDocument();
    expect(api.confirmCapabilityResponsibilities).not.toHaveBeenCalled();
  });

  it('reports missing-narrative deferrals and links returned proposal IDs without generating content', async () => {
    // Arrange
    const data = preview({ pendingImpacts: [{ id: 'impact-a', baselineId: 'baseline-a', controlId: 'AC-1',
      stateHash: 'hash', reason: 'SourceReconciled', sourcesJson: '[]', createdAt: '2026-09-21T00:00:00Z' }] });
    vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(data);
    vi.mocked(api.dispatchCapabilityResponsibilityImpacts).mockResolvedValue({
      delivered: 0, pending: 1, proposalIds: ['proposal-a'], deferred: [{ impactId: 'impact-a', controlId: 'AC-1', reason: 'MissingNarrative' }],
    });
    renderReview();
    await screen.findByText('SourceReconciled');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Deliver pending review impacts' }));
    // Assert
    expect(await screen.findByText(/AC-1: missing narrative/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Review queued proposal proposal-a' })).toHaveAttribute('href',
      '/workspaces/organizations/org-a/systems/system-a/narratives/review?proposal=proposal-a');
    expect(api.confirmCapabilityResponsibilities).not.toHaveBeenCalled();
  });

  it('preserves pending impact work and an explicit error when dispatch fails', async () => {
    // Arrange
    vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(preview({ pendingImpacts: [{
      id: 'impact-a', baselineId: 'baseline-a', controlId: 'AC-1', stateHash: 'hash',
      reason: 'SourceReconciled', sourcesJson: '[]', createdAt: '2026-09-21T00:00:00Z',
    }] }));
    vi.mocked(api.dispatchCapabilityResponsibilityImpacts).mockRejectedValue(new api.ResponsibilityApiError('Queue unavailable', 503));
    renderReview();
    await screen.findByText('SourceReconciled');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Deliver pending review impacts' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Queue unavailable');
    expect(screen.getByRole('heading', { name: 'Pending review impacts (1)' })).toBeInTheDocument();
    expect(screen.queryByText(/delivered,.*pending/)).not.toBeInTheDocument();
  });

  it('blocks unknown or inconsistent provider states rather than inventing allocations', async () => {
    // Arrange
    vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(preview({ items: [
      item('FutureState'), { ...item('PendingReview', 'AC-2'), sourceRevision: 'different-source' },
    ] }));
    // Act
    renderReview();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('inconsistent revisions');
    expect(screen.getByText('Unknown state: FutureState')).toBeInTheDocument();
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
  });

  it('shows inaccessible previews as errors and permits an explicit read retry', async () => {
    // Arrange
    vi.mocked(api.getCapabilityResponsibilities).mockRejectedValueOnce(new api.ResponsibilityApiError('System not accessible.', 404))
      .mockResolvedValueOnce(preview());
    renderReview();
    expect(await screen.findByRole('alert')).toHaveTextContent('System not accessible.');
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry preview' }));
    // Assert
    expect(await screen.findByRole('combobox', { name: 'Allocation for AC-1' })).toHaveValue('');
  });

  it('preserves entered allocations when the server returns a validation error', async () => {
    // Arrange
    vi.mocked(api.confirmCapabilityResponsibilities).mockRejectedValue(new api.ResponsibilityApiError('Provider description is invalid.', 400));
    renderReview();
    await chooseShared();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Confirm selected allocations' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Provider description is invalid.');
    expect(screen.getByRole('combobox', { name: 'Allocation for AC-1' })).toHaveValue('Shared');
    expect(screen.getByRole('textbox', { name: 'Customer responsibility for AC-1' })).toHaveValue('Customer reviews accounts.');
  });

  it('aborts the old system read and never renders its late response under another system', async () => {
    // Arrange
    let resolveOld!: (value: CapabilityResponsibilityResponse) => void;
    vi.mocked(api.getCapabilityResponsibilities).mockImplementationOnce(() => new Promise(resolve => { resolveOld = resolve; }))
      .mockResolvedValueOnce(preview({ systemId: 'system-b', items: [{ ...item(), sourceRevision: 'system-b-source' }] }));
    render(<MemoryRouter initialEntries={['/workspaces/organizations/org-a/systems/system-a/inheritance/subscriptions']}>
      <WorkspaceNavigationProvider workspace={{ kind: 'organization', tenantId: 'org-a' }}>
        <Link to="/workspaces/organizations/org-a/systems/system-b/inheritance/subscriptions">Other system</Link>
        <Routes><Route path="/workspaces/organizations/:tenantId/systems/:id/inheritance/subscriptions" element={<CapabilityResponsibilityReview />} /></Routes>
      </WorkspaceNavigationProvider>
    </MemoryRouter>);
    // Act
    fireEvent.click(screen.getByRole('link', { name: 'Other system' }));
    await screen.findByText('system-b-source');
    await act(async () => { resolveOld(preview({ items: [{ ...item(), sourceRevision: 'stale-source' }] })); });
    // Assert
    expect(screen.getByText('system-b-source')).toBeInTheDocument();
    expect(screen.queryByText('stale-source')).not.toBeInTheDocument();
    expect(vi.mocked(api.getCapabilityResponsibilities).mock.calls[0]?.[1]?.aborted).toBe(true);
  });
});
