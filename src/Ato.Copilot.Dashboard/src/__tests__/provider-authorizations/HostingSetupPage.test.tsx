import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { HostingSetupPage } from '../../features/provider-authorizations/HostingSetupPage';
import * as api from '../../features/provider-authorizations/api';
import * as hosting from '../../features/provider-authorizations/hostingApi';
import { PackageImportError } from '../../features/package-imports/request';
import { boundary, offering } from './testData';
import type { ExternalDecision, OfferingBoundaryOverview } from '../../features/provider-authorizations/types';
import type { HostingScopeRevision } from '../../features/provider-authorizations/hostingTypes';
import '../helpers/dialog';

vi.mock('../../features/provider-authorizations/api', async original => ({
  ...await original<typeof api>(), getOffering: vi.fn(), getBoundary: vi.fn(), getBoundaryOverview: vi.fn(), listMicrosoftReferences: vi.fn(),
}));
vi.mock('../../features/provider-authorizations/hostingApi', async original => ({
  ...await original<typeof hosting>(), getHostingScope: vi.fn(), listHostingScopes: vi.fn(),
}));
vi.mock('../../features/provider-authorizations/HostingPanel', () => ({
  HostingPanel: ({ initialScope, onChanged, onPendingChange }: { initialScope?: HostingScopeRevision; onChanged: () => void; onPendingChange: (value: boolean) => void }) =>
    <section aria-label="Configure hosting form"><input aria-label="Hosting scope name" defaultValue={initialScope?.name ?? ''} />
      <button onClick={() => onPendingChange(true)}>Simulate pending write</button><button onClick={onChanged}>Simulate saved scope</button></section>,
}));
const referenceRefresh = vi.hoisted(() => ({ current: undefined as (() => Promise<void>) | undefined }));
vi.mock('../../features/provider-authorizations/DecisionPanel', () => ({
  DecisionPanel: ({ initialAction, onRefreshOffering }: { initialAction?: string; onRefreshOffering: () => Promise<void> }) => {
    referenceRefresh.current = onRefreshOffering;
    return <section aria-label="Microsoft reference form" data-action={initialAction ?? 'review'}>Microsoft reference editor</section>;
  },
}));
const page = <T,>(items: T[], total = items.length) => ({ items, total, page: 1, pageSize: 10 });
const current: HostingScopeRevision = {
  offeringId: offering.offeringId, offeringRevision: offering.revision,
  snapshot: { revisionId: 'hosting-current', revision: 2, snapshotHash: 'technical-snapshot-hash' },
  name: 'DoD shared hosting', predecessorRevisionId: 'hosting-old', impactReviewId: null,
  permittedScopes: [{ cloud: 'AzureUSGovernment', directoryTenantId: 'directory-id', subscriptionId: 'subscription-id',
    resourceId: '/subscriptions/subscription-id/resourceGroups/shared' }], exclusions: [], citations: [],
};
const overview: OfferingBoundaryOverview = {
  offeringId: offering.offeringId, offeringRevision: offering.revision,
  capabilities: { ...page([{ capabilityId: 'capability-a', candidateId: null, packageId: null, name: 'Audit collection',
    publicationState: 'Published', reviewState: 'Reviewed', releaseId: 'release-a', boundaryRevisionId: boundary.boundaryRevisionId }]),
    total: 3, awaitingReview: 2, published: 1 },
  missionSystems: page([{ assignmentId: 'assignment-a', systemId: 'system-a', systemName: 'Mission Alpha',
    relationshipState: 'Undetermined', associated: true, adoptedCapabilityCount: 2, assignedScopes: [] }]),
};
const mount = (configured = false) => render(<MemoryRouter><HostingSetupPage
  offering={{ ...offering, currentHostingScopeRevisionId: configured ? current.snapshot.revisionId : null }} onChanged={vi.fn()} /></MemoryRouter>);

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(hosting.listHostingScopes).mockResolvedValue(page([]));
  vi.mocked(hosting.getHostingScope).mockResolvedValue(current);
  vi.mocked(api.listMicrosoftReferences).mockResolvedValue(page([]));
  vi.mocked(api.getBoundary).mockResolvedValue({ ...boundary,
    providerResponsibilities: ['Operate platform monitoring'], customerResponsibilities: ['Review mission access'] });
  vi.mocked(api.getBoundaryOverview).mockResolvedValue(overview);
});

