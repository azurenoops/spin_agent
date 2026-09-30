import '../helpers/dialog';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import Assessments from '../../pages/Assessments';
import * as api from '../../api/assessmentWorkspace';
import { useWorkspaceSession } from '../../features/workspaces/WorkspaceBoundary';
import { workspaceSession, invokeClick } from '../helpers/domainPermissions';
import { planWorkspace, resultsWorkspace, resultDetail } from '../fixtures/assessmentWorkspace';

vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: vi.fn() }));
vi.mock('../../components/layout/SystemLayout', () => ({ useSystemContext: () => ({ detail: { systemId: 'system-a', name: 'System A' } }) }));
vi.mock('../../api/assessmentWorkspace', async original => ({
  ...await original<typeof api>(), getAssessmentPlan: vi.fn(), getAssessmentResults: vi.fn(), getAssessmentResult: vi.fn(),
  collectAssessmentResults: vi.fn(), createAssessmentPlan: vi.fn(), finalizeAssessmentPlan: vi.fn(), prepareAssessmentReport: vi.fn(),
}));
vi.mock('../../components/remediation/CreateRemediationTaskModal', () => ({ default: () => <div role="dialog">Task dialog</div> }));
vi.mock('../../components/AddDeviationDialog', () => ({ default: () => <div>Deviation dialog</div> }));
const deniedPlan = { ...planWorkspace, permissions: { canCreatePlan: false, canEditPlan: false, canFinalizePlan: false,
  createReason: 'Plan generation not authorized.', editReason: 'Plan editing not authorized.', finalizeReason: 'Finalization not authorized.' } };
const deniedDetail = { ...resultDetail, permissions: { ...resultDetail.permissions, canReview: false, canReconcile: false,
  canRemediate: false, canRequestDeviation: false } };
function page(query = '?tab=results&plan=sap-a') {
  return <MemoryRouter initialEntries={[`/workspaces/organizations/tenant-a/systems/system-a/assessments${query}`]}><Assessments /></MemoryRouter>;
}
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(useWorkspaceSession).mockReturnValue(workspaceSession('system-a', {}, ['MissionOwner']));
  vi.mocked(api.getAssessmentPlan).mockResolvedValue(deniedPlan);
  vi.mocked(api.getAssessmentResults).mockResolvedValue(resultsWorkspace);
  vi.mocked(api.getAssessmentResult).mockResolvedValue(deniedDetail);
  vi.mocked(api.collectAssessmentResults).mockResolvedValue({ status: 'Completed', message: 'Collection retained, pending review.', resultIds: ['assessment:run-a'] });
});
afterEach(() => { cleanup(); localStorage.clear(); });

