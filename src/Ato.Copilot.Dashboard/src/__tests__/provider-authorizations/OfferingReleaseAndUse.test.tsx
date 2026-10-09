import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { OfferingMissionUse, OfferingReleaseContext } from '../../features/provider-authorizations/OfferingReleaseAndUse';
import * as api from '../../features/provider-authorizations/api';
import { offering } from './testData';
import { offeringOverview } from './overviewFixtures';
import { page } from '../package-imports/fixtures';
import '../helpers/dialog';

vi.mock('../../features/provider-authorizations/api', async original => ({
  ...await original<typeof api>(), getBoundaryOverview: vi.fn(), getOfferingOverview: vi.fn(),
}));
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getOfferingOverview).mockResolvedValue(offeringOverview());
  vi.mocked(api.getBoundaryOverview).mockResolvedValue({
    offeringId: offering.offeringId, offeringRevision: offering.revision, capabilities: { ...page([]), published: 0, awaitingReview: 0 },
    missionSystems: page([{ assignmentId: 'assignment-1', systemId: 'mission-1', systemName: 'Synthetic mission',
      relationshipState: 'ReviewRequired', associated: true, adoptedCapabilityCount: 0, assignedScopes: [] }]),
  });
});
  it('matches the mock with paired published and identity cards, not three competing panels', async () => {
    // Arrange
    vi.mocked(api.getOfferingOverview).mockResolvedValue({
      ...offeringOverview(), capabilities: { ...offeringOverview().capabilities, publishedReleaseRevisions: [3] },
    });
    // Act
    render(<MemoryRouter><OfferingReleaseContext offering={offering} /></MemoryRouter>);
    // Assert
    const release = await screen.findByRole('region', { name: 'Published release & working changes' });
    expect(within(release).getByRole('heading', { name: 'Customer-visible capability snapshots' })).toBeVisible();
    expect(within(release).getByRole('heading', { name: 'Working offering identity' })).toBeVisible();
    expect(within(release).getByText('Published revision 3')).toBeVisible();
    expect(screen.getByRole('navigation', { name: 'Provider release workflow orientation' })).toHaveTextContent('Check scope & duties');
    expect(screen.getByRole('region', { name: 'Working changes' })).toHaveTextContent('not a publication gate result');
    expect(screen.queryByRole('region', { name: 'Compare retained context' })).not.toBeInTheDocument();
  });
  it('keeps exact revision handoffs under collapsed comparison details and never simulates publication', async () => {
    // Arrange / Act
    render(<MemoryRouter><OfferingReleaseContext offering={{ ...offering, currentBoundaryRevisionId: 'scope-boundary',
      currentHostingScopeRevisionId: 'hosting-version' }} /></MemoryRouter>);
    // Assert
    const detail = (await screen.findByText('Retained source & scope comparisons')).closest('details');
    expect(detail).not.toHaveAttribute('open');
    expect(screen.getByRole('link', { name: 'Review boundary change impact', hidden: true })).toHaveAttribute('href',
      api.changeImpactHref(offering.offeringId, { boundaryRevisionId: 'scope-boundary' }));
    expect(screen.getByRole('link', { name: 'Compare identity edits' })).toHaveAttribute('href',
      `${api.authorizationHref(offering.offeringId)}?action=identity`);
    expect(screen.queryByRole('button', { name: /Publish|Discard simulated/ })).not.toBeInTheDocument();
  });
it.each(['release', 'mission'])('exposes %s reads from a newer offering context without rebinding an edit token', async surface => {
  // Arrange
  const data = await api.getBoundaryOverview(offering.offeringId);
  vi.mocked(api.getBoundaryOverview).mockResolvedValue({ ...data, offeringRevision: offering.revision + 1 });
  vi.mocked(api.getOfferingOverview).mockResolvedValue({ ...offeringOverview(), offeringRevision: offering.revision + 1 });
  // Act
  render(<MemoryRouter>{surface === 'release' ? <OfferingReleaseContext offering={offering} /> : <OfferingMissionUse offering={offering} />}</MemoryRouter>);
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('Offering context changed');
  expect(screen.getByRole('link', { name: 'Reopen offering overview' })).toHaveAttribute('href', api.authorizationHref(offering.offeringId));
});

