import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthorizationsPage } from '../../features/provider-authorizations/AuthorizationsPage';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import { PackageImportError } from '../../features/package-imports/request';
import * as api from '../../features/provider-authorizations/api';
import type { BoundaryRevision, OfferingBoundaryOverview } from '../../features/provider-authorizations/types';
import { boundary, offering } from './testData';
import '../helpers/dialog';

vi.mock('../../components/layout/PageLayout', () => ({ default: ({ children }: { children: ReactNode }) => <main>{children}</main> }));
vi.mock('../../components/layout/PageHero', () => ({ default: ({ title, actions }: { title: string; actions?: ReactNode }) => <header><h1>{title}</h1>{actions}</header> }));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => ({ target: { kind: 'csp' }, workspace: { permissions: { canAccessCsp: true } } }),
}));
vi.mock('../../features/provider-authorizations/api', async original => ({
  ...await original<typeof api>(), getOffering: vi.fn(), getBoundary: vi.fn(), listBoundaries: vi.fn(),
  getBoundaryOverview: vi.fn(), createBoundary: vi.fn(), recordDecision: vi.fn(),
}));

const current: BoundaryRevision = {
  ...boundary, name: 'Microsoft 365 DoD Tenant',
  scopeStatement: 'Tenant identity and federation; Exchange Online DoD mailboxes; Teams DoD collaboration.',
  services: ['Exchange Online DoD', 'Teams DoD'],
  includedScopes: [{ cloud: 'AzureUSGovernment', directoryTenantId: 'directory-a',
    subscriptionId: 'subscription-a', resourceId: '/subscriptions/subscription-a/resourceGroups/recorded-group' }],
  exclusions: [{ scope: null, description: 'Mission endpoints', rationale: 'Managed by the Mission Owner.' }],
  providerResponsibilities: ['Provide tenant audit collection.'],
  customerResponsibilities: ['Configure mission access policies.'],
};
const overview: OfferingBoundaryOverview = {
  offeringId: offering.offeringId, offeringRevision: offering.revision,
  capabilities: {
    page: 1, pageSize: 10, total: 2, awaitingReview: 1, published: 1,
    items: [
      { capabilityId: null, candidateId: 'proposal-1', packageId: 'package-1', name: 'Threat monitoring proposal',
        reviewState: 'NeedsReview', publicationState: 'Unpublished', releaseId: null, boundaryRevisionId: boundary.boundaryRevisionId },
      { capabilityId: 'capability-1', candidateId: null, packageId: null, name: 'Published audit collection',
        reviewState: 'Reviewed', publicationState: 'Published', releaseId: 'release-1', boundaryRevisionId: 'older-boundary' },
    ],
  },
  missionSystems: { page: 1, pageSize: 10, total: 1, items: [{
    assignmentId: 'assignment-1', systemId: 'system-1', systemName: 'Example mission',
    relationshipState: 'Undetermined', associated: false, adoptedCapabilityCount: 0,
    assignedScopes: current.includedScopes,
  }] },
};

function mount() {
  return render(<MemoryRouter initialEntries={[`/workspaces/csp/authorizations/offerings/${offering.offeringId}/boundary`]}>
    <WorkspaceNavigationProvider workspace={{ kind: 'csp' }}><AuthorizationsPage /></WorkspaceNavigationProvider>
  </MemoryRouter>);
}
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getOffering).mockResolvedValue({ ...offering, name: 'Azure IL5' });
  vi.mocked(api.getBoundary).mockResolvedValue(current);
  vi.mocked(api.listBoundaries).mockResolvedValue({ items: [current], page: 1, pageSize: 25, total: 1 });
  vi.mocked(api.getBoundaryOverview).mockResolvedValue(overview);
});

