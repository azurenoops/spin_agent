import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { AuthorizationsPage } from '../../features/provider-authorizations/AuthorizationsPage';
import WorkspacePageHeader from '../../components/layout/WorkspacePageHeader';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import * as api from '../../features/provider-authorizations/api';
import { offering } from './testData';
import { offeringOverview, recordedAuthorization } from './overviewFixtures';
import { PackageImportError } from '../../features/package-imports/request';
import { page } from '../package-imports/fixtures';
import '../package-imports/crypto';

vi.mock('../../components/layout/PageLayout', () => ({ default: ({ children }: { children: ReactNode }) => <main>{children}</main> }));
vi.mock('../../components/layout/WorkspacePageHeader', async original => {
  const actual = await original<typeof import('../../components/layout/WorkspacePageHeader')>();
  return { default: vi.fn(actual.default) };
});
vi.mock('../../components/layout/PageHero', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => ({ target: { kind: 'csp' }, workspace: { permissions: { canAccessCsp: true } } }),
}));
vi.mock('../../features/provider-authorizations/api', async original => ({
  ...await original<typeof api>(), getOffering: vi.fn(), getOfferingOverview: vi.fn(), listPackageVersions: vi.fn(), listOfferings: vi.fn(), listFindings: vi.fn(),
  getFinding: vi.fn(), listFindingEvidence: vi.fn(), listPoamItems: vi.fn(), reviewFinding: vi.fn(), submitFindingEvidence: vi.fn(), createPoamItem: vi.fn(),
}));
const finding = { findingId: 'finding-1', offeringId: offering.offeringId, revision: 3,
  title: 'Synthetic logging delay', observation: 'Retained test observation', severityAsStated: 'Moderate',
  controlIds: ['AU-2'], citations: [], workflowState: 'Open', createdAt: '2026-09-26T12:00:00Z' };
