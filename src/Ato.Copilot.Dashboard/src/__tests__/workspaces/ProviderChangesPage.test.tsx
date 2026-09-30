import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import ProviderChangesPage from '../../features/provider-workspace/ProviderChangesPage';
import * as api from '../../features/provider-authorizations/api';
import * as monitoring from '../../features/provider-authorizations/providerMonitoringApi';
import { offering } from '../provider-authorizations/testData';
import { offeringOverview } from '../provider-authorizations/overviewFixtures';
import { page } from '../package-imports/fixtures';
import '../helpers/dialog';

vi.mock('../../features/provider-authorizations/api', async original => ({
  ...await original<typeof api>(), listOfferings: vi.fn(), getOfferingOverview: vi.fn(), getBoundaryOverview: vi.fn(),
  listImpactReviews: vi.fn(), listFindings: vi.fn(), listFindingEvidence: vi.fn(),
}));
vi.mock('../../features/provider-authorizations/providerMonitoringApi', () => ({ getProviderMonitoring: vi.fn() }));
vi.mock('../../features/provider-authorizations/ProviderMonitoringPage', () => ({
  ProviderMonitoringPanel: ({ offeringId }: { offeringId: string }) => <h2>Monitoring {offeringId}</h2>,
}));
const pending = { reviewId: 'review-a', revision: 1, disposition: 'PendingReview', reviewedAt: null, reviewedBy: null,
  contextSnapshotHash: 'hash', stale: true, title: 'Retention change', summary: 'Review recorded retention changes.',
  createdAt: '2026-09-27', affectedCounts: { components: 1, capabilities: 1, scopes: 1, systems: 2 } };

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.listOfferings).mockResolvedValue(page([offering, { ...offering, offeringId: 'other', name: 'Other service' }]));
  vi.mocked(api.listImpactReviews).mockResolvedValue(page([pending, { ...pending, reviewId: 'old-accepted', title: 'Old accepted review',
    disposition: 'AcceptForPublication', reviewedBy: 'Reviewer', reviewedAt: '2026-09-26' }]));
  vi.mocked(api.getOfferingOverview).mockResolvedValue({ ...offeringOverview(), customerActionCount: 3 });
  vi.mocked(api.getBoundaryOverview).mockResolvedValue({ offeringId: offering.offeringId, offeringRevision: 1,
    capabilities: { ...page([]), published: 0, awaitingReview: 0 },
    missionSystems: page([{ assignmentId: 'assignment-a', systemId: 'system-a', systemName: 'Mission Alpha', relationshipState: 'Undetermined',
      associated: false, adoptedCapabilityCount: 0, assignedScopes: [] }]) });
  vi.mocked(api.listFindings).mockResolvedValue(page([]));
  vi.mocked(monitoring.getProviderMonitoring).mockResolvedValue({ offeringId: offering.offeringId, offeringName: offering.name,
    rules: [], evaluations: [], sources: [{ sourceId: 'source-a', name: 'Authorization source', signal: 'AuthorizationExpiry',
      collectionHealth: 'Unreviewed', sourceRevision: 'source-r1', field: 'RemainingDays', value: null,
      sourceTimestamp: null, snapshotJson: '{}' }] });
});

it('shows a real pending queue and separates expired accepted history', async () => {
  // Arrange / Act
  render(<MemoryRouter><ProviderChangesPage /></MemoryRouter>);
  const queue = await screen.findByRole('table', { name: 'Change queue' });
  // Assert
  expect(await within(queue).findByText('Retention change')).toBeVisible();
  expect(within(queue).queryByText('Old accepted review')).not.toBeInTheDocument();
  expect(screen.getByLabelText('Provider reviews')).toHaveTextContent('1');
  expect(screen.getByRole('link', { name: 'Review Retention change' })).toHaveAttribute('href',
    `/workspaces/csp/authorizations/offerings/${offering.offeringId}/impact?reviewId=review-a&returnTo=changes`);
  // Act
  fireEvent.change(screen.getByLabelText('Queue view'), { target: { value: 'history' } });
  // Assert
  expect(await within(queue).findByText('Old accepted review')).toBeVisible();
  expect(within(queue).getByText('Impact accepted for publication')).toBeVisible();
});

it('inspects the exact source without writes and preserves offering context between tabs', async () => {
  // Arrange
  render(<MemoryRouter><ProviderChangesPage /></MemoryRouter>);
  // Act
  fireEvent.click(await screen.findByRole('button', { name: 'Inspect Authorization source' }));
  // Assert
  expect(screen.getByRole('dialog', { name: 'Inspect monitoring source' })).toHaveTextContent('source-r1');
  expect(screen.getByRole('dialog')).toHaveTextContent('not live connector health');
  fireEvent.click(screen.getByRole('button', { name: 'Close dialog' }));
  // Act
  fireEvent.click(screen.getByRole('link', { name: 'Service monitoring' }));
  // Assert
  await waitFor(() => expect(screen.getByRole('heading', { name: `Monitoring ${offering.offeringId}` })).toBeVisible());
  expect(screen.getByRole('link', { name: 'Change queue' })).toHaveAttribute('href', `/provider-changes?offeringId=${offering.offeringId}`);
});

it('preserves known review records when monitoring fails and does not claim complete health', async () => {
  // Arrange
  vi.mocked(monitoring.getProviderMonitoring).mockRejectedValue(new Error('Monitoring unavailable'));
  // Act
  render(<MemoryRouter><ProviderChangesPage /></MemoryRouter>);
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('Monitoring unavailable');
  expect(screen.getByLabelText('Source health')).toHaveTextContent('Unavailable');
  expect(await screen.findByRole('link', { name: 'Review Retention change' })).toBeVisible();
  expect(screen.getByText(/not a complete assessment/)).toBeVisible();
});

it('retains a query-selected offering that is on a later catalog page', async () => {
  // Arrange
  const selected = { ...offering, offeringId: 'later', name: 'Later service' };
  vi.mocked(api.listOfferings).mockImplementation(async pageNumber => ({
    items: pageNumber === 1 ? [offering] : [selected], page: pageNumber ?? 1, pageSize: 1, total: 2,
  }));
  // Act
  render(<MemoryRouter initialEntries={['/provider-changes?offeringId=later']}><ProviderChangesPage /></MemoryRouter>);
  // Assert
  await waitFor(() => expect(screen.getByLabelText('Service offering')).toHaveValue('later'));
  expect(api.listImpactReviews).toHaveBeenCalledWith('later', 1, expect.any(AbortSignal));
  expect(api.listImpactReviews).not.toHaveBeenCalledWith(offering.offeringId, expect.anything(), expect.anything());
});
