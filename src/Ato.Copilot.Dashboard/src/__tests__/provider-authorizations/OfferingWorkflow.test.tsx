import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import type { ReactNode } from 'react';
import ApplicationRoutes from '../../ApplicationRoutes';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import * as api from '../../features/provider-authorizations/api';
import * as packageApi from '../../features/package-imports/api';
import type { Offering } from '../../features/provider-authorizations/types';
import { PackageImportError } from '../../features/package-imports/request';
import { boundary, receipt } from './testData';
import { offeringOverview } from './overviewFixtures';

vi.mock('../../features/auth/RequireAuth', () => ({ default: ({ children }: { children: ReactNode }) => children }));
vi.mock('../../components/layout/PageLayout', () => ({ default: ({ children, leftPanel }: { children: ReactNode; leftPanel?: ReactNode }) => <main>{leftPanel}{children}</main> }));
vi.mock('../../components/layout/PageHero', () => ({ default: ({ title, actions }: { title: string; actions?: ReactNode }) => <header><h1>{title}</h1>{actions}</header> }));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => ({ target: { kind: 'csp' }, workspace: { permissions: { canAccessCsp: true } } }),
}));
vi.mock('../../features/provider-authorizations/HostingSetupPage', () => ({
  HostingSetupPage: ({ offering }: { offering: Offering }) => <section aria-label="Offering hosting">{offering.offeringId} · {offering.revision}</section>,
}));
vi.mock('../../features/provider-authorizations/api', async original => ({
  ...await original<typeof api>(), listOfferings: vi.fn(), getOffering: vi.fn(), getOfferingOverview: vi.fn(), createOffering: vi.fn(),
  listBoundaries: vi.fn(), getBoundary: vi.fn(), getBoundaryOverview: vi.fn(), createBoundary: vi.fn(), listPackageVersions: vi.fn(), uploadPackage: vi.fn(), listDecisions: vi.fn(),
  createDecision: vi.fn(), recordDecision: vi.fn(), listFindings: vi.fn(),
  listImpactReviews: vi.fn(), listImpactTargets: vi.fn(), getAssociatedPackage: vi.fn(), associatePackage: vi.fn(),
}));
vi.mock('../../features/package-imports/api', async original => ({
  ...await original<typeof packageApi>(), getPackageStatus: vi.fn(), receivePackage: vi.fn(), getPackageCandidates: vi.fn(),
}));
vi.mock('../../features/package-imports/uploadIdentity', () => ({
  preparePackageUpload: async (files: File[]) => ({ files, key: 'routing-test-upload-key' }),
}));
const offering: Offering = {
  offeringId: 'offering-a', providerId: 'provider-a', name: 'Synthetic offering', description: 'Source-backed service',
  environments: ['AzureUSGovernment'], revision: 3, lifecycle: 'Draft',
  currentBoundaryRevisionId: null, currentHostingScopeRevisionId: null,
};
const page = <T,>(items: T[]) => ({ items, page: 1, pageSize: 25, total: items.length });
function mount(path: string) {
  return render(<MemoryRouter initialEntries={[`/workspaces/csp${path}`]}>
    <WorkspaceNavigationProvider workspace={{ kind: 'csp' }}>
      <Routes><Route path="/workspaces/csp/*" element={<ApplicationRoutes />} /></Routes>
    </WorkspaceNavigationProvider>
  </MemoryRouter>);
}
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.listOfferings).mockResolvedValue(page([offering]));
  vi.mocked(api.getOffering).mockResolvedValue(offering);
  vi.mocked(api.getOfferingOverview).mockResolvedValue({ ...offeringOverview(), offeringId: offering.offeringId });
  vi.mocked(api.listBoundaries).mockResolvedValue(page([]));
  vi.mocked(api.getBoundaryOverview).mockResolvedValue({
    offeringId: offering.offeringId, offeringRevision: offering.revision,
    capabilities: { ...page([]), awaitingReview: 0, published: 0 }, missionSystems: page([]),
  });
  vi.mocked(api.listPackageVersions).mockResolvedValue(page([]));
  vi.mocked(api.listDecisions).mockResolvedValue(page([]));
  vi.mocked(api.listFindings).mockResolvedValue(page([]));
  vi.mocked(api.listImpactReviews).mockResolvedValue(page([]));
  vi.mocked(api.listImpactTargets).mockResolvedValue(page([]));
  const unassociated = { ...receipt.package, association: null, processingState: 'ReadyForReview' };
  vi.mocked(api.getAssociatedPackage).mockResolvedValue(unassociated);
  vi.mocked(packageApi.receivePackage).mockResolvedValue(unassociated);
  vi.mocked(packageApi.getPackageCandidates).mockResolvedValue(page([]));
});
describe('source-backed offering workflow', () => {
  it('offers a distinct upload action for each existing offering without losing its identity', async () => {
    // Arrange
    vi.mocked(api.listOfferings).mockResolvedValue(page([offering, { ...offering, offeringId: 'offering-b', name: 'Another offering' }]));
    // Act
    mount('/authorizations');
    // Assert
    expect(await screen.findByRole('link', { name: 'Add package to this offering: Synthetic offering' }))
      .toHaveAttribute('href', '/workspaces/csp/authorizations/offerings/offering-a/import');
    expect(screen.getByRole('link', { name: 'Add package to this offering: Another offering' }))
      .toHaveAttribute('href', '/workspaces/csp/authorizations/offerings/offering-b/import');
    expect(screen.getByRole('link', { name: 'Import authorization package' }))
      .toHaveAttribute('href', '/workspaces/csp/authorizations/import');
    expect(api.createOffering).not.toHaveBeenCalled();
  });
  it.each(['', '/boundary', '/packages'])('retains the selected offering in the %s header upload action', async section => {
    // Arrange
    mount(`/authorizations/offerings/offering-a${section}`);
    await screen.findByRole('heading', { name: offering.name });
    expect(screen.getByRole('navigation', { name: 'Offering sections' })).toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('link', { name: 'Add package to this offering' }));
    // Assert
    expect(await screen.findByRole('heading', { name: 'Start with your authorization package' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Boundary revision')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Offering')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Offering name')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Select source files')).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Offering sections' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to offering' }))
      .toHaveAttribute('href', '/workspaces/csp/authorizations/offerings/offering-a');
    expect(api.uploadPackage).not.toHaveBeenCalled();
  });
  it('uses file-first receipt for the selected offering and associates only after boundary confirmation', async () => {
    // Arrange
    const retained = {
      ...receipt,
      package: { ...receipt.package, association: {
        offeringId: offering.offeringId, packageVersionId: receipt.packageVersion.packageVersionId,
        boundaryRevisionId: boundary.boundaryRevisionId,
      } },
      packageVersion: { ...receipt.packageVersion, offeringId: offering.offeringId },
    };
    vi.mocked(api.listBoundaries).mockResolvedValue(page([{ ...boundary, offeringId: offering.offeringId, offeringRevision: offering.revision }]));
    vi.mocked(api.associatePackage).mockResolvedValue(retained);
    vi.mocked(packageApi.getPackageStatus).mockResolvedValue(retained.package);
    const file = new File(['Synthetic authorization source'], 'authorization.txt');
    mount('/authorizations/offerings/offering-a/import');
    await screen.findByLabelText('Select source files');
    // Act
    fireEvent.change(screen.getByLabelText('Select source files'), { target: { files: [file] } });
    fireEvent.click(screen.getByRole('button', { name: 'Upload package' }));
    // Assert
    expect(await screen.findByRole('heading', { name: 'Review extracted scope' })).toBeInTheDocument();
    expect(packageApi.receivePackage).toHaveBeenCalledExactlyOnceWith([file], expect.any(String));
    expect(api.uploadPackage).not.toHaveBeenCalled();
    expect(api.associatePackage).not.toHaveBeenCalled();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Enter offering and boundary manually' }));
    await screen.findByRole('option', { name: /Test service boundary/ });
    expect(screen.queryByLabelText('Offering', { exact: true })).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Boundary revision'), { target: { value: boundary.boundaryRevisionId } });
    fireEvent.click(screen.getByRole('button', { name: 'Associate retained package' }));
    // Assert
    expect(await screen.findByRole('link', { name: 'Continue review in Authorizations' }))
      .toHaveAttribute('href', `/workspaces/csp/authorizations/offerings/offering-a/packages/${retained.package.packageId}`);
    expect(api.associatePackage).toHaveBeenCalledExactlyOnceWith(receipt.package.packageId, {
      offeringId: offering.offeringId, boundaryRevisionId: boundary.boundaryRevisionId,
      expectedOfferingRevision: offering.revision, expectedPackageRevision: receipt.package.revision,
    }, expect.any(String));
    expect(api.createOffering).not.toHaveBeenCalled();
    expect(api.createDecision).not.toHaveBeenCalled();
    expect(api.recordDecision).not.toHaveBeenCalled();
    expect(screen.getByText(/inventory review and publication are separate/i)).toBeInTheDocument();
  });
  it.each([403, 404])('does not render an existing offering upload form after a %s read failure', async status => {
    // Arrange
    vi.mocked(api.getOffering).mockRejectedValue(new PackageImportError('Offering unavailable', status));
    // Act
    mount('/authorizations/offerings/offering-a/import');
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Offering unavailable');
    expect(screen.queryByRole('region', { name: 'Import authorization package' })).not.toBeInTheDocument();
    expect(api.listBoundaries).not.toHaveBeenCalled();
    expect(api.uploadPackage).not.toHaveBeenCalled();
  });
  it('reloads the current offering revision when navigating to a different mutation surface', async () => {
    // Arrange
    vi.mocked(api.getOffering).mockResolvedValueOnce(offering).mockResolvedValue({ ...offering, revision: 4 });
    mount('/authorizations/offerings/offering-a/boundary');
    fireEvent.click(await screen.findByText('Version history', { selector: 'summary' }));
    await screen.findByText(/Offering revision 3/);
    // Act
    fireEvent.click(screen.getByRole('link', { name: 'Change impact' }));
    // Assert
    expect(await screen.findByRole('heading', { name: 'Change impact' })).toBeInTheDocument();
    expect(api.getOffering).toHaveBeenCalledTimes(2);
  });
  it('mounts hosting management with the selected offering alongside inherited references', async () => {
    // Arrange
    mount('/authorizations/offerings/offering-a/inherited-coverage');
    // Act
    await screen.findByRole('heading', { name: 'Synthetic offering' });
    // Assert
    expect(await screen.findByRole('region', { name: 'Offering hosting' })).toHaveTextContent('offering-a · 3');
  });
  it('opens a functional exact impact workspace instead of a generic section notice', async () => {
    // Arrange
    mount('/authorizations/offerings/offering-a/impact');
    // Act
    await screen.findByRole('heading', { name: 'Synthetic offering' });
    // Assert
    expect(await screen.findByRole('button', { name: 'Review a proposed change' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Boundary revision ID')).not.toBeInTheDocument();
    expect(api.listImpactReviews).toHaveBeenCalledWith('offering-a', 1, expect.any(AbortSignal));
  });
  it('lists distinct offerings and opens their own authorization workspace', async () => {
    // Arrange
    vi.mocked(api.listOfferings).mockResolvedValue(page([offering, { ...offering, offeringId: 'offering-b', name: 'Another offering' }]));
    // Act
    mount('/authorizations');
    // Assert
    expect(await screen.findByRole('link', { name: 'Synthetic offering' })).toHaveAttribute('href', '/workspaces/csp/authorizations/offerings/offering-a');
    expect(screen.getByRole('link', { name: 'Another offering' })).toHaveAttribute('href', '/workspaces/csp/authorizations/offerings/offering-b');
    expect(screen.getByRole('link', { name: 'Create offering' })).toHaveAttribute('href', '/workspaces/csp/authorizations/create');
  });
  it('shows denied offering reads as failures, then permits explicit retry without fake empty results', async () => {
    // Arrange
    vi.mocked(api.listOfferings).mockRejectedValueOnce(new PackageImportError('Provider access denied', 403));
    mount('/authorizations');
    // Act
    expect(await screen.findByRole('alert')).toHaveTextContent('Provider access denied');
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    // Assert
    expect(await screen.findByRole('link', { name: 'Synthetic offering' })).toBeInTheDocument();
  });
  it('starts the header import with files rather than offering metadata', async () => {
    // Arrange
    mount('/authorizations/import');
    // Act
    await screen.findByLabelText('Select source files');
    // Assert
    expect(screen.queryByLabelText('Offering')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Boundary revision')).not.toBeInTheDocument();
    expect(api.uploadPackage).not.toHaveBeenCalled();
  });
  it('records a bounded initial boundary without inferring included resources or authorization', async () => {
    // Arrange
    vi.mocked(api.createBoundary).mockResolvedValue({
      offeringId: offering.offeringId, offeringRevision: 4, boundaryRevisionId: 'boundary-a', version: 1,
      snapshotHash: 'boundary-hash', predecessorRevisionId: null, createdAt: '2026-09-24T12:00:00Z',
      name: 'Proposed scope', scopeStatement: 'Services only; no workload coverage asserted.',
      services: [], componentSnapshotIds: [], includedScopes: [], exclusions: [], providerResponsibilities: [], customerResponsibilities: [], citations: [],
    });
    mount(`/authorizations/offerings/offering-a/import?packageId=${receipt.package.packageId}`);
    fireEvent.click(await screen.findByRole('button', { name: 'Enter offering and boundary manually' }));
    await screen.findByText('No boundary revisions recorded.');
    expect(screen.getByLabelText('Boundary name')).toBeEnabled();
    expect(screen.getByLabelText('Explicit scope statement')).toBeEnabled();
    // Act
    fireEvent.change(screen.getByLabelText('Boundary name'), { target: { value: 'Proposed scope' } });
    fireEvent.change(screen.getByLabelText('Explicit scope statement'), { target: { value: 'Services only; no workload coverage asserted.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save boundary revision' }));
    // Assert
    await waitFor(() => expect(api.createBoundary).toHaveBeenCalledWith('offering-a', expect.objectContaining({
      expectedOfferingRevision: 3, predecessorRevisionId: null, includedScopes: [], citations: [],
      scopeStatement: 'Services only; no workload coverage asserted.',
    }), expect.any(String)));
    expect(api.recordDecision).not.toHaveBeenCalled();
    expect(api.uploadPackage).not.toHaveBeenCalled();
  });
  it('keeps a stale boundary form intact and displays the conflict instead of rebinding its revision', async () => {
    // Arrange
    vi.mocked(api.createBoundary).mockRejectedValue(new PackageImportError('Offering revision changed', 409));
    mount(`/authorizations/offerings/offering-a/import?packageId=${receipt.package.packageId}`);
    fireEvent.click(await screen.findByRole('button', { name: 'Enter offering and boundary manually' }));
    await screen.findByText('No boundary revisions recorded.');
    fireEvent.change(screen.getByLabelText('Boundary name'), { target: { value: 'My proposed boundary' } });
    fireEvent.change(screen.getByLabelText('Explicit scope statement'), { target: { value: 'Do not lose this scope.' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save boundary revision' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Offering revision changed');
    expect(screen.getByLabelText('Explicit scope statement')).toHaveValue('Do not lose this scope.');
    expect(api.uploadPackage).not.toHaveBeenCalled();
  });
});

it.each([true, false])('places creation beside import in the hero without an inline form (populated: %s)', async populated => {
  // Arrange
  vi.mocked(api.listOfferings).mockResolvedValue(page(populated ? [offering] : []));
  const view = mount('/authorizations');
  await screen.findByText(populated ? '1 offering' : '0 offerings');
  // Act
  const heroLinks = view.container.querySelectorAll('header a');
  // Assert
  expect(heroLinks).toHaveLength(2);
  expect(heroLinks[0]).toHaveTextContent('Create offering');
  expect(heroLinks[0]).toHaveAttribute('href', '/workspaces/csp/authorizations/create');
  expect(heroLinks[1]).toHaveTextContent('Import authorization package');
  expect(heroLinks[1]).toHaveAttribute('href', '/workspaces/csp/authorizations/import');
  expect(screen.queryByText('Create an offering', { selector: 'summary' })).not.toBeInTheDocument();
  expect(screen.queryByLabelText('Offering name')).not.toBeInTheDocument();
  expect(api.createOffering).not.toHaveBeenCalled();
});

it('opens creation from the header and navigates to the persisted offering without authorizing it', async () => {
  // Arrange
  const created = { ...offering, offeringId: 'created-offering', name: 'Created offering' };
  vi.mocked(api.createOffering).mockResolvedValue(created);
  vi.mocked(api.getOffering).mockResolvedValue(created);
  mount('/authorizations');
  await screen.findByRole('article', { name: offering.name });
  // Act
  fireEvent.click(screen.getByRole('link', { name: 'Create offering' }));
  expect(screen.getByRole('button', { name: 'Create offering' })).toBeDisabled();
  fireEvent.change(screen.getByLabelText('Offering name', { exact: true }), { target: { value: ' Created offering ' } });
  fireEvent.click(screen.getByLabelText('Azure Government', { exact: true }));
  fireEvent.click(screen.getByRole('button', { name: 'Create offering' }));
  // Assert
  expect(await screen.findByRole('heading', { name: 'Created offering', level: 1 })).toBeInTheDocument();
  expect(api.createOffering).toHaveBeenCalledExactlyOnceWith({
    name: 'Created offering', description: '', environments: ['AzureUSGovernment'],
  }, expect.any(String));
  expect(api.getOffering).toHaveBeenCalledWith('created-offering', expect.any(AbortSignal));
  expect(api.recordDecision).not.toHaveBeenCalled();
  expect(api.uploadPackage).not.toHaveBeenCalled();
});

it('retains creation page inputs when the server rejects the request', async () => {
  // Arrange
  vi.mocked(api.createOffering).mockRejectedValue(new PackageImportError('Offering name is already used', 409));
  mount('/authorizations');
  await screen.findByRole('article', { name: offering.name });
  fireEvent.click(screen.getByRole('link', { name: 'Create offering' }));
  fireEvent.change(screen.getByLabelText('Offering name', { exact: true }), { target: { value: 'My offering' } });
  fireEvent.click(screen.getByLabelText('Azure Government', { exact: true }));
  // Act
  fireEvent.click(screen.getByRole('button', { name: 'Create offering' }));
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('Offering name is already used');
  expect(screen.getByLabelText('Offering name', { exact: true })).toHaveValue('My offering');
  expect(screen.getByLabelText('Azure Government', { exact: true })).toBeChecked();
  expect(api.getOffering).not.toHaveBeenCalled();
});

it('preserves the existing dedicated creation URL', async () => {
  // Arrange
  mount('/authorizations/create');
  // Act
  await screen.findByRole('heading', { name: 'Create offering', level: 1 });
  // Assert
  expect(screen.getByLabelText('Offering name')).toBeInTheDocument();
  expect(screen.getByText('Import authorization package', { selector: 'a' })).toBeInTheDocument();
});