it('shows mission association separately from adoption with retained relationship handoff', async () => {
  // Arrange / Act
  render(<MemoryRouter><OfferingMissionUse offering={offering} /></MemoryRouter>);
  // Assert
  expect(await screen.findByText('Synthetic mission')).toBeInTheDocument();
  expect(screen.getByText('Associated', { exact: true })).toBeInTheDocument();
  expect(screen.getByText('0 · no adopted release recorded')).toBeInTheDocument();
  expect(screen.getByRole('region', { name: 'What the Mission Owner still needs to review' })).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'View handoff for Synthetic mission' }));
  expect(screen.getByRole('dialog', { name: 'Mission handoff · Synthetic mission' })).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Inspect service relationship' })).toHaveAttribute('href',
    api.authorizationHref(offering.offeringId, 'missions/assignment-1'));
  expect(screen.getByText(/Association is not adoption/)).toBeInTheDocument();
});
it('shows exact canonical releases and explicit comparison prerequisites rather than a fictional diff', async () => {
  // Arrange
  vi.mocked(api.getOfferingOverview).mockResolvedValue({
    ...offeringOverview(), capabilities: { ...offeringOverview().capabilities, publishedReleaseRevisions: [3] },
  });
  // Act
  render(<MemoryRouter><OfferingReleaseContext offering={{ ...offering, currentHostingScopeRevisionId: 'scope-1' }} /></MemoryRouter>);
  // Assert
  expect(await screen.findByText('Published revision 3')).toBeInTheDocument();
  expect(screen.getByText(/published offering-identity snapshot is not supplied/)).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Review exact capability releases' })).toHaveAttribute('href',
    api.authorizationHref(offering.offeringId, 'inherited-coverage?task=capabilities'));
});

  it('discloses empty releases and missing context prerequisites without comparison or publication success', async () => {
    // Arrange
    const empty = { ...offering, currentBoundaryRevisionId: null, currentHostingScopeRevisionId: null };
    vi.mocked(api.getOfferingOverview).mockResolvedValue({ ...offeringOverview(), capabilities: {
      proposed: 0, awaitingReview: 0, awaitingApproval: 0, published: 0, archived: 0, publishedReleaseRevisions: [],
    } });
    // Act
    render(<MemoryRouter><OfferingReleaseContext offering={empty} /></MemoryRouter>);
    // Assert
    expect(await screen.findByText('No published capabilities')).toBeInTheDocument();
    expect(screen.getByText('Boundary comparison unavailable: no boundary revision recorded.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Record hosting prerequisites' })).toHaveAttribute('href',
      api.authorizationHref(offering.offeringId, 'inherited-coverage?task=hosting'));
    expect(screen.queryByRole('link', { name: 'Stage a scope update' })).not.toBeInTheDocument();
  });

  it('uses mixed canonical versions and pending proposals without forcing a release', async () => {
    // Arrange
    vi.mocked(api.getOfferingOverview).mockResolvedValue({ ...offeringOverview(), capabilities: {
      proposed: 2, awaitingReview: 1, awaitingApproval: 1, published: 2, archived: 0, publishedReleaseRevisions: [2, 5],
    } });
    // Act
    render(<MemoryRouter><OfferingReleaseContext offering={{ ...offering, currentHostingScopeRevisionId: 'scope-1' }} /></MemoryRouter>);
    // Assert
    expect(await screen.findByText('Published revisions 2, 5')).toBeInTheDocument();
    expect(screen.queryByText(/No pending capability proposals/)).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Stage a scope update' })).toHaveAttribute('href',
      api.authorizationHref(offering.offeringId, 'inherited-coverage/propose'));
  });

  it('never fabricates a release version when canonical revisions are missing', async () => {
    // Arrange / Act
    render(<MemoryRouter><OfferingReleaseContext offering={offering} /></MemoryRouter>);
    // Assert
    expect(await screen.findByText('Published version not reported')).toBeInTheDocument();
  });

  it.each(['Provider permission denied (403)', 'Retained projection unavailable (503)'])('shows failed reads rather than empty mission records: %s', async message => {
    // Arrange
    vi.mocked(api.getBoundaryOverview).mockRejectedValue(new Error(message));
    // Act
    render(<MemoryRouter><OfferingMissionUse offering={offering} /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(message);
    expect(screen.queryByText(/No mission hosting allocations recorded/)).not.toBeInTheDocument();
  });

  it('shows release read failure and supports an explicit retry', async () => {
    // Arrange
    vi.mocked(api.getOfferingOverview).mockRejectedValueOnce(new Error('Release context unavailable'));
    render(<MemoryRouter><OfferingReleaseContext offering={offering} /></MemoryRouter>);
    // Act
    const alert = await screen.findByRole('alert');
    // Assert
    expect(alert).toHaveTextContent('Release context unavailable');
    expect(screen.queryByText('No published capabilities')).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    // Assert
    expect(await screen.findByText('Published version not reported')).toBeInTheDocument();
  });

  it('shows empty mission use without inventing an association', async () => {
    // Arrange
    const base = await api.getBoundaryOverview(offering.offeringId);
    vi.mocked(api.getBoundaryOverview).mockResolvedValue({ ...base, missionSystems: page([]) });
    // Act
    render(<MemoryRouter><OfferingMissionUse offering={offering} /></MemoryRouter>);
    // Assert
    expect(await screen.findByText(/No mission hosting allocations recorded/)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Inspect service relationship' })).not.toBeInTheDocument();
  });

  it('paginates exact adopted releases while distinguishing available updates and unknown versions', async () => {
    // Arrange
    const base = await api.getBoundaryOverview(offering.offeringId);
    vi.mocked(api.getBoundaryOverview).mockImplementation(async (_id, _cap, next = 1) => ({
      ...base, missionSystems: {
        total: 2, page: next, pageSize: 1, items: [{
          assignmentId: `assignment-${next}`, systemId: `mission-${next}`, systemName: null,
          relationshipState: next === 1 ? 'ExplicitlyCoveredByRecordedScope' : 'Undetermined',
          associated: false, adoptedCapabilityCount: 2,
          targetTenantName: 'Synthetic organization',
          assignedScopes: [{ kind: 'Service', serviceId: 'svc-1', serviceName: 'Manual shared service', environment: 'ManualService', tenantReference: null }],
          adoptedReleases: next === 1 ? [
            { capabilityId: 'cap-1', capabilityName: 'Protection one', releaseId: 'release-1', revision: 2, currentReleaseRevision: 3, updateAvailable: true },
            { capabilityId: 'cap-2', capabilityName: 'Protection two', releaseId: 'release-2', revision: 3, currentReleaseRevision: null, updateAvailable: true },
            { capabilityId: 'cap-3', capabilityName: 'Protection three', releaseId: 'release-3', revision: 3, currentReleaseRevision: 3, updateAvailable: false },
          ] : undefined,
        }],
      },
    }));
    // Act
    render(<MemoryRouter><OfferingMissionUse offering={offering} /></MemoryRouter>);
    // Assert
    await screen.findByText('Mission name unavailable');
    fireEvent.click(screen.getByRole('button', { name: 'View handoff for Mission name unavailable' }));
    expect(await screen.findByText(/Protection one · Selected revision 2/)).toHaveTextContent('Provider revision 3 available for explicit review');
    expect(screen.getByText(/Protection two · Selected revision 3/)).toHaveTextContent('Provider revision not reported');
    expect(screen.getByText('Mission name unavailable')).toBeInTheDocument();
    expect(screen.getByText('Association pending', { exact: true })).toBeInTheDocument();
    expect(screen.getByText(/Manual shared service/)).toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Close handoff' }));
    fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    // Assert
    expect(await screen.findByText('Exact adopted release versions are not reported here.')).toBeInTheDocument();
  });
  it('uses allocation totals, not invented associated-system totals, and keeps duplicate names tied to exact assignments', async () => {
    // Arrange
    const base = await api.getBoundaryOverview(offering.offeringId);
    vi.mocked(api.getBoundaryOverview).mockResolvedValue({ ...base, missionSystems: page([
      { ...base.missionSystems.items[0]!, assignmentId: 'first', associated: false, relationshipState: 'UnrecognizedLegacyValue' },
      { ...base.missionSystems.items[0]!, assignmentId: 'second', associated: true },
    ]) });
    // Act
    render(<MemoryRouter><OfferingMissionUse offering={offering} /></MemoryRouter>);
    await screen.findByText('2 recorded hosting allocations');
    const opener = screen.getAllByRole('button', { name: 'View handoff for Synthetic mission' })[0]!;
    opener.focus();
    fireEvent.click(opener);
    // Assert
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText(/Recorded relationship code: UnrecognizedLegacyValue/)).toBeInTheDocument();
    expect(within(dialog).getByRole('link', { name: 'Inspect service relationship' })).toHaveAttribute('href',
      api.authorizationHref(offering.offeringId, 'missions/first'));
    expect(within(dialog).getByText('Unrecognized recorded relationship')).toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: /^Mission use$/ })).getByText(/Customer organization not reported · Unrecognized recorded relationship/))
      .toBeInTheDocument();
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: 'Close handoff' }));
    // Assert
    expect(opener).toHaveFocus();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
  it('reports partial mission pages without treating missing records as an empty offering', async () => {
    // Arrange
    const base = await api.getBoundaryOverview(offering.offeringId);
    vi.mocked(api.getBoundaryOverview).mockResolvedValue({ ...base, missionSystems: { ...page([]), total: 3 } });
    // Act
    render(<MemoryRouter><OfferingMissionUse offering={offering} /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Mission allocation records are missing');
    expect(screen.queryByText(/No mission hosting allocations recorded/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry mission records' })).toBeEnabled();
  });
