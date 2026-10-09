import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { OfferingCapabilities } from '../../features/provider-authorizations/OfferingCapabilities';
import { FindingsPage } from '../../features/provider-authorizations/FindingsPage';
import * as api from '../../features/provider-authorizations/api';
import * as catalogApi from '../../features/workspace-operations/api';
import { offering } from './testData';
import { candidate, page } from '../package-imports/fixtures';
import type { OfferingBoundaryCapability } from '../../features/provider-authorizations/types';
import { PackageImportError } from '../../features/package-imports/request';
import * as packageApi from '../../features/package-imports/api';
import '../helpers/dialog';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';

vi.mock('../../features/provider-authorizations/api', async original => ({
  ...await original<typeof api>(), getBoundaryOverview: vi.fn(), listFindings: vi.fn(), listFindingEvidence: vi.fn(),
  getFinding: vi.fn(), submitFindingEvidence: vi.fn(),
}));
vi.mock('../../features/workspace-operations/api', async original => ({
  ...await original<typeof catalogApi>(), getProviderCapability: vi.fn(),
}));
vi.mock('../../features/package-imports/api', async original => ({
  ...await original<typeof packageApi>(), getPackageCandidates: vi.fn(),
}));

const capabilities: OfferingBoundaryCapability[] = ['Azure Backup', 'Azure Key Vault'].map((name, index) => ({
  capabilityId: `capability-${index}`, candidateId: null, packageId: null, name,
  reviewState: 'Reviewed', publicationState: 'Published', releaseId: `release-${index}`, releaseRevision: 3, boundaryRevisionId: 'boundary-3',
}));
const finding = { findingId: 'finding-1', offeringId: offering.offeringId, revision: 1,
  title: 'Collection delay', observation: 'Source observation', severityAsStated: 'Moderate', controlIds: [],
  citations: [], workflowState: 'Open', createdAt: '2026-09-26T12:00:00Z' };
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(packageApi.getPackageCandidates).mockReset();
  vi.mocked(api.getBoundaryOverview).mockImplementation(async (_id, next = 1) => ({
    offeringId: offering.offeringId, offeringRevision: offering.revision,
    capabilities: { items: capabilities.slice(next - 1, next), page: next, pageSize: 1, total: 2, published: 2, awaitingReview: 0 },
    missionSystems: page([]),
  }));
  vi.mocked(catalogApi.getProviderCapability).mockImplementation(async id => ({
    capability: { source: 'Provider', componentId: id, capabilityId: id, name: id,
      description: id === 'capability-0' ? 'Backup implementation' : 'Key management implementation',
      componentName: '', componentType: 'Service', lifecycle: 'Published', reviewState: 'Reviewed',
      sourceFormat: 'OSCAL', sourceReference: null, distinctAdoptionCount: 0, workingRevision: 8, releasedRevision: 3 },
    supportingComponents: [], unresolvedContributorIds: [], sourceArtifacts: [],
    mappedControlIds: [id === 'capability-0' ? 'CP-9' : 'SC-12'], sourceEvidenceReferences: null, implementationNarrative: null,
  }));
  vi.mocked(api.listFindings).mockResolvedValue(page([finding]));
  vi.mocked(api.getFinding).mockResolvedValue(finding);
  vi.mocked(api.listFindingEvidence).mockResolvedValue(page([{
    evidenceId: 'evidence-1', findingId: finding.findingId, offeringId: offering.offeringId, findingRevision: 1,
    fileName: 'retained-assessment.pdf', mediaType: 'application/pdf', byteLength: 120, sha256: 'test-hash',
    description: 'Retained assessment', state: 'PendingReview', createdAt: finding.createdAt, latestReview: null,
  }]));
});

