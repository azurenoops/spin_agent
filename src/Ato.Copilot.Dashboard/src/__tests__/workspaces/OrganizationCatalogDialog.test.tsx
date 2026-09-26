import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import '../helpers/dialog';
import { organizationCatalogRecords } from '../fixtures/organizationCatalog';
import WorkspaceOperationsPage from '../../features/workspace-operations/WorkspaceOperationsPage';
import * as api from '../../features/workspace-operations/api';

vi.mock('../../features/workspace-operations/api', () => ({
  listOrganizationCapabilities: vi.fn(), getOrganizationCapability: vi.fn(),
  getOrganizationCatalogAccess: vi.fn(), addOrganizationCatalogRecord: vi.fn(),
  listSetupSystems: vi.fn(), getSetupSystem: vi.fn(), getSetupSystemAccess: vi.fn(),
  getSetupComponents: vi.fn(), createSetupComponent: vi.fn(),
  prepareCapabilitySetup: vi.fn(), completeCapabilitySetup: vi.fn(), getCapabilitySetup: vi.fn(),
}));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => ({
    target: { kind: 'organization', tenantId: 'org-1' },
    workspace: { displayName: 'Organization A', permissions: { canManageOrganization: true } }, systemAccess: null,
  }),
}));
vi.mock('../../components/layout/PageLayout', () => ({
  default: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));
vi.mock('../../components/layout/PageHero', () => ({
  default: ({ title, actions }: { title: string; actions?: ReactNode }) => <header><h1>{title}</h1>{actions}</header>,
}));

function page(route = '/security-capabilities?dialog=capability') {
  return render(<MemoryRouter initialEntries={[route]}><WorkspaceOperationsPage /></MemoryRouter>);
}
async function createCapability() {
  await screen.findByRole('textbox', { name: 'Name' });
  fireEvent.change(screen.getByRole('textbox', { name: 'Name' }), { target: { value: 'Enterprise monitoring' } });
  fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'Reusable organization monitoring service' } });
  fireEvent.change(screen.getByLabelText('Control family'), { target: { value: 'AU' } });
  await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Continue' })));
}
async function contribution() {
  fireEvent.change(await screen.findByRole('textbox', { name: 'Organization contribution' }),
    { target: { value: 'We operate the central service and maintain enterprise procedures.' } });
  fireEvent.change(screen.getByRole('textbox', { name: 'Organization owner' }),
    { target: { value: 'Enterprise security team' } });
}
async function openComponents() {
  await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Add or link component' })));
}
async function review() {
  await contribution();
  fireEvent.click(screen.getByRole('button', { name: 'Review changes' }));
  await screen.findByRole('heading', { name: 'Review organization changes' });
}
function assertNoSystemWork() {
  for (const fn of [api.listSetupSystems, api.getSetupSystem, api.getSetupSystemAccess,
    api.getSetupComponents, api.createSetupComponent, api.prepareCapabilitySetup,
    api.completeCapabilitySetup, api.getCapabilitySetup]) expect(fn).not.toHaveBeenCalled();
}

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.listSetupSystems).mockResolvedValue({ items: [], totalCount: 0, nextCursor: null });
  vi.mocked(api.getCapabilitySetup).mockRejectedValue(new Error('System setup is not an organization catalog operation'));
  vi.mocked(api.getOrganizationCatalogAccess).mockResolvedValue({ canManageCatalog: true });
  vi.mocked(api.listOrganizationCapabilities).mockImplementation(async (_tenant, query) => {
    const items = organizationCatalogRecords.filter(item => (!query.source || item.source === query.source)
      && item.recordType === (query.grouping === 'component' ? 'component' : 'capability'));
    return { items, total: items.length, page: query.page ?? 1, pageSize: 25, aggregateState: 'Available' };
  });
  vi.mocked(api.getOrganizationCapability).mockImplementation(async (_tenant, source, id) => {
    const capability = organizationCatalogRecords.find(item => item.source === source && item.recordId === id);
    if (!capability) throw new Error('Record not found');
    return { capability, responsibilities: [], narrativeReviews: [], supportingComponents: [],
      controlCoverage: [], providerName: source === 'provider' ? 'Provider A' : null };
  });
  vi.mocked(api.addOrganizationCatalogRecord).mockResolvedValue({
    source: 'local', recordType: 'capability', recordId: 'created-cap', name: 'Enterprise monitoring', existing: false,
  });
});

