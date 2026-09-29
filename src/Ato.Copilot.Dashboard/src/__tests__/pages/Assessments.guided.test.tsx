import '../helpers/dialog';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import Assessments from '../../pages/Assessments';
import * as api from '../../api/assessmentWorkspace';
import { planWorkspace, resultsWorkspace, resultDetail, report } from '../fixtures/assessmentWorkspace';

vi.mock('../../components/layout/SystemLayout', () => ({ useSystemContext: () => ({ detail: { systemId: 'system-a', name: 'SPIN Demo System' } }) }));
vi.mock('../../api/assessmentWorkspace', async original => ({
  ...await original<typeof api>(), getAssessmentPlan: vi.fn(), createAssessmentPlan: vi.fn(), saveAssessmentPlan: vi.fn(),
  previewAssessmentPlan: vi.fn(), finalizeAssessmentPlan: vi.fn(), getAssessmentResults: vi.fn(), getAssessmentResult: vi.fn(),
  collectAssessmentResults: vi.fn(), reconcileAssessmentResult: vi.fn(), reviewAssessmentControl: vi.fn(),
  prepareAssessmentReport: vi.fn(), getAssessmentReport: vi.fn(),
}));
vi.mock('../../features/scan-import/ScanImportDialog', () => ({ default: () => <div role="dialog" aria-label="Import scan results">CKL, XCCDF, Nessus import</div> }));
const mount = (query = '?tab=plan&plan=sap-a') => render(<MemoryRouter initialEntries={[`/systems/system-a/assessments${query}`]}><Assessments /></MemoryRouter>);
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getAssessmentPlan).mockResolvedValue(planWorkspace);
  vi.mocked(api.saveAssessmentPlan).mockResolvedValue(planWorkspace);
  vi.mocked(api.createAssessmentPlan).mockResolvedValue(planWorkspace);
  vi.mocked(api.finalizeAssessmentPlan).mockResolvedValue({ ...planWorkspace, plan: { ...planWorkspace.plan!, status: 'Finalized' },
    permissions: { ...planWorkspace.permissions, canEditPlan: false, canFinalizePlan: false } });
  vi.mocked(api.previewAssessmentPlan).mockResolvedValue({ systemId: 'system-a', sapId: 'sap-a', revision: 2, contentHash: 'plan-hash', content: '# Saved assessment plan\nRetained approach and structured scope.' });
  vi.mocked(api.getAssessmentResults).mockResolvedValue(resultsWorkspace);
  vi.mocked(api.getAssessmentResult).mockResolvedValue(resultDetail);
  vi.mocked(api.reconcileAssessmentResult).mockResolvedValue(resultDetail);
  vi.mocked(api.reviewAssessmentControl).mockResolvedValue(resultDetail);
  vi.mocked(api.prepareAssessmentReport).mockResolvedValue(report);
  vi.mocked(api.getAssessmentReport).mockResolvedValue(report);
  vi.spyOn(window, 'confirm').mockReturnValue(true);
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