it('filters and sorts the approved capability list and opens retained context in a drawer', async () => {
  // Arrange
  render(<MemoryRouter><OfferingCapabilities offering={offering} /></MemoryRouter>);
  await screen.findByRole('table', { name: 'Service implementations' });
  // Act
  fireEvent.change(screen.getByRole('combobox', { name: 'Sort by name' }), { target: { value: 'desc' } });
  // Assert
  const rows = within(screen.getByRole('table', { name: 'Service implementations' })).getAllByRole('row');
  expect(rows[1]).toHaveTextContent('Azure Key Vault');
  // Act
  fireEvent.change(screen.getByRole('combobox', { name: 'Release state' }), { target: { value: 'pending' } });
  // Assert
  expect(screen.getByText('No capabilities match this search.')).toBeInTheDocument();
  // Act
  fireEvent.change(screen.getByRole('combobox', { name: 'Release state' }), { target: { value: 'all' } });
  fireEvent.click(screen.getByRole('button', { name: 'Azure Key Vault' }));
  // Assert
  const drawer = screen.getByRole('dialog', { name: 'Azure Key Vault' });
  expect(drawer).toHaveTextContent('Revision 3');
  expect(drawer).toHaveTextContent('Boundary context');
  expect(drawer).toHaveTextContent('not an immutable release payload');
  expect(within(drawer).getByRole('link', { name: 'Open capability workflow' })).toHaveAttribute('href', '/security-capabilities/capability-1');
  // Act
  fireEvent.keyDown(drawer, { key: 'Escape' });
  // Assert
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
});

it('presents the offering implementation drawer with readable sections, exact duty groups and scoped real actions', async () => {
  // Arrange
  const name = 'An independently operated protection with a deliberately long capability name';
  const description = 'First source paragraph.\n\nSecond source paragraph with the complete retained implementation.';
  vi.mocked(api.getBoundaryOverview).mockResolvedValue({
    offeringId: offering.offeringId, offeringRevision: offering.revision,
    capabilities: { ...page([{ ...capabilities[0]!, name, candidateId: 'candidate-7', packageId: 'package-7' }]), published: 1, awaitingReview: 0 },
    missionSystems: page([]),
  });
  vi.mocked(packageApi.getPackageCandidates).mockResolvedValue(page([candidate({
    candidateId: 'candidate-7', description, controlDuties: { 'CP-9': 'Shared', 'CP-10': 'Shared', 'SC-12': 'Customer manages recorded keys.' },
    revision: 7,
  })]));
  render(<MemoryRouter><WorkspaceNavigationProvider workspace={{ kind: 'csp' }}>
    <OfferingCapabilities offering={offering} />
  </WorkspaceNavigationProvider></MemoryRouter>);
  await screen.findByRole('table', { name: 'Service implementations' });
  // Act
  fireEvent.click(screen.getByRole('button', { name }));
  // Assert
  const drawer = screen.getByRole('dialog', { name });
  expect(drawer).toHaveClass('offering-implementation-drawer');
  expect(within(drawer).getByText(offering.name, { exact: true })).toBeInTheDocument();
  expect(within(drawer).getAllByRole('heading', { level: 3 }).map(heading => heading.textContent)).toEqual([
    'Service implementation', 'Control-duty references', 'Who does what?', 'Scope & applicability', 'Evidence & provenance',
  ]);
  expect(drawer.querySelector('.offering-implementation-statement')?.textContent).toBe(description);
  const group = within(drawer).getByRole('group', { name: 'Shared' });
  expect(group).toHaveTextContent('CP-9');
  expect(group).toHaveTextContent('CP-10');
  expect(within(drawer).getByRole('group', { name: 'Customer manages recorded keys.' })).toHaveTextContent('SC-12');
  expect(within(drawer).getByText('Source and release identities').closest('details')).not.toHaveAttribute('open');
  expect(within(drawer).getByRole('link', { name: 'Open capability workflow' })).toHaveAttribute('href', '/workspaces/csp/security-capabilities/capability-0');
  expect(within(drawer).getByRole('link', { name: 'Review retained source' })).toHaveAttribute('href',
    `/workspaces/csp/authorizations/offerings/${offering.offeringId}/packages/package-7/candidates/candidate-7`);
  expect(within(drawer).queryByRole('textbox')).not.toBeInTheDocument();
  expect(drawer).toHaveTextContent('not an immutable release payload');
  expect(drawer).not.toHaveTextContent('Owns the listed shared-service operations');
});

