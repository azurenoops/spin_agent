import '../helpers/dialog';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import Assessments from '../../pages/Assessments';
import * as api from '../../api/assessmentWorkspace';
import { planWorkspace, resultsWorkspace, resultDetail } from '../fixtures/assessmentWorkspace';
const context = vi.hoisted(() => ({ systemId: 'system-a' }));
vi.mock('../../components/layout/SystemLayout', () => ({ useSystemContext: () => ({ detail: { systemId: context.systemId, name: 'System' } }) }));
vi.mock('../../api/assessmentWorkspace', async original => ({
  ...await original<typeof api>(), getAssessmentPlan: vi.fn(), getAssessmentResults: vi.fn(), collectAssessmentResults: vi.fn(),
}));
beforeEach(() => {
  vi.clearAllMocks(); context.systemId = 'system-a';
  vi.mocked(api.getAssessmentPlan).mockResolvedValue(planWorkspace);
  vi.mocked(api.getAssessmentResults).mockResolvedValue(resultsWorkspace);
  vi.mocked(api.collectAssessmentResults).mockResolvedValue({ status: 'Completed', message: 'Collection retained; review required.', resultIds: [] });
});
afterEach(cleanup);
const page = (withPlan = true) => <MemoryRouter initialEntries={[`/systems/system-a/assessments?tab=results${withPlan ? '&plan=sap-a' : ''}`]}><Assessments /></MemoryRouter>;