describe('Connected assessment workspace', () => {
  it('links a retained result finding to the findings workspace without creating work', async () => {
    // Arrange
    mount('?result=assessment:assessment-a');
    // Act
    const drawer = await screen.findByRole('dialog', { name: 'Result details' });
    // Assert
    const links = await within(drawer).findAllByRole('link', { name: 'Review finding & linked work' });
    expect(links[0]).toHaveAttribute('href', `/systems/system-a/remediation?finding=${encodeURIComponent(resultDetail.findings[0]!.findingId)}`);
    expect(api.createAssessmentPlan).not.toHaveBeenCalled();
  });
  it('groups saved-plan actions using the shared secondary style and retains their behavior', async () => {
    // Arrange
    mount();
    fireEvent.click(await screen.findByText('Plan details & history'));
    // Assert
    const actions = screen.getByRole('group', { name: 'Saved plan actions' });
    const edit = within(actions).getByRole('button', { name: 'Edit title' });
    expect(edit).toHaveClass('aw-plan-tool', 'rounded-[7px]');
    expect(edit.querySelector('svg')).not.toBeNull();
    expect(within(actions).getByRole('button', { name: 'Refresh saved plan' })).toHaveClass('aw-plan-refresh');
    // Act
    fireEvent.click(edit);
    // Assert
    expect(screen.getByRole('dialog', { name: 'Assessment plan title' })).toBeVisible();
    expect(screen.getByRole('textbox', { name: 'Assessment title' })).toHaveValue(planWorkspace.plan!.title);
  });
  it('keeps the saved-plan action group read-only when editing is not authorized', async () => {
    // Arrange
    vi.mocked(api.getAssessmentPlan).mockResolvedValue({ ...planWorkspace,
      permissions: { ...planWorkspace.permissions, canEditPlan: false } });
    mount();
    // Act
    fireEvent.click(await screen.findByText('Plan details & history'));
    // Assert
    const actions = screen.getByRole('group', { name: 'Saved plan actions' });
    expect(within(actions).getAllByRole('button')).toHaveLength(1);
    expect(within(actions).getByRole('button', { name: 'Refresh saved plan' })).toBeEnabled();
  });
  it('shows one retained plan and distinguishes baseline size from plan scope', async () => {
    // Arrange / Act
    mount();
    // Assert
    expect(await screen.findByRole('heading', { name: 'Prepare your assessment plan' })).toBeVisible();
    expect(screen.getByText('339 baseline controls')).toBeVisible();
    expect(screen.getByText('2 controls in assessment scope')).toBeVisible();
    expect(screen.getAllByRole('heading', { name: 'SPIN Demo System · Assessment plan' })).toHaveLength(1);
    expect(screen.getByText('4 planning details need attention.')).toBeVisible();
    expect(api.createAssessmentPlan).not.toHaveBeenCalled();
  });
  it('saves an approach to the same draft and previews saved content without generating another plan', async () => {
    // Arrange
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Add approach' }));
    const drawer = screen.getByRole('dialog', { name: 'Assessment approach' });
    fireEvent.change(within(drawer).getByRole('textbox', { name: 'Approach and procedures' }), { target: { value: 'Examine records and interview the system team.' } });
    fireEvent.click(within(drawer).getByRole('button', { name: 'Save draft' }));
    // Assert
    await waitFor(() => expect(api.saveAssessmentPlan).toHaveBeenCalledWith('system-a', 'sap-a', expect.objectContaining({
      task: 'approach', expectedContentHash: 'plan-hash', expectedRevision: 2,
      assessmentApproach: 'Examine records and interview the system team.',
    })));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Preview saved draft' }));
    // Assert
    expect(await screen.findByText('Retained approach and structured scope.')).toBeVisible();
    expect(api.previewAssessmentPlan).toHaveBeenCalledWith('system-a', 'sap-a', expect.any(AbortSignal));
    expect(api.createAssessmentPlan).not.toHaveBeenCalled();
  });
  it('selects a named lead without authorizing or assigning roles', async () => {
    // Arrange
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Choose lead' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Assessment lead' }), { target: { value: 'person-a' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
    // Assert
    await waitFor(() => expect(api.saveAssessmentPlan).toHaveBeenCalledWith('system-a', 'sap-a', expect.objectContaining({
      task: 'lead', assessmentLeadId: 'person-a',
    })));
  });
  it('preserves edits on stale save and confirms discarding unsaved changes', async () => {
    // Arrange
    vi.mocked(api.saveAssessmentPlan).mockRejectedValue(new Error('The saved plan changed. Refresh before saving.'));
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Add approach' }));
    const drawer = screen.getByRole('dialog', { name: 'Assessment approach' });
    const input = within(drawer).getByRole('textbox', { name: 'Approach and procedures' });
    fireEvent.change(input, { target: { value: 'Unsaved assessment notes.' } });
    // Act
    fireEvent.click(within(drawer).getByRole('button', { name: 'Save draft' }));
    // Assert
    expect(await within(drawer).findByRole('alert')).toHaveTextContent('saved plan changed');
    expect(input).toHaveValue('Unsaved assessment notes.');
    // Act
    vi.mocked(window.confirm).mockReturnValue(false);
    fireEvent.click(within(drawer).getByRole('button', { name: 'Cancel' }));
    // Assert
    expect(drawer).toBeVisible();
  });
  it('does not turn advisory completeness warnings into a new finalization gate', async () => {
    // Arrange
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Finalize plan' }));
    const confirmation = screen.getByRole('dialog', { name: 'Finalize assessment plan' });
    // Assert
    expect(within(confirmation).getByText('No assessment team members assigned.')).toBeVisible();
    // Act
    fireEvent.click(within(confirmation).getByRole('button', { name: 'Finalize saved plan' }));
    // Assert
    await waitFor(() => expect(api.finalizeAssessmentPlan).toHaveBeenCalledWith('system-a', 'sap-a',
      { expectedContentHash: 'plan-hash', expectedRevision: 2 }));
  });
  it('results links back to the same plan and has no duplicate SAP actions or empty list chrome', async () => {
    // Arrange / Act
    mount('?tab=results&plan=sap-a');
    // Assert
    expect(await screen.findByText('No results yet')).toBeVisible();
    expect(screen.getByRole('link', { name: /Continue planning/ }).getAttribute('href')).toContain('plan=sap-a');
    expect(screen.queryByRole('button', { name: /Generate SAP|Finalize SAP|Finalize plan/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: 'Search result sets' })).not.toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Result pages' })).not.toBeInTheDocument();
  });
  it('uses actual execution/configuration permissions and exposes supported imports', async () => {
    // Arrange
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({ role: 'ISSM' }));
    mount('?tab=results&plan=sap-a');
    // Act / Assert
    expect(await screen.findByRole('button', { name: 'Run Azure checks' })).toBeDisabled();
    expect(screen.getByText('Your assignments do not authorize Azure assessment execution.')).toBeVisible();
    expect(screen.queryByRole('link', { name: 'Configure Azure assessment' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'View import options' }));
    expect(screen.getByRole('dialog', { name: 'Import scan results' })).toBeVisible();
    expect(api.collectAssessmentResults).not.toHaveBeenCalled();
    localStorage.clear();
  });
  it('shows original result plan revision and selected-scope gaps without claiming controls passed', async () => {
    // Arrange
    vi.mocked(api.getAssessmentResults).mockResolvedValue({ ...resultsWorkspace, items: [resultDetail.item], totalCount: 1 });
    mount('?tab=results&plan=sap-a&result=assessment%3Arun-a');
    // Assert
    const drawer = await screen.findByRole('dialog', { name: 'Result details' });
    expect(await within(drawer).findByText('Collected against revision 1')).toBeVisible();
    expect(within(drawer).getByText('1 of 2 planned controls have observations.')).toBeVisible();
    expect(within(drawer).getByText('AC-2', { selector: '.aw-gap-control' })).toBeVisible();
    expect(within(drawer).getByText('Collection completion is not a control assessment decision.')).toBeVisible();
    expect(api.reconcileAssessmentResult).not.toHaveBeenCalled();
  });
  it('records an explicit authorized control review with its source revision', async () => {
    // Arrange
    mount('?tab=results&plan=sap-a&result=assessment%3Arun-a');
    // Act
    const review = await screen.findByRole('button', { name: 'Review a control' });
    fireEvent.click(review);
    fireEvent.change(screen.getByRole('combobox', { name: 'Control to review' }), { target: { value: 'AC-1' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Determination' }), { target: { value: 'OtherThanSatisfied' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'CAT severity' }), { target: { value: 'CatII' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Assessor notes' }), { target: { value: 'The retained observation needs remediation.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save control review' }));
    // Assert
    await waitFor(() => expect(api.reviewAssessmentControl).toHaveBeenCalledWith('system-a', 'assessment:run-a',
      expect.objectContaining({ expectedResultRevision: 'result-revision', controlId: 'AC-1', determination: 'OtherThanSatisfied', catSeverity: 'CatII' })));
  });
  it('keeps source failures explicit instead of displaying no results', async () => {
    // Arrange
    vi.mocked(api.getAssessmentResults).mockRejectedValue(new Error('Assessment results unavailable.'));
    // Act
    mount('?tab=results&plan=sap-a');
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Assessment results unavailable.');
    expect(screen.queryByText('No results yet')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry results' })).toBeVisible();
  });
  it('restores the same plan creation request identity after refresh', async () => {
    // Arrange
    vi.mocked(api.getAssessmentPlan).mockResolvedValue({ ...planWorkspace, plan: { ...planWorkspace.plan!, status: 'Finalized' } });
    mount('?tab=plan&plan=sap-a&action=create&planRequest=retained-plan-request');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Create draft' }));
    // Assert
    await waitFor(() => expect(api.createAssessmentPlan).toHaveBeenCalledWith('system-a',
      { requestId: 'retained-plan-request', previousPlanId: 'sap-a', expectedContentHash: 'plan-hash' }));
  });
});