describe('task-led CSP hosting setup', () => {
  it.each([
    ['hosting', 'Configure Azure hosting'], ['capabilities', 'Review offering capabilities'], ['missions', 'Mission system associations'],
  ])('opens the requested %s overview task in a dialog without a mutation', async (task, title) => {
    // Arrange / Act
    render(<MemoryRouter initialEntries={[`/?task=${task}`]}><HostingSetupPage offering={offering} onChanged={vi.fn()} /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('dialog', { name: title })).toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: 'Provider hosting allocation' })).not.toBeInTheDocument();
  });
  it('ignores unsupported deep-linked tasks rather than opening administration', async () => {
    // Arrange / Act
    render(<MemoryRouter initialEntries={['/?task=allocations']}><HostingSetupPage offering={offering} onChanged={vi.fn()} /></MemoryRouter>);
    // Assert
    await waitFor(() => expect(within(screen.getByRole('region', { name: 'Azure hosting' })).getByRole('button', { name: 'Configure hosting' })).toBeEnabled());
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
  it.each([
    ['Suggested next step', 'Configure hosting', 'Configure Azure hosting'],
    ['Azure hosting', 'Configure hosting', 'Configure Azure hosting'],
    ['Microsoft authorization references', 'Add reference', 'Add or review Microsoft references'],
    ['Security capabilities', 'Review capabilities', 'Review offering capabilities'],
    ['Mission systems', 'View associations', 'Mission system associations'],
    ['Shared responsibilities', 'Review responsibilities', 'Review responsibilities'],
  ])('opens %s work in a dialog without expanding its overview card', async (region, action, title) => {
    // Arrange
    mount();
    const card = screen.getByRole('region', { name: region });
    const trigger = within(card).getByRole('button', { name: action });
    await waitFor(() => expect(trigger).toBeEnabled());
    trigger.focus();
    // Act
    fireEvent.click(trigger);
    // Assert
    const dialog = await screen.findByRole('dialog', { name: title });
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    expect(card).not.toContainElement(dialog);
    expect(within(card).queryByRole('textbox')).not.toBeInTheDocument();
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: 'Close dialog' }));
    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });

  it('retains the reference task while explicitly refreshing offering concurrency metadata', async () => {
    // Arrange
    vi.mocked(api.getOffering).mockResolvedValue({ ...offering, revision: offering.revision + 1 });
    mount();
    const card = screen.getByRole('region', { name: 'Microsoft authorization references' });
    await waitFor(() => expect(within(card).getByRole('button', { name: 'Add reference' })).toBeEnabled());
    fireEvent.click(within(card).getByRole('button', { name: 'Add reference' }));
    // Act
    await act(async () => { await referenceRefresh.current!(); });
    // Assert
    expect(screen.getByRole('region', { name: 'Microsoft reference form' })).toBeInTheDocument();
    expect(api.getOffering).toHaveBeenCalledWith(offering.offeringId);
    vi.mocked(api.getOffering).mockResolvedValue({ ...offering, offeringId: 'different' });
    await expect(referenceRefresh.current!()).rejects.toThrow(/requested current revision/);
  });
  it('shows recognizable saved references and opens review without creating a new draft', async () => {
    // Arrange
    const reference: ExternalDecision = { recordId: 'ref-1', offeringId: offering.offeringId,
      revisionId: 'ref-version-1', revision: 1, snapshotHash: 'ref-hash',
      boundaryRevisionId: boundary.boundaryRevisionId, sourceCandidateRefs: [],
      recordKind: 'InheritedMicrosoftReference', reference: 'Microsoft service authorization letter',
      issuingAuthority: null, decisionAsStated: null, issuedOn: null, effectiveOn: null,
      expiresOn: null, expiryBasis: 'NotRecorded', scopeStatement: 'Named services only.',
      conditions: [], citations: [], metadataReviewState: 'Recorded', currentStanding: 'Undetermined',
      recordedBy: 'reviewer', recordedAt: '2026-09-25T12:00:00Z', impactReviewRequired: false };
    vi.mocked(api.listMicrosoftReferences).mockResolvedValue(page([reference]));
    mount(true);
    const trigger = await screen.findByRole('button', { name: 'Review saved references' });
    trigger.focus();
    // Act
    fireEvent.click(trigger);
    // Assert
    expect(screen.getByRole('region', { name: 'Microsoft reference form' })).toHaveAttribute('data-action', 'review');
    expect(screen.getByRole('dialog')).toHaveAccessibleName('Add or review Microsoft references');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Close task' }));
    // Assert
    expect(trigger).toHaveFocus();
  });
  it('locks task closure during a pending write and refreshes after its confirmed receipt', async () => {
    // Arrange
    mount(true);
    const card = screen.getByRole('region', { name: 'Azure hosting' });
    await within(card).findByText('DoD shared hosting');
    fireEvent.click(within(card).getByRole('button', { name: 'Configure hosting' }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Simulate pending write' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Close task' })).toBeDisabled();
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByRole('button', { name: 'Close dialog' })).toBeDisabled();
    // Act
    fireEvent(dialog, new Event('cancel', { bubbles: true, cancelable: true }));
    fireEvent.click(dialog, { clientX: -1, clientY: -1 });
    // Assert
    expect(dialog).toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Simulate saved scope' }));
    // Assert
    expect(screen.queryByRole('region', { name: 'Configure hosting form' })).not.toBeInTheDocument();
    await waitFor(() => expect(hosting.getHostingScope).toHaveBeenCalledTimes(2));
  });
  it('opens allocation administration only on request and keeps missing duties explicit', async () => {
    // Arrange
    vi.mocked(api.getBoundary).mockResolvedValue({ ...boundary, providerResponsibilities: [], customerResponsibilities: [] });
    mount(true);
    const missions = screen.getByRole('region', { name: 'Mission systems' });
    await waitFor(() => expect(within(missions).getByRole('button', { name: 'View associations' })).toBeEnabled());
    // Act
    fireEvent.click(within(missions).getByRole('button', { name: 'View associations' }));
    fireEvent.click(screen.getByText('Provider allocation administration'));
    fireEvent.click(screen.getByRole('button', { name: 'Manage hosting allocations' }));
    // Assert
    expect(screen.getByRole('heading', { name: 'Provider hosting allocation' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Close task' }));
    fireEvent.click(screen.getByRole('button', { name: 'Review responsibilities' }));
    expect(screen.getAllByText('Not documented. Missing duties are not a waiver.')).toHaveLength(2);
  });
  it('explains five areas and a single next action without displaying forms, hashes or repeated warnings', async () => {
    // Arrange
    mount();
    // Act
    const next = await screen.findByRole('region', { name: 'Suggested next step' });
    await waitFor(() => expect(within(next).getByRole('button', { name: 'Configure hosting' })).toBeEnabled());
    // Assert
    for (const name of ['Azure hosting', 'Microsoft authorization references', 'Security capabilities', 'Mission systems', 'Shared responsibilities']) {
      expect(screen.getByRole('heading', { name, level: 2 })).toBeInTheDocument();
    }
    expect(screen.getByRole('list', { name: 'Offering setup checklist' }).children).toHaveLength(5);
    expect(within(next).getAllByRole('button')).toHaveLength(1);
    expect(screen.queryByLabelText('Hosting scope name')).not.toBeInTheDocument();
    expect(screen.queryByText('Microsoft reference editor')).not.toBeInTheDocument();
    expect(screen.queryByText('technical-snapshot-hash')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Create draft' })).not.toBeInTheDocument();
  });

  it('opens only requested hosting work and prefills the exact current snapshot, not history page one', async () => {
    // Arrange
    mount(true);
    const card = await screen.findByRole('region', { name: 'Azure hosting' });
    await within(card).findByText('DoD shared hosting');
    // Act
    fireEvent.click(within(card).getByRole('button', { name: 'Configure hosting' }));
    // Assert
    expect(await screen.findByLabelText('Hosting scope name')).toHaveValue(current.name);
    expect(hosting.getHostingScope).toHaveBeenCalledWith(offering.offeringId, 'hosting-current', expect.any(AbortSignal));
    expect(screen.queryByRole('region', { name: 'Microsoft reference form' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Customer tenant ID')).not.toBeInTheDocument();
  });

  it.each([404, 403, 500])('distinguishes a failed hosting read (%s) from no configured scope and blocks configuration', async status => {
    // Arrange
    vi.mocked(hosting.listHostingScopes).mockRejectedValue(new PackageImportError('Request failed', status));
    mount();
    // Act
    const card = await screen.findByRole('region', { name: 'Azure hosting' });
    // Assert
    expect(await within(card).findByRole('alert')).toHaveTextContent('Azure hosting unavailable');
    expect(within(card).getByRole('button', { name: 'Configure hosting' })).toBeDisabled();
    expect(within(card).queryByText('Not configured')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Hosting scope name')).not.toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Retry Azure hosting' })).toBeEnabled();
  });

  it('restores configuration only after a retry succeeds', async () => {
    // Arrange
    vi.mocked(hosting.listHostingScopes).mockRejectedValueOnce(new Error('Unavailable')).mockResolvedValue(page([]));
    mount();
    const card = await screen.findByRole('region', { name: 'Azure hosting' });
    // Act
    fireEvent.click(await within(card).findByRole('button', { name: 'Retry Azure hosting' }));
    // Assert
    await waitFor(() => expect(within(card).getByRole('button', { name: 'Configure hosting' })).toBeEnabled());
    expect(await within(card).findByText('Not configured')).toBeInTheDocument();
  });

  it('explains reference documents and opens an explicitly named reference task', async () => {
    // Arrange
    mount(true);
    const card = await screen.findByRole('region', { name: 'Microsoft authorization references' });
    await waitFor(() => expect(within(card).getByRole('button', { name: 'Add reference' })).toBeEnabled());
    // Act
    fireEvent.click(within(card).getByRole('button', { name: 'Add reference' }));
    // Assert
    expect(await screen.findByRole('region', { name: 'Microsoft reference form' })).toBeInTheDocument();
    expect(card).toHaveTextContent('Microsoft-issued');
    expect(card).toHaveTextContent('provider');
    expect(screen.queryByLabelText('Hosting scope name')).not.toBeInTheDocument();
  });

  it('shows real publication counts and links to capability review instead of implying inheritance', async () => {
    // Arrange
    mount(true);
    const card = await screen.findByRole('region', { name: 'Security capabilities' });
    await within(card).findByText(/1 published/);
    // Act
    fireEvent.click(within(card).getByRole('button', { name: 'Review capabilities' }));
    // Assert
    const dialog = screen.getByRole('dialog', { name: 'Review offering capabilities' });
    expect(await within(dialog).findByText('Audit collection')).toBeInTheDocument();
    expect(within(dialog).getByRole('link', { name: 'Open Audit collection' })).toHaveAttribute('href', '/security-capabilities/capability-a');
    expect(within(dialog).getByRole('link', { name: 'Review source proposals' })).toHaveAttribute('href', expect.stringContaining('/packages'));
  });

  it('shows associated mission names and adoption counts while keeping the Mission Owner task separate', async () => {
    // Arrange
    mount(true);
    const card = await screen.findByRole('region', { name: 'Mission systems' });
    await waitFor(() => expect(within(card).getByRole('button', { name: 'View associations' })).toBeEnabled());
    // Act
    fireEvent.click(within(card).getByRole('button', { name: 'View associations' }));
    // Assert
    const dialog = screen.getByRole('dialog', { name: 'Mission system associations' });
    expect(await within(dialog).findByText('Mission Alpha')).toBeInTheDocument();
    expect(dialog).toHaveTextContent('2 adopted capabilities');
    expect(card).toHaveTextContent('Select system');
    expect(card).toHaveTextContent('existing');
    expect(screen.queryByLabelText('Customer tenant ID')).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('link', { name: /Mission Alpha/ })).not.toBeInTheDocument();
  });

  it('reviews CSP and customer duties without exposing unrelated historical boundaries', async () => {
    // Arrange
    mount(true);
    const card = await screen.findByRole('region', { name: 'Shared responsibilities' });
    await waitFor(() => expect(within(card).getByRole('button', { name: 'Review responsibilities' })).toBeEnabled());
    // Act
    fireEvent.click(within(card).getByRole('button', { name: 'Review responsibilities' }));
    // Assert
    const dialog = screen.getByRole('dialog', { name: 'Review responsibilities' });
    expect(await within(dialog).findByText('Operate platform monitoring')).toBeInTheDocument();
    expect(dialog).toHaveTextContent('Review mission access');
    expect(api.getBoundary).toHaveBeenCalledWith(offering.offeringId, offering.currentBoundaryRevisionId, expect.any(AbortSignal));
    expect(card).toHaveTextContent('does not accept');
  });

  it('never reports failed capability and mission reads as zero records', async () => {
    // Arrange
    vi.mocked(api.getBoundaryOverview).mockRejectedValue(new Error('Read service unavailable'));
    mount(true);
    // Act
    const capabilities = await screen.findByRole('region', { name: 'Security capabilities' });
    // Assert
    expect(await within(capabilities).findByRole('alert')).toHaveTextContent('Security capabilities unavailable');
    expect(within(capabilities).getByRole('button', { name: 'Review capabilities' })).toBeDisabled();
    const missions = screen.getByRole('region', { name: 'Mission systems' });
    expect(within(missions).getByRole('button', { name: 'View associations' })).toBeDisabled();
    expect(missions).not.toHaveTextContent('0 hosting');
  });
  it('does not turn a failed responsibility refresh into undocumented duties', async () => {
    // Arrange
    const view = mount(true);
    const card = screen.getByRole('region', { name: 'Shared responsibilities' });
    await waitFor(() => expect(within(card).getByRole('button', { name: 'Review responsibilities' })).toBeEnabled());
    fireEvent.click(within(card).getByRole('button', { name: 'Review responsibilities' }));
    vi.mocked(api.getBoundary).mockRejectedValue(new Error('Boundary unavailable'));
    // Act
    view.rerender(<MemoryRouter><HostingSetupPage offering={{ ...offering, revision: offering.revision + 1,
      currentHostingScopeRevisionId: current.snapshot.revisionId }} onChanged={vi.fn()} /></MemoryRouter>);
    // Assert
    expect(await within(card).findByRole('alert')).toHaveTextContent('Shared responsibilities unavailable');
    const dialog = screen.getByRole('dialog', { name: 'Review responsibilities' });
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Review responsibilities unavailable');
    expect(within(dialog).queryByText('Not documented. Missing duties are not a waiver.')).not.toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Close task' })).toBeEnabled();
  });

  it.each([
    ['Security capabilities', 'Review capabilities', 'Review offering capabilities', 2, 1],
    ['Mission systems', 'View associations', 'Mission system associations', 1, 2],
  ] as const)('keeps the %s dialog mounted during pagination and exposes retry inside it', async (region, action, title, capabilityPage, missionPage) => {
    // Arrange
    const pagedOverview = { ...overview, capabilities: { ...overview.capabilities, total: 11 },
      missionSystems: { ...overview.missionSystems, total: 11 } };
    vi.mocked(api.getBoundaryOverview).mockResolvedValueOnce(pagedOverview)
      .mockRejectedValueOnce(new Error('Read interrupted')).mockResolvedValue(pagedOverview);
    mount(true);
    const trigger = within(screen.getByRole('region', { name: region })).getByRole('button', { name: action });
    await waitFor(() => expect(trigger).toBeEnabled());
    fireEvent.click(trigger);
    const dialog = screen.getByRole('dialog', { name: title });
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: 'Next' }));
    // Assert
    expect(screen.getByRole('dialog')).toBe(dialog);
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(`${title} unavailable`);
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: `Retry ${title}` }));
    // Assert
    await waitFor(() => expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument());
    expect(screen.getByRole('dialog')).toBe(dialog);
    expect(api.getBoundaryOverview).toHaveBeenLastCalledWith(offering.offeringId, capabilityPage, missionPage, expect.any(AbortSignal));
  });
});
