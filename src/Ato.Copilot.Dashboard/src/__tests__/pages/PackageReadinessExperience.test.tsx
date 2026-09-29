import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import '../helpers/dialog';
import PackageReadinessExperience from '../../features/systems/PackageReadinessExperience';
import * as api from '../../api/packageReadiness';
vi.mock('../../api/packageReadiness', () => ({
  getPackageReadinessWorkspace: vi.fn(), getPackageReadinessRun: vi.fn(), getPackageReadinessCheck: vi.fn(),
  validatePackageReadiness: vi.fn(), listPackageReadinessRuns: vi.fn(), generatePackageFromReadiness: vi.fn(),
}));
vi.mock('../../components/RetainedPackageContext', () => ({ default: () => <p>Select retained source versions</p> }));
const action: api.PackageReadinessAction = { canView: true, canEdit: false, path: 'security-capabilities/inventory', label: 'Open', reason: 'Read-only access' };
const check: api.PackageReadinessCheck = {
  id: 'inventory', ruleId: 'inventory-v1', title: 'Complete system inventory', outcome: 'Blocking', category: 'inventory',
  required: true, applicability: 'Applicable', why: 'The package needs a traceable inventory.', missingSource: 'No inventory items recorded',
  sources: [], nextSteps: ['Record applicable inventory items.', 'Recheck readiness.'], expectedRole: 'SystemOwner',
  recordedOwner: null, action,
};
const run: api.PackageReadinessRun = {
  id: 'run-a', outcome: 'Blocked', startedAt: '2026-09-29T12:00:00Z', evaluatedAt: '2026-09-29T12:00:01Z', evaluatedBy: 'reviewer',
  sourceHash: 'a'.repeat(64), sourceHashAfter: 'a'.repeat(64), ruleVersion: 'v1',
  counts: { total: 1, blocking: 1, followUp: 0, passed: 0, notApplicable: 0, unavailable: 0, requiredUnavailable: 0 },
  recommendedCheckId: 'inventory', failure: null,
  freshness: { state: 'Current', checkedAt: '2026-09-29T12:00:02Z', currentSourceHash: 'a'.repeat(64), reason: null },
};
const workspace: api.PackageReadinessWorkspace = {
  systemId: 'a', purpose: 'Legacy', selectionHash: 'b'.repeat(64), retainedContext: null,
  source: { state: 'Available', hash: 'a'.repeat(64), ruleVersion: 'v1', reason: null }, latestRun: run,
  permissions: { canValidate: true, canGenerate: false, validateReason: null, generateReason: 'Blocking checks remain.' },
  progress: [
    { id: 'prepare', state: 'Blocked', description: 'Document remaining sources', records: [], totalCount: 0, action },
    { id: 'validate', state: 'Blocked', description: 'One blocker', records: [], totalCount: 1, action },
    { id: 'export', state: 'Recorded', description: 'Historical export retained', records: [], totalCount: 1, action },
    { id: 'emass', state: 'NotRecorded', description: 'No receiving observation recorded; no live eMASS submission connector.', records: [], totalCount: 0, action },
    { id: 'decision', state: 'NotRecorded', description: 'No decision recorded', records: [], totalCount: 0, action },
  ], documents: [{ kind: 'ssp', title: 'SSP', presence: 'Present', status: 'Draft', reviewState: 'Not approved',
    sourceState: 'Working records', validationOutcome: 'Blocking', recordCount: 1, records: [], action }],
  rmf: { phase: 'Prepare', transitions: [], totalCount: 0 },
};
function mount(search = '') {
  return render(<MemoryRouter initialEntries={[`/systems/a/documents${search}`]}>
    <Routes><Route path="/systems/:id/documents" element={<PackageReadinessExperience systemId="a" />} /></Routes>
  </MemoryRouter>);
}
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getPackageReadinessWorkspace).mockResolvedValue(workspace);
  vi.mocked(api.getPackageReadinessRun).mockResolvedValue({ ...workspace, run, checks: { items: [check], totalCount: 1, limit: 50, offset: 0 } });
  vi.mocked(api.getPackageReadinessCheck).mockResolvedValue({ ...workspace, runId: run.id, check });
  vi.mocked(api.listPackageReadinessRuns).mockResolvedValue({ ...workspace, items: [run], totalCount: 1, limit: 20, offset: 0 });
});
describe('Authoritative package readiness', () => {
  it('preserves Legacy, shows actual blockers and keeps export/submission/decision separate', async () => {
    // Arrange / Act
    mount();
    // Assert
    expect(await screen.findByRole('heading', { name: 'Your path to ATO submission' })).toBeVisible();
    await screen.findByText('Package needs work');
    expect(api.getPackageReadinessWorkspace).toHaveBeenCalledWith('a', { purpose: 'Legacy', retainedContext: null }, expect.any(AbortSignal));
    expect(screen.getByText('Existing authorization package (legacy validation)')).toBeVisible();
    expect(screen.getByRole('tab', { name: 'Blocking checks (1)' })).toBeVisible();
    expect(screen.getByText('Historical export retained')).toBeVisible();
    expect(screen.getByText(/No receiving observation recorded/)).toBeVisible();
    expect(screen.getByText('No decision recorded')).toBeVisible();
    expect(api.validatePackageReadiness).not.toHaveBeenCalled();
  });
  it('opens a direct-linked check drawer with unknown ownership and a view-only source action', async () => {
    // Arrange / Act
    mount('?purpose=Legacy&run=run-a&check=inventory');
    // Assert
    const drawer = await screen.findByRole('dialog', { name: 'Complete system inventory' });
    expect(drawer).toHaveClass('ml-auto');
    expect(screen.getByText('Responsible person: Not recorded')).toBeVisible();
    expect(screen.getAllByRole('link', { name: 'View source record' })[0]).toHaveAttribute('href', expect.stringContaining('readinessReturn='));
    expect(screen.queryByRole('link', { name: 'Edit source record' })).not.toBeInTheDocument();
  });
  it('does not claim readiness from a document or from never having run validation', async () => {
    // Arrange
    vi.mocked(api.getPackageReadinessWorkspace).mockResolvedValue({ ...workspace, latestRun: null });
    // Act
    mount();
    // Assert
    expect(await screen.findByText('Readiness not checked')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Check readiness' })).toBeVisible();
    expect(screen.queryByText('Package ready for export')).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: 'Blocking checks (0)' })).not.toBeInTheDocument();
  });
  it('shows stale only after the evaluated snapshot changes', async () => {
    // Arrange
    vi.mocked(api.getPackageReadinessWorkspace).mockResolvedValue({ ...workspace, latestRun: {
      ...run, freshness: { ...run.freshness, state: 'Stale', reason: 'Inventory changed' },
    } });
    vi.mocked(api.getPackageReadinessRun).mockResolvedValue({ ...workspace, run: { ...run,
      freshness: { ...run.freshness, state: 'Stale', reason: 'Inventory changed' } },
      checks: { items: [check], totalCount: 1, limit: 50, offset: 0 } });
    // Act
    mount();
    // Assert
    expect(await screen.findByText('Readiness result is out of date')).toBeVisible();
    expect(screen.getByText('Inventory changed')).toBeVisible();
    expect(screen.queryByText('Package ready for export')).not.toBeInTheDocument();
  });
  it('keeps unavailable reads distinct from an empty or passing checklist', async () => {
    // Arrange
    vi.mocked(api.getPackageReadinessWorkspace).mockRejectedValue(new Error('Access denied'));
    // Act
    mount();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Access denied');
    expect(screen.queryByText('Package ready for export')).not.toBeInTheDocument();
    expect(screen.queryByRole('tab')).not.toBeInTheDocument();
  });
  it('shows an unavailable source fingerprint explicitly even before a first run', async () => {
    // Arrange
    vi.mocked(api.getPackageReadinessWorkspace).mockResolvedValue({ ...workspace, latestRun: null,
      source: { state: 'Unavailable', hash: null, ruleVersion: 'v1', reason: 'The source identity could not be verified.' } });
    // Act
    mount();
    // Assert
    expect(await screen.findByText('Readiness unavailable')).toBeVisible();
    expect(screen.getByText('The source identity could not be verified.')).toBeVisible();
    expect(screen.queryByText('Readiness not checked')).not.toBeInTheDocument();
  });
  it('keeps a denied validation action disabled without inferring access from the role label', async () => {
    // Arrange
    vi.mocked(api.getPackageReadinessWorkspace).mockResolvedValue({ ...workspace,
      permissions: { ...workspace.permissions, canValidate: false, validateReason: 'Validation denied' } });
    // Act
    mount();
    // Assert
    await screen.findByText('Package needs work');
    expect(screen.getByRole('button', { name: 'Recheck readiness' })).toBeDisabled();
    expect(screen.getByText('Validation denied')).toBeVisible();
  });
  it('uses the confirmed selected purpose for validation without generating a package', async () => {
    // Arrange
    vi.mocked(api.validatePackageReadiness).mockResolvedValue({ ...workspace, run,
      checks: { items: [check], totalCount: 1, limit: 50, offset: 0 } });
    // Act
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Recheck readiness' }));
    // Assert
    await waitFor(() => expect(api.validatePackageReadiness).toHaveBeenCalledWith('a',
      { purpose: 'Legacy', retainedContext: null }, expect.any(AbortSignal)));
    expect(api.generatePackageFromReadiness).not.toHaveBeenCalled();
  });
  it('does not restore a ready claim from an old run after a failed revalidation request', async () => {
    // Arrange
    vi.mocked(api.getPackageReadinessWorkspace).mockResolvedValue({ ...workspace, latestRun: { ...run, outcome: 'Ready' } });
    vi.mocked(api.getPackageReadinessRun).mockResolvedValue({ ...workspace, run: { ...run, outcome: 'Ready' },
      checks: { items: [check], totalCount: 1, limit: 50, offset: 0 } });
    vi.mocked(api.validatePackageReadiness).mockRejectedValue(new Error('Evaluation service failed'));
    mount();
    await screen.findByText('Package ready for export');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Recheck readiness' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Evaluation service failed');
    await waitFor(() => expect(api.getPackageReadinessWorkspace).toHaveBeenCalledTimes(2));
    expect(screen.queryByText('Package ready for export')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Prepare validated export' })).toBeDisabled();
  });
  it('clears the old purpose while its cancelled response is still pending', async () => {
    // Arrange
    let finish!: (data: api.PackageReadinessWorkspace) => void;
    vi.mocked(api.getPackageReadinessWorkspace).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }))
      .mockResolvedValueOnce({ ...workspace, purpose: 'InitialSubmission', latestRun: null });
    mount();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Change package purpose' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Package purpose' }), { target: { value: 'InitialSubmission' } });
    fireEvent.click(screen.getByRole('button', { name: 'Use selected purpose' }));
    await screen.findByText('Readiness not checked');
    await act(async () => finish(workspace));
    // Assert
    expect(screen.queryByText('Package needs work')).not.toBeInTheDocument();
    expect(screen.getByText('Initial ATO submission')).toBeVisible();
    expect(api.getPackageReadinessWorkspace).toHaveBeenLastCalledWith('a',
      { purpose: 'InitialSubmission', retainedContext: null }, expect.any(AbortSignal));
  });
});