it('keeps unavailable duties explicit for a draft catalog-only implementation, with no fabricated published revision', async () => {
  // Arrange
  vi.mocked(api.getBoundaryOverview).mockResolvedValue({
    offeringId: offering.offeringId, offeringRevision: offering.revision,
    capabilities: { ...page([{ ...capabilities[0]!, publicationState: 'Draft', releaseRevision: null, boundaryRevisionId: null }]), published: 0, awaitingReview: 1 },
    missionSystems: page([]),
  });
  render(<MemoryRouter><OfferingCapabilities offering={offering} /></MemoryRouter>);
  await screen.findByRole('table', { name: 'Service implementations' });
  // Act
  screen.getByRole('button', { name: 'Azure Backup' }).focus();
  fireEvent.click(screen.getByRole('button', { name: 'Azure Backup' }));
  // Assert
  const drawer = screen.getByRole('dialog', { name: 'Azure Backup' });
  expect(within(drawer).getByRole('heading', { name: 'Who does what?' })).toBeInTheDocument();
  expect(drawer).toHaveTextContent('Pinned published duties are not supplied');
  expect(drawer).toHaveTextContent('Release version not reported');
  expect(drawer).toHaveTextContent('Current catalog context');
  expect(within(drawer).queryByRole('group', { name: 'Shared' })).not.toBeInTheDocument();
  expect(within(drawer).queryByRole('link', { name: 'Review retained source' })).not.toBeInTheDocument();
});

it('retains the exact selected implementation context on source failure and retries without inventing duties', async () => {
  // Arrange
  vi.mocked(packageApi.getPackageCandidates).mockRejectedValueOnce(new Error('Retained implementation is restricted'));
  vi.mocked(api.getBoundaryOverview).mockResolvedValue({
    offeringId: offering.offeringId, offeringRevision: offering.revision,
    capabilities: { ...page([{ ...capabilities[0]!, candidateId: 'candidate-7', packageId: 'package-7' }]), published: 1, awaitingReview: 0 },
    missionSystems: page([]),
  });
  render(<MemoryRouter><OfferingCapabilities offering={offering} /></MemoryRouter>);
  await screen.findByRole('table', { name: 'Service implementations' });
  // Act
  fireEvent.click(screen.getByRole('button', { name: 'Azure Backup' }));
  // Assert
  const drawer = screen.getByRole('dialog', { name: 'Azure Backup' });
  expect(within(drawer).getByRole('alert')).toHaveTextContent('Retained implementation is restricted');
  expect(drawer).toHaveTextContent('Implementation summary unavailable.');
  expect(drawer).toHaveTextContent('Source control references not reported.');
  expect(within(drawer).getByRole('link', { name: 'Review retained source' })).toHaveAttribute('href',
    api.authorizationHref(offering.offeringId, 'packages/package-7/candidates/candidate-7'));
  // Act
  vi.mocked(packageApi.getPackageCandidates).mockResolvedValue(page([candidate({
    candidateId: 'candidate-7', description: 'Recovered retained implementation', controlDuties: {},
  })]));
  fireEvent.click(within(drawer).getByRole('button', { name: 'Retry implementation details' }));
  // Assert
  await waitFor(() => expect(drawer).toHaveTextContent('Recovered retained implementation'));
  expect(within(drawer).queryByRole('alert')).not.toBeInTheDocument();
  expect(drawer).toHaveTextContent('Pinned published duties are not supplied');
  expect(catalogApi.getProviderCapability).not.toHaveBeenCalled();
  // Act
  fireEvent.keyDown(drawer, { key: 'Escape' });
  // Assert
  expect(screen.getByRole('button', { name: 'Azure Backup' })).toHaveFocus();
});

it('rejects a mixed offering revision across capability pages instead of showing a partial baseline', async () => {
  // Arrange
  vi.mocked(api.getBoundaryOverview).mockImplementation(async (_id, next = 1) => ({
    offeringId: offering.offeringId, offeringRevision: offering.revision + next - 1,
    capabilities: { items: capabilities.slice(next - 1, next), page: next, pageSize: 1, total: 2, published: 2, awaitingReview: 0 },
    missionSystems: page([]),
  }));
  // Act
  render(<MemoryRouter><OfferingCapabilities offering={offering} /></MemoryRouter>);
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('Offering changed while capability pages were loading');
  expect(screen.queryByRole('table', { name: 'Service implementations' })).not.toBeInTheDocument();
});

