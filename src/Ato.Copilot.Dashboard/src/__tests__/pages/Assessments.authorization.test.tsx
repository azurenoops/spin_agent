import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import Assessments from '../../pages/Assessments';
import { getAssessmentDetail, getAssessments, runAssessment } from '../../api/assessments';
import { finalizeSap, generateSap, getLatestSap, type SapResponse } from '../../api/sap';
import { createSar } from '../../api/sar';
import { useWorkspaceSession, type WorkspaceSession } from '../../features/workspaces/WorkspaceBoundary';
import type { SystemWorkspacePermissions } from '../../features/workspaces/types';
import { historicalAssessment, readiness, systemDetail, systemId } from '../fixtures/assessmentEnvironment';
import { invokeClick, requireElement, workspaceSession } from '../helpers/domainPermissions';

vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: vi.fn() }));
vi.mock('../../components/layout/SystemLayout', () => ({
  useSystemContext: () => ({ detail: systemDetail }),
}));
vi.mock('../../hooks/useAssessmentReadiness', () => ({
  useAssessmentReadiness: () => ({ ...readiness(true), result: readiness(true), refresh: vi.fn(), block: vi.fn() }),
}));
vi.mock('../../api/assessments', () => ({
  getAssessments: vi.fn(), runAssessment: vi.fn(), getAssessmentDetail: vi.fn(),
}));
vi.mock('../../api/sap', () => ({
  getLatestSap: vi.fn(), generateSap: vi.fn(), finalizeSap: vi.fn(),
}));
vi.mock('../../api/sar', () => ({ getLatestSar: vi.fn().mockResolvedValue(null), createSar: vi.fn() }));
vi.mock('../../api/components', () => ({ getAssessmentComponentRisks: vi.fn().mockResolvedValue(null) }));
vi.mock('../../components/remediation/CreateRemediationTaskModal', () => ({ default: () => <div>Task dialog</div> }));
vi.mock('../../components/AddDeviationDialog', () => ({ default: () => <div>Deviation dialog</div> }));

function session(permissions: Partial<SystemWorkspacePermissions> = {}, roles = ['MissionOwner']): WorkspaceSession {
  return workspaceSession(systemId, permissions, roles);
}
const canonicalPath = `/workspaces/org/tenant-a/systems/${systemId}/assessments`;
const page = (path = canonicalPath) => <MemoryRouter initialEntries={[path]}><Assessments /></MemoryRouter>;
const sap: SapResponse = {
  sapId: 'sap-a', systemId, title: 'Plan', status: 'Draft', format: 'markdown', baselineLevel: 'Moderate',
  totalControls: 1, customerControls: 1, inheritedControls: 0, sharedControls: 0,
  stigBenchmarkCount: 0, controlsWithObjectives: 1, evidenceGaps: 0, familySummaries: [],
  generatedAt: '2026-01-01T00:00:00Z', warnings: [],
};

async function openFinding() {
  fireEvent.click((await screen.findAllByRole('button', { name: 'View' })).at(-1)!);
  fireEvent.click(await screen.findByRole('button', { name: 'AC (1)' }));
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(useWorkspaceSession).mockReturnValue(session());
  vi.mocked(getAssessments).mockResolvedValue([historicalAssessment]);
  vi.mocked(getLatestSap).mockResolvedValue(sap);
  vi.mocked(runAssessment).mockResolvedValue({ assessmentId: 'run-a', systemId, status: 'Completed' });
  vi.mocked(getAssessmentDetail).mockResolvedValue({
    ...historicalAssessment, notAssessedControls: 0, completedAt: historicalAssessment.assessedAt,
    executiveSummary: null, criticalCount: 0, highCount: 1, mediumCount: 0, lowCount: 0, familyResults: [],
    findings: [{
      findingId: 'finding-a', controlId: 'AC-1', controlFamily: 'AC', title: 'Synthetic finding',
      description: 'Test finding', severity: 'High', status: 'Open', resourceType: null, resourceId: null,
      remediationGuidance: null, discoveredAt: historicalAssessment.assessedAt, deviationId: null, deviationType: null,
    }],
  });
});
afterEach(() => { cleanup(); localStorage.clear(); });

