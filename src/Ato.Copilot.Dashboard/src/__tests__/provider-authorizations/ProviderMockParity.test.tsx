import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { OfferingOverview } from '../../features/provider-authorizations/OfferingOverview';
import { OfferingList } from '../../features/provider-authorizations/OfferingList';
import { HostingSetupPage } from '../../features/provider-authorizations/HostingSetupPage';
import { FindingsPage } from '../../features/provider-authorizations/FindingsPage';
import * as api from '../../features/provider-authorizations/api';
import * as hosting from '../../features/provider-authorizations/hostingApi';
import { offering, boundary } from './testData';
import { offeringOverview } from './overviewFixtures';
import { candidate, page } from '../package-imports/fixtures';
import * as packageApi from '../../features/package-imports/api';
import * as catalogApi from '../../features/workspace-operations/api';

vi.mock('../../features/provider-authorizations/api', async original => ({
  ...await original<typeof api>(), getOfferingOverview: vi.fn(), listOfferings: vi.fn(),
  getBoundary: vi.fn(), getBoundaryOverview: vi.fn(), listInheritedProviderReferences: vi.fn(),
  listFindings: vi.fn(), listFindingEvidence: vi.fn(),
}));
vi.mock('../../features/provider-authorizations/hostingApi', async original => ({
  ...await original<typeof hosting>(), listHostingScopes: vi.fn(),
}));
vi.mock('../../features/package-imports/api', async original => ({
  ...await original<typeof packageApi>(), getPackageCandidates: vi.fn(),
}));
vi.mock('../../features/workspace-operations/api', async original => ({
  ...await original<typeof catalogApi>(), getProviderCapability: vi.fn(),
}));

const finding = { findingId: 'finding-1', offeringId: offering.offeringId, revision: 1,
  title: 'Synthetic collection delay', observation: 'Synthetic test observation', severityAsStated: 'Moderate',
  controlIds: ['AU-6'], citations: [], workflowState: 'Open', createdAt: '2026-09-26T12:00:00Z' };
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getOfferingOverview).mockResolvedValue(offeringOverview());
  vi.mocked(api.listOfferings).mockResolvedValue(page([offering]));
  vi.mocked(api.getBoundary).mockResolvedValue(boundary);
  vi.mocked(api.listInheritedProviderReferences).mockResolvedValue(page([]));
  vi.mocked(hosting.listHostingScopes).mockResolvedValue(page([]));
  vi.mocked(api.getBoundaryOverview).mockResolvedValue({
    offeringId: offering.offeringId, offeringRevision: offering.revision,
    capabilities: { ...page([{ capabilityId: null, candidateId: 'candidate /1', packageId: 'package /1',
      name: 'Audit collection proposal', reviewState: 'NeedsReview', publicationState: 'Unpublished',
      releaseId: null, boundaryRevisionId: boundary.boundaryRevisionId }]), published: 0, awaitingReview: 1 },
    missionSystems: page([]),
  });
  vi.mocked(api.listFindings).mockResolvedValue(page([finding]));
  vi.mocked(api.listFindingEvidence).mockResolvedValue(page([{
    evidenceId: 'evidence /1', findingId: finding.findingId, offeringId: offering.offeringId, findingRevision: 1,
    fileName: 'retained-assessment.pdf', mediaType: 'application/pdf', byteLength: 120, sha256: 'test-hash',
    description: 'Retained assessment', state: 'PendingReview', createdAt: finding.createdAt, latestReview: null,
  }]));
  vi.mocked(packageApi.getPackageCandidates).mockResolvedValue(page([candidate({
    candidateId: 'candidate /1', description: 'Retained audit implementation',
    controlDuties: { 'AU-6': 'Shared' },
  })]));
});

it('places release availability, four real metrics and the next-release checklist before record details', async () => {
  // Arrange
  render(<MemoryRouter><OfferingOverview offering={offering} /></MemoryRouter>);
  // Act
  const checklist = await screen.findByRole('region', { name: 'Complete the next release' });
  // Assert
  expect(screen.getByLabelText('Offering metrics').children).toHaveLength(4);
  expect(screen.getByText('Published capabilities are available to customers')).toBeInTheDocument();
  expect(within(checklist).getByText('Sources reviewed')).toBeInTheDocument();
  expect(within(checklist).getByText('Scope confirmed')).toBeInTheDocument();
  expect(within(checklist).getByText('Capability changes reviewed')).toBeInTheDocument();
  expect(within(checklist).getByRole('link', { name: 'Review release candidates' })).toHaveAttribute('href',
    api.authorizationHref(offering.offeringId, 'inherited-coverage?task=capabilities'));
  await waitFor(() => expect(screen.getByLabelText('Open findings')).toHaveTextContent('1'));
  expect(screen.queryByText('Release 1.2')).not.toBeInTheDocument();
});

