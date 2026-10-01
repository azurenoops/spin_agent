import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import * as api from '../../api/responsibilityDrafts';
import ResponsibilityReviewPanel from '../../components/ResponsibilityReviewPanel';
import '../helpers/dialog';

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
function open() {
  return render(<MemoryRouter><ResponsibilityReviewPanel firstPass controlId="AU-11" systemId="system-a"
    baselineId="baseline-a" canConfirm eligible busy={false} blocked={false} generation={0}
    stateExplanation="Review your proposed responsibility." actionError={null} onRefresh={vi.fn()}
    onClose={vi.fn()} onConfirm={vi.fn()} /></MemoryRouter>);
}
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.getResponsibilityDraft).mockResolvedValue(context());
});

it('opens with source-backed values without generating or confirming', async () => {
  // Arrange / Act
  open();
  // Assert
  expect(await screen.findByDisplayValue('Recorded duties.')).toBeVisible();
  expect(screen.getByRole('radio', { name: /My team implements/ })).toBeChecked();
  expect(screen.getAllByText(/From system records/).length).toBeGreaterThan(0);
  expect(screen.getByRole('button', { name: 'Prepare first pass' })).toBeEnabled();
  expect(api.prepareResponsibilityDraft).not.toHaveBeenCalled();
  expect(api.confirmResponsibilityDraft).not.toHaveBeenCalled();
});

it('preserves text edited while the AI suggestion is preparing', async () => {
  // Arrange
  let finish!: (value: api.ResponsibilityDraftContext) => void;
  vi.mocked(api.prepareResponsibilityDraft).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
  open();
  await screen.findByDisplayValue('Recorded duties.');
  // Act
  fireEvent.click(screen.getByRole('button', { name: 'Prepare first pass' }));
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

it('saves a proposed manual draft without requiring AI generation', async () => {
  // Arrange
  vi.mocked(api.prepareResponsibilityDraft).mockResolvedValue({ ...context(), draft: saved() });
  vi.mocked(api.saveResponsibilityDraft).mockResolvedValue({ ...saved(), revision: 2 });
  open(); await screen.findByDisplayValue('Recorded duties.');
  // Act
  fireEvent.click(screen.getByRole('button', { name: 'Save proposed draft' }));
  // Assert
  await waitFor(() => expect(api.prepareResponsibilityDraft).toHaveBeenCalledWith('system-a', 'AU-11', null, 0, false, expect.any(AbortSignal)));
  expect(await screen.findByText('Saved proposed draft · revision 2')).toBeVisible();
  expect(api.confirmResponsibilityDraft).not.toHaveBeenCalled();
});

it('reports generation failure once without discarding edits', async () => {
  // Arrange
  vi.mocked(api.prepareResponsibilityDraft).mockRejectedValue(new api.ResponsibilityDraftError('Generation unavailable.', 503, context()));
  open(); await screen.findByDisplayValue('Recorded duties.');
  fireEvent.change(screen.getByRole('textbox', { name: 'Customer duties' }), { target: { value: 'My retained duties.' } });
  // Act
  fireEvent.click(screen.getByRole('button', { name: 'Prepare first pass' }));
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('Generation unavailable.');
  expect(screen.getAllByRole('alert')).toHaveLength(1);
  expect(screen.getByRole('textbox', { name: 'Customer duties' })).toHaveValue('My retained duties.');
  expect(screen.getByRole('button', { name: 'Use source records without AI' })).toBeEnabled();
});
