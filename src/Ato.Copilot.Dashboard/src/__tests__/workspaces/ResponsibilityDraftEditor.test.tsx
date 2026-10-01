import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import ResponsibilityDraftEditor, { type ResponsibilityDraftEdits } from '../../features/workspace-operations/system-capabilities/ResponsibilityDraftEditor';
import * as api from '../../api/responsibilityDrafts';
import type { ResponsibilityDraftContext, ResponsibilityDraft, ResponsibilityValue } from '../../api/responsibilityDrafts';

vi.mock('../../api/responsibilityDrafts', async original => ({
  ...await original<typeof import('../../api/responsibilityDrafts')>(),
  getResponsibilityDraft: vi.fn(), prepareResponsibilityDraft: vi.fn(), saveResponsibilityDraft: vi.fn(), confirmResponsibilityDraft: vi.fn(),
}));
const field = (value: string): ResponsibilityValue => ({
  value, origin: 'From system records', sourceIds: [], explanation: '', userEdited: false, sourceHash: 'source-1',
});
const context = (controlId = 'AU-11'): ResponsibilityDraftContext => ({
  systemId: 'system-a', controlId, baselineId: 'baseline-a', scopeId: null, canPrepare: true, sourceHash: 'source-1',
  scopes: [], sources: [], questions: ['Verify applicability'], conflicts: [], draft: null,
  sourceValues: { allocation: field('Customer'), provider: field(''), providerDuties: field(''),
    customer: field('Recorded duty'), scope: field(''), exclusions: field(''), source: field(''),
    basis: field('Recorded source basis'), information: field('') },
});
const draft = (controlId = 'AU-11'): ResponsibilityDraft => ({
  id: 'draft-a', revision: 1, status: 'Proposed', sourceHash: 'source-1', isStale: false, generationState: 'NotRequested',
  generationError: null, preparedAt: '2026-10-01', generatedAt: null, preparedBy: 'reviewer',
  reviewedBy: null, reviewedAt: null, values: context(controlId).sourceValues,
  suggestion: { values: context(controlId).sourceValues, questions: [], conflicts: [] }, sources: [], history: [],
});
function editor(controlId: string, edits: ResponsibilityDraftEdits, changed = vi.fn()) {
  return <MemoryRouter><ResponsibilityDraftEditor tenantId="tenant-a" systemId="system-a" source="provider"
    capabilityId="cap-a" controlId={controlId} edits={edits} onChanged={changed} /></MemoryRouter>;
}
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getResponsibilityDraft).mockImplementation(async (_system, control) => context(control));
  vi.mocked(api.prepareResponsibilityDraft).mockImplementation(async (_system, control) => ({ ...context(control), draft: draft(control) }));
  vi.mocked(api.saveResponsibilityDraft).mockImplementation(async (_system, _id, _revision, values) => {
    const saved = draft();
    for (const key of api.responsibilityFields) saved.values[key] = field(values[key]);
    return { ...saved, revision: 2 };
  });
});
describe('upstream responsibility lifecycle in the applied panel', () => {
  it('preserves corrections across controls and refreshed suggestions without competing storage', async () => {
    // Arrange
    const edits: ResponsibilityDraftEdits = new Map();
    const view = render(editor('AU-11', edits));
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Customer responsibility draft' })).toHaveValue('Recorded duty'));
    // Act
    fireEvent.change(screen.getByRole('textbox', { name: 'Customer responsibility draft' }), { target: { value: 'My correction' } });
    view.rerender(editor('AU-2', edits));
    await screen.findByRole('heading', { name: 'Prepared responsibility · AU-2' });
    view.rerender(editor('AU-11', edits));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Prepare first pass' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Prepare first pass' }));
    // Assert
    await waitFor(() => expect(api.prepareResponsibilityDraft).toHaveBeenCalledOnce());
    expect(screen.getByRole('textbox', { name: 'Customer responsibility draft' })).toHaveValue('My correction');
    expect(api.saveResponsibilityDraft).not.toHaveBeenCalled();
  });
  it('saves through the canonical API without accepting responsibility', async () => {
    // Arrange
    const changed = vi.fn();
    render(editor('AU-11', new Map(), changed));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save draft' })).toBeEnabled());
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
    // Assert
    await waitFor(() => expect(api.saveResponsibilityDraft).toHaveBeenCalledOnce());
    expect(api.prepareResponsibilityDraft).toHaveBeenCalledWith('system-a', 'AU-11', null, 0, false, expect.any(AbortSignal));
    expect(api.confirmResponsibilityDraft).not.toHaveBeenCalled();
    expect(changed).toHaveBeenCalledOnce();
  });
  it('preserves edits and never reports success after a failed save', async () => {
    // Arrange
    vi.mocked(api.saveResponsibilityDraft).mockRejectedValue(new Error('Concurrent review changed'));
    const changed = vi.fn();
    render(editor('AU-11', new Map(), changed));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save draft' })).toBeEnabled());
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Concurrent review changed');
    expect(changed).not.toHaveBeenCalled();
    expect(screen.queryByText(/Draft saved\./)).not.toBeInTheDocument();
  });
  it('keeps read-only viewers from saving or confirming canonical drafts', async () => {
    // Arrange
    vi.mocked(api.getResponsibilityDraft).mockResolvedValue({ ...context(), canPrepare: false });
    render(editor('AU-11', new Map()));
    // Act
    await screen.findByText(/assigned ISSM or ISSO/);
    // Assert
    expect(screen.getByRole('button', { name: 'Save draft' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Review saved responsibility' })).toBeDisabled();
  });
  it('requires saved-state review, both acknowledgements and notes before upstream confirmation', async () => {
    // Arrange
    vi.mocked(api.getResponsibilityDraft).mockResolvedValue({ ...context(), draft: draft() });
    vi.mocked(api.confirmResponsibilityDraft).mockResolvedValue({ ...draft(), status: 'Accepted',
      reviewedBy: 'reviewer', reviewedAt: '2026-10-01' });
    render(editor('AU-11', new Map()));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review saved responsibility' })).toBeEnabled());
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Review saved responsibility' }));
    expect(screen.getByRole('button', { name: 'Confirm AU-11 responsibility' })).toBeDisabled();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Provider coverage or system applicability verified' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Customer duties reviewed' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Review notes' }), { target: { value: 'Reviewed current sources and local duties.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirm AU-11 responsibility' }));
    // Assert
    await waitFor(() => expect(api.confirmResponsibilityDraft).toHaveBeenCalledOnce());
    expect(api.confirmResponsibilityDraft).toHaveBeenCalledWith('system-a', expect.objectContaining({ id: 'draft-a', revision: 1 }),
      'Reviewed current sources and local duties.', expect.any(AbortSignal));
  });
});