it('shows customer use and publication separately from the offering lifecycle', async () => {
  // Arrange
  render(<MemoryRouter><OfferingList /></MemoryRouter>);
  // Act
  const table = await screen.findByRole('table', { name: 'Your offerings' });
  // Assert
  expect(within(table).getByRole('columnheader', { name: 'Customer use' })).toBeInTheDocument();
  expect(await within(table).findByText('2 mission systems')).toBeInTheDocument();
  expect(within(table).getByText('4 capabilities published')).toBeInTheDocument();
  expect(within(table).getByText('Release version not reported')).toBeInTheDocument();
});

it('renders the capabilities tab as a table, not a scope page obscured by a dialog', async () => {
  // Arrange
  render(<MemoryRouter initialEntries={['/?task=capabilities']}><HostingSetupPage offering={offering} onChanged={vi.fn()} /></MemoryRouter>);
  // Act
  const table = await screen.findByRole('table', { name: 'Service implementations' });
  // Assert
  expect(within(table).getByRole('columnheader', { name: 'Control references' })).toBeInTheDocument();
  expect(within(table).getByRole('columnheader', { name: 'Responsibilities' })).toBeInTheDocument();
  expect(within(table).getByRole('columnheader', { name: 'Release state' })).toBeInTheDocument();
  expect(within(table).getByRole('link', { name: 'Review source proposal' })).toHaveAttribute('href',
    api.authorizationHref(offering.offeringId, 'packages/package%20%2F1/candidates/candidate%20%2F1'));
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  expect(await within(table).findByText('AU-6: Shared')).toBeInTheDocument();
  expect(within(table).getByText('Source proposal · not a published duty')).toBeInTheDocument();
});

it('lists actual retained evidence separately from service findings with exact access-controlled navigation', async () => {
  // Arrange
  render(<MemoryRouter><FindingsPage offering={offering} onChanged={vi.fn()} /></MemoryRouter>);
  // Act
  const table = await screen.findByRole('table', { name: 'Evidence library' });
  // Assert
  expect(await within(table).findByText('retained-assessment.pdf')).toBeInTheDocument();
  expect(within(table).getByRole('columnheader', { name: 'Customer access' })).toBeInTheDocument();
  expect(within(table).getByRole('link', { name: 'Review evidence access' })).toHaveAttribute('href',
    api.authorizationHref(offering.offeringId, 'evidence/evidence%20%2F1?findingId=finding-1'));
  expect(screen.getByRole('table', { name: 'Service findings' })).not.toContainElement(table);
});

it('uses the published capability API without inventing duties from its offering boundary or release identifier', async () => {
  // Arrange
  vi.mocked(api.getBoundaryOverview).mockResolvedValue({
    offeringId: offering.offeringId, offeringRevision: offering.revision,
    capabilities: { ...page([{ capabilityId: 'capability /1', candidateId: null, packageId: null, name: 'Published protection',
      reviewState: 'Reviewed', publicationState: 'Published', releaseId: 'release-exact', boundaryRevisionId: 'boundary-1' }]), published: 1, awaitingReview: 0 },
    missionSystems: page([]),
  });
  vi.mocked(catalogApi.getProviderCapability).mockResolvedValue({
    capability: { source: 'Provider', componentId: 'component-1', capabilityId: 'capability /1', name: 'Published protection',
      description: 'Current catalog description', componentName: 'Network service', componentType: 'Service',
      lifecycle: 'Published', reviewState: 'Reviewed', sourceFormat: 'Manual', sourceReference: null,
      distinctAdoptionCount: 0, workingRevision: 7, releasedRevision: 3 },
    supportingComponents: [], unresolvedContributorIds: [], sourceArtifacts: [], mappedControlIds: ['SC-7'],
    sourceEvidenceReferences: null, implementationNarrative: null,
  });
  // Act
  render(<MemoryRouter initialEntries={['/?task=capabilities']}><HostingSetupPage offering={offering} onChanged={vi.fn()} /></MemoryRouter>);
  // Assert
  expect(await screen.findByText('Current catalog description')).toBeInTheDocument();
  expect(screen.getByText('SC-7')).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Inspect responsibility split' })).toHaveAttribute('href', '/security-capabilities/capability%20%2F1?tab=responsibilities');
  expect(screen.getByText('release-exact')).toBeInTheDocument();
  expect(screen.queryByText('Release 7')).not.toBeInTheDocument();
  expect(screen.queryByText('Shared')).not.toBeInTheDocument();
});