describe('scope-first authorization boundary', () => {
  it('matches the approved scope card and provider/customer duty hierarchy using recorded values', async () => {
    // Arrange
    mount();
    // Act
    const scope = await screen.findByRole('region', { name: 'Service scope' });
    await within(scope).findByText(current.services[0]!);
    // Assert
    expect(within(scope).getByRole('heading', { name: 'Service scope' })).toBeInTheDocument();
    expect(within(scope).getByText(`Recorded boundary version ${current.version} · Working recorded scope`)).toBeInTheDocument();
    expect(within(scope).getByRole('heading', { name: 'Service scope' }).parentElement)
      .toHaveTextContent(`Recorded boundary version ${current.version} · Working recorded scope`);
    const services = within(scope).getByRole('list', { name: 'Included services' });
    expect(within(services).getAllByRole('listitem').map(item => item.textContent)).toEqual(current.services);
    expect(within(scope).getByText('Not included:')).toBeInTheDocument();
    expect(within(scope).getByText('Mission endpoints')).toBeInTheDocument();
    const provenance = within(scope).getByText('Hosting identity & source provenance').closest('details');
    expect(provenance).not.toHaveAttribute('open');
    expect(provenance).toHaveTextContent(current.snapshotHash);
    const provider = screen.getByRole('region', { name: 'Provider duties' });
    const customer = screen.getByRole('region', { name: 'Customer duties' });
    expect(provider.parentElement).toBe(customer.parentElement);
    expect(provider).toHaveTextContent(current.providerResponsibilities[0]!);
    expect(customer).toHaveTextContent(current.customerResponsibilities[0]!);
    expect(within(provider).getByRole('link', { name: /Explore duties by capability/ }))
      .toHaveAttribute('href', `/workspaces/csp/authorizations/offerings/${offering.offeringId}/inherited-coverage?task=capabilities`);
    expect(customer).toHaveTextContent('Shared control references do not mean the customer’s part is complete.');
    expect(screen.getByText(/Recorded boundary duties are working context/)).toBeInTheDocument();
    expect(api.createBoundary).not.toHaveBeenCalled();
  });

  it('retains long technical source text in provenance rather than expanding the scope overview', async () => {
    // Arrange
    const statement = 'Only the documented shared operations; mission workloads are excluded. ' +
      'Recorded hosting identity; directoryTenantId=directory-a; resourceId=' + '/recorded/resource/'.repeat(25);
    vi.mocked(api.getBoundary).mockResolvedValue({ ...current, scopeStatement: statement });
    mount();
    // Act
    const scope = await screen.findByRole('region', { name: 'Service scope' });
    await within(scope).findByText('Only the documented shared operations; mission workloads are excluded.');
    // Assert
    const full = within(scope).getByText(statement);
    expect(full.closest('details')).not.toHaveAttribute('open');
    expect(within(scope).getByText(/Scope excerpt; read the complete recorded statement/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Edit boundary' }));
    expect(screen.getByLabelText('Explicit scope statement')).toHaveValue(statement);
    expect(api.createBoundary).not.toHaveBeenCalled();
  });

  it('keeps unavailable or restricted scope distinct from missing scope and disables editing', async () => {
    // Arrange
    vi.mocked(api.getBoundary).mockRejectedValue(new Error('Scope access denied (403)'));
    mount();
    // Act
    await screen.findByText('Scope access denied (403)');
    // Assert
    expect(screen.getByRole('button', { name: 'Edit boundary' })).toBeDisabled();
    expect(screen.queryByText(/No boundary recorded/)).not.toBeInTheDocument();
    expect(screen.getAllByText('Responsibilities are unavailable until the current scope loads.')).toHaveLength(2);
    expect(screen.queryByText(current.providerResponsibilities[0]!)).not.toBeInTheDocument();
    expect(api.createBoundary).not.toHaveBeenCalled();
  });

  it('does not label a nonempty long statement with leading blank lines as missing', async () => {
    // Arrange
    const statement = '\n\n' + 'The complete recorded scope is retained without shortening a sentence fragment; '.repeat(7);
    vi.mocked(api.getBoundary).mockResolvedValue({ ...current, scopeStatement: statement });
    mount();
    // Act
    const scope = await screen.findByRole('region', { name: 'Service scope' });
    await within(scope).findByRole('list', { name: 'Included services' });
    // Assert
    expect(within(scope).queryByText('No scope statement recorded.')).not.toBeInTheDocument();
    expect(scope).toHaveTextContent('The complete recorded scope is retained');
  });

  it('blocks an edit from stale linked context without presenting current duties as released duties', async () => {
    // Arrange
    vi.mocked(api.getBoundaryOverview).mockResolvedValue({ ...overview, offeringRevision: offering.revision + 1 });
    mount();
    // Act
    await screen.findByText(/Offering context changed since this page loaded/);
    // Assert
    expect(screen.getByRole('button', { name: 'Edit boundary' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Refresh offering context' })).toBeEnabled();
    expect(screen.getByRole('region', { name: 'Provider duties' })).toHaveTextContent(current.providerResponsibilities[0]!);
    expect(screen.getByText(/not immutable published duty allocations/)).toBeInTheDocument();
    expect(api.createBoundary).not.toHaveBeenCalled();
  });

  it('retains all arbitrary service definitions and multiple duty statements with no provider-specific assumptions', async () => {
    // Arrange
    const services = Array.from({ length: 27 }, (_, index) => `Independent service ${index + 1}`);
    vi.mocked(api.getBoundary).mockResolvedValue({ ...current, services,
      providerResponsibilities: ['First recorded provider duty.', 'Second recorded provider duty.'],
      customerResponsibilities: [], citations: [] });
    mount();
    // Act
    const list = await screen.findByRole('list', { name: 'Included services' });
    // Assert
    expect(within(list).getAllByRole('listitem').map(item => item.textContent)).toEqual(services);
    expect(within(screen.getByRole('region', { name: 'Provider duties' })).getAllByRole('listitem')).toHaveLength(2);
    expect(screen.getByRole('region', { name: 'Customer duties' })).toHaveTextContent('No Mission Owner responsibilities recorded.');
    expect(screen.getByText(/No supporting source citations recorded/)).toBeInTheDocument();
    expect(api.createBoundary).not.toHaveBeenCalled();
  });

  it('preserves recorded scope when the independent linked read model is unavailable', async () => {
    // Arrange
    vi.mocked(api.getBoundaryOverview).mockRejectedValue(new Error('Linked records unavailable'));
    mount();
    // Act
    await screen.findByText('Linked records unavailable');
    // Assert
    expect(await screen.findByRole('list', { name: 'Included services' })).toHaveTextContent(current.services[0]!);
    expect(screen.getByRole('region', { name: 'Provider duties' })).toHaveTextContent(current.providerResponsibilities[0]!);
    expect(screen.queryByText('No linked capability records.')).not.toBeInTheDocument();
    expect(screen.queryByText('No hosting assignments recorded.')).not.toBeInTheDocument();
    expect(api.createBoundary).not.toHaveBeenCalled();
  });

  it('opens the boundary editor in a dialog and returns focus without writing on Escape', async () => {
    // Arrange
    mount();
    const edit = await screen.findByRole('button', { name: 'Edit boundary' });
    await waitFor(() => expect(edit).toBeEnabled());
    edit.focus();
    // Act
    fireEvent.click(edit);
    const dialog = await screen.findByRole('dialog', { name: 'Edit boundary' });
    // Assert
    expect(within(dialog).getByLabelText('Boundary name')).toHaveValue(current.name);
    expect(within(screen.getByRole('region', { name: 'Service scope' })).queryByRole('textbox')).not.toBeInTheDocument();
    expect(document.body.style.overflow).toBe('hidden');
    // Act
    fireEvent(dialog, new Event('cancel', { bubbles: true, cancelable: true }));
    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(edit).toHaveFocus();
    expect(document.body.style.overflow).toBe('');
    expect(api.createBoundary).not.toHaveBeenCalled();
  });

  it('keeps an uncertain boundary save in the dialog until the exact operation is resolved', async () => {
    // Arrange
    vi.mocked(api.createBoundary).mockRejectedValueOnce(new Error('Connection interrupted'))
      .mockResolvedValueOnce({ ...current, version: 2 });
    mount();
    const edit = await screen.findByRole('button', { name: 'Edit boundary' });
    await waitFor(() => expect(edit).toBeEnabled());
    fireEvent.click(edit);
    const dialog = screen.getByRole('dialog', { name: 'Edit boundary' });
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save boundary revision' }));
    await within(dialog).findByText(/Outcome uncertain/);
    // Assert
    expect(dialog).toHaveAttribute('aria-busy', 'true');
    expect(within(dialog).getByRole('button', { name: 'Close dialog' })).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Cancel editing' })).toBeDisabled();
    // Act
    fireEvent(dialog, new Event('cancel', { bubbles: true, cancelable: true }));
    fireEvent.click(dialog, { clientX: -1, clientY: -1 });
    // Assert
    expect(dialog).toBeInTheDocument();
    expect(api.createBoundary).toHaveBeenCalledOnce();
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: 'Retry same operation' }));
    // Assert
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(api.createBoundary).toHaveBeenCalledTimes(2);
    expect(vi.mocked(api.createBoundary).mock.calls[1]).toEqual(vi.mocked(api.createBoundary).mock.calls[0]);
  });

  it('separates offering, cloud and service identity and retains linked records and workflow in disclosures', async () => {
    // Arrange
    mount();
    // Act
    await screen.findByRole('heading', { name: 'Service scope' });
    await screen.findByText(current.name);
    // Assert
    for (const name of ['Provider duties', 'Customer duties', 'Security capabilities', 'Mission systems'])
      expect(screen.getByRole('heading', { name })).toBeInTheDocument();
    expect(screen.getByText('Provider offering', { exact: true }).nextElementSibling).toHaveTextContent('Azure IL5');
    expect(screen.getByText('Recorded cloud environment', { exact: true }).nextElementSibling).toHaveTextContent('Azure Government');
    expect(screen.getByText('Boundary / service scope', { exact: true }).nextElementSibling).toHaveTextContent('Microsoft 365 DoD Tenant');
    const workflow = screen.getByRole('list', { name: 'Offering workflow' });
    expect(within(workflow).getAllByRole('listitem')).toHaveLength(4);
    expect(workflow).toHaveTextContent('Review the package');
    expect(workflow).toHaveTextContent('Confirm the boundary');
    expect(workflow).toHaveTextContent('Review and publish capabilities');
    expect(workflow).toHaveTextContent('Mission Owners associate systems');
    expect(screen.queryByText('Prepare boundary revision')).not.toBeInTheDocument();
    expect(screen.queryByText(/Prepare successor of/)).not.toBeInTheDocument();
  });

  it('preserves the exact scope statement and recorded services, resources and duties', async () => {
    // Arrange
    mount();
    // Act
    const resources = await screen.findByRole('region', { name: 'Service scope' });
    await within(resources).findAllByText(current.scopeStatement);
    // Assert
    expect(resources).toHaveTextContent(current.scopeStatement);
    expect(resources).toHaveTextContent('Exchange Online DoD');
    expect(resources).toHaveTextContent('directory-a');
    expect(resources).toHaveTextContent('subscription-a');
    expect(resources).toHaveTextContent('recorded-group');
    expect(resources).toHaveTextContent('Mission endpoints');
    expect(screen.getByRole('region', { name: 'Provider duties' })).toHaveTextContent('Provide tenant audit collection.');
    expect(screen.getByRole('region', { name: 'Customer duties' })).toHaveTextContent('Configure mission access policies.');
    expect(api.createBoundary).not.toHaveBeenCalled();
  });

  it('uses the exact current boundary for its single edit action and records a successor only on save', async () => {
    // Arrange
    vi.mocked(api.createBoundary).mockResolvedValue({ ...current, version: 2, boundaryRevisionId: 'boundary-2', offeringRevision: 5 });
    mount();
    const edit = await screen.findByRole('button', { name: 'Edit boundary' });
    await waitFor(() => expect(edit).toBeEnabled());
    // Act
    fireEvent.click(edit);
    // Assert
    expect(screen.getAllByRole('button', { name: 'Edit boundary' })).toHaveLength(1);
    expect(screen.getByLabelText('Boundary name')).toHaveValue(current.name);
    expect(screen.getByText(/Saving creates a new version/)).toBeInTheDocument();
    expect(api.getBoundary).toHaveBeenCalledWith(offering.offeringId, current.boundaryRevisionId, expect.any(AbortSignal));
    expect(api.listBoundaries).not.toHaveBeenCalled();
    expect(api.createBoundary).not.toHaveBeenCalled();
    // Act
    fireEvent.change(screen.getByLabelText('Boundary name'), { target: { value: 'Revised recorded service scope' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save boundary revision' }));
    // Assert
    await waitFor(() => expect(api.createBoundary).toHaveBeenCalledWith(offering.offeringId, {
      ...Object.fromEntries(Object.entries(current).filter(([key]) => [
        'name', 'scopeStatement', 'services', 'componentSnapshotIds', 'includedScopes', 'exclusions',
        'providerResponsibilities', 'customerResponsibilities', 'citations',
      ].includes(key))),
      name: 'Revised recorded service scope', expectedOfferingRevision: offering.revision,
      predecessorRevisionId: current.boundaryRevisionId,
    }, expect.any(String)));
    expect(api.recordDecision).not.toHaveBeenCalled();
  });

  it('puts previous versions and snapshot hashes behind collapsed history without changing the editable predecessor', async () => {
    // Arrange
    mount();
    await screen.findByRole('button', { name: 'Edit boundary' });
    await waitFor(() => expect(screen.getByText(current.snapshotHash).closest('details')).not.toHaveAttribute('open'));
    expect(api.listBoundaries).not.toHaveBeenCalled();
    // Act
    fireEvent.click(screen.getByText('Supporting records, versions & workflow', { selector: 'summary' }));
    fireEvent.click(screen.getByText('Version history', { selector: 'summary' }));
    // Assert
    const history = screen.getByText('Version history', { selector: 'summary' }).closest('details')!;
    expect(await within(history).findByText(current.snapshotHash)).toBeVisible();
    expect(api.listBoundaries).toHaveBeenCalledWith(offering.offeringId, 1, expect.any(AbortSignal));
    expect(screen.queryByText(/Prepare successor/)).not.toBeInTheDocument();
  });

  it('links actual source proposals and published capabilities without implying current-boundary applicability', async () => {
    // Arrange
    mount();
    // Act
    const capabilities = await screen.findByRole('region', { name: 'Security capabilities' });
    await within(capabilities).findByText('Threat monitoring proposal');
    // Assert
    expect(within(capabilities).getByRole('link', { name: 'Review source proposal' }))
      .toHaveAttribute('href', `/workspaces/csp/authorizations/offerings/${offering.offeringId}/packages/package-1`);
    expect(within(capabilities).getByRole('link', { name: 'Open published capability' }))
      .toHaveAttribute('href', '/workspaces/csp/security-capabilities/capability-1');
    expect(capabilities).toHaveTextContent('Awaiting review');
    expect(capabilities).toHaveTextContent('Different boundary version');
    expect(capabilities).toHaveTextContent('Published audit collection');
  });

  it('distinguishes assigned hosting from Mission Owner association and capability adoption', async () => {
    // Arrange
    mount();
    // Act
    const missions = await screen.findByRole('region', { name: 'Mission systems' });
    await within(missions).findByText('Example mission');
    // Assert
    expect(missions).toHaveTextContent('Mission Owner association pending');
    expect(missions).toHaveTextContent('0 adopted capabilities');
    expect(missions).toHaveTextContent('Relationship review required');
    expect(within(missions).getByRole('link', { name: 'Manage hosting assignments' }))
      .toHaveAttribute('href', `/workspaces/csp/authorizations/offerings/${offering.offeringId}/inherited-coverage?task=missions`);
  });

  it('shows read failures explicitly rather than inventing zero linked capabilities or enabling a replacement boundary', async () => {
    // Arrange
    vi.mocked(api.getBoundary).mockRejectedValue(new Error('Current boundary is unavailable.'));
    vi.mocked(api.getBoundaryOverview).mockRejectedValue(new Error('Linked records are unavailable.'));
    mount();
    // Act
    await screen.findByText('Current boundary is unavailable.');
    // Assert
    expect(await screen.findByText('Linked records are unavailable.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Edit boundary' })).toBeDisabled();
    expect(screen.queryByText('No linked capability records.')).not.toBeInTheDocument();
    expect(screen.queryByText('No hosting assignments recorded.')).not.toBeInTheDocument();
  });

  it('keeps draft inputs on a stale-save rejection and lets the user cancel without another write', async () => {
    // Arrange
    vi.mocked(api.createBoundary).mockRejectedValue(new PackageImportError('Offering revision changed.', 409));
    mount();
    const edit = await screen.findByRole('button', { name: 'Edit boundary' });
    await waitFor(() => expect(edit).toBeEnabled());
    fireEvent.click(edit);
    fireEvent.change(screen.getByLabelText('Boundary name'), { target: { value: 'Keep my edits' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save boundary revision' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Offering revision changed.');
    expect(screen.getByLabelText('Boundary name')).toHaveValue('Keep my edits');
    // Act
    const cancel = screen.getByRole('button', { name: 'Cancel editing' });
    await waitFor(() => expect(cancel).toBeEnabled());
    fireEvent.click(cancel);
    // Assert
    expect(screen.queryByLabelText('Boundary name')).not.toBeInTheDocument();
    expect(api.createBoundary).toHaveBeenCalledOnce();
  });

  it('does not turn absent scope or responsibility data into universal coverage or completed work', async () => {
    // Arrange
    vi.mocked(api.getBoundary).mockResolvedValue({ ...current, includedScopes: [], providerResponsibilities: [], customerResponsibilities: [] });
    vi.mocked(api.getBoundaryOverview).mockResolvedValue({ ...overview,
      capabilities: { items: [], page: 1, pageSize: 10, total: 0, awaitingReview: 0, published: 0 },
      missionSystems: { items: [], page: 1, pageSize: 10, total: 0 },
    });
    mount();
    // Act
    await screen.findByText('No linked capability records.');
    // Assert
    expect(screen.getByText(/No tenant, subscription or resource identifiers recorded/)).toBeInTheDocument();
    expect(screen.getByText('No hosting assignments recorded.')).toBeInTheDocument();
    expect(screen.getByText('No CSP responsibilities recorded.')).toBeInTheDocument();
    expect(screen.getByText('No Mission Owner responsibilities recorded.')).toBeInTheDocument();
  });

  it('allows an initial boundary only when the offering has no current version', async () => {
    // Arrange
    vi.mocked(api.getOffering).mockResolvedValue({ ...offering, currentBoundaryRevisionId: null });
    mount();
    await screen.findByText(/No boundary recorded. Review the package/);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Edit boundary' }));
    // Assert
    expect(screen.getByLabelText('Boundary name')).toHaveValue('');
    expect(screen.getByRole('button', { name: 'Save boundary revision' })).toBeDisabled();
    expect(api.getBoundary).not.toHaveBeenCalled();
    expect(api.createBoundary).not.toHaveBeenCalled();
  });

  it('retains independent capability and mission paging rather than counting only the visible records', async () => {
    // Arrange
    vi.mocked(api.getBoundaryOverview).mockImplementation(async (_id, capabilityPage = 1, missionPage = 1) => ({
      ...overview, capabilities: { ...overview.capabilities, page: capabilityPage, total: 11 },
      missionSystems: { ...overview.missionSystems, page: missionPage, total: 12 },
    }));
    mount();
    const capabilities = await screen.findByRole('region', { name: 'Security capabilities' });
    await within(capabilities).findByText('11 total records · Page 1 of 2');
    // Act
    fireEvent.click(within(capabilities).getByRole('button', { name: 'Next' }));
    // Assert
    await waitFor(() => expect(api.getBoundaryOverview).toHaveBeenCalledWith(offering.offeringId, 2, 1, expect.any(AbortSignal)));
    await within(capabilities).findByText('11 total records · Page 2 of 2');
    // Act
    const missions = screen.getByRole('region', { name: 'Mission systems' });
    fireEvent.click(within(missions).getByRole('button', { name: 'Next' }));
    // Assert
    await waitFor(() => expect(api.getBoundaryOverview).toHaveBeenCalledWith(offering.offeringId, 2, 2, expect.any(AbortSignal)));
    expect(await within(missions).findByText('12 total records · Page 2 of 2')).toBeInTheDocument();
  });

  it('preserves associated mission and adoption states without requiring access to the private system name', async () => {
    // Arrange
    const mission = overview.missionSystems.items[0];
    if (!mission) throw new Error('The synthetic overview requires a hosting assignment.');
    vi.mocked(api.getBoundaryOverview).mockResolvedValue({ ...overview,
      missionSystems: { ...overview.missionSystems, items: [{
        ...mission, systemName: null, associated: true, adoptedCapabilityCount: 2,
        relationshipState: 'SeparateBoundaryConsumer',
      }] },
    });
    mount();
    // Act
    const missions = await screen.findByRole('region', { name: 'Mission systems' });
    // Assert
    expect(await within(missions).findByText(/Mission system associated/)).toHaveTextContent('2 adopted capabilities');
    expect(missions).toHaveTextContent('System ID: system-1');
    expect(missions).toHaveTextContent('Separate mission boundary consuming provider services');
    expect(within(missions).queryByRole('link', { name: 'Mission system' })).not.toBeInTheDocument();
  });
});