describe('Assessment mutation authorization (#1017)', () => {
  it.each(['AO', 'ISSM'])('ignores a MissionOwner forged %s browser preference', async role => {
    // Arrange
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({ role }));
    render(page());
    await screen.findByRole('button', { name: 'Finalize SAP' });

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Run Assessment' }));

    // Assert
    expect(screen.getAllByRole('button', { name: 'Run Assessment' })[0]).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Generate SAP' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Generate SAR' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Finalize SAP' })).toBeDisabled();
    expect(runAssessment).not.toHaveBeenCalled();
  });

  it('permits an explicitly granted assessment run with multiple server roles', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canRunAssessments: true }, ['MissionOwner', 'ISSM']));
    render(page());
    await screen.findByRole('button', { name: 'Finalize SAP' });

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Run Assessment' }));
    await act(async () => { fireEvent.click(requireElement(screen.getAllByRole('button', { name: 'Run Assessment' })[1])); });

    // Assert
    expect(runAssessment).toHaveBeenCalledWith(systemId);
  });

  it('does not infer SAP or SAR authority from broad granted permissions', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({
      canManageSystem: true, canRunAssessments: true, canDecideAuthorization: true,
    }, ['ISSM', 'AuthorizingOfficial']));
    render(page());
    await screen.findByRole('button', { name: 'Finalize SAP' });

    // Act
    await invokeClick(screen.getByRole('button', { name: 'Finalize SAP' }));

    // Assert
    expect(screen.getByRole('button', { name: 'Generate SAP' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Generate SAR' })).toBeDisabled();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission|not authorized/i);
    expect(finalizeSap).not.toHaveBeenCalled();
    expect(generateSap).not.toHaveBeenCalled();
    expect(createSar).not.toHaveBeenCalled();
  });

  it.each([null, session(), session({ canRunAssessments: false })])('fails closed with absent or denied canonical permission', async access => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(access);
    render(page());
    await screen.findByRole('button', { name: 'Finalize SAP' });

    // Act
    await invokeClick(screen.getByRole('button', { name: 'Run Assessment' }));

    // Assert
    expect(screen.getAllByRole('button', { name: 'Run Assessment' })[0]).toBeDisabled();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission|not authorized/i);
    expect(runAssessment).not.toHaveBeenCalled();
  });

  it('rechecks permission in an already open run dialog after revocation', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canRunAssessments: true }));
    const view = render(page());
    await screen.findByRole('button', { name: 'Finalize SAP' });
    fireEvent.click(screen.getByRole('button', { name: 'Run Assessment' }));

    // Act
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canRunAssessments: false }));
    view.rerender(page());
    const submit = requireElement(screen.getAllByRole('button', { name: 'Run Assessment' })[1]);
    await invokeClick(submit);

    // Assert
    expect(submit).toBeDisabled();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission|not authorized/i);
    expect(runAssessment).not.toHaveBeenCalled();
  });

  it('preserves deliberately unrestricted legacy actions without a workspace session', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(null);
    vi.mocked(finalizeSap).mockResolvedValue({ ...sap, status: 'Finalized' });
    render(page(`/systems/${systemId}/assessments`));
    await screen.findByRole('button', { name: 'Finalize SAP' });

    // Act
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Finalize SAP' })); });

    // Assert
    expect(screen.getByRole('button', { name: 'Generate SAP' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Generate SAR' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Run Assessment' })).toBeEnabled();
    expect(finalizeSap).toHaveBeenCalledWith(systemId, 'sap-a');
  });

  it('rejects finding task and deviation entry handlers for a MissionOwner', async () => {
    // Arrange
    render(page());
    await openFinding();

    // Act
    await invokeClick(screen.getByRole('button', { name: '+ Create Task' }));
    await invokeClick(screen.getByRole('button', { name: '+ Create Deviation' }));

    // Assert
    expect(screen.getByRole('button', { name: '+ Create Task' })).toBeDisabled();
    expect(screen.getByRole('button', { name: '+ Create Deviation' })).toBeDisabled();
    expect(screen.queryByText('Task dialog')).not.toBeInTheDocument();
    expect(screen.queryByText('Deviation dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission/i);
  });

  it('unmounts an open shared task dialog on revocation and does not reopen it after regrant', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageRemediation: true }));
    const view = render(page());
    await openFinding();
    fireEvent.click(screen.getByRole('button', { name: '+ Create Task' }));
    expect(screen.getByText('Task dialog')).toBeInTheDocument();

    // Act
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageRemediation: false }));
    view.rerender(page());

    // Assert
    expect(screen.queryByText('Task dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission changed/i);

    // Act
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageRemediation: true }));
    view.rerender(page());

    // Assert
    expect(screen.queryByText('Task dialog')).not.toBeInTheDocument();
  });

  it('does not infer deviation-request permission from broader remediation or authorization flags', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(session({
      canManageRemediation: true, canManageSystem: true, canDecideAuthorization: true,
    }));
    render(page());
    await openFinding();

    // Act
    await invokeClick(screen.getByRole('button', { name: '+ Create Deviation' }));

    // Assert
    expect(screen.getByRole('button', { name: '+ Create Deviation' })).toBeDisabled();
    expect(screen.queryByText('Deviation dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission/i);
  });

  it.each(['SAP', 'SAR'])('rechecks a legacy-opened %s dialog when workspace permissions appear', async document => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(null);
    const legacy = `/systems/${systemId}/assessments`;
    const view = render(page(legacy));
    await screen.findByRole('button', { name: 'Finalize SAP' });
    fireEvent.click(screen.getByRole('button', { name: `Generate ${document}` }));

    // Act
    vi.mocked(useWorkspaceSession).mockReturnValue(session({ canManageSystem: true, canRunAssessments: true }));
    view.rerender(page(legacy));
    const submit = requireElement(screen.getAllByRole('button', { name: `Generate ${document}` })[1]);
    await invokeClick(submit);

    // Assert
    expect(submit).toBeDisabled();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission/i);
    expect(generateSap).not.toHaveBeenCalled();
    expect(createSar).not.toHaveBeenCalled();
  });
});