it('retries unavailable proposal metadata without reporting an absent control mapping', async () => {
  // Arrange
  vi.mocked(packageApi.getPackageCandidates).mockRejectedValueOnce(new Error('Source temporarily unavailable'));
  render(<MemoryRouter initialEntries={['/?task=capabilities']}><HostingSetupPage offering={offering} onChanged={vi.fn()} /></MemoryRouter>);
  // Act
  fireEvent.click(await screen.findByRole('button', { name: 'Retry implementation details' }));
  // Assert
  expect(await screen.findByText('AU-6: Shared')).toBeInTheDocument();
  expect(screen.queryByText('No mappings recorded')).not.toBeInTheDocument();
});

it('keeps missing offering capability records distinct from a fabricated release', async () => {
  // Arrange
  vi.mocked(api.getBoundaryOverview).mockResolvedValue({
    offeringId: offering.offeringId, offeringRevision: offering.revision,
    capabilities: { ...page([]), published: 0, awaitingReview: 0 }, missionSystems: page([]),
  });
  // Act
  render(<MemoryRouter initialEntries={['/?task=capabilities']}><HostingSetupPage offering={offering} onChanged={vi.fn()} /></MemoryRouter>);
  // Assert
  expect(await screen.findByText(/No capability records linked to this offering/)).toBeInTheDocument();
  expect(screen.queryByRole('table')).not.toBeInTheDocument();
  expect(catalogApi.getProviderCapability).not.toHaveBeenCalled();
});

it('uses canonical mixed release revisions and complete projected metrics, not document editions or page subtotals', async () => {
  // Arrange
  const data = offeringOverview();
  Object.assign(data.capabilities, { publishedReleaseRevisions: [2, 5] });
  Object.assign(data.packages, { total: 12, sourceDocumentCount: 73 });
  Object.assign(data, { openFindingCount: 9 });
  vi.mocked(api.getOfferingOverview).mockResolvedValue(data);
  // Act
  render(<MemoryRouter><OfferingOverview offering={offering} /></MemoryRouter>);
  // Assert
  expect(await screen.findByText('Published revisions 2, 5 are available to customers')).toBeInTheDocument();
  expect(screen.getByLabelText('Offering metrics')).toHaveTextContent('73');
  expect(screen.getByLabelText('Open findings')).toHaveTextContent('9');
  expect(api.listFindings).not.toHaveBeenCalled();
});

it('shows the actual published integer revision on offering rows', async () => {
  // Arrange
  const data = offeringOverview();
  Object.assign(data.capabilities, { publishedReleaseRevisions: [3] });
  vi.mocked(api.getOfferingOverview).mockResolvedValue(data);
  // Act
  render(<MemoryRouter><OfferingList /></MemoryRouter>);
  // Assert
  expect(await screen.findByText('Revision 3 published')).toBeInTheDocument();
  expect(screen.queryByText('Release version not reported')).not.toBeInTheDocument();
});

it('describes retained source totals without assigning superseded documents to active packages', async () => {
  // Arrange
  const data = offeringOverview();
  data.packages = { ...data.packages, total: 1, sourceDocumentCount: 32 };
  vi.mocked(api.getOfferingOverview).mockResolvedValue(data);
  // Act
  render(<MemoryRouter><OfferingOverview offering={offering} /></MemoryRouter>);
  // Assert
  expect(await screen.findByTitle('Retained source documents, including superseded review sources and excluded files')).toHaveTextContent('Includes retained source history');
  expect(screen.getByLabelText('Offering metrics')).toHaveTextContent('32');
  expect(screen.queryByText(/Retained entries across 1 source packages/)).not.toBeInTheDocument();
});
