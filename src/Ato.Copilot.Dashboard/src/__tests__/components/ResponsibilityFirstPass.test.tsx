import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import * as api from '../../api/responsibilityDrafts';
import ResponsibilityReviewPanel from '../../components/ResponsibilityReviewPanel';
import '../helpers/dialog';
import type { CapabilityResponsibilityItem } from '../../api/capabilityResponsibilities';
import { responsibilityItem, responsibilitySnapshotJson } from '../helpers/capabilityResponsibilityFixture';

vi.mock('../../api/responsibilityDrafts', async original => ({
  ...await original<typeof import('../../api/responsibilityDrafts')>(),
  getResponsibilityDraft: vi.fn(), prepareResponsibilityDraft: vi.fn(),
  saveResponsibilityDraft: vi.fn(), confirmResponsibilityDraft: vi.fn(),
}));
const field = (value: string): api.ResponsibilityValue => ({
  value, origin: 'From system records', sourceIds: ['system'], explanation: 'Recorded source.',
  userEdited: false, sourceHash: 'source-1',
});
const fields = (): Record<api.ResponsibilityField, api.ResponsibilityValue> => ({
  allocation: field('Customer'), provider: field(''), providerDuties: field(''),
  customer: field('Recorded duties.'), scope: field(''), exclusions: field(''), source: field(''),
  basis: field('Recorded ownership rationale.'), information: field(''),
});
const saved = (): api.ResponsibilityDraft => ({
  id: 'draft-1', revision: 1, status: 'Proposed', sourceHash: 'source-1', isStale: false,
  generationState: 'Prepared', generationError: null, preparedAt: '2026-10-01T00:00:00Z',
  generatedAt: '2026-10-01T00:00:00Z', preparedBy: 'Reviewer', reviewedBy: null, reviewedAt: null,
  values: fields(), suggestion: { values: fields(), questions: [], conflicts: [] }, sources: [], history: [],
});
const context = (): api.ResponsibilityDraftContext => ({
  systemId: 'system-a', controlId: 'AU-11', baselineId: 'baseline-a', scopeId: null, canPrepare: true,
  sourceHash: 'source-1', scopes: [], sources: [], sourceValues: fields(), questions: [], conflicts: [], draft: null,
});
function open(onDraftConfirmed = vi.fn(), onDraftPrepared = vi.fn(), item?: CapabilityResponsibilityItem) {
  return render(<MemoryRouter><ResponsibilityReviewPanel firstPass controlId="AU-11" systemId="system-a"
    item={item} baselineId="baseline-a" canConfirm eligible busy={false} blocked={false} generation={0}
    stateExplanation="Review your proposed responsibility." actionError={null} onRefresh={vi.fn()}
    onClose={vi.fn()} onConfirm={vi.fn()} onDraftConfirmed={onDraftConfirmed} onDraftPrepared={onDraftPrepared} /></MemoryRouter>);
}
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.getResponsibilityDraft).mockResolvedValue(context());
  vi.mocked(api.prepareResponsibilityDraft).mockResolvedValue({ ...context(), draft: saved() });
});

it('automatically prepares source-backed editable duties without requiring scope selection or confirmation', async () => {
  // Arrange / Act
  const confirmed = vi.fn();
  const prepared = vi.fn();
  open(confirmed, prepared);
  // Assert
  expect(await screen.findByDisplayValue('Recorded duties.')).toBeVisible();
  expect(screen.getByRole('radio', { name: /My team implements/ })).toBeChecked();
  expect(screen.getAllByText(/From system records/).length).toBeGreaterThan(0);
  await waitFor(() => expect(api.prepareResponsibilityDraft).toHaveBeenCalledOnce());
  expect(screen.queryByRole('button', { name: 'Prepare first pass' })).not.toBeInTheDocument();
  expect(screen.queryByRole('combobox', { name: 'Provider scope' })).not.toBeInTheDocument();
  expect(api.confirmResponsibilityDraft).not.toHaveBeenCalled();
  expect(confirmed).not.toHaveBeenCalled();
  expect(prepared).toHaveBeenCalledOnce();
});

it('uses the existing provider-qualified matrix entry when resolving environment scope', async () => {
  // Arrange
  const item = { ...responsibilityItem('MissingAllocation', 'AU-11'),
    sourceSnapshotJson: responsibilitySnapshotJson({ Controls: ['AU-11'] }) };
  // Act
  open(vi.fn(), vi.fn(), item);
  // Assert
  await waitFor(() => expect(api.getResponsibilityDraft).toHaveBeenCalledWith(
    'system-a', 'AU-11', null, expect.any(AbortSignal), { useEnvironment: true, capabilityId: item.capabilityId }));
});

