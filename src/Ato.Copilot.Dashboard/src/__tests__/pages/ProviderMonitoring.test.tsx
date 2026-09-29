import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../helpers/dialog';
import { ProviderMonitoringPanel } from '../../features/provider-authorizations/ProviderMonitoringPage';
import * as api from '../../features/provider-authorizations/providerMonitoringApi';
import type { ProviderMonitoringEvaluation, ProviderMonitoringRule, ProviderMonitoringWorkspace } from '../../features/provider-authorizations/providerMonitoringApi';

vi.mock('../../features/provider-authorizations/providerMonitoringApi', () => ({
  getProviderMonitoring: vi.fn(), saveProviderMonitoringRule: vi.fn(), testProviderMonitoringRule: vi.fn(), evaluateProviderMonitoringRule: vi.fn(),
}));
const rule: ProviderMonitoringRule = { id: 'rule-a', offeringId: 'offering-a', revision: 1, name: 'Evidence age',
  signal: 'EvidenceFreshness', sourceId: 'evidence-a', conditionJson: '{"field":"Change.ageDays","operator":"GreaterThanOrEqual","value":"30"}',
  ownerId: 'Provider reviewer', response: 'CreateProviderImpactReview', cadenceMinutes: 60, isEnabled: true,
  baselineJson: '{}', baselineHash: 'source-v1', lastEvaluatedAt: null };
const evaluation: ProviderMonitoringEvaluation = { id: 'evaluation-a', ruleId: 'rule-a', ruleRevision: 1,
  outcome: 'Matched', collectionHealth: 'Available', createdAt: '2026-09-26T12:00:00Z',
  ruleSnapshotJson: '{}', sourceSnapshotJson: '{}', impactReviewId: null };
const workspace = (): ProviderMonitoringWorkspace => ({
  offeringId: 'offering-a', offeringName: 'Reviewed service', rules: [], evaluations: [],
  sources: [{ sourceId: 'evidence-a', signal: 'EvidenceFreshness', name: 'Reviewed evidence',
    collectionHealth: 'Available', sourceRevision: 'source-v1', field: 'Change.ageDays', value: '40',
    sourceTimestamp: '2026-08-17T12:00:00Z', snapshotJson: '{}' }],
});
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getProviderMonitoring).mockResolvedValue(workspace());
  vi.mocked(api.saveProviderMonitoringRule).mockResolvedValue(rule);
  vi.mocked(api.testProviderMonitoringRule).mockResolvedValue(evaluation);
  vi.mocked(api.evaluateProviderMonitoringRule).mockResolvedValue({ ...evaluation, impactReviewId: 'impact-a' });
});
const open = () => render(<MemoryRouter><ProviderMonitoringPanel offeringId="offering-a" /></MemoryRouter>);

describe('provider-owned source monitoring', () => {
  it('opens focused rule dialogs, cancels without writes and returns focus to the invoker', async () => {
    // Arrange
    open();
    const trigger = await screen.findByRole('button', { name: 'Create monitoring rule' });
    await waitFor(() => expect(trigger).toBeEnabled());
    expect(screen.queryByLabelText('Rule name')).not.toBeInTheDocument();
    // Act
    trigger.focus();
    fireEvent.click(trigger);
    const dialog = screen.getByRole('dialog', { name: 'Create monitoring rule' });
    fireEvent.change(within(dialog).getByLabelText('Rule name'), { target: { value: 'Discard this draft' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
    expect(api.saveProviderMonitoringRule).not.toHaveBeenCalled();
    fireEvent.click(trigger);
    expect(screen.getByLabelText('Rule name')).toHaveValue('');
  });

  it('keeps rejected input and errors in the dialog and prevents dismissal during a write', async () => {
    // Arrange
    let reject!: (reason: Error) => void;
    vi.mocked(api.saveProviderMonitoringRule).mockImplementation(() => new Promise((_, fail) => { reject = fail; }));
    open();
    const trigger = await screen.findByRole('button', { name: 'Create monitoring rule' });
    await waitFor(() => expect(trigger).toBeEnabled());
    fireEvent.click(trigger);
    const dialog = screen.getByRole('dialog', { name: 'Create monitoring rule' });
    fireEvent.change(within(dialog).getByLabelText('Rule name'), { target: { value: 'Retain rule draft' } });
    fireEvent.change(within(dialog).getByLabelText('Owner'), { target: { value: 'Reviewer' } });
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save rule' }));
    fireEvent(dialog, new Event('cancel', { bubbles: true, cancelable: true }));
    // Assert
    expect(dialog).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Close dialog' })).toBeDisabled();
    expect(within(dialog).getByLabelText('Rule name')).toBeDisabled();
    reject(new Error('Rule revision changed'));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Rule revision changed');
    expect(within(dialog).getByLabelText('Rule name')).toHaveValue('Retain rule draft');
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toBeEnabled();
  });

  it('sends an owning offering, reviewed source fence and typed condition', async () => {
    // Arrange
    open();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Create monitoring rule' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Rule name' }), { target: { value: 'Evidence age' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Owner' }), { target: { value: 'Provider reviewer' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save rule' }));
    // Assert
    await waitFor(() => expect(api.saveProviderMonitoringRule).toHaveBeenCalledWith('offering-a',
      expect.objectContaining({ sourceId: 'evidence-a', expectedSourceRevision: 'source-v1', response: 'CreateProviderImpactReview',
        condition: { field: 'Change.ageDays', operator: 'GreaterThanOrEqual', value: '30' } }), expect.any(String), undefined));
  });

  it('tests without creating work and evaluates into existing provider impact review', async () => {
    // Arrange
    vi.mocked(api.getProviderMonitoring).mockResolvedValue({ ...workspace(), rules: [rule] });
    open();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Inspect rule' }));
    fireEvent.click(screen.getByRole('button', { name: 'Test saved rule' }));
    // Assert
    await screen.findByText('No provider review was created by this result.');
    expect(api.evaluateProviderMonitoringRule).not.toHaveBeenCalled();
    // Act
    await waitFor(() => expect(screen.getByRole('button', { name: 'Evaluate now' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Evaluate now' }));
    // Assert
    expect(await screen.findByRole('link', { name: 'Review provider impact' })).toHaveAttribute('href',
      '/workspaces/csp/authorizations/offerings/offering-a/impact?reviewId=impact-a');
    expect(api.evaluateProviderMonitoringRule).toHaveBeenCalledWith('offering-a', 'rule-a', 1, expect.any(String));
  });

  it('does not present missing source collection as a live connection', async () => {
    // Arrange
    const data = workspace();
    data.sources[0]!.collectionHealth = 'Unreviewed';
    vi.mocked(api.getProviderMonitoring).mockResolvedValue(data);
    open();
    // Assert
    await screen.findByText(/Some source facts are missing or unreviewed/);
    expect(screen.getByRole('button', { name: 'Create monitoring rule' })).toBeDisabled();
    expect(screen.getByText(/No Azure, multicloud, or eMASS connector is configured/)).toBeInTheDocument();
  });

  it('shows authorization failures without a rule authoring form', async () => {
    // Arrange
    vi.mocked(api.getProviderMonitoring).mockRejectedValue(new Error('Provider access denied'));
    // Act
    open();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Provider access denied');
    expect(screen.queryByRole('button', { name: 'Save rule' })).not.toBeInTheDocument();
  });
});
