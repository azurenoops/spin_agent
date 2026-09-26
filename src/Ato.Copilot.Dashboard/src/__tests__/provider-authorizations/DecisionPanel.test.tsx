import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { DecisionPanel } from '../../features/provider-authorizations/DecisionPanel';
import * as api from '../../features/provider-authorizations/api';
import type { BoundaryRevision, ExternalDecision, Offering, Page } from '../../features/provider-authorizations/types';
import { PackageImportError } from '../../features/package-imports/request';
import '../package-imports/crypto';

vi.mock('../../features/provider-authorizations/api', () => ({
  listDecisions: vi.fn(), listMicrosoftReferences: vi.fn(), listDecisionHistory: vi.fn(), listBoundaries: vi.fn(),
  createDecision: vi.fn(), reviseDecision: vi.fn(), recordDecision: vi.fn(), lifecycleDecision: vi.fn(),
}));

const offering: Offering = {
  offeringId: 'offering-a', providerId: 'provider-a', name: 'Synthetic offering', description: '',
  environments: ['AzureUSGovernment'], revision: 7, lifecycle: 'Draft',
  currentBoundaryRevisionId: 'boundary-a', currentHostingScopeRevisionId: null,
};
const boundary: BoundaryRevision = {
  offeringId: offering.offeringId, offeringRevision: 7, boundaryRevisionId: 'boundary-a',
  version: 2, snapshotHash: 'boundary-hash', predecessorRevisionId: 'boundary-old', createdAt: '2026-09-01T12:00:00Z',
  name: 'Limited service boundary', scopeStatement: 'Service only; excludes customer workloads.',
  services: [], componentSnapshotIds: [], includedScopes: [], exclusions: [],
  providerResponsibilities: [], customerResponsibilities: [], citations: [],
};
const decision: ExternalDecision = {
  recordId: 'decision-a', offeringId: offering.offeringId, revisionId: 'revision-a', revision: 3,
  snapshotHash: 'decision-hash', boundaryRevisionId: boundary.boundaryRevisionId,
  sourceCandidateRefs: [{ packageId: 'package-a', candidateId: 'candidate-a', revision: 4 }],
  recordKind: 'ProviderDecision', reference: 'SYNTHETIC-REF', issuingAuthority: 'Source authority',
  decisionAsStated: 'Fictional decision', issuedOn: '2026-01-01', effectiveOn: null, expiresOn: null,
  expiryBasis: 'NotRecorded', scopeStatement: 'No customer workloads.', conditions: ['Source condition'],
  citations: [{ packageId: 'package-a', artifactId: 'artifact-a', archivePath: 'original.pdf', locator: 'page 2', quote: 'Fictional test only.' }],
  metadataReviewState: 'Unconfirmed', currentStanding: 'Undetermined',
  recordedBy: null, recordedAt: null, impactReviewRequired: false,
};
const page = <T,>(items: T[], current = 1, total = items.length): Page<T> => ({ items, page: current, pageSize: 1, total });
const onChanged = vi.fn();

function sourceCitation() {
  const citation = decision.citations[0];
  if (!citation) throw new Error('Decision test fixture requires a retained source citation.');
  return citation;
}

function mount(inheritedOnly = false) {
  return render(<DecisionPanel offering={offering} inheritedOnly={inheritedOnly} onChanged={onChanged} />);
}
async function openDecision() {
  fireEvent.click(await screen.findByRole('button', { name: 'Open SYNTHETIC-REF' }));
}
async function startDraft() {
  fireEvent.click(await screen.findByRole('button', { name: 'Add authorization record' }));
  await screen.findByRole('option', { name: /Limited service boundary/ });
}
function change(label: string, value: string) {
  fireEvent.change(screen.getByLabelText(label), { target: { value } });
}
function fillDraft() {
  change('Boundary revision', 'boundary-a');
  change('Reference', 'NEW-REF');
  change('Issuing authority as stated', 'Source authority');
  change('Scope statement', 'Services only.');
}
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(done => { resolve = done; });
  return { promise, resolve };
}

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.listDecisions).mockResolvedValue(page([decision]));
  vi.mocked(api.listMicrosoftReferences).mockResolvedValue(page([]));
  vi.mocked(api.listDecisionHistory).mockResolvedValue(page([decision]));
  vi.mocked(api.listBoundaries).mockResolvedValue(page([boundary]));
  vi.mocked(api.createDecision).mockResolvedValue({ ...decision, recordId: 'new-record', reference: 'NEW-REF' });
  vi.mocked(api.reviseDecision).mockResolvedValue({ ...decision, revision: 4, revisionId: 'revision-next', snapshotHash: 'next-hash' });
  vi.mocked(api.recordDecision).mockResolvedValue({ ...decision, metadataReviewState: 'Recorded', recordedBy: 'human-reviewer', recordedAt: '2026-09-24T12:00:00Z' });
  vi.mocked(api.lifecycleDecision).mockResolvedValue({ eventId: 'event-a', record: { ...decision, currentStanding: 'Withdrawn' }, impactReviewId: 'impact-a' });
});

