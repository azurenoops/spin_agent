import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import ScopedMonitoring from '../../pages/ScopedMonitoring';
import * as api from '../../api/scopedMonitoring';
import type { MonitoringWorkspace } from '../../api/scopedMonitoring';
import '../helpers/dialog';

vi.mock('../../components/layout/SystemLayout', () => ({
  useSystemContext: () => ({ detail: { systemId: 'system-a', name: 'Mission A' } }),
}));
vi.mock('../../api/scopedMonitoring', () => ({
  getMonitoringWorkspace: vi.fn(), saveMonitoringRule: vi.fn(), testMonitoringRule: vi.fn(), dispositionMonitoringImpact: vi.fn(),
}));
const state = (): MonitoringWorkspace => ({
  canManageRules: true, canReviewImpacts: true,
  boundaries: [{ id: 'boundary-a', name: 'Reviewed boundary' }],
  rules: [], coverage: [], changes: [], evaluations: [], impacts: [],
});
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.getMonitoringWorkspace).mockResolvedValue(state()); });

describe('scoped monitoring screens', () => {
  it('does not present missing collection as healthy', async () => {
    // Arrange
    vi.mocked(api.getMonitoringWorkspace).mockResolvedValue({ ...state(), coverage: [
      { assignmentId: 'a', boundaryId: 'boundary-a', resourceId: 'resource-a', providerComponentId: null,
        health: 'Missing', lastSuccessAt: null, error: null },
    ] });
    // Act
    render(<MemoryRouter><ScopedMonitoring /></MemoryRouter>);
    // Assert
    expect(await screen.findByText('No successful collection')).toBeInTheDocument();
    expect(screen.getByText('Missing')).toBeInTheDocument();
    expect(screen.getByText('0 / 1')).toBeInTheDocument();
  });

  it('submits typed conditions and the explicitly reviewed scope', async () => {
    // Arrange
    vi.mocked(api.saveMonitoringRule).mockResolvedValue({
      id: 'rule-a', name: 'Network', boundaryDefinitionId: 'boundary-a', baselineReference: 'baseline-1',
      ownerId: 'owner', signal: 'Alert', triggerCondition: '{"field":"Type","operator":"Equals","value":"Drift"}',
      cadenceMinutes: 60, severityOverride: 'Medium', isEnabled: true, version: 1, lastEvaluatedAt: null,
    });
    render(<MemoryRouter><ScopedMonitoring section="rules" /></MemoryRouter>);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Create rule →' }));
    expect(screen.getByRole('dialog', { name: 'Create monitoring rule' })).toBeVisible();
    fireEvent.change(screen.getByRole('textbox', { name: 'Name' }), { target: { value: 'Network' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Reviewed baseline reference' }), { target: { value: 'baseline-1' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Owner' }), { target: { value: 'owner' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save reviewed rule' }));
    // Assert
    await waitFor(() => expect(api.saveMonitoringRule).toHaveBeenCalledWith('system-a', expect.objectContaining({
      name: 'Network', boundaryDefinitionId: 'boundary-a', baselineReference: 'baseline-1',
      condition: { field: 'Type', operator: 'Equals', value: 'Drift' }, ownerId: 'owner',
    }), undefined));
  });

  it('retains rule edits and exposes save errors inside the dialog', async () => {
    // Arrange
    vi.mocked(api.saveMonitoringRule).mockRejectedValue(new Error('Rule version changed'));
    render(<MemoryRouter><ScopedMonitoring section="rules" /></MemoryRouter>);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Create rule →' }));
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Preserved rule' } });
    fireEvent.change(screen.getByLabelText('Reviewed baseline reference'), { target: { value: 'baseline-1' } });
    fireEvent.change(screen.getByLabelText('Owner'), { target: { value: 'owner' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save reviewed rule' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Rule version changed');
    expect(screen.getByRole('dialog')).toContainElement(screen.getByRole('alert'));
    expect(screen.getByLabelText('Name')).toHaveValue('Preserved rule');
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(api.saveMonitoringRule).toHaveBeenCalledTimes(1);
  });

  it('retains error state and does not infer write permission from local role', async () => {
    // Arrange
    vi.mocked(api.getMonitoringWorkspace).mockResolvedValue({ ...state(), canManageRules: false });
    // Act
    render(<MemoryRouter><ScopedMonitoring section="rules" /></MemoryRouter>);
    // Assert
    await screen.findByText('No scoped rules. Create a rule after reviewing its boundary and baseline.');
    expect(screen.queryByRole('button', { name: 'Create rule →' })).not.toBeInTheDocument();
    expect(api.saveMonitoringRule).not.toHaveBeenCalled();
  });

  it('records a distinct mission disposition without issuing an AO decision', async () => {
    // Arrange
    vi.mocked(api.getMonitoringWorkspace).mockResolvedValue({ ...state(), impacts: [{
      id: 'impact-a', evaluationId: 'eval', controlId: 'SC-7', ownerId: 'reviewer', disposition: 'Pending',
      rationale: null, reviewedBy: null, reviewedAt: null, version: 3, affectedRecordsJson: '{}', narrativeProposalIdsJson: '[]',
    }] });
    render(<MemoryRouter><ScopedMonitoring section="impacts" /></MemoryRouter>);
    // Act
    fireEvent.change(await screen.findByRole('combobox', { name: 'Review outcome' }), { target: { value: 'RecommendReassessment' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Review rationale' }), { target: { value: 'Scope changed.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Record review outcome →' }));
    // Assert
    await waitFor(() => expect(api.dispositionMonitoringImpact).toHaveBeenCalledWith(
      'system-a', 'impact-a', 3, 'RecommendReassessment', 'Scope changed.'));
    expect(screen.queryByRole('option', { name: 'Approve ATO' })).not.toBeInTheDocument();
  });

  it('pins the reviewed impact and version when refresh reorders or updates records', async () => {
    // Arrange
    const impact = { id: 'impact-a', evaluationId: 'eval', controlId: 'SC-7', ownerId: 'reviewer', disposition: 'Pending',
      rationale: null, reviewedBy: null, reviewedAt: null, version: 3, affectedRecordsJson: '{}', narrativeProposalIdsJson: '[]' };
    vi.mocked(api.getMonitoringWorkspace).mockResolvedValue({ ...state(), impacts: [impact] });
    render(<MemoryRouter><ScopedMonitoring section="impacts" /></MemoryRouter>);
    fireEvent.change(await screen.findByRole('combobox', { name: 'Review outcome' }), { target: { value: 'NoImpact' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Review rationale' }), { target: { value: 'Rationale for A only' } });
    vi.mocked(api.getMonitoringWorkspace).mockResolvedValue({ ...state(), impacts: [{ ...impact, id: 'impact-b', controlId: 'AC-2' }, impact] });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    await waitFor(() => expect(screen.getByRole('option', { name: 'AC-2 · reviewer · Pending' })).toBeInTheDocument());
    // Assert
    expect(screen.getByRole('combobox', { name: 'Impact review' })).toHaveValue('impact-a');
    expect(screen.getByRole('textbox', { name: 'Review rationale' })).toHaveValue('Rationale for A only');
    // Act
    vi.mocked(api.getMonitoringWorkspace).mockResolvedValue({ ...state(), impacts: [{ ...impact, version: 4 }] });
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('impact changed');
    expect(screen.getByRole('button', { name: 'Record review outcome →' })).toBeDisabled();
    expect(api.dispositionMonitoringImpact).not.toHaveBeenCalled();
  });
});