function mount(path: string) {
  return render(<MemoryRouter initialEntries={[path]}><WorkspaceNavigationProvider workspace={{ kind: 'csp' }}>
    <AuthorizationsPage />
  </WorkspaceNavigationProvider></MemoryRouter>);
}
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getOffering).mockResolvedValue(offering);
  vi.mocked(api.listOfferings).mockResolvedValue(page([offering]));
  vi.mocked(api.listFindings).mockResolvedValue(page([finding]));
  vi.mocked(api.getFinding).mockResolvedValue(finding);
  vi.mocked(api.listFindingEvidence).mockResolvedValue(page([]));
  vi.mocked(api.listPoamItems).mockResolvedValue(page([]));
  vi.mocked(api.getOfferingOverview).mockResolvedValue(offeringOverview());
  vi.mocked(api.listPackageVersions).mockResolvedValue(page([]));
});
it('never treats uploaded evidence as closure and requires explicit selected evidence', async () => {
  // Arrange
  vi.mocked(api.listFindingEvidence).mockResolvedValue(page([{
    evidenceId: 'evidence-1', findingId: finding.findingId, offeringId: offering.offeringId, findingRevision: 3,
    fileName: 'synthetic-evidence.txt', mediaType: 'text/plain', byteLength: 10, sha256: 'test-hash',
    description: 'Synthetic retained evidence', state: 'PendingReview', createdAt: finding.createdAt, latestReview: null,
  }]));
  vi.mocked(api.reviewFinding).mockResolvedValue({
    reviewId: 'review-1', findingId: finding.findingId, offeringId: offering.offeringId, findingRevision: 4,
    evidenceIds: ['evidence-1'], disposition: 'AcceptClosure', rationale: 'Verified collection',
    reviewedBy: 'Test reviewer', reviewedAt: finding.createdAt, workflowState: 'Closed',
  });
  mount(api.authorizationHref(offering.offeringId, 'findings/finding-1'));
  // Act
  await screen.findByText('synthetic-evidence.txt');
  fireEvent.change(screen.getByLabelText('Review disposition'), { target: { value: 'AcceptClosure' } });
  fireEvent.change(screen.getByLabelText('Review rationale'), { target: { value: 'Verified collection' } });
  // Assert
  expect(screen.getByRole('button', { name: 'Record evidence review' })).toBeDisabled();
  expect(screen.getByText('PendingReview')).toBeInTheDocument();
  fireEvent.click(screen.getByRole('checkbox', { name: 'Select synthetic-evidence.txt for review' }));
  fireEvent.click(screen.getByRole('button', { name: 'Record evidence review' }));
  await screen.findByText('Review recorded: Closed');
  expect(api.reviewFinding).toHaveBeenCalledWith(offering.offeringId, finding.findingId, {
    expectedRevision: 3, evidenceIds: ['evidence-1'], disposition: 'AcceptClosure', rationale: 'Verified collection',
  });
});
it('uses the service-offerings table and no synthetic release claims', async () => {
  // Arrange
  mount(api.authorizationHref());
  // Act
  const table = await screen.findByRole('table', { name: 'Your offerings' });
  // Assert
  expect(within(table).getByRole('link', { name: offering.name })).toBeInTheDocument();
  expect(screen.getByRole('heading', { name: 'Service offerings', level: 1 })).toBeInTheDocument();
  expect(screen.queryByText('Release 1.2 live')).not.toBeInTheDocument();
  expect(WorkspacePageHeader).toHaveBeenCalledWith(expect.objectContaining({
    title: 'Service offerings', eyebrow: 'Provider workspace',
  }), undefined);
});
it('renders the previously missing evidence and findings route with offering tabs', async () => {
  // Arrange
  mount(api.authorizationHref(offering.offeringId, 'findings'));
  // Act
  await screen.findByText(finding.title);
  // Assert
  expect(screen.getByRole('navigation', { name: 'Offering sections' })).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Authorizations & sources' })).toHaveAttribute('href', api.authorizationHref(offering.offeringId, 'packages'));
  expect(screen.getByRole('link', { name: 'Manage finding' })).toHaveAttribute('href', api.authorizationHref(offering.offeringId, 'findings/finding-1'));
  expect(screen.getByText(/Provider remediation and mission risk acceptance/)).toBeInTheDocument();
});
it('retains a failed finding read and retries without presenting a clean service', async () => {
  // Arrange
  vi.mocked(api.listFindings).mockRejectedValueOnce(new Error('Provider service unavailable'));
  mount(api.authorizationHref(offering.offeringId, 'findings'));
  // Act
  await screen.findByRole('alert');
  fireEvent.click(screen.getByRole('button', { name: /Retry/ }));
  // Assert
  expect(await screen.findByText(finding.title)).toBeInTheDocument();
});
it('shows a finding revision conflict without discarding the review rationale', async () => {
  // Arrange
  vi.mocked(api.reviewFinding).mockRejectedValue(new PackageImportError('Finding revision changed', 409));
  mount(api.authorizationHref(offering.offeringId, 'findings/finding-1'));
  // Act
  await screen.findByLabelText('Review rationale');
  fireEvent.change(screen.getByLabelText('Review rationale'), { target: { value: 'Keep this review context' } });
  fireEvent.click(screen.getByRole('button', { name: 'Record evidence review' }));
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('Finding revision changed');
  expect(screen.getByLabelText('Review rationale')).toHaveValue('Keep this review context');
  await waitFor(() => expect(screen.getByRole('button', { name: 'Reload current finding' })).toBeEnabled());
  vi.mocked(api.getFinding).mockResolvedValue({ ...finding, revision: 4 });
  fireEvent.click(screen.getByRole('button', { name: 'Reload current finding' }));
  await screen.findByText('Revision 4');
  expect(screen.getByLabelText('Review rationale')).toHaveValue('Keep this review context');
});
it('creates a provider remediation plan pinned to the current offering and finding', async () => {
  // Arrange
  vi.mocked(api.createPoamItem).mockResolvedValue({
    poamId: 'plan-1', offeringId: offering.offeringId, revision: 1, title: 'Restore logging',
    findingIds: [finding.findingId], correctiveAction: 'Automate collection', milestones: [], citations: [],
    workflowState: 'Open', createdAt: finding.createdAt,
  });
  mount(api.authorizationHref(offering.offeringId, 'findings/finding-1'));
  // Act
  fireEvent.click(await screen.findByRole('button', { name: 'Add remediation plan' }));
  fireEvent.change(screen.getByLabelText('Plan title'), { target: { value: 'Restore logging' } });
  fireEvent.change(screen.getByLabelText('Corrective action'), { target: { value: 'Automate collection' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save remediation plan' }));
  // Assert
  await screen.findByText('Remediation plan saved.');
  expect(api.createPoamItem).toHaveBeenCalledWith(offering.offeringId, {
    expectedOfferingRevision: offering.revision, title: 'Restore logging', findingIds: [finding.findingId],
    correctiveAction: 'Automate collection', ownerAsStated: '', milestones: [], citations: [],
  }, expect.any(String));
});
it('keeps retained source coverage distinct from a reviewed external authorization', async () => {
  // Arrange
  const summary = offeringOverview();
  summary.authorizations = { ...summary.authorizations, ...page([recordedAuthorization]), recorded: 1 };
  vi.mocked(api.getOfferingOverview).mockResolvedValue(summary);
  mount(api.authorizationHref(offering.offeringId, 'packages'));
  // Act
  await screen.findByText(recordedAuthorization.reference);
  // Assert
  expect(screen.getByText('Uploaded authorization documents.zip')).toBeInTheDocument();
  expect(screen.getByText(/26 of 27 files processed/)).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Review recorded authorization' })).toHaveAttribute('href', api.authorizationHref(offering.offeringId, 'decisions/decision-1'));
});