it('prepares a whole-control system first pass across multiple contributions without picking a provider', async () => {
  // Arrange
  vi.mocked(api.getResponsibilityDraft).mockResolvedValue({ ...context(),
    environmentScopeIssue: 'Several recorded provider contributions require source-specific allocation review.' });
  // Act
  open();
  // Assert
  await waitFor(() => expect(api.prepareResponsibilityDraft).toHaveBeenCalledOnce());
  expect(await screen.findByRole('alert')).toHaveTextContent('Several recorded provider contributions');
  expect(screen.getByRole('button', { name: 'Save proposed draft' })).toBeEnabled();
  expect(api.confirmResponsibilityDraft).not.toHaveBeenCalled();
});

it.each(['Customer', 'Inherited'])('retains the existing explicit review path for %s with multiple provider contributions', async allocation => {
  // Arrange
  const record = saved();
  record.values.allocation = field(allocation);
  if (allocation === 'Inherited') {
    record.values.provider = field('Recorded provider');
    record.values.providerDuties = field('Recorded provider duties.');
    record.values.scope = field('Recorded scope');
    record.values.exclusions = field('Recorded exclusions');
    record.values.source = field('Recorded source version');
  }
  vi.mocked(api.getResponsibilityDraft).mockResolvedValue({ ...context(), draft: record,
    environmentScopeIssue: 'Several contributions need source-specific review before provider reliance.' });
  // Act
  open();
  await waitFor(() => expect(screen.getByRole('button', { name: 'Review allocation' })).toBeEnabled());
  fireEvent.click(screen.getByRole('button', { name: 'Review allocation' }));
  fireEvent.click(screen.getByRole('checkbox'));
  // Assert
  if (allocation === 'Customer') expect(screen.getByRole('button', { name: 'Confirm responsibility' })).toBeEnabled();
  else expect(screen.getByRole('button', { name: 'Confirm responsibility' })).toBeDisabled();
  expect(api.confirmResponsibilityDraft).not.toHaveBeenCalled();
});

it('preserves text edited while the AI suggestion is preparing', async () => {
  // Arrange
  let finish!: (value: api.ResponsibilityDraftContext) => void;
  vi.mocked(api.prepareResponsibilityDraft).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
  open();
  await screen.findByDisplayValue('Recorded duties.');
  // Act
  await waitFor(() => expect(api.prepareResponsibilityDraft).toHaveBeenCalledOnce());
  fireEvent.change(screen.getByRole('textbox', { name: 'Customer duties' }), { target: { value: 'My correction during generation.' } });
  const next = saved();
  next.values.customer = { ...field('AI duties.'), origin: 'AI proposed' };
  await act(async () => { finish({ ...context(), draft: next }); });
  // Assert
  expect(screen.getByRole('textbox', { name: 'Customer duties' })).toHaveValue('My correction during generation.');
  expect(api.saveResponsibilityDraft).not.toHaveBeenCalled();
});

it('compares refreshed AI output with unsaved human content before applying', async () => {
  // Arrange
  vi.mocked(api.getResponsibilityDraft).mockResolvedValue({ ...context(), draft: saved() });
  const refreshed = saved();
  refreshed.status = 'ComparisonRequired'; refreshed.revision = 2;
  refreshed.suggestion.values.customer = { ...field('New AI suggestion.'), origin: 'AI proposed' };
  vi.mocked(api.prepareResponsibilityDraft).mockResolvedValue({ ...context(), draft: refreshed });
  open(); await screen.findByDisplayValue('Recorded duties.');
  // Act
  fireEvent.change(screen.getByRole('textbox', { name: 'Customer duties' }), { target: { value: 'Unsaved human correction.' } });
  fireEvent.click(screen.getByRole('button', { name: 'Refresh suggestion' }));
  await screen.findByRole('region', { name: 'Compare refreshed suggestion' });
  fireEvent.click(screen.getByText('customer: changed suggestion'));
  // Assert
  expect(screen.getByText('Your current draft: Unsaved human correction.')).toBeVisible();
  expect(screen.getByRole('textbox', { name: 'Customer duties' })).toHaveValue('Unsaved human correction.');
  expect(api.saveResponsibilityDraft).not.toHaveBeenCalled();
});

