import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import type { ReactNode } from 'react';
import WorkspaceOperationsPage from '../../features/workspace-operations/WorkspaceOperationsPage';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import * as catalogApi from '../../features/workspace-operations/api';
import * as packageApi from '../../features/package-imports/api';
import { page, packageStatus, reviewState } from '../package-imports/fixtures';
import '../package-imports/crypto';

vi.mock('../../components/layout/PageLayout', () => ({ default: ({ children }: { children: ReactNode }) => <main>{children}</main> }));
vi.mock('../../components/layout/PageHero', () => ({
  default: ({ title, actions }: { title: string; actions?: ReactNode }) => <header><h1>{title}</h1>{actions}</header>,
}));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => ({
    target: { kind: 'csp' },
    workspace: { permissions: { canAccessCsp: true, canManageOrganization: false, canManageMemberships: false } },
  }),
}));
vi.mock('../../features/workspace-operations/api', async importOriginal => ({
  ...await importOriginal<typeof catalogApi>(),
  listProviderCatalog: vi.fn(), getProviderCatalogOverview: vi.fn(),
}));
vi.mock('../../features/package-imports/api', async importOriginal => ({
  ...await importOriginal<typeof packageApi>(),
  listPackages: vi.fn(), getPackageStatus: vi.fn(), getPackageReviewState: vi.fn(),
  getPackageEntries: vi.fn(), getPackageCandidates: vi.fn(),
  receivePackage: vi.fn(), previewPackage: vi.fn(), approvePackage: vi.fn(), publishPackage: vi.fn(),
}));

function RouteProbe() {
  const location = useLocation();
  return <output aria-label="Current route">{location.pathname}{location.search}{location.hash}</output>;
}

function renderRoute(route: string) {
  return render(<MemoryRouter initialEntries={[route]}>
    <WorkspaceNavigationProvider workspace={{ kind: 'csp' }}>
      <RouteProbe />
      <Routes>
        <Route path="/workspaces/csp/security-capabilities/*" element={<WorkspaceOperationsPage />} />
        <Route path="/workspaces/csp/authorizations/*" element={<h1>Authorizations destination</h1>} />
      </Routes>
    </WorkspaceNavigationProvider>
  </MemoryRouter>);
}

beforeEach(() => {
  vi.clearAllMocks();
  sessionStorage.clear();
  vi.mocked(catalogApi.listProviderCatalog).mockResolvedValue(page([]));
  vi.mocked(catalogApi.getProviderCatalogOverview).mockResolvedValue({
    providerName: 'Synthetic provider', sourceArtifacts: page([]), authorizationRecord: null,
  });
  vi.mocked(packageApi.listPackages).mockResolvedValue(page([]));
  vi.mocked(packageApi.getPackageStatus).mockResolvedValue(packageStatus());
  vi.mocked(packageApi.getPackageReviewState).mockResolvedValue(reviewState());
  vi.mocked(packageApi.getPackageEntries).mockResolvedValue(page([]));
  vi.mocked(packageApi.getPackageCandidates).mockResolvedValue(page([]));
});

describe('Authorizations owns package import entry', () => {
  it('hands catalog import to Authorizations without uploading or publishing', async () => {
    // Arrange
    renderRoute('/workspaces/csp/security-capabilities');
    await screen.findByRole('heading', { name: 'Synthetic provider offering' });
    // Act
    fireEvent.click(screen.getByRole('link', { name: 'Import authorization package' }));
    // Assert
    expect(screen.getByLabelText('Current route')).toHaveTextContent('/workspaces/csp/authorizations/import');
    expect(packageApi.receivePackage).not.toHaveBeenCalled();
    expect(packageApi.approvePackage).not.toHaveBeenCalled();
    expect(packageApi.publishPackage).not.toHaveBeenCalled();
  });

  it('offers an Authorizations handoff from the source summary instead of a second import editor', async () => {
    // Arrange
    renderRoute('/workspaces/csp/security-capabilities');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'View source package' }));
    // Assert
    expect(await screen.findByRole('link', { name: /open authorizations/i })).toHaveAttribute('href', '/workspaces/csp/authorizations');
    expect(screen.queryByLabelText('Select source files')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /save.*authorization|record.*decision/i })).not.toBeInTheDocument();
  });

  it('hands the old import index to Authorizations instead of retaining a catalog-owned upload page', async () => {
    // Arrange
    renderRoute('/workspaces/csp/security-capabilities/imports');
    // Act
    await waitFor(() => expect(screen.getByLabelText('Current route').textContent).toMatch(/^\/workspaces\/csp\/authorizations(?:[/?#]|$)/));
    // Assert
    expect(screen.queryByLabelText('Select source files')).not.toBeInTheDocument();
    expect(packageApi.receivePackage).not.toHaveBeenCalled();
  });

  it.each([
    '?type=AuthorizationReference&reviewState=Reviewed&page=2&candidate=source%20reference#citation',
    '?type=Capability&reviewState=NeedsReview&page=3&candidate=candidate-7#sources',
  ])('retains package identity and the old bookmark filters during the ownership handoff: %s', async suffix => {
    // Arrange
    const packageId = 'package-1';
    const oldUrl = new URL(`/workspaces/csp/security-capabilities/imports/${packageId}${suffix}`, 'http://localhost');
    renderRoute(`${oldUrl.pathname}${oldUrl.search}${oldUrl.hash}`);
    // Act
    await waitFor(() => expect(screen.getByLabelText('Current route').textContent).toMatch(/^\/workspaces\/csp\/authorizations(?:[/?#]|$)/));
    const destination = new URL(screen.getByLabelText('Current route').textContent!, 'http://localhost');
    // Assert
    expect(destination.pathname.split('/').includes(packageId) || [...destination.searchParams.values()].includes(packageId)).toBe(true);
    for (const [key, value] of oldUrl.searchParams) expect(destination.searchParams.get(key)).toBe(value);
    expect(destination.hash).toBe(oldUrl.hash);
    expect(packageApi.approvePackage).not.toHaveBeenCalled();
    expect(packageApi.publishPackage).not.toHaveBeenCalled();
  });
});