it('uses available retained-source duty/version contracts even when the row also has a canonical capability', async () => {
  // Arrange
  vi.mocked(api.getBoundaryOverview).mockResolvedValue({
    offeringId: offering.offeringId, offeringRevision: offering.revision,
    capabilities: { ...page([{ ...capabilities[0]!, candidateId: 'source-candidate', packageId: 'source-package' }]), published: 1, awaitingReview: 0 },
    missionSystems: page([]),
  });
  vi.mocked(packageApi.getPackageCandidates).mockResolvedValue(page([{
    candidateId: 'source-candidate', name: 'Retained protection', description: 'Exact retained source description',
    type: 'Capability', componentType: 'Service', classification: '', serviceCategory: '', controlDuties: { 'CP-9': 'Shared' },
    contributorIds: [], citations: [], duplicateMatches: [], duplicateResolution: null, rationale: null,
    reviewState: 'Published', revision: 7, confidence: null, publishedRecordId: 'capability-0',
  }]));
  // Act
  render(<MemoryRouter><OfferingCapabilities offering={offering} /></MemoryRouter>);
  await screen.findByText('Exact retained source description');
  fireEvent.click(screen.getByRole('button', { name: 'Azure Backup' }));
  // Assert
  const drawer = screen.getByRole('dialog', { name: 'Azure Backup' });
  expect(drawer).toHaveTextContent('Retained source proposal revision 7');
  expect(drawer).toHaveTextContent('Shared');
  expect(drawer).toHaveTextContent('Revision 3');
  expect(catalogApi.getProviderCapability).not.toHaveBeenCalled();
});

it('exposes source-version changes in an open read-only drawer without replacing its retained release', async () => {
  // Arrange
  const row = { ...capabilities[0]!, candidateId: 'source-candidate', packageId: 'source-package' };
  const response = { offeringId: offering.offeringId, offeringRevision: offering.revision,
    capabilities: { ...page([row]), published: 1, awaitingReview: 0 }, missionSystems: page([]) };
  vi.mocked(api.getBoundaryOverview).mockResolvedValue(response);
  let sourceRevision = 7;
  vi.mocked(packageApi.getPackageCandidates).mockImplementation(async () => page([
    candidate({ candidateId: 'source-candidate', revision: sourceRevision, description: 'Retained source',
      controlDuties: { 'CP-9': 'Shared' } }),
  ]));
  const view = render(<MemoryRouter><OfferingCapabilities offering={offering} /></MemoryRouter>);
  await screen.findByText('Retained source');
  fireEvent.click(screen.getByRole('button', { name: 'Azure Backup' }));
  // Act
  sourceRevision = 8;
  vi.mocked(api.getBoundaryOverview).mockResolvedValue({ ...response, offeringRevision: offering.revision + 1 });
  view.rerender(<MemoryRouter><OfferingCapabilities offering={{ ...offering, revision: offering.revision + 1 }} /></MemoryRouter>);
  // Assert
  const drawer = screen.getByRole('dialog', { name: 'Azure Backup' });
  await within(drawer).findByText(/Retained source proposal revision 8/);
  expect(within(drawer).getByRole('alert')).toHaveTextContent('Source version changed since this drawer opened');
  expect(drawer).toHaveTextContent('Revision 3');
});

it('matches the capability toolbar and support actions using all pages and actual published revision 3', async () => {
  // Arrange
  render(<MemoryRouter><OfferingCapabilities offering={offering} /></MemoryRouter>);
  // Act
  const search = await screen.findByRole('textbox', { name: 'Search capabilities' });
  fireEvent.change(search, { target: { value: 'SC-12' } });
  // Assert
  const table = screen.getByRole('table', { name: 'Service implementations' });
  expect(within(table).getByText('Azure Key Vault')).toBeInTheDocument();
  expect(within(table).queryByText('Azure Backup')).not.toBeInTheDocument();
  expect(screen.getByText('2 capabilities available · 0 awaiting review')).toBeInTheDocument();
  expect(screen.getByText('Live: revision 3')).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'View supporting components' })).toHaveAttribute('href',
    api.authorizationHref(offering.offeringId, 'inherited-coverage'));
  expect(screen.getByRole('link', { name: 'Review mission responsibilities' })).toHaveAttribute('href',
    api.authorizationHref(offering.offeringId, 'inherited-coverage?task=responsibilities'));
  expect(screen.queryByText(/1\.2|1\.3|Live: revision 8/)).not.toBeInTheDocument();
  fireEvent.change(search, { target: { value: 'not a capability' } });
  expect(screen.getByText('No capabilities match this search.')).toBeInTheDocument();
});