it('saves corrections to the automatically prepared draft without accepting responsibility', async () => {
  // Arrange
  vi.mocked(api.prepareResponsibilityDraft).mockResolvedValue({ ...context(), draft: saved() });
  vi.mocked(api.saveResponsibilityDraft).mockResolvedValue({ ...saved(), revision: 2 });
  open(); await screen.findByDisplayValue('Recorded duties.');
  await waitFor(() => expect(screen.getByRole('button', { name: 'Save proposed draft' })).toBeEnabled());
  // Act
  fireEvent.click(screen.getByRole('button', { name: 'Save proposed draft' }));
  // Assert
  await waitFor(() => expect(api.prepareResponsibilityDraft).toHaveBeenCalledWith('system-a', 'AU-11', null, 0, true, expect.any(AbortSignal)));
  expect(await screen.findByText('Saved proposed draft · revision 2')).toBeVisible();
  expect(api.confirmResponsibilityDraft).not.toHaveBeenCalled();
});

it('reports generation failure once without discarding edits', async () => {
  // Arrange
  vi.mocked(api.prepareResponsibilityDraft).mockRejectedValue(new api.ResponsibilityDraftError('Generation unavailable.', 503, context()));
  open(); await screen.findByDisplayValue('Recorded duties.');
  fireEvent.change(screen.getByRole('textbox', { name: 'Customer duties' }), { target: { value: 'My retained duties.' } });
  // Act
  await waitFor(() => expect(api.prepareResponsibilityDraft).toHaveBeenCalledOnce());
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('Generation unavailable.');
  expect(screen.getAllByRole('alert')).toHaveLength(1);
  expect(screen.getByRole('textbox', { name: 'Customer duties' })).toHaveValue('My retained duties.');
  expect(screen.getByRole('button', { name: 'Use source records without AI' })).toBeEnabled();
});

it('does not prepare or replace an accepted responsibility on opening', async () => {
  // Arrange
  vi.mocked(api.getResponsibilityDraft).mockResolvedValue({ ...context(), draft: { ...saved(), status: 'Accepted' } });
  // Act
  open();
  await screen.findByDisplayValue('Recorded duties.');
  // Assert
  expect(api.prepareResponsibilityDraft).not.toHaveBeenCalled();
  expect(api.saveResponsibilityDraft).not.toHaveBeenCalled();
});

it('preserves saved human corrections instead of automatically refreshing their source-only draft', async () => {
  // Arrange
  const corrected = saved();
  corrected.generationState = 'NotRequested';
  corrected.values.customer = { ...field('Saved human correction.'), userEdited: true };
  vi.mocked(api.getResponsibilityDraft).mockResolvedValue({ ...context(), draft: corrected });
  // Act
  open();
  // Assert
  expect(await screen.findByDisplayValue('Saved human correction.')).toBeVisible();
  expect(api.prepareResponsibilityDraft).not.toHaveBeenCalled();
  expect(api.saveResponsibilityDraft).not.toHaveBeenCalled();
});

it('automatically applies the first AI pass only to an untouched source-only proposed draft', async () => {
  // Arrange
  const original = { ...saved(), generationState: 'NotRequested', isStale: true };
  const refreshed = { ...saved(), revision: 2, status: 'ComparisonRequired' };
  refreshed.suggestion.values.customer = { ...field('Fresh supported duties.'), origin: 'AI proposed' };
  vi.mocked(api.getResponsibilityDraft).mockResolvedValue({ ...context(), draft: original });
  vi.mocked(api.prepareResponsibilityDraft).mockResolvedValue({ ...context(), draft: refreshed });
  vi.mocked(api.saveResponsibilityDraft).mockResolvedValue({ ...refreshed, revision: 3, status: 'Proposed',
    values: refreshed.suggestion.values });
  // Act
  open();
  // Assert
  expect(await screen.findByDisplayValue('Fresh supported duties.')).toBeVisible();
  expect(api.saveResponsibilityDraft).toHaveBeenCalledWith('system-a', 'draft-1', 2,
    expect.objectContaining({ customer: 'Fresh supported duties.' }), true, expect.any(AbortSignal));
  expect(api.confirmResponsibilityDraft).not.toHaveBeenCalled();
});

it('does not generate for a read-only viewer or an ambiguous recorded environment', async () => {
  // Arrange
  vi.mocked(api.getResponsibilityDraft).mockResolvedValue({ ...context(), canPrepare: false,
    environmentScopeIssue: 'Resolve overlapping environment scopes.' });
  // Act
  open();
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('Resolve overlapping environment scopes.');
  expect(api.prepareResponsibilityDraft).not.toHaveBeenCalled();
  expect(screen.queryByRole('combobox', { name: 'Provider scope' })).not.toBeInTheDocument();
});
