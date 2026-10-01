import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { Link, MemoryRouter, Route, Routes } from 'react-router-dom';
import * as api from '../../api/capabilityResponsibilities';
import type { CapabilityResponsibilityResponse } from '../../api/capabilityResponsibilities';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import CapabilityResponsibilityReview from '../../pages/CapabilityResponsibilityReview';
import { responsibilityItem as item, responsibilitySnapshotJson } from '../helpers/capabilityResponsibilityFixture';
import '../helpers/dialog';

vi.mock('../../api/capabilityResponsibilities', async importOriginal => {
  const actual = await importOriginal<typeof import('../../api/capabilityResponsibilities')>();
  return {
    ...actual,
    getCapabilityResponsibilities: vi.fn(),
    confirmCapabilityResponsibilities: vi.fn(),
    reconcileCapabilityResponsibilities: vi.fn(),
    dispatchCapabilityResponsibilityImpacts: vi.fn(),
  };
});

const preview = (overrides: Partial<CapabilityResponsibilityResponse> = {}): CapabilityResponsibilityResponse => ({
  systemId: 'system-a',
  baselineId: 'baseline-a',
  baselineName: 'Moderate baseline · CNSSI 1253 IL4',
  canConfirm: true,
  items: [item()],
  pendingImpacts: [],
  ...overrides,
});

function renderReview() {
  return render(<MemoryRouter initialEntries={['/workspaces/organizations/org-a/systems/system-a/inheritance/subscriptions']}>
    <WorkspaceNavigationProvider workspace={{ kind: 'organization', tenantId: 'org-a' }}>
      <Routes>
        <Route path="/workspaces/organizations/:tenantId/systems/:id/inheritance/subscriptions" element={<CapabilityResponsibilityReview />} />
      </Routes>
    </WorkspaceNavigationProvider>
  </MemoryRouter>);
}

async function openControl(controlId = 'AC-1') {
  fireEvent.click(await screen.findByRole('button', { name: `Open ${controlId} responsibility` }));
  return screen.findByRole('dialog', { name: `Review responsibility ${controlId}` });
}

async function chooseShared() {
  const drawer = screen.queryByRole('dialog', { name: 'Review responsibility AC-1' }) ?? await openControl();
  fireEvent.click(within(drawer).getByRole('radio', { name: /Provider and my team/ }));
  fireEvent.change(within(drawer).getByRole('textbox', { name: 'Provider for AC-1' }), { target: { value: 'Reviewed CSP' } });
  fireEvent.change(within(drawer).getByRole('textbox', { name: 'Customer duties' }), {
    target: { value: 'Customer reviews accounts.' },
  });
  fireEvent.change(within(drawer).getByRole('textbox', { name: 'Provider duties' }), { target: { value: 'Maintain service.' } });
  fireEvent.change(within(drawer).getByRole('textbox', { name: 'Basis for this allocation' }), { target: { value: 'Reviewed source.' } });
  fireEvent.click(within(drawer).getByRole('button', { name: 'Review allocation' }));
  fireEvent.click(within(drawer).getByRole('checkbox'));
  return drawer;
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(preview());
  vi.mocked(api.confirmCapabilityResponsibilities).mockResolvedValue(preview({ items: [item('Applied')] }));
});