it('keeps evidence first-class with the exact artifact/access/freshness columns and separate findings', async () => {
  // Arrange
  render(<MemoryRouter><FindingsPage offering={offering} onChanged={vi.fn()} /></MemoryRouter>);
  // Act
  const table = await screen.findByRole('table', { name: 'Evidence library' });
  // Assert
  expect(within(table).getAllByRole('columnheader').map(header => header.textContent)).toEqual([
    'Artifact', 'Customer access', 'Freshness', 'Actions',
  ]);
  expect(within(table).getByText('Provider private')).toBeInTheDocument();
  expect(within(table).getByText('No retained review')).toBeInTheDocument();
  expect(within(table).queryByText('Shareable')).not.toBeInTheDocument();
  expect(within(table).getByRole('link', { name: 'Review evidence access' })).toHaveAttribute('href',
    api.authorizationHref(offering.offeringId, 'evidence/evidence-1?findingId=finding-1'));
  expect(screen.getByRole('table', { name: 'Service findings' })).not.toContainElement(table);
  expect(screen.getByRole('region', { name: 'Contributes to the system package' })).toHaveTextContent('System evidence index');
  expect(screen.getByRole('region', { name: 'Contributes to the system package' })).not.toHaveTextContent('Hosting and boundary description');
  expect(screen.getByRole('button', { name: 'Add finding' })).toBeInTheDocument();
});

it('bounds realistic long table notes without discarding their text or exact retained release', async () => {
  // Arrange
  const description = `${'Provider implementation and retained source provenance. '.repeat(12)}SHA256 ${'a'.repeat(64)}`;
  const detail = await catalogApi.getProviderCapability('capability-0');
  vi.mocked(catalogApi.getProviderCapability).mockResolvedValue({
    ...detail, capability: { ...detail.capability, description }, mappedControlIds: ['CP-9(1)', 'SC-12(3)'],
  });
  vi.mocked(api.listFindingEvidence).mockResolvedValue(page([{
    evidenceId: 'evidence-1', findingId: finding.findingId, offeringId: offering.offeringId, findingRevision: 1,
    fileName: 'azure-backup-retained-assessment-source-provenance.pdf', mediaType: 'application/pdf',
    byteLength: 120, sha256: 'test-hash', description, state: 'PendingReview', createdAt: finding.createdAt, latestReview: null,
  }]));
  // Act
  render(<MemoryRouter><OfferingCapabilities offering={offering} /><FindingsPage offering={offering} onChanged={vi.fn()} /></MemoryRouter>);
  const capabilityTable = await screen.findByRole('table', { name: 'Service implementations' });
  const evidenceTable = await screen.findByRole('table', { name: 'Evidence library' });
  // Assert
  expect(capabilityTable).toHaveClass('table-fixed', 'offering-capability-table');
  expect([...capabilityTable.querySelectorAll('col')].map(col => col.style.width)).toEqual(['50%', '28%', '22%']);
  expect(evidenceTable).toHaveClass('table-fixed', 'min-w-[700px]');
  expect([...evidenceTable.querySelectorAll('col')].map(col => col.style.width)).toEqual(['42%', '24%', '22%', '12%']);
  for (const note of screen.getAllByText(description)) expect(note).toHaveClass('line-clamp-2');
  for (const control of within(capabilityTable).getAllByText('CP-9(1)')) expect(control).toHaveClass('whitespace-nowrap');
  const release = within(capabilityTable).getByText('release-0').closest('details');
  expect(release).not.toHaveAttribute('open');
  expect(release).toHaveTextContent('Revision 3');
  expect(within(evidenceTable).getByRole('link', { name: 'Review evidence access' })).toHaveAttribute('href',
    api.authorizationHref(offering.offeringId, 'evidence/evidence-1?findingId=finding-1'));
});

