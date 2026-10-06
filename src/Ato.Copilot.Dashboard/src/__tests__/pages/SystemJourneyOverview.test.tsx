import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import SystemReadinessOverview from '../../features/systems/SystemReadinessOverview';
import * as readiness from '../../api/packageReadiness';
import * as overview from '../../api/systemOverview';
import { getMonitoringWorkspace } from '../../api/scopedMonitoring';
import { getConMonOverview } from '../../api/conmon';
import { overviewWorkspace, overviewWork, overviewRun } from '../fixtures/systemOverview';
import { getResponsibilityDraft } from '../../api/responsibilityDrafts';
import { appliedResponsibilityContext } from '../helpers/appliedResponsibilityContext';
import '../helpers/dialog';

vi.mock('../../api/packageReadiness', () => ({ getPackageReadinessWorkspace: vi.fn(), validatePackageReadiness: vi.fn() }));
vi.mock('../../api/systemOverview', async importOriginal => ({
  ...await importOriginal<typeof import('../../api/systemOverview')>(),
  getOverviewWork: vi.fn(), confirmOverviewPhase: vi.fn(), explainOverviewGroup: vi.fn(),
}));
vi.mock('../../api/scopedMonitoring', () => ({ getMonitoringWorkspace: vi.fn() }));
vi.mock('../../api/conmon', () => ({ getConMonOverview: vi.fn() }));
vi.mock('../../api/responsibilityDrafts', () => ({ getResponsibilityDraft: vi.fn() }));
function Location() { const location = useLocation(); return <output aria-label="Location">{location.pathname}{location.search}</output>; }
function mount(search = '') {
  return render(<MemoryRouter initialEntries={[`/systems/a${search}`]}>
    <SystemReadinessOverview systemId="a" systemName="Mission Alpha" currentPhase="Prepare" /><Location />
  </MemoryRouter>);
}
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(readiness.getPackageReadinessWorkspace).mockResolvedValue(overviewWorkspace());
  vi.mocked(overview.getOverviewWork).mockResolvedValue(overviewWork());
  vi.mocked(readiness.validatePackageReadiness).mockResolvedValue({
    ...overviewWorkspace(), run: overviewRun, checks: { items: [], totalCount: 0, limit: 50, offset: 0 },
  });
  vi.mocked(getConMonOverview).mockRejectedValue(new Error('ConMon unavailable'));
  vi.mocked(getResponsibilityDraft).mockImplementation(async (system, control, scopeId) => ({
    ...appliedResponsibilityContext(system, control), scopeId,
    scopes: [{ id: 'scope-a', name: 'Recorded provider scope', provider: 'Provider', reviewRequired: false }],
    sourceValues: { ...appliedResponsibilityContext(system, control).sourceValues,
      providerDuties: { value: 'Published provider backup duty', origin: 'From provider source', sourceIds: ['release-7'],
        explanation: 'Pinned release fact, not accepted coverage.', sourceHash: 'a'.repeat(64), userEdited: false } },
  }));
  vi.mocked(getMonitoringWorkspace).mockResolvedValue({
    canManageRules: false, canReviewImpacts: false, boundaries: [{ id: 'area', name: 'Mission API' }],
    rules: [{ id: 'rule', name: 'Review scope drift', boundaryDefinitionId: 'area', baselineReference: 'approved-design-7',
      ownerId: 'owner-a', signal: 'Alert', triggerCondition: '{"field":"Type","operator":"Equals","value":"Drift"}',
      cadenceMinutes: 60, severityOverride: 'Medium', isEnabled: true, version: 1, lastEvaluatedAt: null }],
    coverage: [{ assignmentId: 'assignment', boundaryId: 'area', resourceId: 'resource-a', providerComponentId: null,
      health: 'ScopeUnsupported', lastSuccessAt: null, error: 'Authorized collection is unavailable.' }],
    changes: [], evaluations: [], impacts: [{ id: 'impact', evaluationId: 'evaluation', controlId: 'AC-1', ownerId: 'owner-a',
      disposition: 'Pending', rationale: null, reviewedBy: null, reviewedAt: null, version: 1,
      affectedRecordsJson: '{"evidence":[{"id":"evidence","fileName":"Configuration export"}]}', narrativeProposalIdsJson: '[]' }],
  });
});
describe('RMF journey overview', () => {
  it('separates unconfirmed phase, browsed phase, findings and tasks', async () => {
    // Arrange
    mount();
    // Act
    await screen.findByText('Current RMF phase: Not confirmed');
    fireEvent.click(screen.getByRole('button', { name: 'View Assess phase' }));
    // Assert
    expect(screen.getByText('Viewing phase: Assess')).toBeVisible();
    expect(screen.getByText('Current RMF phase: Not confirmed')).toBeVisible();
    expect(await screen.findByText('4 blocking requirements')).toBeVisible();
    expect(screen.getByText('1 warning')).toBeVisible();
    expect(screen.getByText('5 returned findings')).toBeVisible();
    expect(screen.getByText('2 work groups')).toBeVisible();
    expect(overview.confirmOverviewPhase).not.toHaveBeenCalled();
    expect(readiness.validatePackageReadiness).not.toHaveBeenCalled();
  });
  it('shows explicitly recorded phase and its provenance without marking preceding steps complete', async () => {
    // Arrange
    const workspace = overviewWorkspace();
    workspace.rmf = { ...workspace.rmf, phase: 'Assess', confirmed: true, source: 'Recorded RMF transition',
      actor: 'reviewer', recordedAt: '2026-10-04T14:00:00Z' };
    vi.mocked(readiness.getPackageReadinessWorkspace).mockResolvedValue(workspace);
    // Act
    mount();
    // Assert
    expect(await screen.findByText('Current RMF phase: Assess')).toBeVisible();
    expect(screen.getByText(/Recorded RMF transition/)).toBeVisible();
    expect(screen.queryByText(/Prepare.*Complete/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'View Prepare phase' })).toBeEnabled();
  });
  it('uses supported prerequisite priority and retains underlying technical findings', async () => {
    // Arrange
    mount();
    await screen.findByText('2 work groups');
    // Act
    fireEvent.click(screen.getByText('Review AC-1 requirement responses'));
    // Assert
    expect(screen.getByText(/Rule-based priority/)).toBeVisible();
    expect(screen.getAllByText('Owner not provided').length).toBeGreaterThan(0);
    expect(screen.getByText('Alex Owner')).toBeVisible();
    expect(screen.getByText('Recorded requirement gap 0')).toBeVisible();
    expect(screen.getByRole('link', { name: 'Open Review AC-1 requirement responses' })).toHaveAttribute('href', expect.stringContaining('control=AC-1'));
    expect(screen.getByRole('link', { name: 'Open Review system design' })).toHaveAttribute('href', expect.stringContaining('/profile/SystemDesign'));
    fireEvent.click(within(screen.getByText('ac-1_smt.a.0').closest('details')!).getByText('Technical identifiers'));
    expect(screen.getByText('ac-1_smt.a.0')).toBeVisible();
  });
  it('filters personal work by the server assignment projection, not system roles', async () => {
    // Arrange
    mount();
    await screen.findByText('2 work groups');
    vi.mocked(overview.getOverviewWork).mockResolvedValue({ ...overviewWork(),
      groups: { ...overviewWork().groups, items: [], totalCount: 0 }, recommendedGroupId: null });
    // Act
    fireEvent.click(screen.getByRole('tab', { name: 'Assigned to me' }));
    // Assert
    expect(await screen.findByText('No actions are currently assigned to you.')).toBeVisible();
    expect(screen.getByText(/System-wide gaps may still remain/)).toBeVisible();
    expect(overview.getOverviewWork).toHaveBeenLastCalledWith('a', 'run-a', expect.objectContaining({ mine: true }), expect.any(AbortSignal));
    expect(screen.getByRole('button', { name: 'View all system work' })).toBeVisible();
  });
  it('preserves previous successful results after refresh fails', async () => {
    // Arrange
    mount();
    await screen.findByText('5 returned findings');
    vi.mocked(readiness.validatePackageReadiness).mockRejectedValue(new Error('Validation service disconnected'));
    // Act
    await waitFor(() => expect(screen.getByRole('button', { name: 'Check again' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Check again' }));
    // Assert
    await waitFor(() => expect(readiness.validatePackageReadiness).toHaveBeenCalledOnce());
    expect(await screen.findByText('Validation service disconnected')).toBeVisible();
    expect(screen.getByText('5 returned findings')).toBeVisible();
    expect(screen.getByText(/Previous successful check retained/)).toBeVisible();
    expect(screen.getByText('4 blocking requirements')).toBeVisible();
  });
  it('labels stale results independently of document existence or approval', async () => {
    // Arrange
    const workspace = overviewWorkspace();
    workspace.latestRun = { ...overviewRun, freshness: { ...overviewRun.freshness, state: 'Stale', reason: 'Source changed' } };
    workspace.lastSuccessfulRun = workspace.latestRun;
    vi.mocked(readiness.getPackageReadinessWorkspace).mockResolvedValue(workspace);
    // Act
    mount();
    // Assert
    expect(await screen.findByText('Stale — check again')).toBeVisible();
    const docs = screen.getByRole('region', { name: 'Your ATO package' });
    expect(within(docs).getAllByText('Gaps')[0]).toBeVisible();
    expect(within(docs).getByText('Draft')).toBeVisible();
    expect(within(docs).queryByText('Approved')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Preview working SSP' })).toHaveAttribute('href', '/systems/a/documents/preview');
  });
  it('does not infer eMASS submission or authorization from exported jobs', async () => {
    // Arrange
    const workspace = overviewWorkspace();
    workspace.progress[2] = { ...workspace.progress[2]!, state: 'Recorded', totalCount: 1, records: [{
      kind: 'package', id: 'package', status: 'Failed', recordedAt: '2026-10-05T12:00:00Z', purpose: 'InitialSubmission',
      sourceHash: 'a'.repeat(64), sourceRelationship: 'Historical', action: { ...overviewWorkspace().progress[2]!.action },
    }] };
    vi.mocked(readiness.getPackageReadinessWorkspace).mockResolvedValue(workspace);
    // Act
    mount();
    // Assert
    const milestones = await screen.findByRole('region', { name: 'Package milestones' });
    expect(milestones).toHaveTextContent('Failed');
    expect(milestones).toHaveTextContent('Not recorded');
    expect(milestones).not.toHaveTextContent('Authorized');
  });
  it('does not treat a receiving observation as an actual submission', async () => {
    // Arrange
    const workspace = overviewWorkspace();
    workspace.progress[3] = { ...workspace.progress[3]!, state: 'Recorded', totalCount: 1, records: [{
      kind: 'emass-exchange', id: 'observation', status: 'ReceiptRecorded', recordedAt: '2026-10-05T12:00:00Z',
      purpose: 'InitialSubmission', sourceHash: 'a'.repeat(64), sourceRelationship: 'Historical',
      action: workspace.progress[3]!.action,
    }] };
    vi.mocked(readiness.getPackageReadinessWorkspace).mockResolvedValue(workspace);
    // Act
    mount();
    // Assert
    expect(await screen.findByText('Receiving observations recorded; submission not established by this summary')).toBeVisible();
    expect(screen.getByRole('region', { name: 'Package milestones' })).toHaveTextContent('ReceiptRecorded');
  });
  it('uses pagination for large group sets rather than rendering thousands of findings', async () => {
    // Arrange
    vi.mocked(overview.getOverviewWork).mockResolvedValue({ ...overviewWork(), counts: { total: 2408, blocking: 2405, warnings: 3 },
      groups: { ...overviewWork().groups, totalCount: 60 } });
    // Act
    mount();
    // Assert
    expect(await screen.findByText('2408 returned findings')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Next work groups' })).toBeEnabled();
    expect(screen.getAllByText('Technical identifiers').length).toBeLessThanOrEqual(20);
    fireEvent.click(screen.getByRole('button', { name: 'Next work groups' }));
    await waitFor(() => expect(overview.getOverviewWork).toHaveBeenLastCalledWith('a', 'run-a',
      expect.objectContaining({ offset: 10 }), expect.any(AbortSignal)));
  });
  it('preserves browsed phase, owner filter and expanded groups in return navigation', async () => {
    // Arrange
    mount('?overview=readiness&phase=Implement&owner=all&expanded=ac1');
    // Act
    await screen.findByText('Recorded requirement gap 0');
    // Assert
    expect(screen.getByText('Viewing phase: Implement')).toBeVisible();
    expect(screen.getByRole('link', { name: 'Open Review AC-1 requirement responses' })).toHaveAttribute('href', expect.stringContaining('readinessReturn='));
    expect(screen.getByLabelText('Location')).toHaveTextContent('expanded=ac1');
  });
  it('keeps journey visible in monitoring and exposes real scope gaps and staged evidence impacts', async () => {
    // Arrange
    mount('?overview=monitoring');
    // Act
    await screen.findByText('ScopeUnsupported');
    // Assert
    expect(screen.getByText('Current RMF phase: Not confirmed')).toBeVisible();
    expect(screen.getByText('Authorized collection is unavailable.')).toBeVisible();
    expect(screen.getByText('approved-design-7')).toBeVisible();
    expect(screen.getAllByText('Owner: owner-a')[0]).toBeVisible();
    expect(screen.getByText(/Proposed follow-up does not replace the reviewed baseline/)).toBeVisible();
    expect(screen.getByRole('link', { name: 'Review evidence freshness' })).toHaveAttribute('href', expect.stringContaining('/evidence'));
  });
  it('does not expose phase mutation for read-only callers', async () => {
    // Arrange
    const workspace = overviewWorkspace();
    workspace.rmf.canConfirm = false;
    vi.mocked(readiness.getPackageReadinessWorkspace).mockResolvedValue(workspace);
    // Act
    mount();
    // Assert
    await screen.findByText('Current RMF phase: Not confirmed');
    expect(screen.queryByRole('button', { name: 'Confirm recorded phase' })).not.toBeInTheDocument();
    expect(screen.getByText(/An authorized system manager must confirm/)).toBeVisible();
  });
  it('presents AI explanation as a sourced proposal and does not save phase or accepted records', async () => {
    // Arrange
    vi.mocked(overview.explainOverviewGroup).mockResolvedValue({ systemId: 'a', runId: 'run-a', groupId: 'design',
      origin: 'AI proposed', sourceHash: 'a'.repeat(64), content: 'Review the saved design source first.',
      sources: [{ id: 'source', title: 'Recorded design gap', origin: 'From system records', version: 'version7',
        content: 'Saved finding', href: null }], questions: ['Which record needs approval?'] });
    mount();
    await screen.findByText('2 work groups');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Explain next action with AI' }));
    // Assert
    expect((await screen.findAllByText('Review the saved design source first.'))[0]).toBeVisible();
    expect(screen.getByText('AI proposed — review required')).toBeVisible();
    expect(screen.getByText('Which record needs approval?')).toBeVisible();
    expect(overview.confirmOverviewPhase).not.toHaveBeenCalled();
  });
  it('retains human corrections when AI refreshes and discloses the new suggestion separately', async () => {
    // Arrange
    vi.mocked(overview.explainOverviewGroup).mockResolvedValue({ systemId: 'a', runId: 'run-a', groupId: 'design',
      origin: 'AI proposed', sourceHash: 'a'.repeat(64), content: 'Original proposed action.', sources: [], questions: [] });
    mount();
    await screen.findByText('2 work groups');
    fireEvent.click(screen.getByRole('button', { name: 'Explain next action with AI' }));
    await screen.findByLabelText('Your working explanation');
    // Act
    fireEvent.change(screen.getByLabelText('Your working explanation'), { target: { value: 'My corrected explanation' } });
    vi.mocked(overview.explainOverviewGroup).mockResolvedValue({ systemId: 'a', runId: 'run-a', groupId: 'design',
      origin: 'AI proposed', sourceHash: 'b'.repeat(64), content: 'Refreshed action suggestion.', sources: [], questions: [] });
    fireEvent.click(screen.getByRole('button', { name: 'Refresh explanation' }));
    // Assert
    expect(await screen.findByText('Your corrections have not been replaced')).toBeVisible();
    expect(screen.getByLabelText('Your working explanation')).toHaveValue('My corrected explanation');
    expect(screen.getByText('Refreshed action suggestion.')).toBeVisible();
    expect(overview.confirmOverviewPhase).not.toHaveBeenCalled();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Use refreshed explanation' }));
    // Assert
    expect(screen.getByLabelText('Your working explanation')).toHaveValue('Refreshed action suggestion.');
    fireEvent.click(screen.getByRole('button', { name: 'Close AI help' }));
    expect(screen.queryByRole('region', { name: 'AI first-pass help' })).not.toBeInTheDocument();
  });
  it('carries selected provider duties directly from source and sends only the explicitly selected scope to AI', async () => {
    // Arrange
    vi.mocked(overview.explainOverviewGroup).mockResolvedValue({ systemId: 'a', runId: 'run-a', groupId: 'ac1',
      origin: 'AI proposed', sourceHash: 'a'.repeat(64), content: 'Review responsibility facts.', sources: [], questions: [] });
    mount('?expanded=ac1');
    await screen.findByText('Recorded requirement gap 0');
    fireEvent.click(within(screen.getByText('Recorded requirement gap 0').closest('details')!).getByRole('button', { name: 'Explain this group with AI' }));
    await screen.findByLabelText('Provider scope for the first pass');
    // Act
    await waitFor(() => expect(screen.getByLabelText('Provider scope for the first pass')).toBeEnabled());
    fireEvent.change(screen.getByLabelText('Provider scope for the first pass'), { target: { value: 'scope-a' } });
    // Assert
    expect(await screen.findByText('Published provider backup duty')).toBeVisible();
    expect(screen.getByText(/Pinned release fact, not accepted coverage/)).toBeVisible();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Refresh explanation' }));
    // Assert
    await waitFor(() => expect(overview.explainOverviewGroup).toHaveBeenLastCalledWith('a', 'run-a',
      { groupId: 'ac1', controlId: 'AC-1', scopeId: 'scope-a' }, expect.any(AbortSignal)));
  });
  it('requests reviewable requirement mappings and draft responses without changing accepted records', async () => {
    // Arrange
    vi.mocked(overview.explainOverviewGroup).mockResolvedValue({ systemId: 'a', runId: 'run-a', groupId: 'ac1',
      origin: 'AI proposed', sourceHash: 'a'.repeat(64), content: 'Proposed source-qualified response.', sources: [{
        id: 'catalog', title: 'Pinned source requirements', origin: 'From system records', version: 'rev5',
        content: 'ac-1_smt.a: actual source requirement', href: null,
      }], questions: ['Which evidence supports this response?'] });
    mount('?expanded=ac1');
    await screen.findByText('Recorded requirement gap 0');
    fireEvent.click(within(screen.getByText('Recorded requirement gap 0').closest('details')!).getByRole('button', { name: 'Explain this group with AI' }));
    await screen.findByLabelText('Your working explanation');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Map existing text with AI' }));
    // Assert
    await waitFor(() => expect(overview.explainOverviewGroup).toHaveBeenLastCalledWith('a', 'run-a',
      { groupId: 'ac1', controlId: 'AC-1', scopeId: null, mode: 'MapRequirements' }, expect.any(AbortSignal)));
    // Act
    await waitFor(() => expect(screen.getByRole('button', { name: 'Prepare draft responses with AI' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Prepare draft responses with AI' }));
    // Assert
    await waitFor(() => expect(overview.explainOverviewGroup).toHaveBeenLastCalledWith('a', 'run-a',
      { groupId: 'ac1', controlId: 'AC-1', scopeId: null, mode: 'DraftResponses' }, expect.any(AbortSignal)));
    expect(overview.confirmOverviewPhase).not.toHaveBeenCalled();
  });
  it('surfaces phase gate denial without discarding the confirmation rationale', async () => {
    // Arrange
    vi.mocked(overview.confirmOverviewPhase).mockRejectedValue(new Error('Recorded gate is not met'));
    mount();
    await screen.findByText('Current RMF phase: Not confirmed');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Confirm recorded phase' }));
    fireEvent.change(screen.getByLabelText('Basis for this recorded phase'), { target: { value: 'Keep the rationale' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirm phase' }));
    // Assert
    expect(await screen.findByText('Recorded gate is not met')).toBeVisible();
    expect(screen.getByLabelText('Basis for this recorded phase')).toHaveValue('Keep the rationale');
    expect(screen.getByText('Current RMF phase: Not confirmed')).toBeVisible();
  });
  it('preserves the server-retained previous successful check when the latest persisted evaluation failed', async () => {
    // Arrange
    const workspace = overviewWorkspace();
    workspace.latestRun = { ...overviewRun, id: 'failed-run', outcome: 'Failed',
      failure: { code: 'FAILED', message: 'Latest evaluation could not complete' } };
    vi.mocked(readiness.getPackageReadinessWorkspace).mockResolvedValue(workspace);
    // Act
    mount();
    // Assert
    expect(await screen.findByText('Latest evaluation could not complete')).toBeVisible();
    expect(await screen.findByText('5 returned findings')).toBeVisible();
    expect(overview.getOverviewWork).toHaveBeenCalledWith('a', 'run-a', expect.any(Object), expect.any(AbortSignal));
    expect(screen.getByText(/Previous successful check retained/)).toBeVisible();
  });
  it('does not reinterpret legacy check counts as individual finding totals', async () => {
    // Arrange
    vi.mocked(overview.getOverviewWork).mockResolvedValue({ ...overviewWork(), findingsAvailable: false,
      counts: { total: 0, blocking: 0, warnings: 0 }, groups: { ...overviewWork().groups, items: [], totalCount: 0 }, recommendedGroupId: null });
    // Act
    mount();
    // Assert
    expect(await screen.findByText(/Individual findings were not retained/)).toBeVisible();
    expect(screen.queryByText('0 returned findings')).not.toBeInTheDocument();
  });
  it('shows an unavailable evaluator without claiming an empty or ready package', async () => {
    // Arrange
    vi.mocked(readiness.getPackageReadinessWorkspace).mockRejectedValue(new Error('Workspace read denied'));
    // Act
    mount();
    // Assert
    expect(await screen.findByText('Workspace read denied')).toBeVisible();
    expect(screen.queryByText('0 blocking requirements')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Check again' })).toBeDisabled();
  });
  it('requires an explicit confirmation action and keeps source-backed current phase separate from browsing', async () => {
    // Arrange
    vi.mocked(overview.confirmOverviewPhase).mockResolvedValue({ ...overviewWorkspace().rmf, confirmed: true,
      source: 'RmfPhase.Confirmed', actor: 'owner-a', recordedAt: '2026-10-05T12:00:00Z' });
    mount();
    await screen.findByText('Current RMF phase: Not confirmed');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Confirm recorded phase' }));
    fireEvent.change(screen.getByLabelText('Basis for this recorded phase'), { target: { value: 'Reviewed preparation state' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirm phase' }));
    // Assert
    await waitFor(() => expect(overview.confirmOverviewPhase).toHaveBeenCalledWith('a', {
      phase: 'Prepare', expectedPhase: 'Prepare', notes: 'Reviewed preparation state',
    }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(readiness.validatePackageReadiness).not.toHaveBeenCalled();
  });
  it('paginates individual findings inside a group without losing other groups', async () => {
    // Arrange
    const data = overviewWork();
    data.counts = { total: 25, blocking: 24, warnings: 1 };
    data.groups.items[1] = { ...data.groups.items[1]!, total: 24, blocking: 23, warnings: 1,
      findings: { ...data.groups.items[1]!.findings, totalCount: 24 } };
    vi.mocked(overview.getOverviewWork).mockResolvedValue(data);
    mount('?expanded=ac1');
    await screen.findByText('Recorded requirement gap 0');
    vi.mocked(overview.getOverviewWork).mockResolvedValue({ ...data, groups: {
      ...data.groups, totalCount: 1, items: [{ ...data.groups.items[1]!, findings: {
        totalCount: 24, limit: 20, offset: 20, items: [{ ...data.groups.items[1]!.findings.items[0]!,
          id: 'last-finding', description: 'Final requirement gap', recordId: 'ac-1_smt.final' }],
      } }],
    } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Next findings' }));
    // Assert
    expect(await screen.findByText('Final requirement gap')).toBeVisible();
    expect(screen.getByText('Review system design')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Next findings' })).toBeDisabled();
  });
  it('keeps explanations and phase records unchanged when AI is unavailable', async () => {
    // Arrange
    vi.mocked(overview.explainOverviewGroup).mockRejectedValue(new Error('AI connection unavailable'));
    mount();
    await screen.findByText('2 work groups');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Explain next action with AI' }));
    // Assert
    expect(await screen.findByText('AI connection unavailable')).toBeVisible();
    expect(screen.queryByText('AI proposed — review required')).not.toBeInTheDocument();
    expect(overview.confirmOverviewPhase).not.toHaveBeenCalled();
  });
  it('shows monitoring plan, observations, evaluation versions and reviewed follow-up without mutating the baseline', async () => {
    // Arrange
    vi.mocked(getConMonOverview).mockResolvedValue({
      systemId: 'a', systemName: 'Mission Alpha', currentPhase: 'Monitor',
      plan: { planId: 'plan', assessmentFrequency: 'Quarterly', annualReviewDate: '2027-01-01',
        reportDistribution: ['ISSM'], significantChangeTriggers: ['Boundary change'], createdAt: '2026-10-01', modifiedAt: null },
      status: { currentComplianceScore: 0, authorizedBaselineScore: null, scoreDelta: null, openFindings: 2,
        resolvedFindings: 0, openPoamItems: 1, overduePoamItems: 1, monitoringEnabled: false,
        driftAlertCount: 0, autoRemediationRuleCount: 0, lastMonitoringCheck: '2026-10-04T10:00:00Z' },
      expiration: { hasActiveAuthorization: false, decisionType: null, decisionDate: null, expirationDate: null,
        daysUntilExpiration: null, alertLevel: 'None', alertMessage: 'No active decision recorded.', isExpired: false },
      reauthorization: { isTriggered: false, triggers: [], unreviewedChangeCount: 1 },
      agreementAlerts: [], significantChanges: [], reports: [],
    });
    const scoped = await getMonitoringWorkspace('a');
    vi.mocked(getMonitoringWorkspace).mockResolvedValue({ ...scoped, changes: [{
      sourceId: 'observed-change', kind: 'ProviderSource', title: 'Recorded source revision changed',
      controlId: 'AC-1', changeDetails: '{"before":"version7","after":"version8"}', attribution: 'Observed source record', observedAt: '2026-10-05T12:00:00Z',
    }], evaluations: [{ id: 'evaluation', ruleId: 'rule', ruleVersion: 1, outcome: 'Stale', evaluatedAt: '2026-10-04T10:00:00Z',
      ruleSnapshotJson: '{}', inputSnapshotJson: '{}' }],
    impacts: [{ ...scoped.impacts[0]!, reviewedBy: 'reviewer-a', reviewedAt: '2026-10-05T12:00:00Z', rationale: 'Review current configuration evidence' }] });
    // Act
    mount('?overview=monitoring');
    // Assert
    expect(await screen.findByText(/Plan cadence: Quarterly/)).toBeVisible();
    expect(screen.getByText('Recorded source revision changed')).toBeVisible();
    expect(screen.getByText('Review current configuration evidence')).toBeVisible();
    expect(screen.getByText(/Review: reviewer-a/)).toBeVisible();
    expect(readiness.validatePackageReadiness).not.toHaveBeenCalled();
    expect(overview.confirmOverviewPhase).not.toHaveBeenCalled();
  });
  it('updates only the confirmed readiness snapshot after a successful server recheck', async () => {
    // Arrange
    mount();
    await screen.findByText('5 returned findings');
    const newer = { ...overviewRun, id: 'run-b', evaluatedAt: '2026-10-05T15:00:00Z' };
    vi.mocked(readiness.validatePackageReadiness).mockResolvedValue({ ...overviewWorkspace(), run: newer,
      checks: { items: [], totalCount: 0, limit: 50, offset: 0 } });
    vi.mocked(readiness.getPackageReadinessWorkspace).mockResolvedValue({ ...overviewWorkspace(), latestRun: newer, lastSuccessfulRun: newer });
    vi.mocked(overview.getOverviewWork).mockResolvedValue({ ...overviewWork(), runId: 'run-b' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Check again' }));
    // Assert
    await waitFor(() => expect(overview.getOverviewWork).toHaveBeenLastCalledWith('a', 'run-b', expect.any(Object), expect.any(AbortSignal)));
    expect(screen.getByText('Current saved result')).toBeVisible();
    expect(overview.confirmOverviewPhase).not.toHaveBeenCalled();
  });
  it('preserves old findings when the server records a failed evaluation and exposes reload', async () => {
    // Arrange
    mount();
    await screen.findByText('5 returned findings');
    const failed = { ...overviewRun, id: 'failed-new', outcome: 'Failed' as const,
      failure: { code: 'FAILED', message: 'Current evaluation unavailable' } };
    vi.mocked(readiness.validatePackageReadiness).mockResolvedValue({ ...overviewWorkspace(), run: failed,
      checks: { items: [], totalCount: 0, limit: 50, offset: 0 } });
    vi.mocked(readiness.getPackageReadinessWorkspace).mockResolvedValue({ ...overviewWorkspace(), latestRun: failed });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Check again' }));
    // Assert
    expect(await screen.findByText('Current evaluation unavailable')).toBeVisible();
    expect(screen.getByText('5 returned findings')).toBeVisible();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Reload saved readiness' }));
    // Assert
    await waitFor(() => expect(readiness.getPackageReadinessWorkspace).toHaveBeenCalledTimes(3));
  });
  it('retains a group page on failed finding pagination and allows reviewing the prior findings', async () => {
    // Arrange
    const data = overviewWork();
    data.counts = { total: 25, blocking: 24, warnings: 1 };
    data.groups.items[1] = { ...data.groups.items[1]!, total: 24, blocking: 23, warnings: 1,
      findings: { ...data.groups.items[1]!.findings, totalCount: 24 } };
    vi.mocked(overview.getOverviewWork).mockResolvedValue(data);
    mount('?expanded=ac1');
    await screen.findByText('Recorded requirement gap 0');
    vi.mocked(overview.getOverviewWork).mockRejectedValue(new Error('Finding page unavailable'));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Next findings' }));
    // Assert
    expect(await screen.findByText('Finding page unavailable')).toBeVisible();
    expect(screen.getByText('Recorded requirement gap 0')).toBeVisible();
  });
  it('supports phase and ownership keyboard navigation without writing phase records', async () => {
    // Arrange
    mount();
    await screen.findByText('5 returned findings');
    // Act
    const prepare = screen.getByRole('button', { name: 'View Prepare phase' });
    prepare.focus();
    fireEvent.keyDown(prepare, { key: 'End' });
    // Assert
    expect(screen.getByRole('button', { name: 'View Monitor phase' })).toHaveFocus();
    expect(screen.getByText('Viewing phase: Monitor')).toBeVisible();
    // Act
    fireEvent.keyDown(screen.getByRole('button', { name: 'View Monitor phase' }), { key: 'Home' });
    fireEvent.keyDown(screen.getByRole('button', { name: 'View Prepare phase' }), { key: 'ArrowRight' });
    expect(screen.getByRole('button', { name: 'View Categorize phase' })).toHaveFocus();
    fireEvent.keyDown(screen.getByRole('button', { name: 'View Categorize phase' }), { key: 'ArrowLeft' });
    const all = screen.getByRole('tab', { name: 'All system work' });
    all.focus(); fireEvent.keyDown(all, { key: 'ArrowRight' });
    // Assert
    expect(screen.getByRole('tab', { name: 'Assigned to me' })).toHaveFocus();
    expect(overview.confirmOverviewPhase).not.toHaveBeenCalled();
    await waitFor(() => expect(overview.getOverviewWork).toHaveBeenLastCalledWith('a', 'run-a',
      expect.objectContaining({ mine: true }), expect.any(AbortSignal)));
  });
  it('does not fabricate a workflow action when the server cannot provide one', async () => {
    // Arrange
    const data = overviewWork();
    data.groups.items[1]!.action = { canView: false, canEdit: false, path: null, label: null, reason: 'Source workflow unavailable' };
    vi.mocked(overview.getOverviewWork).mockResolvedValue(data);
    // Act
    mount('?expanded=ac1');
    // Assert
    expect(await screen.findByText('Source workflow unavailable')).toBeVisible();
    expect(screen.queryByRole('link', { name: 'Open Review AC-1 requirement responses' })).not.toBeInTheDocument();
  });
});