describe('isolated external decision panel', () => {
  it('creates an unconfirmed draft with explicit boundary and unknown dates, never records it automatically', async () => {
    // Arrange
    mount();
    await startDraft();
    expect(screen.getByLabelText('Boundary revision')).toHaveValue('');
    expect(screen.getByLabelText('Boundary revision')).toBeInvalid();
    // Act
    fillDraft();
    fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
    // Assert
    await waitFor(() => expect(api.createDecision).toHaveBeenCalledWith('offering-a', expect.objectContaining({
      expectedOfferingRevision: 7, boundaryRevisionId: 'boundary-a', recordKind: 'ProviderDecision',
      issuedOn: null, effectiveOn: null, expiresOn: null, expiryBasis: 'NotRecorded', sourceCandidateRefs: [],
    }), expect.any(String)));
    expect(api.recordDecision).not.toHaveBeenCalled();
    expect(onChanged).toHaveBeenCalledOnce();
    expect(await screen.findByText(/SPIN does not issue a mission ATO/)).toBeInTheDocument();
  });

  it('keeps date basis explicit and prevents discarding a stated expiry date', async () => {
    // Arrange
    mount();
    await startDraft();
    fillDraft();
    // Act
    change('Expiry basis', 'DateStated');
    // Assert
    expect(screen.getByLabelText('Expiry date as stated')).toBeInvalid();
    change('Expiry date as stated', '2027-03-04');
    change('Expiry basis', 'NoExpiryStated');
    expect(screen.getByLabelText('Expiry date as stated')).toHaveValue('2027-03-04');
    fireEvent.submit(screen.getByRole('button', { name: 'Save draft' }).closest('form')!);
    expect(await screen.findByRole('alert')).toHaveTextContent('Expiry basis');
    expect(api.createDecision).not.toHaveBeenCalled();
    change('Expiry basis', 'DateStated');
    fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
    await waitFor(() => expect(api.createDecision).toHaveBeenCalledWith('offering-a', expect.objectContaining({
      expiryBasis: 'DateStated', expiresOn: '2027-03-04',
    }), expect.any(String)));
  });

  it('records only after human review of the exact immutable revision and snapshot', async () => {
    // Arrange
    mount();
    await openDecision();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Review metadata' }));
    change('Review rationale', 'Compared with retained original.');
    expect(screen.getByLabelText('I reviewed this exact source metadata and boundary snapshot.')).toBeInvalid();
    fireEvent.click(screen.getByLabelText('I reviewed this exact source metadata and boundary snapshot.'));
    fireEvent.click(screen.getByRole('button', { name: 'Record external metadata' }));
    // Assert
    await waitFor(() => expect(api.recordDecision).toHaveBeenCalledWith('offering-a', decision, 'Compared with retained original.'));
    expect(api.createDecision).not.toHaveBeenCalled();
    expect(await screen.findByText('human-reviewer')).toBeInTheDocument();
  });

  it('uses the new aggregate concurrency revision after recording while preserving immutable source identity', async () => {
    // Arrange
    vi.mocked(api.recordDecision).mockResolvedValue({ ...decision, revision: 4, metadataReviewState: 'Recorded',
      recordedBy: 'human-reviewer', recordedAt: '2026-09-24T12:00:00Z' });
    mount();
    await openDecision();
    fireEvent.click(screen.getByRole('button', { name: 'Review metadata' }));
    change('Review rationale', 'Compared the exact source.');
    fireEvent.click(screen.getByLabelText('I reviewed this exact source metadata and boundary snapshot.'));
    fireEvent.click(screen.getByRole('button', { name: 'Record external metadata' }));
    await screen.findByText('human-reviewer');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Withdraw or supersede' }));
    change('Lifecycle action', 'Withdrawn');
    change('Lifecycle effective date', '2026-09-24');
    change('Lifecycle rationale', 'External withdrawal with evidence.');
    fireEvent.click(screen.getByRole('button', { name: 'Add citation' }));
    Object.entries(sourceCitation()).forEach(([key, value]) => change(`${key} 1`, value));
    fireEvent.click(screen.getByRole('button', { name: 'Save lifecycle event' }));
    // Assert
    await waitFor(() => expect(api.lifecycleDecision).toHaveBeenCalledWith('offering-a', 'decision-a',
      expect.objectContaining({ expectedRevision: 4, kind: 'Withdrawn' }), expect.any(String)));
    expect(api.recordDecision).toHaveBeenCalledWith('offering-a',
      expect.objectContaining({ revision: 3, revisionId: 'revision-a', snapshotHash: 'decision-hash' }), 'Compared the exact source.');
  });

  it('revises the selected existing record, preserving dates, citations and candidate revision without overwriting history', async () => {
    // Arrange
    mount();
    await openDecision();
    fireEvent.click(screen.getByRole('button', { name: 'Revise draft' }));
    // Act
    change('Reference', 'CORRECTED-REF');
    fireEvent.click(screen.getByRole('button', { name: 'Save successor draft' }));
    // Assert
    await waitFor(() => expect(api.reviseDecision).toHaveBeenCalledWith('offering-a', 'decision-a', expect.objectContaining({
      expectedRevision: 3, reference: 'CORRECTED-REF', issuedOn: '2026-01-01', effectiveOn: null,
      expiresOn: null, expiryBasis: 'NotRecorded', citations: decision.citations, sourceCandidateRefs: decision.sourceCandidateRefs,
    })));
    expect(api.createDecision).not.toHaveBeenCalled();
    expect(decision.reference).toBe('SYNTHETIC-REF');
  });

  it('retains inputs after 409 and requires explicit refreshed revision adoption before retry', async () => {
    // Arrange
    vi.mocked(api.reviseDecision).mockRejectedValueOnce(new PackageImportError('Decision revision changed', 409));
    mount();
    await openDecision();
    fireEvent.click(screen.getByRole('button', { name: 'Revise draft' }));
    change('Reference', 'RETAIN-ME');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save successor draft' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Decision revision changed');
    expect(screen.getByLabelText('Reference')).toHaveValue('RETAIN-ME');
    expect(screen.getByRole('button', { name: 'Save successor draft' })).toBeDisabled();
    vi.mocked(api.listDecisions).mockResolvedValue(page([{ ...decision, revision: 4, revisionId: 'remote-revision', snapshotHash: 'remote-hash' }]));
    fireEvent.click(screen.getByRole('button', { name: 'Refresh current records' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Use refreshed revision with retained inputs' }));
    expect(screen.getByLabelText('Reference')).toHaveValue('RETAIN-ME');
    fireEvent.click(screen.getByRole('button', { name: 'Save successor draft' }));
    await waitFor(() => expect(api.reviseDecision).toHaveBeenLastCalledWith('offering-a', 'decision-a', expect.objectContaining({
      expectedRevision: 4, reference: 'RETAIN-ME',
    })));
  });

  it('prevents concurrent double submit and keeps an uncertain retry on the original intent and idempotency key', async () => {
    // Arrange
    const pending = deferred<ExternalDecision>();
    vi.mocked(api.createDecision).mockReturnValueOnce(pending.promise).mockRejectedValueOnce(new Error('Connection lost'));
    mount();
    await startDraft();
    fillDraft();
    const form = screen.getByRole('button', { name: 'Save draft' }).closest('form')!;
    // Act
    fireEvent.submit(form);
    fireEvent.submit(form);
    // Assert
    expect(api.createDecision).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('button', { name: 'Cancel editing' })).toBeDisabled();
    await act(async () => { pending.resolve({ ...decision, reference: 'NEW-REF' }); });
    fireEvent.click(screen.getByRole('button', { name: 'Add authorization record' }));
    await screen.findByRole('option', { name: /Limited service boundary/ });
    fillDraft();
    fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Outcome uncertain');
    expect(screen.getByLabelText('Reference')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancel editing' })).toBeDisabled();
    const original = vi.mocked(api.createDecision).mock.calls[1];
    fireEvent.click(screen.getByRole('button', { name: 'Retry same operation' }));
    await waitFor(() => expect(api.createDecision).toHaveBeenCalledTimes(3));
    expect(vi.mocked(api.createDecision).mock.calls[2]).toEqual(original);
  });

  it('paginates decisions and boundary choices without automatically selecting coverage', async () => {
    // Arrange
    vi.mocked(api.listDecisions).mockImplementation(async (_id, current = 1) => page(current === 1 ? [decision] : [{ ...decision, recordId: 'b', reference: 'SECOND' }], current, 2));
    vi.mocked(api.listBoundaries).mockImplementation(async (_id, current = 1) => page(current === 1 ? [boundary] : [{ ...boundary, boundaryRevisionId: 'boundary-b', name: 'Second boundary' }], current, 2));
    mount();
    await screen.findByRole('button', { name: 'Open SYNTHETIC-REF' });
    // Act
    fireEvent.click(within(screen.getByRole('region', { name: 'Decision list' })).getByRole('button', { name: 'Next' }));
    // Assert
    expect(await screen.findByRole('button', { name: 'Open SECOND' })).toBeInTheDocument();
    await startDraft();
    fireEvent.click(within(screen.getByRole('region', { name: 'Boundary choices' })).getByRole('button', { name: 'Next' }));
    await screen.findByRole('option', { name: /Second boundary/ });
    expect(screen.getByLabelText('Boundary revision')).toHaveValue('');
    change('Boundary revision', 'boundary-b');
    change('Reference', 'PAGE-TWO');
    change('Scope statement', 'Explicitly bounded.');
    fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
    await waitFor(() => expect(api.createDecision).toHaveBeenCalledWith('offering-a', expect.objectContaining({ boundaryRevisionId: 'boundary-b' }), expect.any(String)));
  });

  it('shows paginated immutable history including source metadata, review actor, dates and snapshot', async () => {
    // Arrange
    vi.mocked(api.listDecisionHistory).mockImplementation(async (_id, _record, current = 1) => page([{
      ...decision, revision: current, revisionId: `historical-${current}`, snapshotHash: `historical-hash-${current}`,
      reference: `HISTORY-${current}`, metadataReviewState: 'Recorded', recordedBy: 'prior-reviewer',
      recordedAt: '2026-01-02T12:00:00Z', expiryBasis: 'NoExpiryStated',
    }], current, 2));
    mount();
    await openDecision();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'View history' }));
    // Assert
    const history = await screen.findByRole('region', { name: 'Decision history' });
    expect(await within(history).findByText('HISTORY-1')).toBeInTheDocument();
    expect(within(history).getByText('historical-hash-1')).toBeInTheDocument();
    expect(within(history).getByText('prior-reviewer')).toBeInTheDocument();
    expect(within(history).getByText('Fictional test only.')).toBeInTheDocument();
    expect(within(history).getByText('No expiry stated in source')).toBeInTheDocument();
    expect(within(history).queryByRole('textbox')).not.toBeInTheDocument();
    fireEvent.click(within(history).getByRole('button', { name: 'Next' }));
    expect(await within(history).findByText('HISTORY-2')).toBeInTheDocument();
    expect(api.listDecisionHistory).toHaveBeenLastCalledWith('offering-a', 'decision-a', 2, expect.any(AbortSignal));
  });

  it('separates inherited Microsoft references from provider decisions in list and creation', async () => {
    // Arrange
    vi.mocked(api.listMicrosoftReferences).mockResolvedValue(page([{ ...decision, recordId: 'inherited-a', recordKind: 'InheritedMicrosoftReference', reference: 'MICROSOFT-REF' }]));
    mount(true);
    // Act
    await screen.findByRole('button', { name: 'Open MICROSOFT-REF' });
    fireEvent.click(screen.getByRole('button', { name: 'Add reference' }));
    await screen.findByRole('option', { name: /Limited service boundary/ });
    fillDraft();
    fireEvent.click(screen.getByRole('button', { name: 'Save reference draft' }));
    // Assert
    expect(screen.queryByRole('button', { name: 'Open SYNTHETIC-REF' })).not.toBeInTheDocument();
    await waitFor(() => expect(api.createDecision).toHaveBeenCalledWith('offering-a', expect.objectContaining({
      recordKind: 'InheritedMicrosoftReference',
    }), expect.any(String)));
  });

  it('requires lifecycle rationale, source evidence, explicit date and replacement for supersession', async () => {
    // Arrange
    vi.mocked(api.listDecisions).mockResolvedValue(page([{ ...decision, metadataReviewState: 'Recorded', currentStanding: 'CurrentAsRecorded' }]));
    mount();
    await openDecision();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Withdraw or supersede' }));
    change('Lifecycle action', 'Superseded');
    change('Lifecycle effective date', '2026-09-24');
    change('Lifecycle rationale', 'Replaced by source decision.');
    // Assert
    expect(screen.getByLabelText('Replacement revision ID')).toBeInvalid();
    fireEvent.submit(screen.getByRole('button', { name: 'Save lifecycle event' }).closest('form')!);
    expect(await screen.findByRole('alert')).toHaveTextContent('source evidence');
    expect(api.lifecycleDecision).not.toHaveBeenCalled();
    change('Replacement revision ID', 'replacement-exact-revision');
    fireEvent.click(screen.getByRole('button', { name: 'Add citation' }));
    Object.entries({ ...sourceCitation() }).forEach(([key, value]) => change(`${key} 1`, value));
    fireEvent.click(screen.getByRole('button', { name: 'Save lifecycle event' }));
    await waitFor(() => expect(api.lifecycleDecision).toHaveBeenCalledWith('offering-a', 'decision-a', {
      expectedRevision: 3, kind: 'Superseded', effectiveOn: '2026-09-24', replacementRevisionId: 'replacement-exact-revision',
      rationale: 'Replaced by source decision.', citations: decision.citations,
    }, expect.any(String)));
    expect(api.recordDecision).not.toHaveBeenCalled();
  });

  it('supports exact manually entered source candidate references with no invented revision', async () => {
    // Arrange
    mount();
    await startDraft();
    fillDraft();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Add source candidate' }));
    change('Candidate package ID 1', 'package-retained');
    change('Candidate ID 1', 'candidate-retained');
    // Assert
    expect(screen.getByLabelText('Candidate revision 1')).toBeInvalid();
    change('Candidate revision 1', '12');
    fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
    await waitFor(() => expect(api.createDecision).toHaveBeenCalledWith('offering-a', expect.objectContaining({
      sourceCandidateRefs: [{ packageId: 'package-retained', candidateId: 'candidate-retained', revision: 12 }],
    }), expect.any(String)));
  });

  it('surfaces denied reads and retries without presenting successful empty records', async () => {
    // Arrange
    vi.mocked(api.listDecisions).mockRejectedValueOnce(new PackageImportError('Provider access denied', 403));
    mount();
    // Act
    expect(await screen.findByRole('alert')).toHaveTextContent('Provider access denied');
    // Assert
    expect(screen.queryByText('No decisions on this page.')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByRole('button', { name: 'Open SYNTHETIC-REF' })).toBeInTheDocument();
  });

  it('requires renewed human confirmation after a stale review finds the exact record on a later page', async () => {
    // Arrange
    vi.mocked(api.recordDecision).mockRejectedValueOnce(new PackageImportError('Snapshot changed', 409));
    mount();
    await openDecision();
    fireEvent.click(screen.getByRole('button', { name: 'Review metadata' }));
    change('Review rationale', 'Retain my review explanation.');
    fireEvent.click(screen.getByLabelText('I reviewed this exact source metadata and boundary snapshot.'));
    fireEvent.click(screen.getByRole('button', { name: 'Record external metadata' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Snapshot changed');
    const refreshed = { ...decision, revision: 8, revisionId: 'exact-new-revision', snapshotHash: 'exact-new-hash' };
    vi.mocked(api.listDecisions).mockImplementation(async (_id, current = 1) =>
      page(current === 1 ? [{ ...decision, recordId: 'unrelated', reference: 'UNRELATED' }] : [refreshed], current, 2));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Refresh current records' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Use refreshed revision with retained inputs' }));
    // Assert
    expect(screen.getByLabelText('Review rationale')).toHaveValue('Retain my review explanation.');
    expect(screen.getByLabelText('I reviewed this exact source metadata and boundary snapshot.')).not.toBeChecked();
    fireEvent.submit(screen.getByRole('button', { name: 'Record external metadata' }).closest('form')!);
    expect(await screen.findByRole('alert')).toHaveTextContent('explicit human review');
    expect(api.recordDecision).toHaveBeenCalledTimes(1);
    fireEvent.click(screen.getByLabelText('I reviewed this exact source metadata and boundary snapshot.'));
    fireEvent.click(screen.getByRole('button', { name: 'Record external metadata' }));
    await waitFor(() => expect(api.recordDecision).toHaveBeenLastCalledWith('offering-a', refreshed, 'Retain my review explanation.'));
  });

  it('keeps stale draft inputs when the selected record disappears instead of using a different decision', async () => {
    // Arrange
    vi.mocked(api.reviseDecision).mockRejectedValueOnce(new PackageImportError('Revision changed', 409));
    mount();
    await openDecision();
    fireEvent.click(screen.getByRole('button', { name: 'Revise draft' }));
    change('Reference', 'RETAINED-DRAFT');
    fireEvent.click(screen.getByRole('button', { name: 'Save successor draft' }));
    await screen.findByRole('alert');
    vi.mocked(api.listDecisions).mockResolvedValue(page([{ ...decision, recordId: 'unrelated-record' }]));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Refresh current records' }));
    // Assert
    expect(await screen.findByText(/The selected decision is unavailable/)).toBeInTheDocument();
    expect(screen.getByLabelText('Reference')).toHaveValue('RETAINED-DRAFT');
    expect(screen.queryByRole('button', { name: 'Use refreshed revision with retained inputs' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save successor draft' })).toBeDisabled();
    expect(api.reviseDecision).toHaveBeenCalledTimes(1);
  });

  it('does not record missing authority or evidence, and blocks actions without exact snapshot metadata', async () => {
    // Arrange
    vi.mocked(api.listDecisions).mockResolvedValue(page([{ ...decision, issuingAuthority: null, citations: [] }]));
    const view = mount();
    await openDecision();
    fireEvent.click(screen.getByRole('button', { name: 'Review metadata' }));
    change('Review rationale', 'Human attempted review.');
    fireEvent.click(screen.getByLabelText('I reviewed this exact source metadata and boundary snapshot.'));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Record external metadata' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('supporting citations');
    expect(api.recordDecision).not.toHaveBeenCalled();
    view.unmount();
    vi.mocked(api.listDecisions).mockResolvedValue(page([{ ...decision, snapshotHash: '' }]));
    mount();
    await openDecision();
    expect(screen.getByRole('alert')).toHaveTextContent('Exact revision and snapshot are unavailable');
    expect(screen.getByRole('button', { name: 'Review metadata' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Revise draft' })).toBeDisabled();
  });

  it('appends withdrawal evidence without reusing the original decision citations by default', async () => {
    // Arrange
    vi.mocked(api.listDecisions).mockResolvedValue(page([{ ...decision, metadataReviewState: 'Recorded', currentStanding: 'Expired' }]));
    mount();
    await openDecision();
    fireEvent.click(screen.getByRole('button', { name: 'Withdraw or supersede' }));
    // Act
    change('Lifecycle action', 'Withdrawn');
    change('Lifecycle effective date', '2026-09-24');
    change('Lifecycle rationale', 'Withdrawn per original notice.');
    fireEvent.submit(screen.getByRole('button', { name: 'Save lifecycle event' }).closest('form')!);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('supporting source evidence');
    expect(api.lifecycleDecision).not.toHaveBeenCalled();
    expect(screen.queryByLabelText('Replacement revision ID')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Add citation' }));
    const evidence = { ...sourceCitation(), artifactId: 'withdrawal-notice', quote: 'Withdrawn on source-stated date.' };
    Object.entries(evidence).forEach(([key, value]) => change(`${key} 1`, value));
    fireEvent.click(screen.getByRole('button', { name: 'Save lifecycle event' }));
    await waitFor(() => expect(api.lifecycleDecision).toHaveBeenCalledWith('offering-a', 'decision-a', {
      expectedRevision: 3, kind: 'Withdrawn', effectiveOn: '2026-09-24',
      rationale: 'Withdrawn per original notice.', citations: [evidence],
    }, expect.any(String)));
    expect(await screen.findByRole('button', { name: 'Withdraw or supersede' })).toBeDisabled();
  });

  it('preserves an unknown expiry rather than converting it to no-expiry and edits only successor citations', async () => {
    // Arrange
    mount();
    await openDecision();
    fireEvent.click(screen.getByRole('button', { name: 'Revise draft' }));
    // Act
    change('quote 1', 'Successor source quote.');
    change('Issue date as stated', '');
    change('Effective date as stated', '2026-09-24');
    change('Decision as stated', 'Updated source statement.');
    change('Conditions (one per line)', 'One condition\nAnother condition');
    fireEvent.click(screen.getByRole('button', { name: 'Save successor draft' }));
    // Assert
    await waitFor(() => expect(api.reviseDecision).toHaveBeenCalledWith('offering-a', 'decision-a', expect.objectContaining({
      issuedOn: null, effectiveOn: '2026-09-24', expiresOn: null, expiryBasis: 'NotRecorded',
      decisionAsStated: 'Updated source statement.', conditions: ['One condition', 'Another condition'],
      citations: [{ ...sourceCitation(), quote: 'Successor source quote.' }],
    })));
    expect(sourceCitation().quote).toBe('Fictional test only.');
  });

  it('resets selected private snapshots and drafts when the offering changes', async () => {
    // Arrange
    const view = mount();
    await openDecision();
    fireEvent.click(screen.getByRole('button', { name: 'Revise draft' }));
    change('Reference', 'PRIVATE-OFFERING-A-DRAFT');
    vi.mocked(api.listDecisions).mockResolvedValue(page([]));
    // Act
    view.rerender(<DecisionPanel offering={{ ...offering, offeringId: 'offering-b' }} onChanged={onChanged} />);
    // Assert
    expect(await screen.findByText('No decisions on this page.')).toBeInTheDocument();
    expect(screen.queryByDisplayValue('PRIVATE-OFFERING-A-DRAFT')).not.toBeInTheDocument();
    expect(screen.queryByText('Fictional test only.')).not.toBeInTheDocument();
    expect(api.listDecisions).toHaveBeenLastCalledWith('offering-b', 1, expect.any(AbortSignal), 'ProviderDecision');
    expect(api.reviseDecision).not.toHaveBeenCalled();
  });

  it('retains stale new-draft inputs until the parent supplies a refreshed offering revision', async () => {
    // Arrange
    vi.mocked(api.createDecision).mockRejectedValueOnce(new PackageImportError('Offering revision changed', 409));
    const view = mount();
    await startDraft();
    fillDraft();
    fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
    await screen.findByRole('alert');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Refresh current records' }));
    // Assert
    expect(onChanged).toHaveBeenCalledOnce();
    expect(screen.queryByRole('button', { name: 'Use refreshed revision with retained inputs' })).not.toBeInTheDocument();
    view.rerender(<DecisionPanel offering={{ ...offering, revision: 8 }} onChanged={onChanged} />);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Use refreshed revision with retained inputs' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Use refreshed revision with retained inputs' }));
    expect(screen.getByLabelText('Reference')).toHaveValue('NEW-REF');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save draft' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
    await waitFor(() => expect(api.createDecision).toHaveBeenLastCalledWith('offering-a', expect.objectContaining({
      expectedOfferingRevision: 8, reference: 'NEW-REF',
    }), expect.any(String)));
  });
});