describe('system subscription responsibility review', () => {
  it('matches the responsibility mock hierarchy and opens row review in a drawer', async () => {
    // Arrange / Act
    renderReview();
    const matrix = await screen.findByRole('region', { name: 'Responsibility matrix' });

    // Assert
    expect(within(matrix).getByRole('columnheader', { name: 'Control' })).toBeVisible();
    expect(screen.getByText('Baseline: Moderate baseline · CNSSI 1253 IL4')).toBeVisible();
    expect(screen.queryByText('Baseline: baseline-a')).not.toBeInTheDocument();
    expect(within(matrix).getByRole('columnheader', { name: 'Provider capability' })).toBeVisible();
    expect(within(matrix).getByText('Reviewed access capability')).toBeVisible();
    expect(within(matrix).getByText(/Flankspeed/)).toBeVisible();
    expect(within(matrix).queryByText(/source-1/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Review allocations' })).toBeEnabled();
    expect(screen.getByRole('link', { name: 'Preview contribution →' })).toHaveAttribute(
      'href',
      '/workspaces/organizations/org-a/systems/system-a/documents#ssp-sections',
    );
    expect(screen.getByRole('link', { name: 'View package readiness →' })).toHaveAttribute(
      'href',
      '/workspaces/organizations/org-a/systems/system-a',
    );
    const drawer = await openControl();
    expect(drawer).toHaveClass('max-w-3xl');
    expect(within(drawer).getByText('Provider contribution')).toBeVisible();
    expect(within(drawer).getByText('Technical source details')).not.toBeVisible();
    expect(within(drawer).getByText('source-1')).not.toBeVisible();
    fireEvent.click(within(drawer).getByText('Review provider scope & evidence'));
    fireEvent.click(within(drawer).getByText('Technical source details'));
    expect(within(drawer).getByText('source-1')).toBeVisible();
    expect(within(matrix).getByRole('link', { name: 'Review evidence for AC-1' })).toHaveAttribute(
      'href',
      '/workspaces/organizations/org-a/systems/system-a/evidence',
    );
  });

  it('opens the first reviewable control from the header action', async () => {
    // Arrange
    renderReview();
    await screen.findByRole('region', { name: 'Responsibility matrix' });

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Review allocations' }));

    // Assert
    expect(await screen.findByRole('dialog', { name: 'Review responsibility AC-1' })).toBeVisible();
  });

  it('defaults inherited allocations to the authoritative provider profile name', async () => {
    // Arrange
    renderReview();
    const drawer = await openControl();

    // Act
    fireEvent.click(within(drawer).getByRole('radio', { name: /Provider covers the control/ }));

    // Assert
    expect(within(drawer).getByRole('textbox', { name: 'Provider for AC-1' })).toHaveValue('Flankspeed');
    expect(within(drawer).getByText(/Flankspeed · Reviewed access capability/)).toBeVisible();
  });

  it('requires a baseline and preserves organization-scoped navigation', async () => {
    // Arrange
    vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(preview({
      baselineId: null,
      items: [item('MissingBaseline')],
    }));

    // Act
    renderReview();

    // Assert
    expect(await screen.findByText(/Select a baseline before confirming allocations/)).toBeVisible();
    expect(screen.getByRole('link', { name: 'Select or review baseline' })).toHaveAttribute(
      'href',
      '/workspaces/organizations/org-a/systems/system-a/baseline',
    );
    const drawer = await openControl();
    fireEvent.click(within(drawer).getByRole('radio', { name: /Provider and my team/ }));
    expect(within(drawer).getByRole('button', { name: 'Review allocation' })).toBeDisabled();
  });

  it('uses server confirmation permission rather than browser role preferences', async () => {
    // Arrange
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({ role: 'ISSM' }));
    vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(preview({ canConfirm: false }));

    // Act
    renderReview();
    const drawer = await openControl();

    // Assert
    expect(within(drawer).getByText(/effective assigned ISSM or ISSO/i)).toBeVisible();
    expect(within(drawer).getByRole('button', { name: 'Review information gap' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Reconcile current baseline' })).toBeDisabled();
    localStorage.removeItem('ato-dashboard-settings');
  });

  it('starts with no inferred allocation and confirms only the selected control and displayed revisions', async () => {
    // Arrange
    renderReview();
    const drawer = await openControl();
    expect(within(drawer).getByRole('radio', { name: /I need more information/ })).toBeChecked();

    // Act
    await chooseShared();
    fireEvent.click(within(drawer).getByRole('button', { name: 'Confirm responsibility' }));

    // Assert
    await waitFor(() => expect(api.confirmCapabilityResponsibilities).toHaveBeenCalledWith('system-a', 'capability-a', {
      baselineId: 'baseline-a',
      sourceRevision: 'source-1',
      reviewRevision: 'review-1',
      providerCoverageVerified: true,
      customerDutiesReviewed: true,
      reviewNotes: 'Provider duties: Maintain service.\n\nBasis for this allocation: Reviewed source.',
      allocations: [{
        controlId: 'AC-1',
        inheritanceType: 'Shared',
        provider: 'Reviewed CSP',
        customerResponsibility: 'Customer reviews accounts.',
      }],
    }, expect.anything()));
    expect(api.reconcileCapabilityResponsibilities).not.toHaveBeenCalled();
  });

  it('shows actual state, source ownership and effective designation without inventing allocations', async () => {
    // Arrange
    const states = ['MissingAllocation', 'PendingReview', 'ConflictingAllocations', 'PreservedOverride', 'OutsideBaseline', 'Inactive'];
    vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(preview({
      items: states.map((state, index) => ({
        ...item(state, `AC-${index + 1}`),
        effectiveInheritanceType: state === 'PreservedOverride' ? 'Customer' : null,
        designationSource: state === 'PreservedOverride' ? 'Manual' : null,
      })),
    }));

    // Act
    renderReview();

    // Assert
    await screen.findByRole('region', { name: 'Responsibility matrix' });
    for (const label of ['Missing allocation', 'Pending review', 'Conflicting allocations', 'Preserved override', 'Outside baseline', 'Inactive']) {
      expect(screen.getByText(label, { exact: true })).toBeVisible();
    }
    expect(screen.getAllByText(/Flankspeed · Published provider component/).length).toBeGreaterThan(0);
    expect(screen.getByText('Customer')).toBeVisible();
  });

  it('preserves a stale drawer and refreshes revisions only on request after a 409', async () => {
    // Arrange
    vi.mocked(api.confirmCapabilityResponsibilities).mockRejectedValue(new api.ResponsibilityApiError('Review changed.', 409));
    vi.mocked(api.getCapabilityResponsibilities).mockResolvedValueOnce(preview()).mockResolvedValueOnce(preview({
      items: [{ ...item('PendingReview'), sourceRevision: 'source-2', reviewRevision: 'review-2' }],
    }));
    renderReview();
    const drawer = await chooseShared();

    // Act
    fireEvent.click(within(drawer).getByRole('button', { name: 'Confirm responsibility' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(/changed.*review again/i);
    expect(api.getCapabilityResponsibilities).toHaveBeenCalledTimes(1);
    fireEvent.click(within(drawer).getByRole('button', { name: 'Refresh saved state' }));
    await waitFor(() => expect(api.getCapabilityResponsibilities).toHaveBeenCalledTimes(2));
    fireEvent.click(within(drawer).getByText('Review provider scope & evidence'));
    fireEvent.click(within(drawer).getByText('Technical source details'));
    expect(await within(drawer).findByText('source-2')).toBeVisible();
  });

  it('disables mutation while preserving draft context after write permission is denied', async () => {
    // Arrange
    vi.mocked(api.confirmCapabilityResponsibilities).mockRejectedValue(new api.ResponsibilityApiError('ISSM or ISSO required.', 403));
    renderReview();
    const drawer = await chooseShared();

    // Act
    fireEvent.click(within(drawer).getByRole('button', { name: 'Confirm responsibility' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('ISSM or ISSO required.');
    expect(within(drawer).getByRole('button', { name: 'Confirm responsibility' })).toBeDisabled();
    expect(within(drawer).getByRole('button', { name: 'Refresh saved state' })).toBeVisible();
  });

  it('keeps reconciliation and mark-only impact delivery separate from narrative generation', async () => {
    // Arrange
    const data = preview({ pendingImpacts: [{
      id: 'impact-a',
      baselineId: 'baseline-a',
      controlId: 'AC-1',
      stateHash: 'hash-a',
      reason: 'SourceReconciled',
      sourcesJson: '[{"SubscriptionId":"subscription-a","IsActive":false}]',
      createdAt: '2026-09-21T00:00:00Z',
    }] });
    vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(data);
    vi.mocked(api.reconcileCapabilityResponsibilities).mockResolvedValue(data);
    vi.mocked(api.dispatchCapabilityResponsibilityImpacts).mockResolvedValue({
      delivered: 1,
      pending: 0,
      proposalIds: [],
      deferred: [],
    });
    renderReview();
    await screen.findByText(/AC-1 · SourceReconciled/);

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Reconcile current baseline' }));
    await waitFor(() => expect(api.reconcileCapabilityResponsibilities).toHaveBeenCalledOnce());
    fireEvent.click(screen.getByRole('button', { name: 'Deliver pending review impacts' }));

    // Assert
    expect(await screen.findByText(/1 delivered, 0 pending/)).toBeVisible();
    expect(screen.queryByRole('button', { name: /generate|approve/i })).not.toBeInTheDocument();
  });

  it('reports deferred impact delivery and returned proposal links', async () => {
    // Arrange
    vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(preview({ pendingImpacts: [{
      id: 'impact-a', baselineId: 'baseline-a', controlId: 'AC-1', stateHash: 'hash',
      reason: 'SourceReconciled', sourcesJson: '[]', createdAt: '2026-09-21T00:00:00Z',
    }] }));
    vi.mocked(api.dispatchCapabilityResponsibilityImpacts).mockResolvedValue({
      delivered: 0,
      pending: 1,
      proposalIds: ['proposal-a'],
      deferred: [{ impactId: 'impact-a', controlId: 'AC-1', reason: 'MissingNarrative' }],
    });
    renderReview();
    await screen.findByText(/AC-1 · SourceReconciled/);

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Deliver pending review impacts' }));

    // Assert
    expect(await screen.findByText(/AC-1: missing narrative/)).toBeVisible();
    expect(screen.getByRole('link', { name: 'Review queued proposal proposal-a' })).toHaveAttribute(
      'href',
      '/workspaces/organizations/org-a/systems/system-a/narratives/review?proposal=proposal-a',
    );
  });

  it('keeps the drawer and entered allocation after a validation error', async () => {
    // Arrange
    vi.mocked(api.confirmCapabilityResponsibilities).mockRejectedValueOnce(
      new api.ResponsibilityApiError('Provider description is invalid.', 400),
    );
    renderReview();
    const drawer = await chooseShared();

    // Act
    fireEvent.click(within(drawer).getByRole('button', { name: 'Confirm responsibility' }));

    // Assert
    expect(await within(drawer).findByRole('alert')).toHaveTextContent('Provider description is invalid.');
    expect(within(drawer).getByText('Customer reviews accounts.')).toBeVisible();

    // Act
    fireEvent.click(within(drawer).getByRole('button', { name: 'Back to edit' }));
    fireEvent.change(within(drawer).getByRole('textbox', { name: 'Provider for AC-1' }), { target: { value: 'Corrected provider' } });
    fireEvent.click(within(drawer).getByRole('button', { name: 'Review allocation' }));
    fireEvent.click(within(drawer).getByRole('checkbox'));
    fireEvent.click(within(drawer).getByRole('button', { name: 'Confirm responsibility' }));

    // Assert
    expect(await screen.findByText(/Selected allocation confirmed/)).toBeVisible();
    expect(screen.queryByText('Provider description is invalid.')).not.toBeInTheDocument();
  });

  it('fails closed for inconsistent subscription revisions', async () => {
    // Arrange
    vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(preview({
      items: [item('FutureState'), { ...item('PendingReview', 'AC-2'), sourceRevision: 'different-source' }],
    }));

    // Act
    renderReview();

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('inconsistent revisions');
    expect(screen.getByText('Unknown state: FutureState')).toBeVisible();
  });

  it('shows inaccessible previews as errors and supports an explicit retry', async () => {
    // Arrange
    vi.mocked(api.getCapabilityResponsibilities).mockRejectedValueOnce(
      new api.ResponsibilityApiError('System not accessible.', 404),
    ).mockResolvedValueOnce(preview());
    renderReview();
    expect(await screen.findByRole('alert')).toHaveTextContent('System not accessible.');

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry preview' }));

    // Assert
    expect(await screen.findByRole('button', { name: 'Open AC-1 responsibility' })).toBeEnabled();
  });

  it('aborts the old system read and never renders its late response under another system', async () => {
    // Arrange
    let resolveOld!: (value: CapabilityResponsibilityResponse) => void;
    vi.mocked(api.getCapabilityResponsibilities).mockImplementationOnce(() => new Promise(resolve => { resolveOld = resolve; }))
      .mockResolvedValueOnce(preview({
        systemId: 'system-b',
        items: [{ ...item(), sourceRevision: 'system-b-source' }],
      }));
    render(<MemoryRouter initialEntries={['/workspaces/organizations/org-a/systems/system-a/inheritance/subscriptions']}>
      <WorkspaceNavigationProvider workspace={{ kind: 'organization', tenantId: 'org-a' }}>
        <Link to="/workspaces/organizations/org-a/systems/system-b/inheritance/subscriptions">Other system</Link>
        <Routes>
          <Route path="/workspaces/organizations/:tenantId/systems/:id/inheritance/subscriptions" element={<CapabilityResponsibilityReview />} />
        </Routes>
      </WorkspaceNavigationProvider>
    </MemoryRouter>);

    // Act
    fireEvent.click(screen.getByRole('link', { name: 'Other system' }));
    const drawer = await openControl();
    fireEvent.click(within(drawer).getByText('Review provider scope & evidence'));
    fireEvent.click(within(drawer).getByText('Technical source details'));
    await within(drawer).findByText('system-b-source');
    await act(async () => { resolveOld(preview({ items: [{ ...item(), sourceRevision: 'stale-source' }] })); });

    // Assert
    expect(within(drawer).getByText('system-b-source')).toBeVisible();
    expect(screen.queryByText(/stale-source/)).not.toBeInTheDocument();
    expect(vi.mocked(api.getCapabilityResponsibilities).mock.calls[0]?.[1]?.aborted).toBe(true);
  });

  it('shows current and reviewed redacted provider snapshots in the selected-control drawer', async () => {
    // Arrange
    vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(preview({ items: [{
      ...item('PendingReview'),
      sourceSnapshotJson: responsibilitySnapshotJson({ Name: 'Current published capability', Controls: ['AC-1'] }),
      reviewedSourceSnapshotJson: responsibilitySnapshotJson({ Name: 'Persisted reviewed capability', Controls: ['AC-1'] }),
      reviewedSourceRevision: 'REVIEWED-OPAQUE-PIN',
    }] }));
    renderReview();

    // Act
    const drawer = await openControl();
    fireEvent.click(within(drawer).getByText('Review provider scope & evidence'));
    fireEvent.click(within(drawer).getByText('Technical source details'));

    // Assert
    expect(within(drawer).getByText(/"Name": "Persisted reviewed capability"/)).toBeVisible();
    expect(within(drawer).getByText(/Flankspeed · Current published capability/)).toBeVisible();
    expect(within(drawer).getByRole('region', { name: 'Provider contribution' })).toHaveTextContent('[redacted]');
  });

  it('withholds unavailable provider content while preserving review access', async () => {
    // Arrange
    vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(preview({ items: [{
      ...item('PendingReview'),
      sourceAvailable: false,
      sourceSnapshotJson: null,
      reviewedSourceSnapshotJson: responsibilitySnapshotJson({ Name: 'Previously reviewed public capability' }),
      reviewedSourceRevision: 'REVIEWED-OPAQUE-PIN',
    }] }));
    renderReview();

    // Act
    const drawer = await openControl();

    // Assert
    expect(within(drawer).getByText(/Provider source unavailable/)).toBeVisible();
    expect(within(drawer).getByText(/Flankspeed · Previously reviewed public capability/)).toBeVisible();
    fireEvent.click(within(drawer).getByRole('radio', { name: /Provider and my team/ }));
    expect(within(drawer).getByRole('button', { name: 'Review allocation' })).toBeDisabled();
  });

  it('blocks removed historical controls while allowing a currently mapped control to be reviewed', async () => {
    // Arrange
    const sourceSnapshotJson = responsibilitySnapshotJson({ Controls: ['AC-2'] });
    vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(preview({ items: [
      { ...item('PendingReview'), sourceSnapshotJson },
      { ...item('MissingAllocation', 'AC-2'), sourceSnapshotJson },
    ] }));
    renderReview();

    // Act / Assert
    const removed = await openControl();
    expect(within(removed).getByText(/AC-1 is no longer mapped/)).toBeVisible();
    fireEvent.click(within(removed).getByRole('radio', { name: /Provider and my team/ }));
    expect(within(removed).getByRole('button', { name: 'Review allocation' })).toBeDisabled();
    fireEvent.click(within(removed).getByRole('button', { name: 'Close dialog' }));
    const current = await openControl('AC-2');
    expect(within(current).getByRole('radio', { name: /Provider and my team/ })).toBeEnabled();
  });

  it('shows a snapshot error and disables confirmation for malformed selected-control data', async () => {
    // Arrange
    vi.mocked(api.getCapabilityResponsibilities).mockResolvedValue(preview({
      items: [{ ...item(), sourceSnapshotJson: '{}' }],
    }));
    renderReview();

    // Act
    const drawer = await openControl();

    // Assert
    expect(within(drawer).getByRole('alert')).toHaveTextContent(/snapshot.*malformed/i);
    fireEvent.click(within(drawer).getByRole('radio', { name: /Provider and my team/ }));
    expect(within(drawer).getByRole('button', { name: 'Review allocation' })).toBeDisabled();
    expect(api.confirmCapabilityResponsibilities).not.toHaveBeenCalled();
  });
});