describe('Assessment collection admission and recovery', () => {
  it('does not expose an executable run while readiness is unknown', async () => {
    // Arrange
    vi.mocked(api.getAssessmentResults).mockReturnValue(new Promise(() => {}));
    // Act
    render(page());
    // Assert
    expect(await screen.findByText('Loading collection access and retained results…')).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Run Azure checks' })).not.toBeInTheDocument();
    expect(api.collectAssessmentResults).not.toHaveBeenCalled();
  });
  it.each([
    ['NotConfigured', 'Configure the system assessment subscriptions.'],
    ['CloudMismatch', 'The environment does not match the deployment cloud.'],
    ['Unsupported', 'This execution scope is not supported.'],
    ['Unavailable', 'The attached subscription is unavailable.'],
    ['Denied', 'Select an authorized organization before assessing this system.'],
  ])('explains %s without fabricating connectivity or results', async (state, message) => {
    // Arrange
    vi.mocked(api.getAssessmentResults).mockResolvedValue({ ...resultsWorkspace, collection: { ...resultsWorkspace.collection,
      runReason: message, azure: { ...resultsWorkspace.collection.azure, state, message } } });
    // Act
    render(page());
    // Assert
    expect((await screen.findAllByText(message))[0]).toBeVisible();
    expect(screen.getByRole('button', { name: 'Run Azure checks' })).toBeDisabled();
    expect(api.collectAssessmentResults).not.toHaveBeenCalled();
  });
  it('keeps historical results visible when Azure access is unavailable', async () => {
    // Arrange
    vi.mocked(api.getAssessmentResults).mockResolvedValue({ ...resultsWorkspace, items: [resultDetail.item], totalCount: 1 });
    // Act
    render(page());
    // Assert
    expect(await screen.findByRole('button', { name: 'Open result Azure configuration checks' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Run Azure checks' })).toBeDisabled();
    expect(screen.getByText('Not reviewed')).toBeVisible();
  });
  it('refreshes readiness without executing collection', async () => {
    // Arrange
    render(page());
    await screen.findByRole('button', { name: 'Run Azure checks' });
    fireEvent.click(screen.getByText('Scope and access requirements'));
    vi.mocked(api.getAssessmentResults).mockResolvedValue({ ...resultsWorkspace,
      collection: { ...resultsWorkspace.collection, canRunAzure: true, runReason: null, azure: { ...resultsWorkspace.collection.azure, state: 'Ready', message: 'Scope verified.' } } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Refresh readiness' }));
    // Assert
    await waitFor(() => expect(screen.getByRole('button', { name: 'Run Azure checks' })).toBeEnabled());
    expect(api.collectAssessmentResults).not.toHaveBeenCalled();
  });
  it('prevents double submission and retains a stable key when retrying a failed request', async () => {
    // Arrange
    vi.mocked(api.getAssessmentResults).mockResolvedValue({ ...resultsWorkspace,
      collection: { ...resultsWorkspace.collection, canRunAzure: true, runReason: null,
        azure: { ...resultsWorkspace.collection.azure, state: 'Ready' } } });
    vi.mocked(api.collectAssessmentResults).mockRejectedValueOnce(new Error('Connection interrupted; retained work may exist.'));
    render(page());
    fireEvent.click(await screen.findByRole('button', { name: 'Run Azure checks' }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Start scoped checks' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Connection interrupted');
    const first = vi.mocked(api.collectAssessmentResults).mock.calls[0]![1];
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Start scoped checks' }));
    // Assert
    await waitFor(() => expect(api.collectAssessmentResults).toHaveBeenCalledTimes(2));
    expect(vi.mocked(api.collectAssessmentResults).mock.calls[1]![1].requestId).toBe(first.requestId);
  });
  it('allows preliminary collection without a finalized SAP', async () => {
    // Arrange
    vi.mocked(api.getAssessmentPlan).mockResolvedValue({ ...planWorkspace, plan: null, plans: [] });
    vi.mocked(api.getAssessmentResults).mockResolvedValue({ ...resultsWorkspace,
      collection: { ...resultsWorkspace.collection, canRunAzure: true, runReason: null,
        azure: { ...resultsWorkspace.collection.azure, state: 'Ready' } } });
    render(page(false));
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Run Azure checks' }));
    fireEvent.click(screen.getByRole('button', { name: 'Start scoped checks' }));
    // Assert
    await waitFor(() => expect(api.collectAssessmentResults).toHaveBeenCalledWith('system-a',
      expect.objectContaining({ planId: null, expectedPlanHash: null })));
  });
  it('reports partial work as partial rather than successful assessment', async () => {
    // Arrange
    vi.mocked(api.getAssessmentResults).mockResolvedValue({ ...resultsWorkspace,
      collection: { ...resultsWorkspace.collection, canRunAzure: true, runReason: null,
        azure: { ...resultsWorkspace.collection.azure, state: 'Ready' } } });
    vi.mocked(api.collectAssessmentResults).mockResolvedValue({ status: 'Partial', message: 'One configured scope failed. Completed observations were retained.', resultIds: ['assessment:run-a'] });
    render(page());
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Run Azure checks' }));
    fireEvent.click(screen.getByRole('button', { name: 'Start scoped checks' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('One configured scope failed');
  });
  it('cancels stale system reads and does not show their late result', async () => {
    // Arrange
    let resolve!: (value: typeof resultsWorkspace) => void;
    vi.mocked(api.getAssessmentResults).mockReturnValueOnce(new Promise(done => { resolve = done; }));
    const view = render(page());
    await screen.findByText('Loading collection access and retained results…');
    const signal = vi.mocked(api.getAssessmentResults).mock.calls[0]![2]!;
    // Act
    context.systemId = 'system-b';
    vi.mocked(api.getAssessmentPlan).mockResolvedValue({ ...planWorkspace, systemId: 'system-b' });
    vi.mocked(api.getAssessmentResults).mockResolvedValue({ ...resultsWorkspace, systemId: 'system-b' });
    await act(async () => { view.rerender(page()); resolve({ ...resultsWorkspace, items: [resultDetail.item], totalCount: 1 }); });
    // Assert
    expect(signal.aborted).toBe(true);
    expect(screen.queryByRole('button', { name: 'Open result Azure configuration checks' })).not.toBeInTheDocument();
  });
});