describe('Assessment server action permissions', () => {
  it('keeps plan editing/finalization separate from Azure and SAR actions', async () => {
    // Arrange / Act
    render(page('?tab=plan&plan=sap-a'));
    // Assert
    expect(await screen.findByRole('button', { name: 'Finalize plan' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Choose lead' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Run Azure checks' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Prepare draft SAR' })).not.toBeInTheDocument();
    expect(api.createAssessmentPlan).not.toHaveBeenCalled();
  });
  it.each(['ISSM', 'AO', 'Administrator'])('ignores a forged %s browser persona', async role => {
    // Arrange
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({ role }));
    render(page());
    // Act
    await invokeClick(await screen.findByRole('button', { name: 'Run Azure checks' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Run Azure checks' })).toBeDisabled();
    expect(api.collectAssessmentResults).not.toHaveBeenCalled();
    expect(api.prepareAssessmentReport).not.toHaveBeenCalled();
  });
  it('offers configuration only when that operation is independently granted', async () => {
    // Arrange
    vi.mocked(api.getAssessmentResults).mockResolvedValue({ ...resultsWorkspace,
      collection: { ...resultsWorkspace.collection, canConfigureAzure: true } });
    // Act
    render(page());
    // Assert
    expect(await screen.findByRole('link', { name: 'Configure Azure assessment →' })).toHaveAttribute('href', '/systems/system-a/assessments/environment');
    expect(screen.getByRole('button', { name: 'Run Azure checks' })).toBeDisabled();
  });
  it('executes an explicitly granted run with the selected plan and request identity', async () => {
    // Arrange
    vi.mocked(api.getAssessmentResults).mockResolvedValue({ ...resultsWorkspace,
      collection: { ...resultsWorkspace.collection, canRunAzure: true, runReason: null,
        azure: { ...resultsWorkspace.collection.azure, state: 'Ready' } } });
    render(page());
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Run Azure checks' }));
    fireEvent.click(screen.getByRole('button', { name: 'Start scoped checks' }));
    // Assert
    await waitFor(() => expect(api.collectAssessmentResults).toHaveBeenCalledWith('system-a',
      { planId: 'sap-a', expectedPlanHash: 'plan-hash', requestId: expect.any(String) }));
  });
  it('does not infer document authority from broad system or assessment authority', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(workspaceSession('system-a',
      { canManageSystem: true, canRunAssessments: true, canDecideAuthorization: true }, ['ISSM', 'AuthorizingOfficial']));
    render(page('?tab=plan&plan=sap-a'));
    // Act
    await invokeClick(await screen.findByRole('button', { name: 'Finalize plan' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Finalize saved plan' })).toBeDisabled();
    expect(api.finalizeAssessmentPlan).not.toHaveBeenCalled();
  });
  it('fails closed when the authorization projection fails to load', async () => {
    // Arrange
    vi.mocked(api.getAssessmentResults).mockRejectedValue(new Error('Workspace permission is unavailable.'));
    // Act
    render(page());
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Workspace permission is unavailable.');
    expect(screen.queryByRole('button', { name: 'Run Azure checks' })).not.toBeInTheDocument();
    expect(api.collectAssessmentResults).not.toHaveBeenCalled();
  });
  it('does not expose denied finding actions', async () => {
    // Arrange / Act
    render(page('?tab=results&plan=sap-a&result=assessment%3Arun-a'));
    // Assert
    const dialog = await screen.findByRole('dialog', { name: 'Result details' });
    await within(dialog).findByRole('heading', { name: /Access observation/ });
    expect(within(dialog).queryByRole('button', { name: 'Create remediation task' })).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'Request deviation' })).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'Review a control' })).not.toBeInTheDocument();
  });
  it('closes pending finding writes when the server workspace context changes', async () => {
    // Arrange
    vi.mocked(api.getAssessmentResult).mockResolvedValue({ ...deniedDetail, permissions: { ...deniedDetail.permissions, canRemediate: true } });
    const view = render(page('?tab=results&plan=sap-a&result=assessment%3Arun-a'));
    fireEvent.click(await screen.findByRole('button', { name: 'Create remediation task' }));
    expect(screen.getByText('Task dialog')).toBeVisible();
    // Act
    vi.mocked(useWorkspaceSession).mockReturnValue(workspaceSession('system-a', { canManageSystem: true }, ['SystemOwner']));
    await act(async () => { view.rerender(page('?tab=results&plan=sap-a&result=assessment%3Arun-a')); });
    // Assert
    expect(screen.queryByText('Task dialog')).not.toBeInTheDocument();
  });
  it('rechecks a pending collection after access changes instead of using its old grant', async () => {
    // Arrange
    vi.mocked(useWorkspaceSession).mockReturnValue(workspaceSession('system-a', { canRunAssessments: true }, ['Sca']));
    vi.mocked(api.getAssessmentResults).mockResolvedValue({ ...resultsWorkspace,
      collection: { ...resultsWorkspace.collection, canRunAzure: true, runReason: null,
        azure: { ...resultsWorkspace.collection.azure, state: 'Ready' } } });
    const view = render(page());
    fireEvent.click(await screen.findByRole('button', { name: 'Run Azure checks' }));
    // Act
    vi.mocked(api.getAssessmentResults).mockResolvedValue(resultsWorkspace);
    vi.mocked(useWorkspaceSession).mockReturnValue(workspaceSession('system-a', { canRead: true }, ['SystemOwner']));
    await act(async () => { view.rerender(page()); });
    // Assert
    expect(await screen.findByRole('button', { name: 'Start scoped checks' })).toBeDisabled();
    expect(api.collectAssessmentResults).not.toHaveBeenCalled();
  });
});
