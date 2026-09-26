import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ImpactPanel } from '../../features/provider-authorizations/ImpactPanel';
import * as api from '../../features/provider-authorizations/api';
import * as presentation from '../../features/provider-authorizations/changeImpactApi';
import { PackageImportError } from '../../features/package-imports/request';
import { offering } from './testData';
import { acceptedImpact, emptyImpactPage, impactContext, impactDetails, impactOptions, impactPreview, pendingImpact } from './impactFixtures';

vi.mock('../../features/provider-authorizations/api', () => ({
  getOffering: vi.fn(), listImpactReviews: vi.fn(), previewImpact: vi.fn(), reviewImpact: vi.fn(),
}));
vi.mock('../../features/provider-authorizations/changeImpactApi', () => ({
  listImpactOptions: vi.fn(), getImpactOption: vi.fn(), getImpactDetails: vi.fn(),
}));
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.getOffering).mockResolvedValue(offering);
  vi.mocked(api.listImpactReviews).mockResolvedValue(emptyImpactPage);
  vi.mocked(api.previewImpact).mockResolvedValue(impactPreview);
  vi.mocked(api.reviewImpact).mockResolvedValue(acceptedImpact);
  vi.mocked(presentation.listImpactOptions).mockImplementation(async (_id, kind, page = 1) => ({
    ...emptyImpactPage, page, items: impactOptions[kind], total: impactOptions[kind].length,
  }));
  vi.mocked(presentation.getImpactOption).mockImplementation(async (_id, kind, id) => {
    const option = impactOptions[kind].find(item => item.id === id);
    if (!option) throw new PackageImportError('Selected source is unavailable.', 404);
    return option;
  });
  vi.mocked(presentation.getImpactDetails).mockResolvedValue(impactDetails);
});
function show(props: Partial<React.ComponentProps<typeof ImpactPanel>> = {}) {
  return render(<MemoryRouter><ImpactPanel offering={offering} onChanged={vi.fn()} {...props} /></MemoryRouter>);
}
async function prepare() {
  const start = await screen.findByRole('button', { name: 'Review a proposed change' });
  await waitFor(() => expect(start).toBeEnabled());
  fireEvent.click(start);
  const capability = await screen.findByRole('checkbox', { name: /^Logging coverage/ });
  await waitFor(() => expect(capability).toBeEnabled());
  fireEvent.click(capability);
  fireEvent.click(await screen.findByRole('checkbox', { name: /^Recorded ATO/ }));
  fireEvent.click(await screen.findByRole('checkbox', { name: /^Revised logging package/ }));
  fireEvent.click(screen.getByRole('checkbox', { name: 'I reviewed the selected changes and supporting versions.' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Assess impact' })).toBeEnabled());
  fireEvent.click(screen.getByRole('button', { name: 'Assess impact' }));
}
async function decide() {
  await screen.findByRole('region', { name: 'Affected mission systems' });
  fireEvent.change(screen.getByLabelText('Review decision'), { target: { value: 'AcceptForPublication' } });
  fireEvent.change(screen.getByLabelText('Review rationale'), { target: { value: 'Reviewed exact scope and affected systems.' } });
}
describe('purpose-led exact change impact', () => {
  it('leads with the purpose of Change impact instead of a technical preparation form', async () => {
    // Arrange
    show();
    // Act
    await screen.findByText(/No impact reviews recorded/);
    // Assert
    expect(screen.getByRole('heading', { name: 'Change impact' })).toBeInTheDocument();
    expect(screen.getByText(/which security capabilities and mission systems could be affected/i)).toBeInTheDocument();
    expect(screen.queryByLabelText('Boundary revision ID')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Proposed snapshot SHA-256 1')).not.toBeInTheDocument();
    expect(presentation.listImpactOptions).not.toHaveBeenCalled();
  });
  it('explains detected changes and offers Review changes instead of persisted-review terminology', async () => {
    // Arrange
    vi.mocked(api.listImpactReviews).mockResolvedValue({ ...emptyImpactPage, items: [{ ...acceptedImpact, stale: true }], total: 1 });
    show();
    // Act
    await screen.findByText('Logging coverage change');
    // Assert
    expect(screen.getByText('Changes detected since this review')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Review changes' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Open persisted review' })).not.toBeInTheDocument();
    expect(screen.queryByText(acceptedImpact.contextSnapshotHash)).not.toBeInTheDocument();
  });
  it('recovers all five sections with named relationships and a saved outcome, without restoring approval', async () => {
    // Arrange
    vi.mocked(presentation.getImpactDetails).mockResolvedValue({ ...impactDetails, review: acceptedImpact, rationale: 'Reviewed logging duties.' });
    show({ initialReviewId: 'impact-1' });
    // Act
    await screen.findByText('Mission Alpha');
    // Assert
    for (const title of ['Proposed change', 'Affected capabilities', 'Affected mission systems', 'Required action', 'Review outcome'])
      expect(screen.getByRole('region', { name: title })).toBeInTheDocument();
    expect(screen.getByText('Test reviewer')).toBeInTheDocument();
    expect(screen.getByText('Reviewed logging duties.')).toBeInTheDocument();
    expect(screen.getByText(/does not publish capabilities/)).toBeInTheDocument();
    expect(screen.getByText(acceptedImpact.contextSnapshotHash).closest('details')).not.toHaveAttribute('open');
    expect(screen.queryByRole('button', { name: 'Save review decision' })).not.toBeInTheDocument();
    expect(api.previewImpact).not.toHaveBeenCalled();
    expect(api.reviewImpact).not.toHaveBeenCalled();
  });
  it('binds named selections to exact server context and reviews the returned preview separately', async () => {
    // Arrange
    show({ publicationHref: '/workspaces/csp/security-capabilities/capability-1?tab=review' });
    // Act
    await prepare(); await decide();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save review decision' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Save review decision' }));
    // Assert
    await waitFor(() => expect(api.previewImpact).toHaveBeenCalledWith('offering-1', impactContext, expect.any(String)));
    expect(api.reviewImpact).toHaveBeenCalledWith('offering-1', impactPreview, 'AcceptForPublication', 'Reviewed exact scope and affected systems.');
    expect(await screen.findByText('Review decision saved. No publication was performed.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Continue to publication review' })).toHaveAttribute('href', '/workspaces/csp/security-capabilities/capability-1?tab=review');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review a proposed change' })).toBeEnabled());
  });
  it('shows preview blockers and disables acceptance without disabling rejection', async () => {
    // Arrange
    vi.mocked(api.previewImpact).mockResolvedValue({ ...impactPreview, blockers: [{ code: 'EXPIRED', message: 'Recorded authority expired.' }] });
    show();
    // Act
    await prepare(); await decide();
    // Assert
    expect(screen.getByText('Recorded authority expired.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save review decision' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Review decision'), { target: { value: 'Reject' } });
    expect(screen.getByRole('button', { name: 'Save review decision' })).toBeEnabled();
  });
  it('retains rationale after conflict and requires an explicit updated assessment', async () => {
    // Arrange
    vi.mocked(api.reviewImpact).mockRejectedValue(new PackageImportError('Context changed. Generate a new preview.', 409));
    show();
    await prepare(); await decide();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save review decision' }));
    // Assert
    expect(await screen.findByText(/Context changed\. Generate a new preview/)).toBeInTheDocument();
    expect(screen.getByLabelText('Review rationale')).toHaveValue('Reviewed exact scope and affected systems.');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save review decision' })).toBeDisabled());
    const update = screen.getByRole('button', { name: 'Update impact review' });
    await waitFor(() => expect(update).toBeEnabled());
    fireEvent.click(update);
    expect(await screen.findByText(/previous selection is shown for review/)).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'I reviewed the selected changes and supporting versions.' })).not.toBeChecked();
    expect(api.previewImpact).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(screen.getByRole('checkbox', { name: /^Logging coverage/ })).toBeChecked());
    fireEvent.click(screen.getByRole('checkbox', { name: 'I reviewed the selected changes and supporting versions.' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Assess impact' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Assess impact' }));
    expect(await screen.findByLabelText('Review rationale')).toHaveValue('Reviewed exact scope and affected systems.');
  });
  it('invalidates the assessment after a named context edit', async () => {
    // Arrange
    show(); await prepare(); await decide();
    // Act
    fireEvent.click(screen.getByRole('radio', { name: /^Expanded government services/ }));
    // Assert
    expect(screen.queryByRole('button', { name: 'Save review decision' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Assess impact' })).toBeDisabled();
    expect(api.reviewImpact).not.toHaveBeenCalled();
  });
  it('retains uncertain preview identity and freezes selection until the same request is recovered', async () => {
    // Arrange
    let reject!: (reason: Error) => void;
    vi.mocked(api.previewImpact).mockReturnValueOnce(new Promise((_resolve, fail) => { reject = fail; }));
    show();
    // Act
    await prepare();
    fireEvent.click(screen.getByRole('button', { name: 'Saving…' }));
    reject(new Error('Response lost.'));
    const retry = await screen.findByRole('button', { name: 'Retry same operation' });
    // Assert
    expect(screen.getByRole('button', { name: 'Close change selection' })).toBeDisabled();
    expect(screen.getByRole('checkbox', { name: /^Logging coverage/ })).toBeDisabled();
    fireEvent.click(retry);
    await screen.findByRole('region', { name: 'Affected mission systems' });
    expect(api.previewImpact).toHaveBeenCalledTimes(2);
    expect(vi.mocked(api.previewImpact).mock.calls[0]).toEqual(vi.mocked(api.previewImpact).mock.calls[1]);
  });
  it('keeps an uncertain decision retry available and sends the original decision unchanged', async () => {
    // Arrange
    vi.mocked(api.reviewImpact).mockRejectedValueOnce(new Error('Response lost.'));
    show(); await prepare(); await decide();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save review decision' }));
    const retry = await screen.findByRole('button', { name: 'Retry same operation' });
    // Assert
    expect(screen.getByLabelText('Review rationale')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Assess impact' })).toBeDisabled();
    fireEvent.click(retry);
    await screen.findByText('Review decision saved. No publication was performed.');
    expect(vi.mocked(api.reviewImpact).mock.calls[0]).toEqual(vi.mocked(api.reviewImpact).mock.calls[1]);
  });
  it('reports denied tracker reads and requires retry instead of opening a ready form', async () => {
    // Arrange
    vi.mocked(api.listImpactReviews).mockRejectedValueOnce(new PackageImportError('Provider context denied.', 403)).mockResolvedValue(emptyImpactPage);
    show();
    // Act
    await screen.findByText(/Change reviews unavailable/);
    // Assert
    expect(screen.getByRole('button', { name: 'Review a proposed change' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText(/No impact reviews recorded/)).toBeInTheDocument();
  });
  it('does not report no affected systems when the required detail service fails', async () => {
    // Arrange
    vi.mocked(presentation.getImpactDetails).mockRejectedValue(new PackageImportError('Impact details route unavailable.', 404));
    show({ initialReviewId: 'impact-1' });
    // Act
    await screen.findByText(/Impact analysis unavailable/);
    // Assert
    expect(screen.getByText(/No conclusion about affected systems can be made/)).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Affected mission systems' })).not.toBeInTheDocument();
    expect(screen.queryByText(/None recorded in this review/)).not.toBeInTheDocument();
  });
  it('blocks approval if the offering changes or the assessment is expired', async () => {
    // Arrange
    const view = show();
    await prepare(); await decide();
    // Act
    view.rerender(<MemoryRouter><ImpactPanel offering={{ ...offering, revision: 5 }} onChanged={vi.fn()} /></MemoryRouter>);
    // Assert
    expect(screen.getByRole('button', { name: 'Save review decision' })).toBeDisabled();
    expect(screen.getByText(/Changes detected or this assessment expired/)).toBeInTheDocument();
    expect(api.reviewImpact).not.toHaveBeenCalled();
  });
  it('blocks an already expired assessment', async () => {
    // Arrange
    vi.mocked(api.previewImpact).mockResolvedValue({ ...impactPreview, expiresAt: '2020-01-01T00:00:00Z' });
    show();
    // Act
    await prepare(); await decide();
    // Assert
    expect(screen.getByRole('button', { name: 'Save review decision' })).toBeDisabled();
  });
  it('opens a stale saved review and carries exact context into a new explicit assessment', async () => {
    // Arrange
    vi.mocked(presentation.getImpactDetails).mockResolvedValue({ ...impactDetails, review: { ...pendingImpact, stale: true } });
    vi.mocked(api.getOffering).mockResolvedValue({ ...offering, revision: 9 });
    show({ initialReviewId: 'impact-1' });
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Update impact review' }));
    await waitFor(() => expect(screen.getByRole('checkbox', { name: /^Logging coverage/ })).toBeChecked());
    fireEvent.click(screen.getByRole('checkbox', { name: 'I reviewed the selected changes and supporting versions.' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Assess impact' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Assess impact' }));
    // Assert
    await waitFor(() => expect(api.previewImpact).toHaveBeenCalledWith('offering-1', { ...impactContext, expectedOfferingRevision: 9 }, expect.any(String)));
    expect(api.reviewImpact).not.toHaveBeenCalled();
  });
  it('carries a source capability without accepting or assessing automatically', async () => {
    // Arrange
    show({ source: { kind: 'Capability', id: 'capability-1' } });
    // Act
    await waitFor(() => expect(screen.getByRole('checkbox', { name: /^Logging coverage/ })).toBeChecked());
    // Assert
    expect(presentation.getImpactOption).toHaveBeenCalledWith('offering-1', 'Capability', 'capability-1', expect.any(AbortSignal));
    expect(screen.getByRole('button', { name: 'Assess impact' })).toBeDisabled();
    expect(api.previewImpact).not.toHaveBeenCalled();
    expect(api.reviewImpact).not.toHaveBeenCalled();
  });
  it('keeps the package-associated historical boundary instead of substituting the latest one', async () => {
    // Arrange
    show({ offering: { ...offering, currentBoundaryRevisionId: 'boundary-2' },
      source: { kind: 'Package', id: 'version-1', boundaryRevisionId: 'boundary-1' } });
    // Act
    await waitFor(() => expect(screen.getByRole('checkbox', { name: /^Revised logging package/ })).toBeChecked());
    // Assert
    expect(screen.getByRole('radio', { name: /^Government services/ })).toBeChecked();
    expect(screen.getByRole('radio', { name: /^Expanded government services/ })).not.toBeChecked();
    expect(screen.getByText(/Raw package claims do not by themselves establish affected coverage/)).toBeInTheDocument();
    expect(api.previewImpact).not.toHaveBeenCalled();
  });
  it('seeds a boundary change once, including its exact boundary context', async () => {
    // Arrange
    show({ source: { kind: 'Boundary', id: 'boundary-2' } });
    // Act
    await waitFor(() => expect(within(screen.getByRole('group', { name: 'Proposed changes' })).getByRole('checkbox', { name: /^Expanded government services/ })).toBeChecked());
    // Assert
    expect(screen.getByRole('radio', { name: /^Expanded government services/ })).toBeChecked();
    expect(screen.getAllByRole('button', { name: 'Remove Expanded government services' })).toHaveLength(2);
  });
  it('shows missing source/options explicitly and prevents an assessment', async () => {
    // Arrange
    vi.mocked(presentation.getImpactOption).mockRejectedValue(new PackageImportError('Capability is not in this offering.', 404));
    show({ source: { kind: 'Capability', id: 'unrelated' } });
    // Act
    await screen.findByText(/Selected source unavailable/);
    // Assert
    expect(screen.getByRole('button', { name: 'Assess impact' })).toBeDisabled();
    expect(api.previewImpact).not.toHaveBeenCalled();
  });
  it('can report empty recorded relationships without claiming no impact and handles unavailable names', async () => {
    // Arrange
    vi.mocked(presentation.getImpactDetails).mockResolvedValue({ ...impactDetails,
      affectedSystems: emptyImpactPage, affectedCapabilities: { ...impactDetails.affectedCapabilities,
        items: impactDetails.affectedCapabilities.items.map(item => ({ ...item, name: null })) } });
    show({ initialReviewId: 'impact-1' });
    // Act
    await screen.findByText('Name unavailable');
    // Assert
    expect(screen.getByText(/does not establish that no systems or responsibilities could be affected/)).toBeInTheDocument();
  });
  it('allows assessment without authorization while clearly retaining unestablished coverage', async () => {
    // Arrange
    show({ source: { kind: 'Capability', id: 'capability-1' } });
    await waitFor(() => expect(screen.getByRole('checkbox', { name: /^Logging coverage/ })).toBeChecked());
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: 'I reviewed the selected changes and supporting versions.' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Assess impact' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Assess impact' }));
    // Assert
    expect(screen.getByText(/If no authorization is recorded, coverage remains unestablished/)).toBeInTheDocument();
    await waitFor(() => expect(api.previewImpact).toHaveBeenCalledWith('offering-1', expect.objectContaining({
      authorizationRevisionIds: [], changes: impactContext.changes,
    }), expect.any(String)));
    expect(api.reviewImpact).not.toHaveBeenCalled();
  });
  it('carries a server-provided 64-bit revision without numeric conversion', async () => {
    // Arrange
    const option = { id: 'component-1', name: 'Canonical audit service', version: 'Saved component',
      summary: 'Canonical component change.', change: { kind: 'Component', recordId: 'component-1',
        expectedRevision: '638944416000000001', proposedSnapshotHash: 'a'.repeat(64) } };
    vi.mocked(presentation.listImpactOptions).mockImplementation(async (_id, kind, page = 1) => ({
      ...emptyImpactPage, page, items: kind === 'Component' ? [option] : impactOptions[kind],
      total: kind === 'Component' ? 1 : impactOptions[kind].length,
    }));
    show();
    fireEvent.click(await screen.findByRole('button', { name: 'Review a proposed change' }));
    await waitFor(() => expect(screen.getByLabelText('Change type')).toBeEnabled());
    // Act
    fireEvent.change(screen.getByLabelText('Change type'), { target: { value: 'Component' } });
    fireEvent.click(await screen.findByRole('checkbox', { name: /^Canonical audit service/ }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'I reviewed the selected changes and supporting versions.' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Assess impact' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Assess impact' }));
    // Assert
    await waitFor(() => expect(api.previewImpact).toHaveBeenCalledWith('offering-1', expect.objectContaining({
      changes: [option.change],
    }), expect.any(String)));
  });
  it('refreshes selected versions after a preview conflict without losing the source selections', async () => {
    // Arrange
    vi.mocked(api.previewImpact).mockRejectedValueOnce(new PackageImportError('Saved capability changed.', 409));
    show();
    await prepare();
    await screen.findByText(/Saved capability changed/);
    const currentChange = { kind: 'Capability', recordId: 'capability-1', expectedRevision: 8, proposedSnapshotHash: 'f'.repeat(64) };
    vi.mocked(presentation.getImpactOption).mockImplementation(async (_id, kind, id) => {
      const option = impactOptions[kind].find(item => item.id === id);
      if (!option) throw new PackageImportError('Selected source unavailable.', 404);
      return kind === 'Capability' ? { ...option, version: 'Working revision 8', change: currentChange } : option;
    });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Refresh selected versions' }));
    await screen.findByText('Logging coverage · Working revision 8');
    // Assert
    expect(screen.getByRole('checkbox', { name: /^Recorded ATO/ })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: /^Revised logging package/ })).toBeChecked();
    expect(screen.getByRole('button', { name: 'Assess impact' })).toBeDisabled();
    fireEvent.click(screen.getByRole('checkbox', { name: 'I reviewed the selected changes and supporting versions.' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Assess impact' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Assess impact' }));
    await waitFor(() => expect(api.previewImpact).toHaveBeenLastCalledWith('offering-1',
      { ...impactContext, changes: [currentChange] }, expect.any(String)));
  });
});