describe('organization-only catalog dialog', () => {
  it('matches the source-card mock with organization context and no system-use step', async () => {
    // Arrange
    page();
    // Act
    const create = await screen.findByRole('radio', { name: 'Create in organization' });
    // Assert
    expect(create).toBeChecked();
    expect(screen.getByRole('radio', { name: 'Inherit from CSP' })).not.toBeChecked();
    expect(screen.getByText('Organization: Organization A')).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Capability' })).toBeChecked();
    expect(screen.getByRole('radio', { name: 'Component' })).not.toBeChecked();
    expect(screen.getByLabelText('Organization addition progress')).toHaveTextContent('Source & details');
    expect(screen.getByLabelText('Organization addition progress')).not.toHaveTextContent('System use');
  });

  it('shows component chips on source details and removes staged links without writes', async () => {
    // Arrange
    page();
    await screen.findByRole('textbox', { name: 'Name' });
    // Act
    await openComponents();
    fireEvent.click(await screen.findByRole('checkbox', { name: /Enterprise operations team/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Done selecting components' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Remove Enterprise operations team' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Search organization-wide components')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Remove Enterprise operations team' }));
    expect(screen.queryByRole('button', { name: 'Remove Enterprise operations team' })).not.toBeInTheDocument();
    expect(api.addOrganizationCatalogRecord).not.toHaveBeenCalled();
  });

  it('presents CSP offerings with provider-managed read-only treatment from the mock', async () => {
    // Arrange
    page();
    // Act
    fireEvent.click(await screen.findByRole('radio', { name: 'Inherit from CSP' }));
    // Assert
    expect(await screen.findByRole('radio', { name: /Backup and recovery/ })).toBeInTheDocument();
    expect(screen.getByText('Provider-managed · Read-only')).toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: 'Name' })).not.toBeInTheDocument();
    assertNoSystemWork();
  });

  it('requires staging or discarding an inline component before leaving its editor', async () => {
    // Arrange
    page();
    await screen.findByRole('textbox', { name: 'Name' });
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Organization service' } });
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'Organization service description' } });
    await openComponents();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Add new component' }));
    fireEvent.change(screen.getByLabelText('Component name'), { target: { value: 'Unstaged policy' } });
    // Assert
    expect(screen.getByRole('button', { name: 'Continue' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Done selecting components' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Discard component' }));
    expect(screen.getByRole('button', { name: 'Continue' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Done selecting components' })).toBeEnabled();
    expect(api.addOrganizationCatalogRecord).not.toHaveBeenCalled();
  });

  it('requires a nonblank source description before continuing or staging a component', async () => {
    // Arrange
    page();
    await screen.findByRole('textbox', { name: 'Name' });
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Enterprise service' } });
    // Act
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: '   ' } });
    // Assert
    expect(screen.getByRole('button', { name: 'Continue' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'Organization service description' } });
    await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Continue' })));
    await openComponents();
    fireEvent.click(screen.getByRole('button', { name: 'Add new component' }));
    fireEvent.change(screen.getByLabelText('Component name'), { target: { value: 'Enterprise policy' } });
    expect(screen.getByRole('button', { name: 'Stage component' })).toBeDisabled();
  });
  it.each(['capability', 'component'] as const)('shows authored organization contribution on %s details instead of system designations', async recordType => {
    // Arrange
    vi.mocked(api.getOrganizationCapability).mockResolvedValue({
      capability: { ...organizationCatalogRecords[0]!, recordType, responsibility: 'Mixed',
        organizationContribution: 'Central operating procedures and organization service delivery',
        organizationOwner: 'Enterprise operations' },
      responsibilities: [], narrativeReviews: [],
    });
    // Act
    page(`/security-capabilities/local/local-cap?recordType=${recordType}`);
    // Assert
    expect(await screen.findByText('Central operating procedures and organization service delivery')).toBeInTheDocument();
    expect(screen.getByText('Enterprise operations')).toBeInTheDocument();
    expect(screen.queryByText('Mixed')).not.toBeInTheDocument();
  });

  it('opens without discovering or selecting a system, even from a legacy setup URL', async () => {
    // Arrange
    page('/security-capabilities/setup?system=old-system&operation=old-system-operation');
    // Act
    const dialog = await screen.findByRole('dialog', { name: 'Add a security capability' });
    await within(dialog).findByRole('textbox', { name: 'Name' });
    // Assert
    expect(within(dialog).queryByLabelText('Apply to')).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('heading', { name: 'Choose a system' })).not.toBeInTheDocument();
    expect(api.listOrganizationCapabilities).toHaveBeenCalledWith('org-1',
      expect.objectContaining({ systemId: undefined }), expect.any(AbortSignal));
    assertNoSystemWork();
  });

  it('creates an organization capability with no system or components and links to its detail', async () => {
    // Arrange
    page();
    await createCapability();
    await review();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save to organization' }));
    await screen.findByRole('heading', { name: 'Added to organization' });
    // Assert
    const [, body] = vi.mocked(api.addOrganizationCatalogRecord).mock.calls[0]!;
    expect(body).toMatchObject({ source: 'local', recordType: 'capability', components: [], newComponents: [],
      capability: { name: 'Enterprise monitoring', category: 'AU' },
      organizationContribution: 'We operate the central service and maintain enterprise procedures.',
      owner: 'Enterprise security team' });
    expect(body).not.toHaveProperty('systemId');
    expect(body).not.toHaveProperty('subscribe');
    expect(screen.getByRole('link', { name: 'View capability' })).toHaveAttribute('href',
      '/security-capabilities/local/created-cap?recordType=capability');
    assertNoSystemWork();
    fireEvent.click(screen.getByRole('button', { name: 'Done' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('stages existing and new organization components until the final save', async () => {
    // Arrange
    page();
    await createCapability();
    await openComponents();
    fireEvent.click(await screen.findByRole('checkbox', { name: /Enterprise operations team/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Add new component' }));
    fireEvent.change(screen.getByLabelText('Component name'), { target: { value: 'Enterprise backup policy' } });
    fireEvent.change(screen.getByLabelText('Component description'), { target: { value: 'Enterprise backup standards' } });
    fireEvent.change(screen.getByLabelText('Component type'), { target: { value: 'Policy' } });
    fireEvent.click(screen.getByRole('button', { name: 'Stage component' }));
    await review();
    expect(api.addOrganizationCatalogRecord).not.toHaveBeenCalled();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save to organization' }));
    await screen.findByRole('heading', { name: 'Added to organization' });
    // Assert
    expect(api.addOrganizationCatalogRecord).toHaveBeenCalledWith('org-1', expect.objectContaining({
      components: [{ source: 'local', recordId: 'local-component' }],
      newComponents: [expect.objectContaining({ name: 'Enterprise backup policy', componentType: 'Policy' })],
    }));
    assertNoSystemWork();
  });

  it.each(['capability', 'component'] as const)('adopts a CSP %s for the organization, without subscribing a system', async recordType => {
    // Arrange
    vi.mocked(api.addOrganizationCatalogRecord).mockResolvedValue({
      source: 'provider', recordType, recordId: `provider-${recordType}`, name: 'Provider offering', existing: false,
    });
    page(`/security-capabilities/provider/provider-${recordType === 'capability' ? 'cap' : 'component'}?recordType=${recordType}&dialog=capability&setupSource=provider&setupRecord=provider-${recordType === 'capability' ? 'cap' : 'component'}`);
    // Act
    await screen.findByRole('button', { name: 'Continue' });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Continue' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
    await review();
    fireEvent.click(screen.getByRole('button', { name: 'Save to organization' }));
    await screen.findByRole('heading', { name: 'Added to organization' });
    // Assert
    expect(api.addOrganizationCatalogRecord).toHaveBeenCalledWith('org-1', expect.objectContaining({
      source: 'provider', recordType, recordId: `provider-${recordType === 'capability' ? 'cap' : 'component'}`,
    }));
    assertNoSystemWork();
  });

  it('creates a standalone organization component without inventing a capability', async () => {
    // Arrange
    page();
    fireEvent.click(await screen.findByRole('radio', { name: 'Component' }));
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Enterprise operations team' } });
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'Organization-wide operations team' } });
    fireEvent.change(screen.getByLabelText('Component type'), { target: { value: 'Person' } });
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
    await review();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save to organization' }));
    await screen.findByRole('heading', { name: 'Added to organization' });
    // Assert
    const [, body] = vi.mocked(api.addOrganizationCatalogRecord).mock.calls[0]!;
    expect(body).toMatchObject({ recordType: 'component', component: { name: 'Enterprise operations team', componentType: 'Person' } });
    expect(body.capability).toBeUndefined();
    assertNoSystemWork();
  });

  it('reuses an existing organization capability', async () => {
    // Arrange
    page();
    fireEvent.click(await screen.findByRole('radio', { name: 'Use existing organization record' }));
    fireEvent.click(await screen.findByRole('radio', { name: /Enterprise monitoring/ }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Continue' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
    await review();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save to organization' }));
    await screen.findByRole('heading', { name: 'Added to organization' });
    // Assert
    expect(api.addOrganizationCatalogRecord).toHaveBeenCalledWith('org-1', expect.objectContaining({
      source: 'local', recordId: 'local-cap', recordType: 'capability',
    }));
    expect(vi.mocked(api.addOrganizationCatalogRecord).mock.calls[0]![1].capability).toBeUndefined();
  });

  it('cancels staged inline components without creating anything', async () => {
    // Arrange
    page();
    await createCapability();
    await openComponents();
    fireEvent.click(screen.getByRole('button', { name: 'Add new component' }));
    fireEvent.change(screen.getByLabelText('Component name'), { target: { value: 'Unsaved policy' } });
    fireEvent.change(screen.getByLabelText('Component description'), { target: { value: 'Unsaved policy description' } });
    fireEvent.click(screen.getByRole('button', { name: 'Stage component' }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    // Assert
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(api.addOrganizationCatalogRecord).not.toHaveBeenCalled();
    assertNoSystemWork();
  });

  it('does not treat organization membership or system permission as catalog write authority', async () => {
    // Arrange
    vi.mocked(api.getOrganizationCatalogAccess).mockResolvedValue({ canManageCatalog: false });
    page();
    // Act
    await screen.findByText(/Organization catalog management permission is required/);
    // Assert
    expect(screen.queryByRole('button', { name: 'Continue' })).not.toBeInTheDocument();
    expect(api.addOrganizationCatalogRecord).not.toHaveBeenCalled();
    assertNoSystemWork();
  });

  it('surfaces authorization lookup errors and permits retry', async () => {
    // Arrange
    vi.mocked(api.getOrganizationCatalogAccess).mockRejectedValueOnce(new Error('Access lookup unavailable'));
    page();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Retry' }));
    // Assert
    expect(await screen.findByRole('textbox', { name: 'Name' })).toBeInTheDocument();
    expect(api.getOrganizationCatalogAccess).toHaveBeenCalledTimes(2);
  });

  it('prevents double submission and dismissal while saving', async () => {
    // Arrange
    let finish!: (value: Awaited<ReturnType<typeof api.addOrganizationCatalogRecord>>) => void;
    vi.mocked(api.addOrganizationCatalogRecord).mockReturnValue(new Promise(resolve => { finish = resolve; }));
    page();
    await createCapability();
    await review();
    // Act
    const save = screen.getByRole('button', { name: 'Save to organization' });
    fireEvent.click(save);
    fireEvent.click(save);
    // Assert
    expect(api.addOrganizationCatalogRecord).toHaveBeenCalledOnce();
    expect(screen.getByRole('button', { name: 'Close dialog' })).toBeDisabled();
    await act(async () => finish({ source: 'local', recordId: 'created-cap', recordType: 'capability', name: 'Enterprise monitoring', existing: false }));
    expect(await screen.findByRole('heading', { name: 'Added to organization' })).toBeInTheDocument();
  });

  it('shows a failed save and retries the identical idempotent request', async () => {
    // Arrange
    vi.mocked(api.addOrganizationCatalogRecord).mockRejectedValueOnce(new Error('Connection interrupted'));
    page();
    await createCapability();
    await review();
    fireEvent.click(screen.getByRole('button', { name: 'Save to organization' }));
    await screen.findByText('Connection interrupted');
    const first = vi.mocked(api.addOrganizationCatalogRecord).mock.calls[0]![1];
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry save' }));
    // Assert
    await screen.findByRole('heading', { name: 'Added to organization' });
    expect(api.addOrganizationCatalogRecord).toHaveBeenLastCalledWith('org-1', first);
    expect(first.idempotencyKey).toBeTruthy();
  });

  it('keeps contribution and owner required, permits review edits before save, and removes staged components', async () => {
    // Arrange
    page();
    await createCapability();
    expect(screen.getByRole('button', { name: 'Review changes' })).toBeDisabled();
    await openComponents();
    fireEvent.click(screen.getByRole('button', { name: 'Add new component' }));
    fireEvent.change(screen.getByLabelText('Component name'), { target: { value: 'Temporary team' } });
    fireEvent.change(screen.getByLabelText('Component description'), { target: { value: 'Not yet saved' } });
    fireEvent.change(screen.getByLabelText('Component owner'), { target: { value: 'Draft owner' } });
    fireEvent.click(screen.getByRole('button', { name: 'Stage component' }));
    fireEvent.click(screen.getByRole('button', { name: 'Remove Temporary team' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: /Enterprise operations team/ }));
    fireEvent.click(screen.getByRole('checkbox', { name: /Enterprise operations team/ }));
    await review();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Organization contribution' }), { target: { value: 'Updated organization contribution' } });
    fireEvent.click(screen.getByRole('button', { name: 'Review changes' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save to organization' }));
    // Assert
    await screen.findByRole('heading', { name: 'Added to organization' });
    expect(api.addOrganizationCatalogRecord).toHaveBeenCalledWith('org-1', expect.objectContaining({
      components: [], newComponents: [], organizationContribution: 'Updated organization contribution',
    }));
  });

  it('supports switching CSP source and type, lookup failure, and retrying the selected source', async () => {
    // Arrange
    page();
    await screen.findByRole('textbox', { name: 'Name' });
    const dialog = within(screen.getByRole('dialog'));
    await act(async () => fireEvent.click(dialog.getByRole('radio', { name: 'Inherit from CSP' })));
    await act(async () => fireEvent.click(dialog.getByRole('radio', { name: 'Component' })));
    vi.mocked(api.getOrganizationCapability).mockRejectedValueOnce(new Error('Source temporarily unavailable'));
    // Act
    fireEvent.click(await dialog.findByRole('radio', { name: /Provider backup platform/ }));
    await dialog.findByText('Source temporarily unavailable');
    expect(dialog.getByRole('button', { name: 'Continue' })).toBeDisabled();
    fireEvent.click(dialog.getByRole('button', { name: 'Retry' }));
    // Assert
    await waitFor(() => expect(dialog.getByRole('button', { name: 'Continue' })).toBeEnabled());
    expect(api.getOrganizationCapability).toHaveBeenLastCalledWith('org-1', 'provider', 'provider-component', undefined, expect.any(AbortSignal), 'component');
    assertNoSystemWork();
  });

  it('retains existing organization contribution and CSP source components', async () => {
    // Arrange
    vi.mocked(api.getOrganizationCapability).mockResolvedValue({
      capability: { ...organizationCatalogRecords[1]!, organizationContribution: 'Existing organization duty', organizationOwner: 'Existing owner' },
      responsibilities: [], narrativeReviews: [],
      supportingComponents: [{ id: 'provider-component', source: 'provider', name: 'Provider platform', componentType: 'Service' }],
    });
    page('/security-capabilities?dialog=capability&setupSource=provider&setupRecord=provider-cap');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Continue' })).toBeEnabled());
    // Act
    await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Continue' })));
    // Assert
    expect(screen.getByRole('textbox', { name: 'Organization contribution' })).toHaveValue('Existing organization duty');
    expect(screen.getByRole('textbox', { name: 'Organization owner' })).toHaveValue('Existing owner');
    expect(screen.getByText('Provider platform · Service')).toBeInTheDocument();
    expect(screen.getByText(/Existing source components are retained/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Remove Provider platform' })).not.toBeInTheDocument();
  });

  it('allows editing after server validation rejection but not after an ambiguous failed save', async () => {
    // Arrange
    vi.mocked(api.addOrganizationCatalogRecord).mockRejectedValueOnce(Object.assign(new Error('Invalid organization metadata'), { status: 400 }));
    page();
    await createCapability();
    await review();
    fireEvent.click(screen.getByRole('button', { name: 'Save to organization' }));
    await screen.findByText('Invalid organization metadata');
    const firstKey = vi.mocked(api.addOrganizationCatalogRecord).mock.calls[0]![1].idempotencyKey;
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Organization owner' }), { target: { value: 'Corrected owner' } });
    fireEvent.click(screen.getByRole('button', { name: 'Review changes' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save to organization' }));
    // Assert
    await screen.findByRole('heading', { name: 'Added to organization' });
    expect(vi.mocked(api.addOrganizationCatalogRecord).mock.calls[1]![1]).toMatchObject({
      owner: 'Corrected owner', idempotencyKey: expect.not.stringMatching(firstKey),
    });
  });

  it('searches and pages records without losing a selected source, and reports partial/empty results', async () => {
    // Arrange
    vi.mocked(api.listOrganizationCapabilities).mockImplementation(async (_tenant, query) => ({
      items: query.page === 2 || query.search ? [] : [organizationCatalogRecords[1]!],
      total: query.search ? 0 : 26, page: query.page ?? 1, pageSize: 25,
      aggregateState: query.page === 2 ? 'Partial' : 'Available',
    }));
    page();
    const dialog = within(await screen.findByRole('dialog'));
    await dialog.findByRole('textbox', { name: 'Name' });
    fireEvent.click(dialog.getByRole('radio', { name: 'Inherit from CSP' }));
    fireEvent.click(await dialog.findByRole('radio', { name: /Backup and recovery/ }));
    // Act
    fireEvent.click(dialog.getByRole('button', { name: 'Next' }));
    await dialog.findByText('The catalog returned partial results: Partial');
    fireEvent.change(dialog.getByLabelText('Search capabilities'), { target: { value: 'No matches' } });
    // Assert
    await dialog.findByText('No eligible capabilities match this search.');
    expect(dialog.getByRole('button', { name: 'Continue' })).toBeEnabled();
    expect(api.listOrganizationCapabilities).toHaveBeenCalledWith('org-1',
      expect.objectContaining({ search: 'No matches', page: 1 }), expect.any(AbortSignal));
  });

  it('discards inline drafts and can return to creating a local capability', async () => {
    // Arrange
    page();
    await createCapability();
    await openComponents();
    fireEvent.click(screen.getByRole('button', { name: 'Add new component' }));
    fireEvent.click(screen.getByRole('button', { name: 'Discard component' }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    fireEvent.click(screen.getByRole('radio', { name: 'Use existing organization record' }));
    fireEvent.click(screen.getByRole('radio', { name: 'Create new organization record' }));
    // Assert
    expect(screen.getByRole('textbox', { name: 'Name' })).toHaveValue('Enterprise monitoring');
    expect(api.addOrganizationCatalogRecord).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'Organization service description' } });
    fireEvent.change(screen.getByLabelText('Implementation status'), { target: { value: 'Implemented' } });
    await act(async () => fireEvent.click(screen.getByRole('button', { name: 'Cancel' })));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
