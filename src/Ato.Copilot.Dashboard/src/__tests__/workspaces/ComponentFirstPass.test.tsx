import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { Component, type ReactNode } from 'react';
import ComponentFirstPass from '../../features/workspace-operations/system-capabilities/ComponentFirstPass';
import { getResponsibilityDraft, prepareResponsibilityDraft, type ResponsibilityDraft } from '../../api/responsibilityDrafts';
import { appliedResponsibilityContext } from '../helpers/appliedResponsibilityContext';
import { componentReviewFixture } from '../fixtures/componentReview';

vi.mock('../../api/responsibilityDrafts', () => ({ getResponsibilityDraft: vi.fn(), prepareResponsibilityDraft: vi.fn() }));
const sourceContext = () => ({
  ...appliedResponsibilityContext('system', 'CP-9'),
  scopes: [{ id: 'scope', name: 'Flankspeed production', provider: 'Flankspeed', reviewRequired: false }],
});
class ParserFailureBoundary extends Component<{ children: ReactNode }, { error: string }> {
  state = { error: '' };
  static getDerivedStateFromError(error: Error) { return { error: error.message }; }
  render() { return this.state.error ? <p role="alert">{this.state.error}</p> : this.props.children; }
}
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(getResponsibilityDraft).mockImplementation(async (_system, _control, scopeId) => ({
    ...sourceContext(), scopeId,
    sources: scopeId ? [{ id: 'release', title: 'Published release 7', origin: 'From provider source', version: 'pinned-v7',
      content: '{"description":"Azure Backup: RPO target 24 hours; RTO target 8 hours."}', href: null }] : [],
  }));
});
function preparedDraft(patch: Partial<ResponsibilityDraft> = {}): ResponsibilityDraft {
  const context = sourceContext();
  return { id: 'draft', revision: 1, status: 'Proposed', sourceHash: 'source-1', isStale: false,
    generationState: 'Prepared', generationError: null, preparedAt: '2026-10-02', generatedAt: '2026-10-02',
    preparedBy: 'author', reviewedAt: null, reviewedBy: null, sources: [], values: context.sourceValues,
    suggestion: { values: context.sourceValues, questions: [], conflicts: [] }, history: [], ...patch };
}
it('explains absent controls without pretending AI preparation is available', () => {
  // Arrange
  const data = componentReviewFixture();
  // Act
  render(<ComponentFirstPass data={{ ...data, item: { ...data.item, controlIds: [] } }}
    systemId="system" canUse onBusyChange={vi.fn()} onUse={vi.fn()} currentUsage="" />);
  // Assert
  expect(screen.getByText(/A baseline control and supporting capability are required/)).toBeVisible();
  expect(getResponsibilityDraft).not.toHaveBeenCalled();
});
it('does not silently reinterpret unexpected parser failures as provider facts', async () => {
  // Arrange
  vi.mocked(getResponsibilityDraft).mockImplementation(async (_system, _control, scopeId) => ({
    ...sourceContext(), scopeId, sources: scopeId ? [{ id: 'release', title: 'Published source',
      origin: 'From provider source', version: 'pinned', content: '{"description":"RPO target 24 hours"}', href: null }] : [],
  }));
  render(<ParserFailureBoundary><ComponentFirstPass data={componentReviewFixture()} systemId="system" canUse
    onBusyChange={vi.fn()} onUse={vi.fn()} currentUsage="" /></ParserFailureBoundary>);
  await screen.findByLabelText('Provider scope');
  const parse = JSON.parse;
  const parser = vi.spyOn(JSON, 'parse').mockImplementation((content, reviver) => {
    if (content === '{"description":"RPO target 24 hours"}') throw new RangeError('Source parser unavailable');
    return parse(content, reviver);
  });
  try {
    // Act
    fireEvent.change(screen.getByLabelText('Provider scope'), { target: { value: 'scope' } });
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Source parser unavailable');
    expect(screen.queryByText('24 hours')).not.toBeInTheDocument();
  } finally { parser.mockRestore(); }
});
it('retries source-read failures and changes control context without inventing scope', async () => {
  // Arrange
  vi.mocked(getResponsibilityDraft).mockRejectedValueOnce(new Error('Source read failed'))
    .mockImplementation(async (_system, controlId) => ({ ...sourceContext(), controlId }));
  const data = componentReviewFixture();
  render(<ComponentFirstPass data={{ ...data, item: { ...data.item, controlIds: ['CP-9', 'CP-10'] } }}
    systemId="system" canUse onBusyChange={vi.fn()} onUse={vi.fn()} currentUsage="" />);
  // Act
  await screen.findByText('Source read failed');
  fireEvent.click(screen.getByRole('button', { name: 'Retry source context' }));
  await screen.findByLabelText('Provider scope');
  fireEvent.change(screen.getByLabelText('Control context'), { target: { value: 'CP-10' } });
  // Assert
  await waitFor(() => expect(getResponsibilityDraft).toHaveBeenLastCalledWith('system', 'CP-10', null, expect.any(AbortSignal)));
});
it.each([
  ['RPO target 1 hour; RTO target 1 day.', '1 hour', '1 day'],
  ['{"RPO":"2 days","RTO":"1 minute"}', '2 days', '1 minute'],
  ['{"rpoMinutes":"2","rtoHours":1}', '2 minutes', '1 hour'],
  ['["RPO: 24 hours",null,false,5,{"note":"RTO: 8 minutes"}]', '24 hours', '8 minutes'],
])('quotes explicit provider units from %s', async (content, rpo, rto) => {
  // Arrange
  vi.mocked(getResponsibilityDraft).mockImplementation(async (_system, _control, scopeId) => ({
    ...sourceContext(), scopeId, sources: [{ id: 'release', title: 'Published release', origin: 'From provider source',
      version: 'pinned', content, href: null }],
  }));
  render(<ComponentFirstPass data={componentReviewFixture()} systemId="system" canUse onBusyChange={vi.fn()} onUse={vi.fn()} currentUsage="" />);
  await screen.findByLabelText('Provider scope');
  // Act
  fireEvent.change(screen.getByLabelText('Provider scope'), { target: { value: 'scope' } });
  // Assert
  expect(await screen.findByText(rpo)).toBeVisible();
  expect(screen.getByText(rto)).toBeVisible();
  expect(screen.getByText('Results not verified')).toBeVisible();
});
it('carries explicit published facts unchanged and explains read-only preparation and missing targets', async () => {
  // Arrange
  const context = sourceContext();
  const sourceValues = { ...context.sourceValues };
  for (const field of ['provider', 'allocation', 'providerDuties', 'customer', 'scope', 'exclusions'] as const)
    sourceValues[field] = { ...sourceValues[field], value: `Published ${field}`, origin: 'From provider source' };
  vi.mocked(getResponsibilityDraft).mockImplementation(async (_system, _control, scopeId) => ({
    ...context, scopeId, canPrepare: false, sourceValues,
    scopes: [{ ...context.scopes[0]!, reviewRequired: true }],
    sources: [{ id: 'release', title: 'Pinned source', origin: 'From provider source', version: 'release-7',
      content: '{"retention":"Published retention record"}', href: '/systems/system/provider-relationships' }],
  }));
  const data = componentReviewFixture();
  render(<ComponentFirstPass data={{ ...data, item: { ...data.item, source: 'local' } }}
    systemId="system" canUse={false} onBusyChange={vi.fn()} onUse={vi.fn()} currentUsage="" />);
  await screen.findByLabelText('Provider scope');
  // Act
  fireEvent.change(screen.getByLabelText('Provider scope'), { target: { value: 'scope' } });
  // Assert
  expect(await screen.findByText('The selected provider scope requires applicability review.')).toBeVisible();
  expect(screen.getByText('No explicit RPO or RTO target is available in the selected source.')).toBeVisible();
  expect(screen.getByRole('button', { name: 'Prepare first pass' })).toBeDisabled();
  expect(screen.getByText('Published responsibility split')).toBeVisible();
  expect(screen.getByText('Provider duties')).toBeVisible();
  expect(screen.getByText('Customer duties')).toBeVisible();
  expect(screen.getByRole('link', { name: 'Open source record', hidden: true })).toHaveAttribute('href', '/systems/system/provider-relationships');
  // Act
  fireEvent.change(screen.getByLabelText('Provider scope'), { target: { value: '' } });
  // Assert
  await waitFor(() => expect(getResponsibilityDraft).toHaveBeenLastCalledWith('system', 'CP-9', null, expect.any(AbortSignal)));
});
it('blocks stale or failed proposals and retains their unresolved questions', async () => {
  // Arrange
  const draft = preparedDraft({ isStale: true, generationState: 'Failed', generationError: 'Generation failed' });
  vi.mocked(getResponsibilityDraft).mockResolvedValue({ ...sourceContext(), draft });
  render(<ComponentFirstPass data={componentReviewFixture()} systemId="system" canUse onBusyChange={vi.fn()} onUse={vi.fn()} currentUsage="" />);
  // Act
  await screen.findByLabelText('Provider scope');
  // Assert
  expect(screen.getByRole('alert')).toHaveTextContent('Generation failed');
  expect(screen.getByText(/saved proposal has stale sources/)).toBeVisible();
  expect(screen.getByRole('button', { name: 'Use proposed wording' })).toBeDisabled();
  expect(screen.getByText(/Source references: Not recorded/)).toBeVisible();
});
it.each([null, ''])('blocks a failed generation even without a diagnostic message (%s)', async generationError => {
  // Arrange
  const draft = preparedDraft({ generationState: 'Failed', generationError });
  vi.mocked(getResponsibilityDraft).mockResolvedValue({ ...sourceContext(), draft });
  render(<ComponentFirstPass data={componentReviewFixture()} systemId="system" canUse onBusyChange={vi.fn()} onUse={vi.fn()} currentUsage="" />);
  // Act
  await screen.findByLabelText('Provider scope');
  // Assert
  expect(screen.getByRole('alert')).toHaveTextContent('AI preparation failed');
  expect(screen.getByRole('button', { name: 'Use proposed wording' })).toBeDisabled();
});
it('refreshes a recorded duty using the current revision and requires explicit copying', async () => {
  // Arrange
  const draft = preparedDraft();
  vi.mocked(getResponsibilityDraft).mockResolvedValue({ ...sourceContext(), draft });
  vi.mocked(prepareResponsibilityDraft).mockResolvedValue({ ...sourceContext(), draft: { ...draft, revision: 2 } });
  const use = vi.fn();
  render(<ComponentFirstPass data={componentReviewFixture()} systemId="system" canUse onBusyChange={vi.fn()} onUse={use} currentUsage="" />);
  await screen.findByLabelText('Provider scope');
  // Act
  fireEvent.click(screen.getByRole('button', { name: 'Prepare first pass' }));
  await waitFor(() => expect(prepareResponsibilityDraft).toHaveBeenCalledWith('system', 'CP-9', null, 1, true, expect.any(AbortSignal)));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Use proposed wording' })).toBeEnabled());
  expect(use).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button', { name: 'Use proposed wording' }));
  // Assert
  expect(use).toHaveBeenCalledWith('Recorded duty', { draftId: 'draft', revision: 2 });
});
it.each([false, true])('cancels a pending preparation on unmount (failure=%s)', async failed => {
  // Arrange
  let resolve!: (context: ReturnType<typeof sourceContext>) => void;
  let reject!: (reason: Error) => void;
  const pending = new Promise<ReturnType<typeof sourceContext>>((done, error) => { resolve = done; reject = error; });
  vi.mocked(prepareResponsibilityDraft).mockReturnValue(pending);
  const use = vi.fn();
  const mounted = render(<ComponentFirstPass data={componentReviewFixture()} systemId="system" canUse onBusyChange={vi.fn()} onUse={use} currentUsage="" />);
  await screen.findByLabelText('Provider scope');
  // Act
  fireEvent.click(screen.getByRole('button', { name: 'Prepare first pass' }));
  const signal = vi.mocked(prepareResponsibilityDraft).mock.calls[0]?.[5];
  mounted.unmount();
  await act(async () => { if (failed) reject(new Error('Cancelled')); else resolve(sourceContext()); await pending.catch(() => undefined); });
  // Assert
  expect(signal?.aborted).toBe(true);
  expect(use).not.toHaveBeenCalled();
});
it('quotes recovery targets only from selected published provider source, not verified results', async () => {
  // Arrange
  const use = vi.fn();
  render(<ComponentFirstPass data={componentReviewFixture()} systemId="system" canUse onBusyChange={vi.fn()} onUse={use} currentUsage="" />);
  await screen.findByLabelText('Provider scope');
  // Act
  fireEvent.change(screen.getByLabelText('Provider scope'), { target: { value: 'scope' } });
  // Assert
  expect(await screen.findByText('24 hours')).toBeVisible();
  expect(screen.getByText('8 hours')).toBeVisible();
  expect(screen.getByText('Results not verified')).toBeVisible();
  expect(screen.getByText(/Maximum data loss target/)).toBeVisible();
  expect(screen.getByText(/Time to restore target/)).toBeVisible();
  expect(screen.getByText('pinned-v7')).toBeInTheDocument();
  expect(use).not.toHaveBeenCalled();
});
it('reads explicit structured recovery objectives and short units without promoting system results', async () => {
  // Arrange
  vi.mocked(getResponsibilityDraft).mockImplementation(async (_system, _control, scopeId) => ({
    ...sourceContext(), scopeId,
    sources: [
      { id: 'release', title: 'Published release 7', origin: 'From provider source', version: 'pinned-v7',
        content: '{"rpoHours":24,"description":"Recovery time objective (RTO): 8h."}', href: null },
      { id: 'result', title: 'Unreviewed system note', origin: 'From system records', version: 'note',
        content: '{"description":"RPO target 1 hour; RTO target 2 hours."}', href: null },
    ],
  }));
  render(<ComponentFirstPass data={componentReviewFixture()} systemId="system" canUse onBusyChange={vi.fn()} onUse={vi.fn()} currentUsage="" />);
  await screen.findByLabelText('Provider scope');
  // Act
  fireEvent.change(screen.getByLabelText('Provider scope'), { target: { value: 'scope' } });
  // Assert
  expect(await screen.findByText('24 hours')).toBeVisible();
  expect(screen.getByText('8 hours')).toBeVisible();
  expect(screen.queryByText('1 hour')).not.toBeInTheDocument();
  expect(screen.queryByText('2 hours')).not.toBeInTheDocument();
});
it('prepares reviewable proposals and never replaces human wording automatically', async () => {
  // Arrange
  const use = vi.fn();
  const context = sourceContext();
  const value = { ...context.sourceValues.scope, value: 'AI wording needing confirmation', origin: 'AI proposed' as const, sourceIds: ['system-scope'] };
  const draft: ResponsibilityDraft = { id: 'draft', revision: 1, status: 'Proposed', sourceHash: 'source-1', isStale: false,
    generationState: 'Prepared', generationError: null, preparedAt: '2026-10-02', generatedAt: '2026-10-02',
    preparedBy: 'author', reviewedAt: null, reviewedBy: null,
    sources: [], values: { ...context.sourceValues, scope: value },
    suggestion: { values: { ...context.sourceValues, scope: value }, questions: ['Which workloads are protected?'], conflicts: [] }, history: [] };
  vi.mocked(prepareResponsibilityDraft).mockResolvedValue({ ...context, draft });
  render(<ComponentFirstPass data={componentReviewFixture()} systemId="system" canUse onBusyChange={vi.fn()} onUse={use} currentUsage="Human correction" />);
  await screen.findByLabelText('Provider scope');
  // Act
  fireEvent.click(screen.getByRole('button', { name: 'Prepare first pass' }));
  // Assert
  expect(await screen.findByText('AI wording needing confirmation')).toBeVisible();
  expect(screen.getByText('Which workloads are protected?')).toBeVisible();
  expect(use).not.toHaveBeenCalled();
  expect(screen.getByRole('button', { name: 'Use proposed wording' })).toBeDisabled();
  // Act
  fireEvent.click(screen.getByLabelText('Replace my current wording with this proposal'));
  fireEvent.click(screen.getByRole('button', { name: 'Use proposed wording' }));
  // Assert
  expect(use).toHaveBeenCalledWith('AI wording needing confirmation', { draftId: 'draft', revision: 1 });
  expect(prepareResponsibilityDraft).toHaveBeenCalledWith('system', 'CP-9', null, 0, true, expect.any(AbortSignal));
});
it('shows preparation failures without manufacturing a proposal', async () => {
  // Arrange
  vi.mocked(prepareResponsibilityDraft).mockRejectedValue(new Error('AI unavailable'));
  const use = vi.fn();
  render(<ComponentFirstPass data={componentReviewFixture()} systemId="system" canUse onBusyChange={vi.fn()} onUse={use} currentUsage="Keep me" />);
  await screen.findByLabelText('Provider scope');
  // Act
  fireEvent.click(screen.getByRole('button', { name: 'Prepare first pass' }));
  // Assert
  await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('AI unavailable'));
  expect(use).not.toHaveBeenCalled();
});
