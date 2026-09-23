import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import '../helpers/dialog';
import { preparedProviderSetup } from '../fixtures/capabilitySetup';
import { MemoryRouter, useLocation, useNavigate } from 'react-router-dom';
import type { ReactNode } from 'react';
import WorkspaceOperationsPage from '../../features/workspace-operations/WorkspaceOperationsPage';
import * as api from '../../features/workspace-operations/api';

vi.mock('../../features/workspace-operations/api', async importOriginal => ({
  WorkspaceOperationError: (await importOriginal<typeof api>()).WorkspaceOperationError,
  listProviderCatalog: vi.fn(),
  getProviderCatalogOverview: vi.fn(),
  getProviderCapability: vi.fn(),
  createProviderCapability: vi.fn(),
  listOrganizations: vi.fn(),
  getOrganization: vi.fn(),
  listOrganizationCapabilities: vi.fn(),
  getOrganizationCapability: vi.fn(),
  listProviderSubscribers: vi.fn(),
  getWorkingRevision: vi.fn(),
  saveWorkingRevision: vi.fn(),
  generatePublicationPreview: vi.fn(),
  approveWorkingRevision: vi.fn(),
  publishWorkingRevision: vi.fn(),
  createOrganization: vi.fn(),
  beginOrganizationProvisioning: vi.fn(),
  getOrganizationProvisioning: vi.fn(),
  getCurrentOrganizationProvisioning: vi.fn(),
  resumeOrganizationProvisioning: vi.fn(),
  completeCapabilitySetup: vi.fn(),
  prepareCapabilitySetup: vi.fn(),
  getCapabilitySetup: vi.fn(),
  reviewNarrativeProposal: vi.fn(),
  listSetupSystems: vi.fn(),
  getSetupSystem: vi.fn(),
  getSetupSystemAccess: vi.fn(),
  getSetupComponents: vi.fn(),
  createSetupComponent: vi.fn(),
  getOrganizationCatalogAccess: vi.fn(),
  addOrganizationCatalogRecord: vi.fn(),
}));

const session: {
  target: { kind: 'csp' } | { kind: 'organization'; tenantId: string };
  workspace: { permissions: { canManageMemberships: boolean; canManageOrganization: boolean; canAccessCsp: boolean } };
  systemAccess: null | {
    systemId: string;
    permissions: { canManageSystem: boolean; canReviewNarratives: boolean };
  };
} = {
  target: { kind: 'csp' },
  workspace: {
    permissions: {
      canManageMemberships: true,
      canManageOrganization: true,
      canAccessCsp: true,
    },
  },
  systemAccess: null,
};

vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => session,
}));
vi.mock('../../components/layout/PageLayout', () => ({
  default: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));
vi.mock('../../components/layout/PageHero', () => ({
  default: ({ title, description, actions }: { title: string; description?: string; actions?: ReactNode }) =>
    <header><h1>{title}</h1>{description && <p>{description}</p>}{actions}</header>,
}));

function HistoryControls() {
  const navigate = useNavigate();
  const location = useLocation();
  return <><button onClick={() => navigate(-1)}>History back</button>
    <button onClick={() => navigate(1)}>History forward</button>
    <button onClick={() => navigate('/security-capabilities/capability-2?tab=review')}>Change capability</button>
    <button onClick={() => {
      session.target = { kind: 'organization', tenantId: 'org-b' };
      navigate('/security-capabilities?workspace=org-b');
    }}>Change organization</button>
    <button onClick={() => navigate('/organizations/org-b/provisioning?key=key-b')}>
      Change provisioning target
    </button>
    <button onClick={() => navigate('/systems/system-1/security-capabilities/setup?step=2&operation=setup-b')}>
      Change setup operation
    </button>
    <button onClick={() => navigate('/systems/system-1/security-capabilities/setup?step=1')}>
      Start fresh setup
    </button>
    <output aria-label="Current route">{location.pathname}{location.search}</output></>;
}

function page(route: string, entries = [route]) {
  return render(
    <MemoryRouter initialEntries={entries} initialIndex={entries.length - 1}>
      <HistoryControls />
      <WorkspaceOperationsPage />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  session.target = { kind: 'csp' };
  session.systemAccess = null;
  vi.mocked(api.getOrganizationCatalogAccess).mockResolvedValue({ canManageCatalog: true });
  vi.mocked(api.getSetupSystem).mockResolvedValue({ systemId: 'system-1', name: 'Mission system' });
  vi.mocked(api.getSetupSystemAccess).mockResolvedValue({
    systemId: 'system-1', roles: ['Reader'], permissions: {
      canRead: true, canManageSystem: false, canEditProfile: false, canAuthorNarratives: false,
      canReviewNarratives: false, canManageEvidence: false, canRunAssessments: false,
      canManageRemediation: false, canDecideAuthorization: false,
    },
  });
  vi.mocked(api.listSetupSystems).mockResolvedValue({
    items: [{ systemId: 'system-1', name: 'Mission system', acronym: 'MS' }], nextCursor: null, totalCount: 1,
  });
  vi.mocked(api.getSetupComponents).mockResolvedValue({
    systemId: 'system-1', items: [{ id: 'component-1', name: 'SOC analysts', componentType: 'Person', description: null }],
    nextCursor: null, totalCount: 1, summary: { personCount: 1, placeCount: 0, thingCount: 0, policyCount: 0, totalCount: 1 },
  });
  vi.mocked(api.listOrganizationCapabilities).mockResolvedValue({
    items: [{ source: 'local', recordId: 'fresh-record', name: 'Existing monitoring', description: 'Local monitoring',
      category: 'AU', availability: 'Planned', isSubscribed: false, systemCount: 1, mutationAuthority: 'organization', recordType: 'capability' }],
    page: 1, pageSize: 25, total: 1, aggregateState: 'Available',
  });
  vi.mocked(api.getOrganizationCapability).mockResolvedValue({
    capability: { source: 'local', recordId: 'fresh-record', name: 'Existing monitoring', description: '',
      category: 'AU', availability: 'Planned', isSubscribed: false, systemCount: 1, mutationAuthority: 'organization', recordType: 'capability' },
    responsibilities: [], narrativeReviews: [],
  });
  vi.mocked(api.listProviderCatalog).mockResolvedValue({
    items: [{
      source: 'provider', componentId: 'component-1', capabilityId: 'capability-1',
      name: 'Threat monitoring', description: 'Detect threats', componentName: 'Sentinel',
      componentType: 'Service', lifecycle: 'Active', reviewState: 'NeedsReview',
      sourceFormat: 'OSCAL', sourceReference: 'provider-ssp', distinctAdoptionCount: 2,
      workingRevision: 4, releasedRevision: 3,
    }],
    page: 1, pageSize: 25, total: 1,
  });
  vi.mocked(api.getProviderCatalogOverview).mockResolvedValue({
    providerName: 'Provider A', sourceArtifacts: {
      items: [{ componentId: 'component-1', componentName: 'Sentinel', sourceFileName: 'Provider SSP', sourceReference: 'provider-ssp', sourceFormat: 'OscalJson' }],
      page: 1, pageSize: 25, total: 1, aggregateState: 'Available',
    },
    authorizationRecord: null,
  });
  vi.mocked(api.getProviderCapability).mockImplementation(async capabilityId => ({
    capability: {
      source: 'provider', componentId: 'component-1', capabilityId, name: 'Threat monitoring',
      description: 'Detect threats', componentName: 'Sentinel', componentType: 'Service',
      lifecycle: 'Active', reviewState: 'NeedsReview', sourceFormat: 'OSCAL', sourceReference: 'provider-ssp',
      distinctAdoptionCount: 2, workingRevision: 4, releasedRevision: 3,
    },
    supportingComponents: [{ id: 'component-1', name: 'Sentinel', componentType: 'Service', source: 'provider' }],
    unresolvedContributorIds: ['person-1'],
    sourceArtifacts: [{ componentId: 'component-1', componentName: 'Sentinel', sourceFileName: 'Provider SSP', sourceReference: 'provider-ssp', sourceFormat: 'OscalJson' }],
    mappedControlIds: ['AU-6'], sourceEvidenceReferences: null, implementationNarrative: null,
  }));
  vi.mocked(api.listOrganizations).mockResolvedValue({
    items: [], page: 1, pageSize: 25, total: 0,
  });
  vi.mocked(api.listProviderSubscribers).mockResolvedValue({
    items: [], page: 1, pageSize: 25, total: 0,
  });
  vi.mocked(api.getWorkingRevision).mockResolvedValue({
    capabilityId: 'capability-1', revision: 4, snapshotHash: 'working-hash',
    approvedRevision: null, updatedAt: '2026-09-20T00:00:00Z',
    contributors: ['person-1'], controlDuties: { 'AU-6': 'Provider' },
    classification: 'Impact Level 5', serviceCategory: 'Monitoring',
    approvalState: 'NotApproved', approvedPreviewId: null, approvedPreviewHash: null,
    approvedAt: null, approvedBy: null,
  });
  vi.mocked(api.getCurrentOrganizationProvisioning).mockResolvedValue(null);
});

describe('workspace operations dashboard T050-T059', () => {
  it('bootstraps the first working revision without inventing defaults or approval', async () => {
    // Arrange
    const persisted = { ...(await api.getWorkingRevision('capability-1')), revision: 1,
      classification: 'CUI', serviceCategory: 'Identity', contributors: [], controlDuties: {} };
    vi.mocked(api.getWorkingRevision).mockRejectedValueOnce(new api.WorkspaceOperationError('Working revision was not found.', 404, 'WORKING_REVISION_NOT_FOUND'));
    vi.mocked(api.saveWorkingRevision).mockResolvedValueOnce(persisted);
    page('/security-capabilities/capability-1');
    // Act
    await screen.findByText('No working revision yet. Enter classification and service category to save the first revision.');
    // Assert
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Classification')).toHaveValue('');
    expect(screen.getByLabelText('Classification').closest('details')).toHaveAttribute('open');
    expect(screen.getByLabelText('Service category')).toHaveValue('');
    expect(screen.getByRole('button', { name: 'Save working revision' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Review publication impact' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Classification'), { target: { value: 'CUI' } });
    fireEvent.change(screen.getByLabelText('Service category'), { target: { value: 'Identity' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save working revision' }));
    await waitFor(() => expect(api.saveWorkingRevision).toHaveBeenCalledWith('capability-1', {
      expectedRevision: 1, classification: 'CUI', serviceCategory: 'Identity', contributors: [], controlDuties: {},
    }, expect.any(AbortSignal)));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review publication impact' })).toBeEnabled());
    expect(api.approveWorkingRevision).not.toHaveBeenCalled();
  });

  it.each([
    [404, 'CAPABILITY_NOT_FOUND'], [403, 'WORKING_REVISION_NOT_FOUND'], [500, 'INTERNAL_ERROR'],
  ])('does not bootstrap a working revision after %s / %s', async (status, code) => {
    // Arrange
    vi.mocked(api.getWorkingRevision).mockRejectedValueOnce(new api.WorkspaceOperationError('Cannot load working revision.', Number(status), String(code)));
    page('/security-capabilities/capability-1');
    // Act
    await screen.findByRole('alert');
    fireEvent.change(screen.getByLabelText('Classification'), { target: { value: 'CUI' } });
    fireEvent.change(screen.getByLabelText('Service category'), { target: { value: 'Identity' } });
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Cannot load working revision.');
    expect(screen.getByRole('button', { name: 'Save working revision' })).toBeDisabled();
    expect(api.saveWorkingRevision).not.toHaveBeenCalled();
  });

  it('requires a valid capability before enabling first-save initialization', async () => {
    // Arrange
    vi.mocked(api.getWorkingRevision).mockRejectedValueOnce(new api.WorkspaceOperationError('Working revision was not found.', 404, 'WORKING_REVISION_NOT_FOUND'));
    vi.mocked(api.getProviderCapability).mockRejectedValueOnce(new api.WorkspaceOperationError('Capability was not found.', 404, 'CAPABILITY_NOT_FOUND'));
    page('/security-capabilities/capability-1');
    // Act
    await screen.findByRole('alert');
    fireEvent.change(screen.getByLabelText('Classification'), { target: { value: 'CUI' } });
    fireEvent.change(screen.getByLabelText('Service category'), { target: { value: 'Identity' } });
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Capability was not found.');
    expect(screen.getByRole('button', { name: 'Save working revision' })).toBeDisabled();
    expect(api.saveWorkingRevision).not.toHaveBeenCalled();
  });

  it('preserves an unsaved first revision across conflict until explicit reload', async () => {
    // Arrange
    const latest = { ...(await api.getWorkingRevision('capability-1')), revision: 2, classification: 'Latest classification' };
    vi.mocked(api.getWorkingRevision)
      .mockRejectedValueOnce(new api.WorkspaceOperationError('Working revision was not found.', 404, 'WORKING_REVISION_NOT_FOUND'))
      .mockResolvedValueOnce(latest);
    vi.mocked(api.saveWorkingRevision).mockRejectedValueOnce(new api.WorkspaceOperationError('Revision conflict.', 409));
    page('/security-capabilities/capability-1');
    await screen.findByText('No working revision yet. Enter classification and service category to save the first revision.');
    // Act
    fireEvent.change(screen.getByLabelText('Classification'), { target: { value: 'My classification' } });
    fireEvent.change(screen.getByLabelText('Service category'), { target: { value: 'Identity' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save working revision' }));
    // Assert
    await waitFor(() => expect(screen.getByRole('button', { name: 'Reload latest revision' })).toBeEnabled());
    expect(screen.getByLabelText('Classification')).toHaveValue('My classification');
    expect(screen.getByRole('button', { name: 'Save working revision' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Reload latest revision' }));
    expect(screen.getByLabelText('Classification')).toHaveValue('Latest classification');
    expect(screen.getByRole('button', { name: 'Save working revision' })).toBeEnabled();
  });

  it('presents the provider capability-first mock with version columns and add action', async () => {
    // Arrange
    page('/security-capabilities');
    // Act
    await screen.findByRole('heading', { name: 'Capabilities you provide' });
    // Assert
    expect(screen.getByRole('button', { name: 'By capability' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'Add capability' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Published version' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Working revision' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Threat monitoring' })).toHaveAttribute('href', '/security-capabilities/capability-1');
    expect(api.listProviderCatalog).toHaveBeenCalledWith(expect.objectContaining({ grouping: 'capability' }), expect.any(AbortSignal));
  });

  it('presents authoring as delivery, readiness and source-evidence cards', async () => {
    // Arrange
    page('/security-capabilities/capability-1');
    // Act
    await screen.findByRole('heading', { name: 'Components that deliver this capability' });
    // Assert
    expect(screen.getByRole('heading', { name: 'Publication readiness' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Source evidence' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Provider implementation narrative' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Link another existing component' })).toBeInTheDocument();
  });

  it('adds a provider capability through the existing reviewed-creation contract', async () => {
    // Arrange
    vi.mocked(api.createProviderCapability).mockResolvedValue({
      id: 'new-provider-cap', componentId: 'component-1', name: 'Incident response',
      description: 'Coordinate provider response', mappedNistControlIds: [], status: 'NeedsReview', mappedBy: 'User',
    });
    page('/security-capabilities');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Add capability' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Add provider capability' }));
    fireEvent.click(await dialog.findByRole('radio', { name: /Threat monitoring/ }));
    fireEvent.change(dialog.getByRole('textbox', { name: 'Name' }), { target: { value: 'Incident response' } });
    fireEvent.change(dialog.getByRole('textbox', { name: 'Description' }), { target: { value: 'Coordinate provider response' } });
    fireEvent.click(dialog.getByRole('button', { name: 'Create capability' }));
    // Assert
    await waitFor(() => expect(api.createProviderCapability).toHaveBeenCalledWith('component-1', {
      name: 'Incident response', description: 'Coordinate provider response', mappedNistControlIds: [], markMappedImmediately: false,
    }));
    await waitFor(() => expect(screen.getByLabelText('Current route')).toHaveTextContent('/security-capabilities/new-provider-cap'));
  });

  it('shows source overview metadata without inferring an authorization record', async () => {
    // Arrange
    page('/security-capabilities');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'View source package' }));
    // Assert
    expect(screen.getByText('Provider SSP')).toBeInTheDocument();
    expect(screen.getByText('Not recorded · separate from publication')).toBeInTheDocument();
  });

  it('labels source package provenance separately from verified source evidence', async () => {
    // Arrange
    page('/security-capabilities/capability-1');
    // Act
    await screen.findByRole('heading', { name: 'Source evidence' });
    // Assert
    expect(screen.getByText('No separately identified source evidence recorded.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Source package provenance' })).toBeInTheDocument();
  });

  it('pages source artifacts without presenting the first page as the full source package', async () => {
    // Arrange
    vi.mocked(api.getProviderCatalogOverview).mockImplementation(async page => ({
      providerName: 'Provider A', authorizationRecord: null, sourceArtifacts: {
        items: [{ componentId: `component-${page}`, componentName: `Source ${page}`, sourceFileName: null, sourceReference: null, sourceFormat: 'Manual' }],
        page: page ?? 1, pageSize: 25, total: 26, aggregateState: 'Available',
      },
    }));
    page('/security-capabilities');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'View source package' }));
    const offering = within(screen.getByRole('region', { name: 'Provider offering' }));
    fireEvent.click(offering.getByRole('button', { name: 'Next' }));
    // Assert
    expect(await offering.findByText('Source 2')).toBeInTheDocument();
    expect(api.getProviderCatalogOverview).toHaveBeenLastCalledWith(2, expect.any(AbortSignal));
    expect(offering.getByText('26 source artifacts')).toBeInTheDocument();
  });

  it('stages provider component selection, saves the original concurrency token and blocks publishing unsaved changes', async () => {
    // Arrange
    vi.mocked(api.saveWorkingRevision).mockResolvedValue({
      ...(await api.getWorkingRevision('capability-1')), revision: 5, contributors: ['person-1', 'component-1'],
    });
    page('/security-capabilities/capability-1');
    await screen.findByLabelText('Classification');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Link another existing component' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: /Threat monitoring/ }));
    // Assert
    expect(screen.getByText(/Unsaved working changes/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Review publication impact' })).toBeDisabled();
    expect(api.saveWorkingRevision).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Save working revision' }));
    await waitFor(() => expect(api.saveWorkingRevision).toHaveBeenCalledWith('capability-1', expect.objectContaining({
      expectedRevision: 4, contributors: ['person-1', 'component-1'],
    }), expect.any(AbortSignal)));
  });

  it('preserves a failed provider creation and prevents duplicate retries after an ambiguous outcome', async () => {
    // Arrange
    vi.mocked(api.createProviderCapability).mockRejectedValue(new Error('Connection interrupted'));
    page('/security-capabilities');
    fireEvent.click(await screen.findByRole('button', { name: 'Add capability' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Add provider capability' }));
    fireEvent.click(await dialog.findByRole('radio', { name: /Threat monitoring/ }));
    fireEvent.change(dialog.getByRole('textbox', { name: 'Name' }), { target: { value: 'Monitoring' } });
    fireEvent.change(dialog.getByRole('textbox', { name: 'Description' }), { target: { value: 'Provider monitoring' } });
    // Act
    fireEvent.click(dialog.getByRole('button', { name: 'Create capability' }));
    // Assert
    expect(await dialog.findByRole('alert')).toHaveTextContent(/uncertain/);
    expect(dialog.getByRole('button', { name: 'Create capability' })).toBeDisabled();
    expect(dialog.getByRole('textbox', { name: 'Name' })).toHaveValue('Monitoring');
    fireEvent.click(dialog.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(api.createProviderCapability).toHaveBeenCalledOnce();
  });

  it.each(['provider', 'organizations', 'library'])('recognizes the Available aggregate contract for %s', async (view) => {
    // Arrange
    const available = { items: [], page: 1, pageSize: 25, total: 0, aggregateState: 'Available' };
    vi.mocked(api.listProviderCatalog).mockResolvedValue(available);
    vi.mocked(api.listOrganizations).mockResolvedValue(available);
    vi.mocked(api.listOrganizationCapabilities).mockResolvedValue(available);
    if (view === 'library') session.target = { kind: 'organization', tenantId: 'org-1' };
    // Act
    page(view === 'organizations' ? '/organizations' : '/security-capabilities');
    await screen.findByText(/No .*available|No organizations/);
    // Assert
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('T050 preserves contextual catalog state in the URL', async () => {
    // Arrange
    page('/security-capabilities?grouping=component&search=threat&page=2');
    // Act
    await screen.findByRole('heading', { name: 'Capabilities you provide' });
    // Assert
    expect(api.listProviderCatalog).toHaveBeenCalledWith(expect.objectContaining({
      grouping: 'component', search: 'threat', page: 2,
    }), expect.any(AbortSignal));
  });

  it('T050 synchronizes provider review and sort controls with browser history', async () => {
    // Arrange
    page('/security-capabilities?review=Approved&sort=status&direction=desc', [
      '/security-capabilities?review=NeedsReview&sort=name&direction=asc',
      '/security-capabilities?review=Approved&sort=status&direction=desc',
    ]);
    await waitFor(() => expect(api.listProviderCatalog).toHaveBeenCalledWith(expect.objectContaining({
      review: 'Approved', sort: 'status', direction: 'desc',
    }), expect.any(AbortSignal)));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'History back' }));
    // Assert
    await waitFor(() => expect(screen.getByLabelText('Review state')).toHaveValue('NeedsReview'));
    expect(screen.getByLabelText('Sort')).toHaveValue('name');
    expect(screen.getByLabelText('Direction')).toHaveValue('asc');
  });

  it('T050 clears prior filter data synchronously while the next request loads', async () => {
    // Arrange
    let finish!: (value: Awaited<ReturnType<typeof api.listProviderCatalog>>) => void;
    vi.mocked(api.listProviderCatalog)
      .mockResolvedValueOnce({
        items: [{
          source: 'provider', componentId: 'component-old', capabilityId: 'capability-old',
          name: 'Old provider record', description: '', componentName: 'Old component',
          componentType: 'Service', lifecycle: 'Active', reviewState: 'NeedsReview',
          sourceFormat: 'OSCAL', sourceReference: null, distinctAdoptionCount: 1,
          workingRevision: 1, releasedRevision: null,
        }], page: 1, pageSize: 25, total: 1,
      })
      .mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    page('/security-capabilities?lifecycle=Active');
    await screen.findByText('Old provider record');
    // Act
    fireEvent.change(screen.getByLabelText('Lifecycle'), { target: { value: 'Draft' } });
    // Assert
    expect(screen.getByText('Loading workspace data…')).toBeInTheDocument();
    expect(screen.queryByText('Old provider record')).not.toBeInTheDocument();
    await act(async () => finish({ items: [], page: 1, pageSize: 25, total: 0 }));
  });

  it('T051 renders provider source metadata and truthful paging', async () => {
    // Arrange
    page('/security-capabilities');
    const sourceButton = await screen.findByRole('button', { name: /Threat monitoring/ });
    // Act
    fireEvent.click(sourceButton);
    // Assert
    expect(screen.getByRole('complementary', { name: 'Source details' })).toHaveTextContent('provider-ssp');
    expect(screen.getByText(/1 total record/)).toBeInTheDocument();
    fireEvent.keyDown(screen.getByRole('complementary', { name: 'Source details' }), { key: 'Escape' });
    expect(screen.queryByRole('complementary', { name: 'Source details' })).not.toBeInTheDocument();
    expect(sourceButton).toHaveFocus();
  });

  it('T052 keeps a conflicted form on its original revision until explicit reload', async () => {
    // Arrange
    vi.mocked(api.getWorkingRevision)
      .mockResolvedValueOnce({
        capabilityId: 'capability-1', revision: 4, snapshotHash: 'working-hash',
        approvedRevision: null, updatedAt: '2026-09-20T00:00:00Z',
        contributors: ['person-1'], controlDuties: { 'AU-6': 'Provider' },
        classification: 'Impact Level 5', serviceCategory: 'Monitoring',
        approvalState: 'NotApproved', approvedPreviewId: null, approvedPreviewHash: null,
        approvedAt: null, approvedBy: null,
      })
      .mockResolvedValueOnce({
        capabilityId: 'capability-1', revision: 5, snapshotHash: 'latest-hash',
        approvedRevision: null, updatedAt: '2026-09-21T00:00:00Z',
        contributors: ['person-2'], controlDuties: { 'AU-6': 'Shared' },
        classification: 'Impact Level 6', serviceCategory: 'Protection',
        approvalState: 'NotApproved', approvedPreviewId: null, approvedPreviewHash: null,
        approvedAt: null, approvedBy: null,
      });
    vi.mocked(api.saveWorkingRevision).mockRejectedValue(Object.assign(new Error('Revision changed'), { status: 409 }));
    page('/security-capabilities/capability-1?tab=responsibilities');
    // Act
    await waitFor(() => expect(screen.getByLabelText('Classification')).toHaveValue('Impact Level 5'));
    expect(screen.getByLabelText('Service category')).toHaveValue('Monitoring');
    expect(screen.getByLabelText(/Contributors/)).toHaveValue('person-1');
    expect(screen.getByLabelText(/Control duties/)).toHaveValue('AU-6: Provider');
    fireEvent.change(screen.getByLabelText('Classification'), { target: { value: 'Edited classification' } });
    fireEvent.click(await screen.findByRole('button', { name: 'Save working revision' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(/preserved/i);
    expect(screen.getByLabelText('Classification')).toHaveValue('Edited classification');
    expect(screen.getByRole('button', { name: 'Save working revision' })).toBeDisabled();
    await waitFor(() => expect(screen.getByRole('region', { name: 'Revision conflict comparison' }))
      .toHaveTextContent(/Editing revision 4.*Latest revision 5/i));
    expect(api.saveWorkingRevision).toHaveBeenCalledWith('capability-1', expect.objectContaining({
      expectedRevision: 4,
      contributors: ['person-1'],
      controlDuties: { 'AU-6': 'Provider' },
    }), expect.any(AbortSignal));
    fireEvent.click(screen.getByRole('button', { name: 'Reload latest revision' }));
    expect(screen.getByLabelText('Classification')).toHaveValue('Impact Level 6');
    expect(screen.getByLabelText(/Contributors/)).toHaveValue('person-2');
    expect(screen.getByRole('button', { name: 'Save working revision' })).toBeEnabled();
  });

  it('T052 supports keyboard navigation across authoring tabs while retaining URL state', async () => {
    // Arrange
    page('/security-capabilities/capability-1?tab=implementation');
    const implementation = await screen.findByRole('tab', { name: 'Implementation' });
    implementation.focus();
    // Act
    fireEvent.keyDown(implementation, { key: 'ArrowRight' });
    // Assert
    expect(screen.getByRole('tab', { name: 'Coverage & duties' })).toHaveFocus();
    expect(screen.getByRole('tab', { name: 'Coverage & duties' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByLabelText('Current route')).toHaveTextContent('tab=responsibilities');
  });

  it('T053 renders canonical publication impact and binds approval and publish to the exact preview', async () => {
    // Arrange
    vi.mocked(api.generatePublicationPreview).mockResolvedValue({
      previewId: 'preview-1', capabilityId: 'capability-1', revision: 4,
      workingSnapshotHash: 'working-hash', previewHash: 'preview-hash',
      generatedAt: '2026-09-22T00:00:00Z', expiresAt: '2099-09-22T01:00:00Z', isStale: false,
      contributorChanges: [{ value: 'person-1', changeKind: 'Added' }],
      dutyChanges: [{ key: 'AU-6', before: 'Customer', after: 'Provider', changeKind: 'Changed' }],
      referenceChanges: [{ value: 'provider-ssp', changeKind: 'Added' }],
      affectedOrganizations: ['org-1'],
      affectedSystems: [{ organizationId: 'org-1', systemId: 'system-1' }],
      delivery: { impactWrites: 1, distinctOrganizations: 1, distinctSystems: 1 },
      notifications: { recipientCount: 2, distinctOrganizations: 1 },
    });
    vi.mocked(api.approveWorkingRevision).mockResolvedValue({
      ...(await api.getWorkingRevision('capability-1')),
      approvedRevision: 4, approvalState: 'Approved', approvedPreviewId: 'preview-1',
      approvedPreviewHash: 'preview-hash',
    });
    vi.mocked(api.publishWorkingRevision).mockResolvedValue({
      releaseId: 'release-1', capabilityId: 'capability-1', revision: 4,
      snapshotHash: 'working-hash', publishedAt: '2026-09-22T00:00:00Z', impactCount: 1, existing: false,
    });
    page('/security-capabilities/capability-1?tab=review');
    // Act
    await waitFor(() => expect(screen.getByRole('button', {
      name: 'Generate publication preview',
    })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Generate publication preview' }));
    // Assert
    expect(await screen.findByText('AU-6: Customer → Provider')).toBeInTheDocument();
    expect(screen.getByText('Added: provider-ssp')).toBeInTheDocument();
    expect(screen.getByText(/1 affected organization.*1 affected system/i)).toBeInTheDocument();
    expect(screen.getByText(/1 impact write.*2 notification recipients/i)).toBeInTheDocument();
    expect(screen.getByText(/Delivery projection.*1 organization.*1 system/i)).toBeInTheDocument();
    expect(screen.getByText(/Notification projection.*1 organization/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Approve exact preview' })).toBeDisabled();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Source evidence and coverage reviewed' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Provider and customer duties reviewed' }));
    fireEvent.click(screen.getByRole('button', { name: 'Approve exact preview' }));
    await waitFor(() => expect(api.approveWorkingRevision).toHaveBeenCalledWith(
      'capability-1', 4, 'preview-1', 'preview-hash', expect.any(AbortSignal),
    ));
    fireEvent.click(screen.getByRole('button', { name: 'Publish release' }));
    await waitFor(() => expect(api.publishWorkingRevision).toHaveBeenCalledWith(
      'capability-1', expect.objectContaining({
        revision: 4, approvedRevision: 4, previewId: 'preview-1', previewHash: 'preview-hash',
      }), expect.any(AbortSignal),
    ));
  });

  it('T053 rejects stale or expired previews and regenerates before approval', async () => {
    // Arrange
    vi.mocked(api.generatePublicationPreview).mockResolvedValue({
      previewId: 'preview-stale', capabilityId: 'capability-1', revision: 4,
      workingSnapshotHash: 'working-hash', previewHash: 'preview-hash',
      generatedAt: '2026-09-20T00:00:00Z', expiresAt: '2026-09-20T01:00:00Z', isStale: true,
      contributorChanges: [], dutyChanges: [], referenceChanges: [], affectedOrganizations: [],
      affectedSystems: [], delivery: { impactWrites: 0, distinctOrganizations: 0, distinctSystems: 0 },
      notifications: { recipientCount: 0, distinctOrganizations: 0 },
    });
    page('/security-capabilities/capability-1?tab=review');
    // Act
    await waitFor(() => expect(screen.getByRole('button', {
      name: 'Generate publication preview',
    })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Generate publication preview' }));
    // Assert
    expect(await screen.findByText(/This preview is stale or expired/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Approve exact preview' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Regenerate publication preview' })).toBeInTheDocument();
  });

  it('T053 discards a preview response after the capability route changes', async () => {
    // Arrange
    vi.mocked(api.getWorkingRevision).mockImplementation(async capabilityId => ({
      capabilityId, revision: 4, snapshotHash: `${capabilityId}-hash`,
      approvedRevision: null, updatedAt: '2026-09-20T00:00:00Z',
      contributors: [], controlDuties: {}, classification: 'IL5', serviceCategory: 'Monitoring',
      approvalState: 'NotApproved', approvedPreviewId: null, approvedPreviewHash: null,
      approvedAt: null, approvedBy: null,
    }));
    let finish!: (value: Awaited<ReturnType<typeof api.generatePublicationPreview>>) => void;
    vi.mocked(api.generatePublicationPreview).mockImplementation(() => new Promise(resolve => {
      finish = resolve;
    }));
    page('/security-capabilities/capability-1?tab=review');
    await waitFor(() => expect(screen.getByRole('button', {
      name: 'Generate publication preview',
    })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Generate publication preview' }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Change capability' }));
    finish({
      previewId: 'stale-route-preview', capabilityId: 'capability-1', revision: 4,
      workingSnapshotHash: 'capability-1-hash', previewHash: 'stale-hash',
      generatedAt: '2026-09-22T00:00:00Z', expiresAt: '2099-09-22T01:00:00Z', isStale: false,
      contributorChanges: [{ value: 'must-not-render', changeKind: 'Added' }],
      dutyChanges: [], referenceChanges: [], affectedOrganizations: [], affectedSystems: [],
      delivery: { impactWrites: 0, distinctOrganizations: 0, distinctSystems: 0 },
      notifications: { recipientCount: 0, distinctOrganizations: 0 },
    });
    // Assert
    await waitFor(() => expect(api.getWorkingRevision).toHaveBeenCalledWith(
      'capability-2', expect.any(AbortSignal),
    ));
    expect(screen.queryByText(/must-not-render/)).not.toBeInTheDocument();
  });

  it('T054 shows the CSP organization empty state without entering customer scope', async () => {
    // Arrange
    page('/organizations?lifecycle=active');
    // Act
    await screen.findByRole('heading', { name: 'Organizations' });
    // Assert
    expect(screen.getByText('No organizations are available.')).toBeInTheDocument();
    expect(screen.queryByText(/support workspace active/i)).not.toBeInTheDocument();
  });

  it('T054 reports partial aggregate availability and synchronizes organization history', async () => {
    // Arrange
    vi.mocked(api.listOrganizations).mockResolvedValue({
      items: [], page: 1, pageSize: 25, total: 42, aggregateState: 'Partial',
    });
    page('/organizations?search=second&lifecycle=Active', [
      '/organizations?search=first&lifecycle=Draft',
      '/organizations?search=second&lifecycle=Active',
    ]);
    expect(await screen.findByRole('alert')).toHaveTextContent(/unavailable or partial.*Partial/i);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'History back' }));
    // Assert
    await waitFor(() => expect(screen.getByLabelText('Search')).toHaveValue('first'));
    expect(screen.getByLabelText('lifecycle')).toHaveValue('Draft');
    expect(api.listOrganizations).toHaveBeenLastCalledWith(expect.objectContaining({
      search: 'first', lifecycle: 'Draft',
    }), expect.any(AbortSignal));
  });

  it('T055 persists one organization creation key before navigating to recoverable provisioning', async () => {
    // Arrange
    const created = {
      tenantId: 'org-new', operationId: 'operation-new', displayName: 'New organization',
      status: 'Active', onboardingState: 'Pending', existing: false,
    };
    vi.mocked(api.createOrganization).mockResolvedValue(created);
    vi.mocked(api.getOrganizationProvisioning).mockResolvedValue({
      operationId: created.operationId, tenantId: created.tenantId, tenantState: 'Completed',
      administratorState: 'Pending', membershipState: 'Pending', lastError: null,
    });
    page('/organizations/new');
    fireEvent.change(await screen.findByLabelText('displayName'), {
      target: { value: 'New organization' },
    });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Create organization and start enrollment' }));
    // Assert
    await waitFor(() => expect(api.createOrganization).toHaveBeenCalledOnce());
    const key = vi.mocked(api.createOrganization).mock.calls[0]![1];
    expect(key).toBeTruthy();
    await waitFor(() => expect(screen.getByLabelText('Current route')).toHaveTextContent(
      `/organizations/org-new/provisioning?key=${key}`,
    ));
    expect(api.getOrganizationProvisioning).toHaveBeenCalledWith('org-new', key, expect.any(AbortSignal));
  });

  it('T055 keeps administrator and membership enrollment outcomes separate', async () => {
    // Arrange
    vi.mocked(api.beginOrganizationProvisioning).mockResolvedValue({
      operationId: 'operation-1', tenantId: 'org-1', tenantState: 'Completed',
      administratorState: 'Pending', membershipState: 'Completed', lastError: 'Administrator failed',
    });
    page('/organizations/org-1/provisioning');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Start enrollment' }));
    // Assert
    expect(await screen.findByText('Administrator: Pending')).toBeInTheDocument();
    expect(screen.getByText('Membership: Completed')).toBeInTheDocument();
  });

  it('T055 restores an interrupted provisioning operation by idempotency key', async () => {
    // Arrange
    vi.mocked(api.getOrganizationProvisioning).mockResolvedValue({
      operationId: 'operation-1', tenantId: 'org-1', tenantState: 'Completed',
      administratorState: 'Failed', membershipState: 'Completed', lastError: 'Administrator failed',
    });
    // Act
    page('/organizations/org-1/provisioning?key=create-key');
    // Assert
    expect(await screen.findByText('Administrator: Failed')).toBeInTheDocument();
    expect(api.getOrganizationProvisioning).toHaveBeenCalledWith('org-1', 'create-key', expect.any(AbortSignal));
  });

  it('T055 persists the provisioning key before the network operation can be interrupted', async () => {
    // Arrange
    let finish!: (value: Awaited<ReturnType<typeof api.beginOrganizationProvisioning>>) => void;
    vi.mocked(api.beginOrganizationProvisioning).mockImplementation(() => new Promise(resolve => {
      finish = resolve;
    }));
    page('/organizations/org-1/provisioning');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Start enrollment' }));
    // Assert
    await waitFor(() => expect(screen.getByLabelText('Current route').textContent).toMatch(/\?key=.+/));
    finish({
      operationId: 'operation-1', tenantId: 'org-1', tenantState: 'Completed',
      administratorState: 'Pending', membershipState: 'Pending', lastError: null,
    });
    await screen.findByText('Administrator: Pending');
  });

  it('T055 resolves a keyless enrollment route through the current operation without creating another', async () => {
    // Arrange
    vi.mocked(api.getCurrentOrganizationProvisioning).mockResolvedValue({
      operationId: 'operation-current', tenantId: 'org-1', tenantState: 'Completed',
      administratorState: 'Pending', membershipState: 'Completed', lastError: null,
      idempotencyKey: 'stable-current-key',
    });
    // Act
    page('/organizations/org-1/provisioning');
    // Assert
    expect(await screen.findByText('Administrator: Pending')).toBeInTheDocument();
    expect(api.getCurrentOrganizationProvisioning).toHaveBeenCalledWith('org-1', expect.any(AbortSignal));
    expect(api.beginOrganizationProvisioning).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.getByLabelText('Current route')).toHaveTextContent(
      '/organizations/org-1/provisioning?key=stable-current-key',
    ));
  });

  it('T055 links Enrollment status to the current operation stable key', async () => {
    // Arrange
    vi.mocked(api.getOrganization).mockResolvedValue({
      id: 'org-1', displayName: 'Organization one', lifecycle: 'Active', onboarding: 'Pending',
      systems: [], subscriptions: [], activity: [],
    });

    vi.mocked(api.getCurrentOrganizationProvisioning).mockResolvedValue({
      operationId: 'operation-current', tenantId: 'org-1', tenantState: 'Completed',
      administratorState: 'Pending', membershipState: 'Pending', lastError: null,
      idempotencyKey: 'stable-current-key',
    });

    // Act
    page('/organizations/org-1');
    // Assert
    expect(await screen.findByRole('link', { name: 'Enrollment status' })).toHaveAttribute(
      'href', '/organizations/org-1/provisioning?key=stable-current-key',
    );
  });

  it('T055 clears provisioning state and ignores an old resume after tenant/key changes', async () => {
    // Arrange
    let finishLoad!: (value: Awaited<ReturnType<typeof api.getOrganizationProvisioning>>) => void;
    let finishResume!: (value: Awaited<ReturnType<typeof api.resumeOrganizationProvisioning>>) => void;
    vi.mocked(api.getOrganizationProvisioning).mockImplementation((tenantId) => {
      if (tenantId === 'org-a') return Promise.resolve({
        operationId: 'operation-a', tenantId: 'org-a', tenantState: 'Completed',
        administratorState: 'Pending', membershipState: 'Pending', lastError: null,
        idempotencyKey: 'key-a',
      });
      return new Promise(resolve => { finishLoad = resolve; });
    });
    vi.mocked(api.resumeOrganizationProvisioning).mockImplementation(() =>
      new Promise(resolve => { finishResume = resolve; }));
    page('/organizations/org-a/provisioning?key=key-a');
    await screen.findByText('Administrator: Pending');
    for (const [name, value] of [
      ['directoryTenantId', 'directory-a'], ['objectId', 'object-a'], ['personId', 'person-a'],
    ] as const) fireEvent.change(screen.getByLabelText(name), { target: { value } });
    fireEvent.click(screen.getByRole('button', { name: 'Resume incomplete enrollment' }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Change provisioning target' }));
    // Assert
    expect(screen.getByText('Loading workspace data…')).toBeInTheDocument();
    expect(screen.queryByText('Administrator: Pending')).not.toBeInTheDocument();
    expect(screen.queryByDisplayValue('directory-a')).not.toBeInTheDocument();
    finishResume({
      operationId: 'operation-a', tenantId: 'org-a', tenantState: 'Completed',
      administratorState: 'Completed', membershipState: 'Completed', lastError: null,
      idempotencyKey: 'key-a',
    });
    expect(screen.queryByText('Administrator: Completed')).not.toBeInTheDocument();
    await waitFor(() => expect(api.getOrganizationProvisioning)
      .toHaveBeenCalledWith('org-b', 'key-b', expect.any(AbortSignal)));
    finishLoad({
      operationId: 'operation-b', tenantId: 'org-b', tenantState: 'Completed',
      administratorState: 'Failed', membershipState: 'Pending', lastError: 'Needs attention',
      idempotencyKey: 'key-b',
    });
    expect(await screen.findByText('Administrator: Failed')).toBeInTheDocument();
    expect(screen.getByLabelText('directoryTenantId')).toHaveValue('');
  });

  it('T056 requires a support reason, reference choice and acknowledgement', async () => {
    // Arrange
    page('/organizations');
    // Act
    await screen.findByRole('heading', { name: 'Organizations' });
    // Assert
    expect(screen.queryByRole('button', { name: 'Start audited support' })).not.toBeInTheDocument();
  });

  it('T057 renders an organization library with local-only mutation labels', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    vi.mocked(api.listOrganizationCapabilities).mockResolvedValue({
      items: [{
        source: 'local', recordId: 'local-1', name: 'Local monitoring', description: 'Local',
        category: 'Detection', availability: 'Available', isSubscribed: false, systemCount: 1,
        mutationAuthority: 'organization', recordType: 'capability',
      }],
      page: 1, pageSize: 25, total: 1, aggregateState: 'Available',
    });

    page('/security-capabilities?source=local');
    // Act
    await screen.findByText('Local monitoring');
    // Assert
    expect(screen.getByText(/Organization managed/)).toBeInTheDocument();
  });

  it('matches the capability library hierarchy with grouping buttons and structured columns', async () => {
      // Arrange
      session.target = { kind: 'organization', tenantId: 'org-1' };
      vi.mocked(api.listOrganizationCapabilities).mockResolvedValue({
        items: [], page: 1, pageSize: 25, total: 0, aggregateState: 'Available',
      });
      // Act
      page('/security-capabilities');
      // Assert
      expect(await screen.findByRole('button', { name: 'By capability' })).toHaveAttribute('aria-pressed', 'true');
      expect(screen.getByRole('button', { name: 'By component' })).toHaveAttribute('aria-pressed', 'false');
      expect(screen.getByRole('columnheader', { name: 'Capability / supporting components' })).toBeInTheDocument();
      expect(screen.getByRole('columnheader', { name: 'Source & responsibility' })).toBeInTheDocument();
      expect(screen.getByRole('columnheader', { name: 'Readiness' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /Add capability/ })).toBeEnabled();
    });

    it('matches capability detail cards without inventing responsibility', async () => {
      // Arrange
      session.target = { kind: 'organization', tenantId: 'org-1' };
      vi.mocked(api.getOrganizationCapability).mockResolvedValue({
        capability: {
          source: 'provider', recordId: 'provider-1', name: 'Monitoring', description: 'Detect events',
          category: 'Detection', availability: 'Available', isSubscribed: false, systemCount: 0,
          mutationAuthority: 'provider', recordType: 'capability',
        },
        responsibilities: [], narrativeReviews: [],
      });
      // Act
      page('/security-capabilities/provider/provider-1');
      // Assert
      expect(await screen.findByRole('heading', { name: 'What delivers this capability' })).toBeInTheDocument();
      expect(screen.getByRole('heading', { name: 'Mapped control coverage' })).toBeInTheDocument();
      expect(screen.getByRole('heading', { name: 'Responsibility' })).toBeInTheDocument();
      expect(screen.getByRole('heading', { name: 'Evidence & narratives' })).toBeInTheDocument();
      expect(screen.getByText('No organization contribution recorded.')).toBeInTheDocument();
      expect(screen.getByText('CSP offering available to adopt')).toBeInTheDocument();
      expect(screen.queryByText('Shared')).not.toBeInTheDocument();
    });

    it('presents capability setup as a guided flow instead of raw request fields', async () => {
      // Arrange
      session.target = { kind: 'organization', tenantId: 'org-1' };
      session.systemAccess = { systemId: 'system-1', permissions: { canManageSystem: true, canReviewNarratives: false } };
      // Act
      page('/systems/system-1/security-capabilities/setup');
      // Assert
      expect(await screen.findByRole('heading', { name: 'What does your system need to do?' })).toBeInTheDocument();
      expect(within(screen.getByRole('dialog')).getByLabelText('Source')).toBeInTheDocument();
      expect(screen.queryByLabelText('recordId')).not.toBeInTheDocument();
      expect(screen.queryByLabelText('componentIds')).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: /Continue/ })).toBeInTheDocument();
    });
  it('T057 synchronizes library search state after browser history navigation', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    vi.mocked(api.listOrganizationCapabilities).mockResolvedValue({
      items: [], page: 1, pageSize: 25, total: 0, aggregateState: 'Available',
    });

    page('/security-capabilities?search=second', [
      '/security-capabilities?search=first',
      '/security-capabilities?search=second',
    ]);
    expect(await screen.findByPlaceholderText('Search capability library')).toHaveValue('second');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'History back' }));
    // Assert
    await waitFor(() => expect(screen.getByPlaceholderText('Search capability library')).toHaveValue('first'));
  });

  it('T057 never exposes organization A records while organization B loads', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-a' };
    let finish!: (value: Awaited<ReturnType<typeof api.listOrganizationCapabilities>>) => void;
    vi.mocked(api.listOrganizationCapabilities).mockImplementation((tenantId) => {
      if (tenantId === 'org-a') return Promise.resolve({
        items: [{
          source: 'local', recordId: 'org-a-record', name: 'Organization A only', description: '',
          category: 'Detection', availability: 'Available', isSubscribed: false, systemCount: 0,
          mutationAuthority: 'organization', recordType: 'capability',
        }],
        page: 1, pageSize: 25, total: 1, aggregateState: 'Available',
      });
      return new Promise(resolve => { finish = resolve; });
    });
    page('/security-capabilities');
    await screen.findByText('Organization A only');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Change organization' }));
    // Assert
    expect(screen.getByText('Loading workspace data…')).toBeInTheDocument();
    expect(screen.queryByText('Organization A only')).not.toBeInTheDocument();
    await waitFor(() => expect(api.listOrganizationCapabilities)
      .toHaveBeenCalledWith('org-b', expect.any(Object), expect.any(AbortSignal)));
    await act(async () => finish({
      items: [], page: 1, pageSize: 25, total: 0, aggregateState: 'Available',
    }));
  });

  it('keeps organization library requests independent of stale system query parameters', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    page('/security-capabilities?system=system-a');
    // Act
    await screen.findByText('Existing monitoring');
    // Assert
    expect(screen.queryByLabelText('System')).not.toBeInTheDocument();
    expect(api.listOrganizationCapabilities).toHaveBeenCalledWith('org-1',
      expect.objectContaining({ systemId: undefined }), expect.any(AbortSignal));
  });

  it.each(['component', 'capability'] as const)('preserves %s record type and system scope in library links', async recordType => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    vi.mocked(api.listOrganizationCapabilities).mockResolvedValue({
      items: [{
        source: 'provider', recordId: 'record-1', name: 'Backup service', description: '',
        category: 'Service', availability: 'Published', isSubscribed: false, systemCount: 0,
        mutationAuthority: 'provider', recordType,
      }],
      page: 1, pageSize: 25, total: 1,
    });
    // Act
    page('/systems/system-1/security-capabilities');
    // Assert
    expect(await screen.findByRole('link', { name: 'Backup service' })).toHaveAttribute(
      'href', `/systems/system-1/security-capabilities/provider/record-1?recordType=${recordType}`,
    );
  });

  it('opens legacy component detail and pages child capabilities with typed scoped links', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    vi.mocked(api.getOrganizationCapability).mockResolvedValue({
      capability: {
        source: 'provider', recordId: 'component-1', name: 'Backup service', description: 'Recovery platform',
        category: 'Service', availability: 'Published', isSubscribed: false, systemCount: 0,
        mutationAuthority: 'provider', recordType: 'component',
      },
      responsibilities: [], narrativeReviews: [],
    });
    vi.mocked(api.listOrganizationCapabilities).mockResolvedValue({
      items: [{
        source: 'provider', recordId: 'capability-1', name: 'Restore capability', description: '',
        category: 'Service', availability: 'Available', isSubscribed: false, systemCount: 0,
        mutationAuthority: 'provider', recordType: 'capability',
      }],
      page: 1, pageSize: 25, total: 26,
    });
    // Act
    page('/systems/system-1/security-capabilities/provider/component-1');
    // Assert
    expect(await screen.findByText('Recovery platform')).toBeInTheDocument();
    const child = await screen.findByRole('link', { name: 'Restore capability' });
    expect(child).toHaveAttribute('href',
      '/systems/system-1/security-capabilities/provider/capability-1?recordType=capability');
    expect(screen.queryByRole('heading', { name: 'Responsibilities' })).not.toBeInTheDocument();
    await waitFor(() => expect(api.listOrganizationCapabilities).toHaveBeenCalledWith(
      'org-1', expect.objectContaining({
        grouping: 'capability', componentId: 'component-1', source: 'provider', systemId: 'system-1', page: 1,
      }), expect.any(AbortSignal),
    ));
    fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    await waitFor(() => expect(api.listOrganizationCapabilities).toHaveBeenLastCalledWith(
      'org-1', expect.objectContaining({ page: 2, componentId: 'component-1' }), expect.any(AbortSignal),
    ));
  });

  it('T058 presents persisted responsibility and narrative provenance', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    vi.mocked(api.getOrganizationCapability).mockResolvedValue({
      capability: {
        source: 'provider', recordId: 'provider-1', name: 'Provider monitoring', description: '',
        category: 'Detection', availability: 'Available', isSubscribed: true, systemCount: 1,
        mutationAuthority: 'provider', recordType: 'capability',
      },
      responsibilities: [{
        systemId: 'system-1', controlId: 'AU-6', designation: 'Shared',
        confirmedBy: 'reviewer', confirmedAt: '2026-01-01T00:00:00Z', sourceRevision: '4',
      }],
      narrativeReviews: [{
        id: 'proposal-1', systemId: 'system-1', controlId: 'AU-6', narrativeType: 'Implementation',
        status: 'Pending', revision: 2, provenance: { source: 'provider' },
        createdAt: '2026-01-01T00:00:00Z', createdBy: 'author',
        reviewedAt: null, reviewedBy: null, reviewNote: null,
      }],
    });
    page('/systems/system-1/security-capabilities/provider/provider-1');
    // Act
    await screen.findByText(/AU-6 · Shared/);
    // Assert
    expect(screen.getByText(/"source":"provider"/)).toBeInTheDocument();
  });

  it('T058 uses the system route scope instead of a conflicting query parameter', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    session.systemAccess = {
      systemId: 'route-system',
      permissions: { canManageSystem: true, canReviewNarratives: true },
    };
    vi.mocked(api.getOrganizationCapability).mockResolvedValue({
      capability: {
        source: 'provider', recordId: 'provider-1', name: 'Provider monitoring', description: '',
        category: 'Detection', availability: 'Available', isSubscribed: true, systemCount: 1,
        mutationAuthority: 'provider', recordType: 'capability',
      },
      responsibilities: [], narrativeReviews: [],
    });
    // Act
    page('/systems/route-system/security-capabilities/provider/provider-1?system=query-system');
    // Assert
    await waitFor(() => expect(api.getOrganizationCapability).toHaveBeenCalledWith(
      'org-1', 'provider', 'provider-1', 'route-system', expect.any(AbortSignal), undefined,
    ));
  });

  it('T059 reports each setup write and resumes the durable operation', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    session.systemAccess = {
      systemId: 'system-1',
      permissions: { canManageSystem: true, canReviewNarratives: false },
    };
    vi.mocked(api.getCapabilitySetup).mockResolvedValue({
      operationId: 'operation-1', idempotencyKey: 'setup-key', tenantId: 'org-1',
      systemId: 'system-1', source: 'local', recordId: '',
      componentIds: ['component-1'], inlineLocalCapability: {
        name: 'Local monitoring', provider: 'Organization', category: 'Detection',
        description: 'Local implementation', implementationStatus: 'Planned', owner: 'Security team',
      }, recordState: 'Completed', componentLinksState: 'Failed',
      subscriptionState: 'Pending', subscribeRequested: true,
      outcomes: [{ writeKind: 'component', writeId: 'component-1', state: 'Failed', error: 'Conflict',
        updatedAt: '2026-01-01T00:00:00Z' }],
      lastError: 'One write failed', createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z',
    });
    vi.mocked(api.completeCapabilitySetup).mockResolvedValue({
      operationId: 'operation-1', recordState: 'Completed', componentLinksState: 'Completed',
      subscriptionState: 'Completed', outcomes: [], lastError: null,
    });
    page('/systems/system-1/security-capabilities/setup?operation=operation-1&step=3');
    // Act
    await screen.findByText(/component-1.*Failed/);
    fireEvent.click(screen.getByRole('button', { name: 'Retry incomplete writes' }));
    // Assert
    await waitFor(() => expect(api.completeCapabilitySetup).toHaveBeenCalledWith(
      'org-1',
      {
        idempotencyKey: 'setup-key', source: 'local', recordId: '', systemId: 'system-1',
        componentIds: ['component-1'], subscribe: true,
        preparedOperationId: 'operation-1',
        inlineLocalCapability: {
          name: 'Local monitoring', provider: 'Organization', category: 'Detection',
          description: 'Local implementation', implementationStatus: 'Planned', owner: 'Security team',
        },
      },
      expect.any(AbortSignal),
    ));
  });

  it('T059 hydrates the review step from persisted setup intent after refresh', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    session.systemAccess = {
      systemId: 'system-1',
      permissions: { canManageSystem: true, canReviewNarratives: false },
    };
    vi.mocked(api.getCapabilitySetup).mockResolvedValue({
      operationId: 'operation-1', idempotencyKey: 'setup-key', tenantId: 'org-1',
      systemId: 'system-1', source: 'provider', recordId: 'provider-1',
      componentIds: ['component-1', 'component-2'], inlineLocalCapability: null,
      subscribeRequested: true, recordState: 'Completed', componentLinksState: 'Pending',
      subscriptionState: 'Pending', outcomes: [], lastError: null,
      createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z',
    });
    // Act
    page('/systems/system-1/security-capabilities/setup?operation=operation-1&step=2');
    // Assert
    expect(await screen.findByText('provider-1')).toBeInTheDocument();
    expect(screen.getByText('component-1, component-2')).toBeInTheDocument();
    expect(screen.getByText('Requested')).toBeInTheDocument();
    expect(screen.getByText('setup-key')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Apply setup' })).toBeDisabled();
  });

  it('T066 applies dark-theme variants to controls, alerts and data surfaces', async () => {
    // Arrange
    vi.mocked(api.listProviderCatalog).mockResolvedValue({
      items: [], page: 1, pageSize: 25, total: 0, aggregateState: 'Partial',
    });
    // Act
    page('/security-capabilities');
    // Assert
    expect(await screen.findByRole('alert')).toHaveClass('dark:bg-amber-950');
    expect(screen.getByPlaceholderText('Search provider catalog').className).toContain('dark:bg-gray-800');
    expect(screen.getByRole('button', { name: 'By capability' }).className).toContain('dark:bg-indigo-950');
    expect(screen.getByText('No provider catalog records are available.').className).toContain('dark:border-gray-700');
  });

  it('T059 clears operation A intent synchronously and disables apply while operation B loads', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    session.systemAccess = {
      systemId: 'system-1',
      permissions: { canManageSystem: true, canReviewNarratives: false },
    };
    let finishB!: (value: Awaited<ReturnType<typeof api.getCapabilitySetup>>) => void;
    const operationA = {
      operationId: 'setup-a', idempotencyKey: 'setup-key-a', tenantId: 'org-1',
      systemId: 'system-1', source: 'local', recordId: 'record-a',
      componentIds: ['component-a'], inlineLocalCapability: {
        name: 'Operation A capability', provider: 'Organization', category: 'Detection',
        description: 'Operation A intent', implementationStatus: 'Planned', owner: 'Team A',
      }, subscribeRequested: false, recordState: 'Prepared', componentLinksState: 'Prepared',
      subscriptionState: 'NotRequested', outcomes: [], lastError: null,
      createdAt: '2026-09-22T00:00:00Z', updatedAt: '2026-09-22T00:00:00Z',
    };
    vi.mocked(api.getCapabilitySetup).mockImplementation((_tenantId, operationId) => {
      if (operationId === 'setup-a') return Promise.resolve(operationA);
      return new Promise(resolve => { finishB = resolve; });
    });
    page('/systems/system-1/security-capabilities/setup?step=2&operation=setup-a');
    await screen.findByText('Operation A capability');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Change setup operation' }));
    // Assert
    expect(screen.queryByText(/Operation A capability/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Apply setup' })).toBeDisabled();
    expect(api.completeCapabilitySetup).not.toHaveBeenCalled();
    await act(async () => finishB({
      ...operationA,
      operationId: 'setup-b',
      idempotencyKey: 'setup-key-b',
      recordId: 'record-b',
      inlineLocalCapability: { ...operationA.inlineLocalCapability, name: 'Operation B capability' },
    }));
    expect(await screen.findByText('Operation B capability')).toBeInTheDocument();
  });

  it('T059 resets operation intent and idempotency when navigating to a fresh setup', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    session.systemAccess = {
      systemId: 'system-1',
      permissions: { canManageSystem: true, canReviewNarratives: false },
    };
    vi.mocked(api.getCapabilitySetup).mockResolvedValue({
      operationId: 'setup-a', idempotencyKey: 'setup-key-a', tenantId: 'org-1',
      systemId: 'system-1', source: 'local', recordId: 'record-a',
      componentIds: ['component-a'], inlineLocalCapability: {
        name: 'Operation A capability', provider: 'Organization', category: 'Detection',
        description: 'Operation A intent', implementationStatus: 'Planned', owner: 'Team A',
      }, subscribeRequested: false, recordState: 'Prepared', componentLinksState: 'Prepared',
      subscriptionState: 'NotRequested', outcomes: [], lastError: null,
      createdAt: '2026-09-22T00:00:00Z', updatedAt: '2026-09-22T00:00:00Z',
    });
    vi.mocked(api.prepareCapabilitySetup).mockImplementation((_tenantId, request) => Promise.resolve({
      operationId: 'setup-fresh', idempotencyKey: request.idempotencyKey, tenantId: 'org-1',
      systemId: 'system-1', source: 'local', recordId: 'fresh-record',
      componentIds: [], inlineLocalCapability: null, subscribeRequested: false,
      recordState: 'Prepared', componentLinksState: 'Prepared', subscriptionState: 'NotRequested',
      outcomes: [], lastError: null, createdAt: '2026-09-23T00:00:00Z', updatedAt: '2026-09-23T00:00:00Z',
    }));
    page('/systems/system-1/security-capabilities/setup?step=2&operation=setup-a');
    await screen.findByText('Operation A capability');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Start fresh setup' }));
    // Assert
    expect(screen.queryByText(/Operation A capability/)).not.toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Create a new local capability' })).not.toBeChecked();
    const choice = await screen.findByRole('radio', { name: /Existing monitoring/ });
    expect(choice).not.toBeChecked();
    fireEvent.click(choice);
    fireEvent.click(screen.getByRole('button', { name: 'Continue →' }));
    await screen.findByRole('heading', { name: 'Connect the components that deliver it' });
    fireEvent.click(screen.getByRole('button', { name: 'Continue →' }));
    await waitFor(() => expect(api.prepareCapabilitySetup).toHaveBeenCalledOnce());
    expect(vi.mocked(api.prepareCapabilitySetup).mock.calls[0]![1]).toEqual(expect.objectContaining({
      recordId: 'fresh-record',
      componentIds: [],
      inlineLocalCapability: undefined,
      idempotencyKey: expect.not.stringMatching(/^setup-key-a$/),
    }));
  });

  it('T059 submits inline local capability creation and renders each write outcome', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    session.systemAccess = {
      systemId: 'system-1',
      permissions: { canManageSystem: true, canReviewNarratives: false },
    };
    const prepared = {
      operationId: 'setup-1', idempotencyKey: 'setup-key', tenantId: 'org-1',
      systemId: 'system-1', source: 'local', recordId: 'new-local-id',
      componentIds: ['component-1'], inlineLocalCapability: {
        name: 'Local monitoring', provider: 'Organization', category: 'Detection',
        description: 'Local implementation', implementationStatus: 'Planned', owner: 'Security team',
      }, subscribeRequested: false, recordState: 'Prepared', componentLinksState: 'Prepared',
      subscriptionState: 'NotRequested', outcomes: [], lastError: null,
      createdAt: '2026-09-22T00:00:00Z', updatedAt: '2026-09-22T00:00:00Z',
    };
    vi.mocked(api.prepareCapabilitySetup).mockResolvedValue(prepared);
    vi.mocked(api.getCapabilitySetup).mockResolvedValue(prepared);
    vi.mocked(api.completeCapabilitySetup).mockResolvedValue({
      operationId: 'setup-1', recordState: 'Completed', componentLinksState: 'Completed',
      subscriptionState: 'NotRequested', outcomes: [
        { writeKind: 'record', writeId: 'new-local-id', state: 'Completed', error: null, updatedAt: '2026-09-22T00:00:00Z' },
        { writeKind: 'component-link', writeId: 'component-1', state: 'Completed', error: null, updatedAt: '2026-09-22T00:00:00Z' },
      ], lastError: null,
    });
    page('/systems/system-1/security-capabilities/setup?step=1');
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: 'Create a new local capability' }));
    for (const [name, value] of [
      ['Capability name', 'Local monitoring'],
      ['Provider', 'Organization'],
      ['Category', 'Detection'],
      ['Description', 'Local implementation'],
      ['Implementation status', 'Planned'],
      ['Owner', 'Security team'],
    ] as const) fireEvent.change(screen.getByLabelText(name), { target: { value } });
    fireEvent.click(screen.getByRole('button', { name: 'Continue →' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: /SOC analysts/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Continue →' }));
    await screen.findByRole('heading', { name: 'Review responsibilities before applying' });
    fireEvent.click(screen.getByRole('checkbox', { name: /I reviewed the source/ }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply setup' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Apply setup' }));
    // Assert
    expect(api.prepareCapabilitySetup).toHaveBeenCalledWith(
      'org-1', expect.objectContaining({
        idempotencyKey: expect.any(String), source: 'local', recordId: '', systemId: 'system-1',
        componentIds: ['component-1'], subscribe: false,
        inlineLocalCapability: expect.objectContaining({ name: 'Local monitoring' }),
      }), expect.any(AbortSignal),
    );
    expect(screen.getByLabelText('Current route')).toHaveTextContent(
      '/systems/system-1/security-capabilities/setup?step=2&operation=setup-1',
    );
    expect(screen.getByLabelText('Current route')).not.toHaveTextContent('Local monitoring');
    await waitFor(() => expect(api.completeCapabilitySetup).toHaveBeenCalledWith(
      'org-1', expect.objectContaining({
        source: 'local', recordId: 'new-local-id', systemId: 'system-1',
        preparedOperationId: 'setup-1',
        inlineLocalCapability: expect.objectContaining({ name: 'Local monitoring', owner: 'Security team' }),
      }), expect.any(AbortSignal),
    ));
    expect(await screen.findByText(/new-local-id.*Completed/)).toBeInTheDocument();
    expect(screen.getByText(/component-1.*Completed/)).toBeInTheDocument();
  });

  it('opens legacy organization setup without system selection', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    page('/security-capabilities/setup?source=provider&record=provider-1');
    // Act
    await screen.findByRole('radio', { name: 'Inherit from CSP' });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Continue' })).toBeEnabled());
    await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Continue' })));
    // Assert
    expect(screen.getByLabelText('Current route')).toHaveTextContent('/security-capabilities/setup?source=provider&record=provider-1');
    expect(screen.getByRole('heading', { name: 'Define the organization contribution' })).toBeInTheDocument();
    expect(api.getSetupSystemAccess).not.toHaveBeenCalled();
    expect(api.listSetupSystems).not.toHaveBeenCalled();
    expect(api.prepareCapabilitySetup).not.toHaveBeenCalled();
  });

  it('selects named local components and keeps inline creation separate from applying setup', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    session.systemAccess = { systemId: 'system-1', permissions: { canManageSystem: true, canReviewNarratives: false } };
    vi.mocked(api.createSetupComponent).mockResolvedValue({ id: 'component-2', name: 'Incident policy' });
    page('/systems/system-1/security-capabilities/setup?stage=components&record=fresh-record');
    // Act
    fireEvent.click(await screen.findByRole('checkbox', { name: /SOC analysts/ }));
    fireEvent.click(screen.getByText('+ Add a component inline'));
    fireEvent.change(screen.getByLabelText('Component name'), { target: { value: 'Incident policy' } });
    fireEvent.change(screen.getByLabelText('Component type'), { target: { value: 'Policy' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create and select component' }));
    // Assert
    await screen.findByText('2 local components selected across pages.');
    expect(api.createSetupComponent).toHaveBeenCalledWith('system-1', {
      name: 'Incident policy', componentType: 'Policy', status: 'Planned', description: '',
    }, expect.any(AbortSignal));
    expect(api.prepareCapabilitySetup).not.toHaveBeenCalled();
    expect(api.completeCapabilitySetup).not.toHaveBeenCalled();
  });

  it('surfaces inline component write failures without selecting a nonexistent record', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    session.systemAccess = { systemId: 'system-1', permissions: { canManageSystem: true, canReviewNarratives: false } };
    vi.mocked(api.createSetupComponent).mockRejectedValue(new Error('Component write conflict'));
    page('/systems/system-1/security-capabilities/setup?stage=components&record=fresh-record');
    // Act
    fireEvent.click(await screen.findByText('+ Add a component inline'));
    fireEvent.change(screen.getByLabelText('Component name'), { target: { value: 'Incident policy' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create and select component' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Component write conflict');
    expect(screen.getByText('0 local components selected across pages.')).toBeInTheDocument();
    expect(api.completeCapabilitySetup).not.toHaveBeenCalled();
  });

  it('renders persisted supporting components and control duties in the detail cards', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    vi.mocked(api.getOrganizationCapability).mockResolvedValue({
      capability: { source: 'provider', recordId: 'monitoring', recordType: 'capability', name: 'Monitoring',
        description: 'Detect events', category: 'Detection', availability: 'Available', isSubscribed: true,
        systemCount: 1, mutationAuthority: 'provider', responsibility: 'Shared', reviewState: 'ReviewRequired' },
      supportingComponents: [
        { id: 'platform', name: 'SIEM platform', componentType: 'Thing', source: 'provider', description: 'Analyze telemetry' },
        { id: 'team', name: 'SOC analysts', componentType: 'Person', source: 'local' },
        { id: 'policy', name: 'Monitoring procedure', componentType: 'Policy', source: 'local' },
        { id: 'site', name: 'Operations center', componentType: 'Place', source: 'local' },
      ],
      controlCoverage: [{ controlId: 'AU-6', designation: 'Shared', remainingDuty: 'Review system alerts', systemId: 'system-1' }],
      responsibilities: [], narrativeReviews: [], sourceReference: 'Provider SSP section 4', providerName: 'Provider A',
    });
    // Act
    page('/security-capabilities/provider/monitoring');
    // Assert
    expect(await screen.findByText('SIEM platform')).toBeInTheDocument();
    expect(screen.getByText('SOC analysts')).toBeInTheDocument();
    expect(screen.getByText('Monitoring procedure')).toBeInTheDocument();
    expect(screen.getByText('Operations center')).toBeInTheDocument();
    expect(screen.getByText('Provider-managed · Analyze telemetry')).toBeInTheDocument();
    expect(screen.getByText('Review system alerts')).toBeInTheDocument();
    expect(screen.getByText('Provider SSP section 4')).toBeInTheDocument();
    expect(screen.getByText('Needs review')).toBeInTheDocument();
  });

  it('pages named setup capability choices without discarding the selection', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    session.systemAccess = { systemId: 'system-1', permissions: { canManageSystem: true, canReviewNarratives: false } };
    vi.mocked(api.listOrganizationCapabilities).mockImplementation(async (_tenant, query) => ({
      items: query.page === 2 ? [] : [{ source: 'local', recordId: 'monitoring', recordType: 'capability', name: 'Monitoring',
        description: '', category: 'Detection', availability: 'Planned', isSubscribed: false, systemCount: 1, mutationAuthority: 'organization' }],
      page: query.page ?? 1, pageSize: 25, total: 26, aggregateState: 'Available',
    }));
    page('/systems/system-1/security-capabilities/setup');
    // Act
    fireEvent.click(await screen.findByRole('radio', { name: /Monitoring/ }));
    fireEvent.click(screen.getByRole('button', { name: 'More capabilities' }));
    // Assert
    await screen.findByText(/previously selected capability is retained/);
    expect(api.listOrganizationCapabilities).toHaveBeenLastCalledWith('org-1', expect.objectContaining({ page: 2 }), expect.any(AbortSignal));
    fireEvent.click(screen.getByRole('button', { name: 'Previous capabilities' }));
    expect(await screen.findByRole('radio', { name: /Monitoring/ })).toBeChecked();
  });

  it('opens and cancels Add capability as a dialog over the originating detail without writes', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    page('/security-capabilities/provider/provider-1?recordType=capability');
    await screen.findByRole('heading', { name: 'What delivers this capability' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Add capability' }));
    const dialog = await screen.findByRole('dialog', { name: 'Add a security capability' });
    // Assert
    expect(await within(dialog).findByRole('radio', { name: 'Inherit from CSP' })).toBeChecked();
    expect(api.listSetupSystems).not.toHaveBeenCalled();
    expect(screen.getByLabelText('Current route')).toHaveTextContent('/security-capabilities/provider/provider-1?');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.getByLabelText('Current route')).toHaveTextContent('/security-capabilities/provider/provider-1?recordType=capability');
    expect(api.prepareCapabilitySetup).not.toHaveBeenCalled();
    expect(api.completeCapabilitySetup).not.toHaveBeenCalled();
  });

  it('adds provider capabilities by subscription without offering API-rejected local component links', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    session.systemAccess = { systemId: 'system-1', permissions: { canManageSystem: true, canReviewNarratives: false } };
    page('/systems/system-1/security-capabilities/setup?source=provider&record=provider-1');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Continue →' }));
    await screen.findByRole('heading', { name: 'Connect the components that deliver it' });
    // Assert
    expect(screen.getByRole('checkbox', { name: /Subscribe the selected system/ })).toBeChecked();
    expect(screen.queryByRole('checkbox', { name: /SOC analysts/ })).not.toBeInTheDocument();
    expect(screen.queryByText('+ Add a component inline')).not.toBeInTheDocument();
    expect(api.getSetupComponents).not.toHaveBeenCalled();
  });

  it('locks apply and dismissal during writes, then offers Done and scoped capability navigation', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    session.systemAccess = { systemId: 'system-1', permissions: { canManageSystem: true, canReviewNarratives: false } };
    vi.mocked(api.getCapabilitySetup).mockResolvedValue({
      operationId: 'setup-lock', idempotencyKey: 'lock-key', tenantId: 'org-1', systemId: 'system-1',
      source: 'provider', recordId: 'provider-1', componentIds: [], inlineLocalCapability: null, subscribeRequested: true,
      recordState: 'Pending', componentLinksState: 'Pending', subscriptionState: 'Pending', outcomes: [],
      lastError: null, createdAt: '2026-09-23T00:00:00Z', updatedAt: '2026-09-23T00:00:00Z',
    });
    let finish!: (result: Awaited<ReturnType<typeof api.completeCapabilitySetup>>) => void;
    vi.mocked(api.completeCapabilitySetup).mockReturnValue(new Promise(resolve => { finish = resolve; }));
    page('/systems/system-1/security-capabilities/setup?operation=setup-lock&step=2');
    await screen.findByText('lock-key');
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: /I reviewed the source/ }));
    const apply = screen.getByRole('button', { name: 'Apply setup' });
    fireEvent.click(apply);
    fireEvent.click(apply);
    // Assert
    expect(api.completeCapabilitySetup).toHaveBeenCalledOnce();
    expect(screen.getByRole('button', { name: 'Close dialog' })).toBeDisabled();
    await act(async () => finish({
      operationId: 'setup-lock', recordState: 'Completed', componentLinksState: 'Completed',
      subscriptionState: 'Completed', outcomes: [], lastError: null,
    }));
    expect(await screen.findByRole('heading', { name: 'Capability added' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'View capability' })).toHaveAttribute(
      'href', '/systems/system-1/security-capabilities/provider/provider-1?recordType=capability');
    fireEvent.click(screen.getByRole('button', { name: 'Done' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.getByLabelText('Current route')).toHaveTextContent('/systems/system-1/security-capabilities');
  });

  it('resolves organization catalog authority without navigating away or requesting system authority', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    page('/security-capabilities/provider/provider-1?dialog=capability&setupSource=provider&setupRecord=provider-1');
    // Act
    await screen.findByRole('radio', { name: 'Inherit from CSP' });
    // Assert
    await waitFor(() => expect(screen.getByRole('button', { name: 'Continue' })).toBeEnabled());
    expect(screen.getByLabelText('Current route')).toHaveTextContent('/security-capabilities/provider/provider-1?');
    expect(api.prepareCapabilitySetup).not.toHaveBeenCalled();
    expect(api.getOrganizationCatalogAccess).toHaveBeenCalledWith('org-1', expect.any(AbortSignal));
    expect(api.getSetupSystemAccess).not.toHaveBeenCalled();
    expect(api.listSetupSystems).not.toHaveBeenCalled();
  });

  it('reloads durable failed outcomes and retries the same operation without claiming success', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    session.systemAccess = { systemId: 'system-1', permissions: { canManageSystem: true, canReviewNarratives: false } };
    const prepared = preparedProviderSetup();
    vi.mocked(api.getCapabilitySetup).mockResolvedValueOnce(prepared).mockResolvedValue(preparedProviderSetup({
      recordState: 'Completed', componentLinksState: 'Completed', subscriptionState: 'Failed',
      lastError: 'Subscription interrupted', outcomes: [
        { writeKind: 'subscription', writeId: 'system-1', state: 'Failed', error: 'Subscription interrupted', updatedAt: prepared.updatedAt },
      ],
    }));
    vi.mocked(api.completeCapabilitySetup).mockRejectedValueOnce(new Error('Write interrupted'))
      .mockResolvedValue({ ...prepared, recordState: 'Completed', componentLinksState: 'Completed', subscriptionState: 'Completed' });
    page('/systems/system-1/security-capabilities/setup?operation=setup-provider&step=2');
    await screen.findByText('provider-key');
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: /I reviewed the source/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Apply setup' }));
    await screen.findByRole('button', { name: 'Retry incomplete writes' });
    // Assert
    expect(screen.queryByRole('heading', { name: 'Capability added' })).not.toBeInTheDocument();
    expect(screen.getByText('Subscription interrupted', { exact: true })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry incomplete writes' }));
    await screen.findByRole('heading', { name: 'Capability added' });
    const requests = vi.mocked(api.completeCapabilitySetup).mock.calls;
    expect(requests).toHaveLength(2);
    expect(requests[1]![1]).toEqual(requests[0]![1]);
  });

  it('prepares a new immutable intent when changing a reviewed capability', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    session.systemAccess = { systemId: 'system-1', permissions: { canManageSystem: true, canReviewNarratives: false } };
    const prepared = preparedProviderSetup();
    let next = prepared;
    vi.mocked(api.getCapabilitySetup).mockImplementation(async (_tenant, id) => id === prepared.operationId ? prepared : next);
    vi.mocked(api.prepareCapabilitySetup).mockImplementation(async (_tenant, request) => {
      next = preparedProviderSetup({ ...request, operationId: 'edited-setup', subscribeRequested: request.subscribe,
        inlineLocalCapability: request.inlineLocalCapability ?? null });
      return next;
    });
    page('/systems/system-1/security-capabilities/setup?operation=setup-provider&step=2');
    await screen.findByText('provider-key');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    fireEvent.change(within(screen.getByRole('dialog')).getByLabelText('Source'), { target: { value: 'local' } });
    fireEvent.click(await screen.findByRole('radio', { name: /Existing monitoring/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Continue →' }));
    fireEvent.click(screen.getByRole('button', { name: 'Continue →' }));
    // Assert
    await waitFor(() => expect(api.prepareCapabilitySetup).toHaveBeenCalledOnce());
    expect(vi.mocked(api.prepareCapabilitySetup).mock.calls[0]![1]).toMatchObject({
      source: 'local', recordId: 'fresh-record', systemId: 'system-1', subscribe: false,
      idempotencyKey: expect.not.stringMatching(/^provider-key$/),
    });
    await screen.findByRole('heading', { name: 'Review responsibilities before applying' });
    expect(screen.getByRole('checkbox', { name: /I reviewed the source/ })).not.toBeChecked();
    expect(api.completeCapabilitySetup).not.toHaveBeenCalled();
  });

  it('keeps apply blocked after an intent load failure and supports explicit retry', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    session.systemAccess = { systemId: 'system-1', permissions: { canManageSystem: true, canReviewNarratives: false } };
    vi.mocked(api.getCapabilitySetup).mockRejectedValueOnce(new Error('Intent unavailable')).mockResolvedValue(preparedProviderSetup());
    page('/systems/system-1/security-capabilities/setup?operation=setup-provider&step=2');
    // Act
    await screen.findByRole('button', { name: 'Reload setup' });
    fireEvent.click(screen.getByRole('checkbox', { name: /I reviewed the source/ }));
    // Assert
    expect(screen.getByRole('button', { name: 'Apply setup' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Reload setup' }));
    await screen.findByText('provider-key');
    expect(api.getCapabilitySetup).toHaveBeenCalledTimes(2);
    expect(screen.getByRole('checkbox', { name: /I reviewed the source/ })).not.toBeChecked();
    expect(api.completeCapabilitySetup).not.toHaveBeenCalled();
  });

  it('offers a new subscription intent instead of replaying a legacy provider no-op', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    session.systemAccess = { systemId: 'system-1', permissions: { canManageSystem: true, canReviewNarratives: false } };
    vi.mocked(api.getCapabilitySetup).mockResolvedValue(preparedProviderSetup({
      subscribeRequested: false, recordState: 'Completed', componentLinksState: 'Completed', subscriptionState: 'NotRequested',
    }));
    page('/systems/system-1/security-capabilities/setup?operation=setup-provider&step=3');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Prepare provider subscription' }));
    // Assert
    expect(screen.queryByRole('heading', { name: 'Capability added' })).not.toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: /Subscribe the selected system/ })).toBeChecked();
    expect(api.completeCapabilitySetup).not.toHaveBeenCalled();
  });

  it('does not call a legacy local setup complete when its required system link is missing', async () => {
    // Arrange
    session.target = { kind: 'organization', tenantId: 'org-1' };
    session.systemAccess = { systemId: 'system-1', permissions: { canManageSystem: true, canReviewNarratives: false } };
    vi.mocked(api.getCapabilitySetup).mockResolvedValue(preparedProviderSetup({
      source: 'local', subscribeRequested: false, recordState: 'Completed', componentLinksState: 'Completed',
      subscriptionState: 'NotRequested', outcomes: [],
    }));
    page('/systems/system-1/security-capabilities/setup?operation=setup-provider&step=3');
    // Act
    await screen.findByText(/Record: Completed/);
    // Assert
    expect(screen.queryByRole('heading', { name: 'Capability added' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry incomplete writes' })).toBeEnabled();
  });
});