it('keeps failed capability metadata visible and permits retry even when a control search hides its row', async () => {
  // Arrange
  vi.mocked(catalogApi.getProviderCapability).mockRejectedValueOnce(new Error('Implementation unavailable'));
  render(<MemoryRouter><OfferingCapabilities offering={offering} /></MemoryRouter>);
  await screen.findByRole('alert');
  // Act
  fireEvent.change(screen.getByRole('textbox', { name: 'Search capabilities' }), { target: { value: 'CP-9' } });
  // Assert
  expect(screen.getAllByRole('status').some(status => status.textContent?.includes('Search is incomplete'))).toBe(true);
  expect(screen.queryByText('No capabilities match this search.')).not.toBeInTheDocument();
  // Act
  fireEvent.click(screen.getByRole('button', { name: 'Retry capability search' }));
  // Assert
  expect(await screen.findByText('Backup implementation')).toBeInTheDocument();
  expect(screen.getByRole('table', { name: 'Service implementations' })).toHaveTextContent('CP-9');
  expect(screen.queryByText('No mappings recorded')).not.toBeInTheDocument();
});

it('does not derive offering release versions from current catalog working or released metadata', async () => {
  // Arrange
  vi.mocked(api.getBoundaryOverview).mockResolvedValue({
    offeringId: offering.offeringId, offeringRevision: offering.revision,
    capabilities: { ...page(capabilities.map(item => ({ ...item, releaseRevision: null }))), published: 2, awaitingReview: 0 },
    missionSystems: page([]),
  });
  // Act
  render(<MemoryRouter><OfferingCapabilities offering={offering} /></MemoryRouter>);
  // Assert
  expect(await screen.findByText('Release version not reported')).toBeInTheDocument();
  expect(screen.queryByText('Live: revision 3')).not.toBeInTheDocument();
  expect(screen.queryByText('Live: revision 8')).not.toBeInTheDocument();
});

it('aggregates evidence from later finding pages without selecting a finding or assuming a shared attachment', async () => {
  // Arrange
  vi.mocked(api.listFindings).mockImplementation(async (_id, next = 1) => ({
    items: [{ ...finding, findingId: `finding-${next}` }], page: next, pageSize: 1, total: 2,
  }));
  vi.mocked(api.listFindingEvidence).mockImplementation(async (_id, findingId) => page([{
    evidenceId: `evidence-${findingId}`, findingId, offeringId: offering.offeringId, findingRevision: 1,
    fileName: `${findingId}.pdf`, mediaType: 'application/pdf', byteLength: 120, sha256: 'test-hash',
    description: 'Retained assessment', state: 'PendingReview', createdAt: finding.createdAt, latestReview: null,
  }]));
  // Act
  render(<MemoryRouter><FindingsPage offering={offering} onChanged={vi.fn()} /></MemoryRouter>);
  // Assert
  const table = await screen.findByRole('table', { name: 'Evidence library' });
  expect(within(table).getByText('finding-1.pdf')).toBeInTheDocument();
  expect(within(table).getByText('finding-2.pdf')).toBeInTheDocument();
  expect(within(table).getAllByText('Provider private')).toHaveLength(2);
  expect(within(table).queryByRole('link', { name: /Download/ })).not.toBeInTheDocument();
});

it('does not present failed aggregated evidence as an empty or customer-accessible library', async () => {
  // Arrange
  vi.mocked(api.listFindingEvidence).mockRejectedValue(new Error('Evidence unavailable'));
  render(<MemoryRouter><FindingsPage offering={offering} onChanged={vi.fn()} /></MemoryRouter>);
  // Act
  const alert = await screen.findByRole('alert');
  // Assert
  expect(alert).toHaveTextContent('Evidence unavailable');
  expect(screen.queryByText(/No finding evidence retained/)).not.toBeInTheDocument();
  expect(screen.queryByRole('table', { name: 'Evidence library' })).not.toBeInTheDocument();
  expect(screen.getByRole('table', { name: 'Service findings' })).toBeInTheDocument();
});

it('opens the index upload query with an explicit finding choice and reuses the revision-fenced upload form', async () => {
  // Arrange
  vi.mocked(api.listFindings).mockResolvedValue(page([finding, { ...finding, findingId: 'finding-2', title: 'Second finding' }]));
  vi.mocked(api.listFindingEvidence).mockResolvedValue(page([]));
  vi.mocked(api.getFinding).mockResolvedValue({ ...finding, findingId: 'finding-2', title: 'Second finding', revision: 7 });
  vi.mocked(api.submitFindingEvidence).mockRejectedValue(new PackageImportError('Finding revision changed', 409));
  render(<MemoryRouter initialEntries={[api.authorizationHref(offering.offeringId, 'findings?action=upload')]}>
    <FindingsPage offering={offering} onChanged={vi.fn()} />
  </MemoryRouter>);
  const dialog = await screen.findByRole('dialog', { name: 'Submit remediation evidence' });
  await within(dialog).findByRole('option', { name: 'Second finding' });
  expect(within(dialog).queryByLabelText('Evidence description')).not.toBeInTheDocument();
  expect(api.submitFindingEvidence).not.toHaveBeenCalled();
  // Act
  fireEvent.change(within(dialog).getByLabelText('Target finding'), { target: { value: 'finding-2' } });
  await within(dialog).findByLabelText('Evidence description');
  fireEvent.change(within(dialog).getByLabelText('Evidence file (1 byte–10 MiB)'), { target: { files: [new File(['evidence'], 'evidence.txt')] } });
  fireEvent.change(within(dialog).getByLabelText('Evidence description'), { target: { value: 'Retained evidence description' } });
  fireEvent.click(within(dialog).getByRole('button', { name: 'Submit evidence for review' }));
  // Assert
  expect(await within(dialog).findByRole('alert')).toHaveTextContent('Finding revision changed');
  expect(api.submitFindingEvidence).toHaveBeenCalledWith(offering.offeringId, 'finding-2', 7,
    'Retained evidence description', expect.objectContaining({ name: 'evidence.txt' }), expect.any(String));
  expect(within(dialog).getByLabelText('Evidence description')).toHaveValue('Retained evidence description');
  const cancel = within(dialog).getByRole('button', { name: 'Cancel' });
  await waitFor(() => expect(cancel).toBeEnabled());
  fireEvent.click(cancel);
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
});

it('requires a target selection even for a single finding and never uploads on query navigation or cancel', async () => {
  // Arrange
  render(<MemoryRouter initialEntries={[api.authorizationHref(offering.offeringId, 'findings?action=upload')]}>
    <FindingsPage offering={offering} onChanged={vi.fn()} />
  </MemoryRouter>);
  // Act
  const dialog = await screen.findByRole('dialog', { name: 'Submit remediation evidence' });
  await within(dialog).findByRole('option', { name: finding.title });
  // Assert
  expect(within(dialog).getByLabelText('Target finding')).toHaveValue('');
  expect(within(dialog).queryByLabelText('Evidence description')).not.toBeInTheDocument();
  fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  expect(api.submitFindingEvidence).not.toHaveBeenCalled();
  expect(api.getFinding).not.toHaveBeenCalled();
});

it('blocks an upload when the selected finding read does not match its offering', async () => {
  // Arrange
  vi.mocked(api.getFinding).mockResolvedValue({ ...finding, offeringId: 'another-offering' });
  render(<MemoryRouter initialEntries={[api.authorizationHref(offering.offeringId, 'findings?action=upload')]}>
    <FindingsPage offering={offering} onChanged={vi.fn()} />
  </MemoryRouter>);
  const dialog = await screen.findByRole('dialog', { name: 'Submit remediation evidence' });
  await within(dialog).findByRole('option', { name: finding.title });
  // Act
  fireEvent.change(within(dialog).getByLabelText('Target finding'), { target: { value: finding.findingId } });
  // Assert
  expect(await within(dialog).findByRole('alert')).toHaveTextContent('The selected finding could not be verified in this offering');
  expect(within(dialog).queryByLabelText('Evidence description')).not.toBeInTheDocument();
  expect(api.submitFindingEvidence).not.toHaveBeenCalled();
});
