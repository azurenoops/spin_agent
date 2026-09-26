import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { axe } from 'vitest-axe';
import MissionAssociationWizard from '../../features/provider-relationships/MissionAssociationWizard';
import * as api from '../../features/provider-relationships/api';
import { getSetupSystem, listSetupSystems } from '../../features/workspace-operations/api';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import { allocation, capability, adoption, relationship } from './fixtures';
import { MissionReviewRequiredError, prepareAdoption } from '../../features/provider-relationships/adoptionPreparation';

vi.mock('../../features/provider-relationships/api', async importOriginal => ({
  ProviderRelationshipError: (await importOriginal<typeof import('../../features/provider-relationships/api')>()).ProviderRelationshipError,
  listSystemHostingAllocations: vi.fn(), listApplicableProviderCapabilities: vi.fn(),
  listAllSystemHostingAllocations: vi.fn(),
  associateProviderRelationship: vi.fn(), proposeProviderCapabilityAdoption: vi.fn(),
}));
vi.mock('../../features/workspace-operations/api', () => ({ listSetupSystems: vi.fn(), getSetupSystem: vi.fn() }));
vi.mock('../../features/provider-relationships/adoptionPreparation', async importOriginal => ({
  ...await importOriginal<typeof import('../../features/provider-relationships/adoptionPreparation')>(),
  prepareAdoption: vi.fn(),
}));
const session = vi.hoisted(() => ({
  workspace: { kind: 'organization', tenantId: 'org-a', mode: 'ordinary' },
  systemAccess: { systemId: 'system-a', permissions: { canRead: true } },
}));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => session }));

function mount(scoped = true, entry = false, hostingOnly = false, environmentEntry = false) {
  return render(<MemoryRouter initialEntries={[hostingOnly || environmentEntry
    ? '/workspaces/organizations/org-a/systems/system-a/profile/EnvironmentAndDeployment/hosting' : scoped
    ? `/workspaces/organizations/org-a/systems/system-a/provider-relationships${entry ? '' : '/setup'}`
    : '/workspaces/organizations/org-a/provider-relationships/setup']}>
    <WorkspaceNavigationProvider workspace={{ kind: 'organization', tenantId: 'org-a' }}>
      <Routes>
        <Route path="/workspaces/organizations/org-a/systems/:id/profile/EnvironmentAndDeployment/hosting"
          element={<MissionAssociationWizard hostingOnly={hostingOnly} environmentEntry={environmentEntry} />} />
        <Route path="/workspaces/organizations/org-a/provider-relationships/setup" element={<MissionAssociationWizard />} />
        <Route path="/workspaces/organizations/org-a/systems/:id/provider-relationships" element={<MissionAssociationWizard />} />
        <Route path="/workspaces/organizations/org-a/systems/:id/provider-relationships/setup" element={<MissionAssociationWizard />} />
      </Routes>
    </WorkspaceNavigationProvider>
  </MemoryRouter>);
}

async function chooseHostingScope() {
  const button = await screen.findByRole('button', { name: 'Choose hosting scope' });
  await waitFor(() => expect(button).toBeEnabled());
  await act(async () => fireEvent.click(button));
}

async function review() {
  await chooseHostingScope();
  fireEvent.click(await screen.findByRole('radio', { name: /Harbor hosting/ }));
  fireEvent.click(screen.getByRole('button', { name: 'Choose capabilities' }));
  fireEvent.click(await screen.findByRole('checkbox', { name: /Security monitoring/ }));
  fireEvent.click(screen.getByRole('button', { name: 'Review responsibilities' }));
}

async function confirmStep() {
  await review();
  fireEvent.click(screen.getByRole('button', { name: 'Continue to confirmation' }));
}

async function confirmRefreshedSubscriptions() {
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Confirm subscriptions' })
    ?? screen.queryByRole('alert')
    ?? screen.queryByRole('heading', { name: /^(Associations|Hosting association) recorded$/ })).toBeTruthy());
  const button = screen.queryByRole('button', { name: 'Confirm subscriptions' });
  if (button) {
    await act(async () => {
      fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
      fireEvent.click(button);
    });
  }
}

it('guides Environment through provider, scope and capability selection to real duty confirmation', async () => {
  // Arrange
  mount(true, false, false, true);
  // Act
  fireEvent.click(await screen.findByRole('radio', { name: 'Harbor provider' }));
  fireEvent.click(screen.getByRole('button', { name: 'Choose hosting scope' }));
  fireEvent.click(await screen.findByRole('radio', { name: /Harbor hosting/ }));
  fireEvent.click(screen.getByRole('button', { name: 'Choose capabilities' }));
  fireEvent.click(await screen.findByRole('checkbox', { name: /Security monitoring/ }));
  fireEvent.click(screen.getByRole('button', { name: 'Review responsibilities' }));
  fireEvent.click(screen.getByRole('button', { name: 'Continue to confirmation' }));
  fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
  fireEvent.click(screen.getByRole('button', { name: 'Associate this allocation' }));
  await confirmRefreshedSubscriptions();
  // Assert
  expect(await screen.findByRole('link', { name: 'Confirm responsibilities: Security monitoring' }))
    .toHaveAttribute('href', '/workspaces/organizations/org-a/systems/system-a/security-capabilities/provider/capability-a?tab=coverage');
  expect(api.associateProviderRelationship).toHaveBeenCalledTimes(1);
  expect(api.proposeProviderCapabilityAdoption).toHaveBeenCalledTimes(1);
  expect(screen.queryByRole('button', { name: 'Choose a different system' })).not.toBeInTheDocument();
});

beforeEach(() => {
  vi.resetAllMocks();
  session.workspace.kind = 'organization';
  session.workspace.mode = 'ordinary';
  session.systemAccess.systemId = 'system-a';
  session.systemAccess.permissions.canRead = true;
  vi.mocked(listSetupSystems).mockResolvedValue({
    items: [{ systemId: 'system-a', name: 'Vanguard', acronym: 'VAN' }], nextCursor: null, totalCount: 1,
  });
  vi.mocked(getSetupSystem).mockResolvedValue({ systemId: 'system-a', name: 'Vanguard' });
  vi.mocked(api.listAllSystemHostingAllocations).mockResolvedValue([allocation]);
  vi.mocked(api.listSystemHostingAllocations).mockResolvedValue({
    items: [allocation], page: 1, pageSize: 25, total: 1,
  });
  vi.mocked(api.listApplicableProviderCapabilities).mockResolvedValue({
    items: [capability], page: 1, pageSize: 25, total: 1,
  });
  vi.mocked(api.associateProviderRelationship).mockResolvedValue(relationship);
  vi.mocked(api.proposeProviderCapabilityAdoption).mockResolvedValue(adoption);
  vi.mocked(prepareAdoption).mockImplementation(async (_systemId, item) => ({
    capability: { ...item, canProposeAdoption: true,
      outstandingDecisions: item.outstandingDecisions.filter(value => value !== 'MissionAssociationRequired') },
    body: {
      assignmentId: item.assignmentId, expectedAssignmentRevision: item.assignmentRevision,
      capabilityId: item.capabilityId, releaseId: item.releaseId,
      contextSnapshotHash: item.applicability.snapshotHash, applicabilityPreviewHash: item.applicabilityPreviewHash,
    },
  }));
});

describe('Mission Owner association task', () => {
  it('associates hosting from Environment without reading or subscribing to capabilities', async () => {
    // Arrange
    mount(true, false, true);
    // Act
    await chooseHostingScope();
    expect(screen.queryByRole('link', { name: 'Choose a different system' })).not.toBeInTheDocument();
    fireEvent.click(await screen.findByRole('radio', { name: /Harbor hosting/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Review hosting association' }));
    // Assert
    expect(screen.getByRole('heading', { name: 'Review hosting association' })).toBeVisible();
    expect(screen.queryByText('Select security capabilities', { exact: true })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Associate this allocation' })).toBeDisabled();
    expect(api.associateProviderRelationship).not.toHaveBeenCalled();
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm this hosting association/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Associate this allocation' }));
    // Assert
    await screen.findByRole('heading', { name: 'Hosting association recorded' });
    expect(api.associateProviderRelationship).toHaveBeenCalledExactlyOnceWith('system-a',
      { assignmentId: 'assignment-a', expectedAssignmentRevision: 3 }, expect.any(String));
    expect(api.listApplicableProviderCapabilities).not.toHaveBeenCalled();
    expect(prepareAdoption).not.toHaveBeenCalled();
    expect(api.proposeProviderCapabilityAdoption).not.toHaveBeenCalled();
    expect(screen.getByRole('link', { name: 'Back to Environment' })).toHaveAttribute(
      'href', '/workspaces/organizations/org-a/systems/system-a/profile/EnvironmentAndDeployment');
    expect(screen.getByRole('link', { name: 'Add security capabilities' })).toHaveAttribute(
      'href', '/workspaces/organizations/org-a/systems/system-a/security-capabilities/add');
  });

  it('retains already-associated hosting without a duplicate write or capability dependency', async () => {
    // Arrange
    vi.mocked(api.listSystemHostingAllocations).mockResolvedValue({
      items: [{ ...allocation, relationshipId: 'existing', canAssociate: false }], page: 1, pageSize: 25, total: 1,
    });
    mount(true, false, true);
    // Act
    await chooseHostingScope();
    fireEvent.click(await screen.findByRole('radio', { name: /Harbor hosting/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Review hosting association' }));
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm this hosting association/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Keep this hosting association' }));
    // Assert
    await screen.findByRole('heading', { name: 'Hosting association recorded' });
    expect(screen.getByText('Existing hosting relationship retained.')).toBeVisible();
    expect(api.associateProviderRelationship).not.toHaveBeenCalled();
    expect(api.listApplicableProviderCapabilities).not.toHaveBeenCalled();
  });

  it('leaves organization-only systems usable when no hosting is allocated', async () => {
    // Arrange
    vi.mocked(api.listSystemHostingAllocations).mockResolvedValue({ items: [], page: 1, pageSize: 25, total: 0 });
    mount(true, false, true);
    // Act
    await chooseHostingScope();
    // Assert
    await screen.findByText(/No existing hosting allocations/);
    expect(screen.getByText(/You can use organization capabilities without a provider/)).toBeVisible();
    expect(screen.getByRole('button', { name: 'Review hosting association' })).toBeDisabled();
    expect(api.listApplicableProviderCapabilities).not.toHaveBeenCalled();
    expect(api.associateProviderRelationship).not.toHaveBeenCalled();
  });

  it.each(['provider', 'support', 'mismatch', 'denied'])('blocks hosting before reads in %s context', async context => {
    // Arrange
    if (context === 'provider') session.workspace.kind = 'csp';
    if (context === 'support') session.workspace.mode = 'support';
    if (context === 'mismatch') session.systemAccess.systemId = 'system-b';
    if (context === 'denied') session.systemAccess.permissions.canRead = false;
    // Act
    mount(true, false, true);
    // Assert
    expect(screen.getByRole('alert')).toBeVisible();
    expect(getSetupSystem).not.toHaveBeenCalled();
    expect(api.listSystemHostingAllocations).not.toHaveBeenCalled();
    expect(api.associateProviderRelationship).not.toHaveBeenCalled();
  });

  it('retries only the hosting write with its original revision and idempotency key', async () => {
    // Arrange
    vi.mocked(api.associateProviderRelationship).mockRejectedValueOnce(new api.ProviderRelationshipError(
      'Hosting service unavailable', 'SERVICE_UNAVAILABLE', 503));
    mount(true, false, true);
    await chooseHostingScope();
    fireEvent.click(await screen.findByRole('radio', { name: /Harbor hosting/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Review hosting association' }));
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm this hosting association/ }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Associate this allocation' }));
    await screen.findByText('Hosting service unavailable');
    const first = vi.mocked(api.associateProviderRelationship).mock.calls[0];
    fireEvent.click(screen.getByRole('button', { name: 'Retry incomplete operations' }));
    // Assert
    await screen.findByRole('heading', { name: 'Hosting association recorded' });
    expect(api.associateProviderRelationship).toHaveBeenCalledTimes(2);
    expect(vi.mocked(api.associateProviderRelationship).mock.calls[1]).toEqual(first);
    expect(api.listApplicableProviderCapabilities).not.toHaveBeenCalled();
    expect(api.proposeProviderCapabilityAdoption).not.toHaveBeenCalled();
  });

  it('offers an explicit scoped task CTA from the existing provider relationships entry route', async () => {
    // Arrange
    mount(true, true);
    const entry = screen.getByRole('link', { name: 'Start guided association' });
    // Assert
    expect(entry).toHaveAttribute('href', '/workspaces/organizations/org-a/systems/system-a/provider-relationships/setup');
    expect(api.listSystemHostingAllocations).not.toHaveBeenCalled();
    // Act
    fireEvent.click(entry);
    // Assert
    await waitFor(() => expect(screen.getByRole('button', { name: 'Choose hosting scope' })).toBeEnabled());
    expect(screen.getByText('Vanguard')).toBeVisible();
    expect(api.associateProviderRelationship).not.toHaveBeenCalled();
    expect(api.proposeProviderCapabilityAdoption).not.toHaveBeenCalled();
  });

  it('offers named authorized systems and enters the canonical system route before hosting reads', async () => {
    // Arrange
    mount(false);
    // Act
    await screen.findByRole('option', { name: 'Vanguard · VAN' });
    fireEvent.change(await screen.findByRole('combobox', { name: 'System' }), { target: { value: 'system-a' } });
    await chooseHostingScope();
    // Assert
    expect(await screen.findByRole('radio', { name: /Harbor hosting/ })).toBeVisible();
    expect(api.listSystemHostingAllocations).toHaveBeenCalledWith('system-a', 1, expect.any(AbortSignal));
    expect(api.associateProviderRelationship).not.toHaveBeenCalled();
  });

  it('reviews sources and duties without writes, duty acceptance, or coverage assertion', async () => {
    // Arrange
    mount();
    // Act
    await review();
    // Assert
    expect(screen.getByText('Provider: operate monitoring')).toBeVisible();
    expect(screen.getByText('Customer: investigate alerts')).toBeVisible();
    expect(screen.getByText(/Harbor SSP/)).toBeVisible();
    expect(screen.getByText(/does not accept control duties/i)).toBeVisible();
    expect(api.associateProviderRelationship).not.toHaveBeenCalled();
    expect(api.proposeProviderCapabilityAdoption).not.toHaveBeenCalled();
  });

  it('requires an explicit second confirmation of refreshed duties after saving a new association', async () => {
    // Arrange
    mount();
    await confirmStep();
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Associate this allocation' }));
    // Assert
    expect(await screen.findByRole('heading', { name: 'Review refreshed capability subscriptions' })).toBeVisible();
    expect(screen.getByText('Customer: investigate alerts')).toBeVisible();
    expect(api.associateProviderRelationship).toHaveBeenCalledTimes(1);
    expect(api.proposeProviderCapabilityAdoption).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Confirm subscriptions' })).toBeDisabled();
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm subscriptions' }));
    // Assert
    expect(await screen.findByRole('heading', { name: 'Associations recorded' })).toBeVisible();
    expect(api.proposeProviderCapabilityAdoption).toHaveBeenCalledTimes(1);
  });

  it('retains the separate subscription confirmation requirement when the post-association read must be retried', async () => {
    // Arrange
    vi.mocked(prepareAdoption).mockRejectedValueOnce(new Error('Exact capability read unavailable.'));
    mount();
    await confirmStep();
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Associate this allocation' }));
    await screen.findByRole('alert');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry incomplete operations' }));
    // Assert
    expect(await screen.findByRole('heading', { name: 'Review refreshed capability subscriptions' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Confirm subscriptions' })).toBeDisabled();
    expect(api.associateProviderRelationship).toHaveBeenCalledTimes(1);
    expect(api.proposeProviderCapabilityAdoption).not.toHaveBeenCalled();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Change capability selection' }));
    // Assert
    expect(await screen.findByRole('checkbox', { name: /Security monitoring/ })).toBeChecked();
    expect(screen.getByRole('button', { name: 'Review responsibilities' })).toBeEnabled();
    expect(api.proposeProviderCapabilityAdoption).not.toHaveBeenCalled();
  });

  it('requires explicit final confirmation and reports canonical adoption without claiming duties were accepted', async () => {
    // Arrange
    mount();
    await confirmStep();
    // Act
    expect(screen.getByRole('button', { name: 'Associate this allocation' })).toBeDisabled();
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Associate this allocation' }));
    await confirmRefreshedSubscriptions();
    // Assert
    expect(await screen.findByRole('heading', { name: 'Associations recorded' })).toBeVisible();
    expect(api.associateProviderRelationship).toHaveBeenCalledWith('system-a', {
      assignmentId: allocation.assignmentId, expectedAssignmentRevision: allocation.revision,
    }, expect.any(String));
    expect(api.proposeProviderCapabilityAdoption).toHaveBeenCalledWith('system-a', {
      assignmentId: allocation.assignmentId, expectedAssignmentRevision: allocation.revision,
      capabilityId: capability.capabilityId, releaseId: capability.releaseId,
      contextSnapshotHash: capability.applicability.snapshotHash, applicabilityPreviewHash: capability.applicabilityPreviewHash,
    }, expect.any(String));
    expect(screen.getByRole('link', { name: 'Review subscription responsibilities' })).toHaveAttribute(
      'href', '/workspaces/organizations/org-a/systems/system-a/inheritance/subscriptions');
  });

  it('blocks failed allocation reads and preserves an honest retry', async () => {
    // Arrange
    vi.mocked(api.listSystemHostingAllocations).mockRejectedValueOnce(new Error('Hosting service unavailable.'));
    mount();
    // Act
    await chooseHostingScope();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Hosting service unavailable.');
    expect(screen.getByRole('button', { name: 'Choose capabilities' })).toBeDisabled();
    expect(screen.queryByText(/No existing hosting allocations/)).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry hosting scopes' }));
    // Assert
    expect(await screen.findByRole('radio', { name: /Harbor hosting/ })).toBeVisible();
  });

  it('retains partial success and retries only the failed adoption with the original key', async () => {
    // Arrange
    vi.mocked(api.proposeProviderCapabilityAdoption).mockRejectedValueOnce(new Error('Network response lost.'));
    mount();
    await confirmStep();
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Associate this allocation' }));
    await confirmRefreshedSubscriptions();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Network response lost.');
    expect(screen.getByText(/Hosting relationship recorded/)).toBeVisible();
    expect(screen.queryByRole('heading', { name: 'Associations recorded' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Review current choices' })).not.toBeInTheDocument();
    const originalKey = vi.mocked(api.proposeProviderCapabilityAdoption).mock.calls[0]![2];
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry incomplete operations' }));
    // Assert
    expect(await screen.findByRole('heading', { name: 'Associations recorded' })).toBeVisible();
    expect(api.associateProviderRelationship).toHaveBeenCalledTimes(1);
    expect(vi.mocked(api.proposeProviderCapabilityAdoption).mock.calls[1]![2]).toBe(originalKey);
    expect(prepareAdoption).toHaveBeenCalledTimes(1);
  });

  it('blocks provider-only context instead of granting customer access', async () => {
    // Arrange
    session.workspace.kind = 'csp';
    // Act
    mount(false);
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('organization workspace');
    await waitFor(() => expect(listSetupSystems).not.toHaveBeenCalled());
  });

  it('keeps failed capability reads blocked and preserves the selected hosting allocation', async () => {
    // Arrange
    vi.mocked(api.listApplicableProviderCapabilities).mockRejectedValueOnce(new Error('Capability source unavailable.'));
    mount();
    await chooseHostingScope();
    fireEvent.click(await screen.findByRole('radio', { name: /Harbor hosting/ }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Choose capabilities' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Capability source unavailable.');
    expect(screen.getByRole('button', { name: 'Review responsibilities' })).toBeDisabled();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry capabilities' }));
    // Assert
    expect(await screen.findByRole('checkbox', { name: /Security monitoring/ })).toBeVisible();
    expect(api.associateProviderRelationship).not.toHaveBeenCalled();
  });

  it('does not enable allocation selection using a local MissionOwner persona', async () => {
    // Arrange
    vi.mocked(api.listSystemHostingAllocations).mockResolvedValue({
      items: [{ ...allocation, canAssociate: false }], page: 1, pageSize: 25, total: 1,
    });
    mount();
    // Act
    await chooseHostingScope();
    // Assert
    expect(await screen.findByRole('radio', { name: /Harbor hosting/ })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Choose capabilities' })).toBeDisabled();
  });

  it('retains an existing relationship and adopts without resubmitting the association', async () => {
    // Arrange
    vi.mocked(api.listSystemHostingAllocations).mockResolvedValue({
      items: [{ ...allocation, relationshipId: 'existing-relationship', canAssociate: false }],
      page: 1, pageSize: 25, total: 1,
    });
    mount();
    await confirmStep();
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm subscriptions' }));
    await confirmRefreshedSubscriptions();
    // Assert
    expect(await screen.findByRole('heading', { name: 'Associations recorded' })).toBeVisible();
    expect(screen.getByText('Existing hosting relationship retained.')).toBeVisible();
    expect(api.associateProviderRelationship).not.toHaveBeenCalled();
    expect(api.proposeProviderCapabilityAdoption).toHaveBeenCalledTimes(1);
  });

  it('allows explicit hosting-only confirmation without creating a subscription or request for a Mission Owner', async () => {
    // Arrange
    vi.mocked(api.listApplicableProviderCapabilities).mockResolvedValue({
      items: [{ ...capability, canProposeAdoption: false, canConfirmResponsibilities: false }],
      page: 1, pageSize: 25, total: 1,
    });
    mount();
    await chooseHostingScope();
    fireEvent.click(await screen.findByRole('radio', { name: /Harbor hosting/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Choose capabilities' }));
    expect(await screen.findByRole('checkbox', { name: /Security monitoring/ })).toBeDisabled();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Continue with hosting association only' }));
    fireEvent.click(screen.getByRole('button', { name: 'Continue to confirmation' }));
    // Assert
    expect(api.associateProviderRelationship).not.toHaveBeenCalled();
    expect(screen.getByText(/No capability subscriptions are included/)).toBeVisible();
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Associate this allocation' }));
    await confirmRefreshedSubscriptions();
    // Assert
    expect(await screen.findByRole('heading', { name: 'Hosting association recorded' })).toBeVisible();
    expect(screen.getByText(/No capability subscription or pending request was created/)).toBeVisible();
    expect(await screen.findByText('Customer: investigate alerts')).toBeVisible();
    expect(screen.getByRole('heading', { name: 'Published capabilities and duties — read-only' })).toBeVisible();
    expect(api.associateProviderRelationship).toHaveBeenCalledTimes(1);
    expect(prepareAdoption).not.toHaveBeenCalled();
    expect(api.proposeProviderCapabilityAdoption).not.toHaveBeenCalled();
  });

  it('preserves hosting-only success when the fresh read-only capability review is unavailable', async () => {
    // Arrange
    vi.mocked(api.listApplicableProviderCapabilities)
      .mockResolvedValueOnce({ items: [{ ...capability, canProposeAdoption: false, canConfirmResponsibilities: false }],
        page: 1, pageSize: 25, total: 1 })
      .mockRejectedValueOnce(new Error('Published review unavailable.'));
    mount();
    await chooseHostingScope();
    fireEvent.click(await screen.findByRole('radio', { name: /Harbor hosting/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Choose capabilities' }));
    await screen.findByRole('checkbox', { name: /Security monitoring/ });
    fireEvent.click(screen.getByRole('button', { name: 'Continue with hosting association only' }));
    fireEvent.click(screen.getByRole('button', { name: 'Continue to confirmation' }));
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Associate this allocation' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Published review unavailable.');
    expect(screen.getByRole('heading', { name: 'Hosting association recorded' })).toBeVisible();
    expect(screen.queryByText(/No published capabilities are currently returned/)).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry published capability review' }));
    // Assert
    expect(await screen.findByText('Customer: investigate alerts')).toBeVisible();
    expect(api.associateProviderRelationship).toHaveBeenCalledTimes(1);
    expect(api.proposeProviderCapabilityAdoption).not.toHaveBeenCalled();
  });

  it('refreshes hosting choices after definitive stale association rejection and requires a new confirmed intent', async () => {
    // Arrange
    vi.mocked(api.associateProviderRelationship).mockRejectedValueOnce(
      new api.ProviderRelationshipError('Hosting revision changed.', 'AUTHORIZATION_CONTEXT_STALE', 409));
    mount();
    await confirmStep();
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Associate this allocation' }));
    await confirmRefreshedSubscriptions();
    await screen.findByRole('alert');
    const oldKey = vi.mocked(api.associateProviderRelationship).mock.calls[0]![2];
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Review current choices' }));
    await screen.findByRole('radio', { name: /Harbor hosting/ });
    fireEvent.click(screen.getByRole('button', { name: 'Choose capabilities' }));
    await screen.findByRole('checkbox', { name: /Security monitoring/ });
    fireEvent.click(screen.getByRole('button', { name: 'Review responsibilities' }));
    fireEvent.click(screen.getByRole('button', { name: 'Continue to confirmation' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Associate this allocation' })).toBeDisabled();
    expect(api.associateProviderRelationship).toHaveBeenCalledTimes(1);
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Associate this allocation' }));
    await confirmRefreshedSubscriptions();
    // Assert
    expect(await screen.findByRole('heading', { name: 'Associations recorded' })).toBeVisible();
    expect(vi.mocked(api.associateProviderRelationship).mock.calls[1]![2]).not.toBe(oldKey);
  });

  it.each(['AUTHORIZATION_CONTEXT_STALE', 'RESPONSIBILITY_CONTEXT_STALE'])(
    'retains association success and confirms a new adoption intent only after reviewing %s rejection', async code => {
      // Arrange
      vi.mocked(api.proposeProviderCapabilityAdoption).mockRejectedValueOnce(
        new api.ProviderRelationshipError('Published context changed.', code));
      mount();
      await confirmStep();
      fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
      fireEvent.click(screen.getByRole('button', { name: 'Associate this allocation' }));
      await confirmRefreshedSubscriptions();
      await screen.findByRole('alert');
      const previous = vi.mocked(api.proposeProviderCapabilityAdoption).mock.calls[0]!;
      const refreshed = { ...capability, applicabilityPreviewHash: 'new-preview' };
      vi.mocked(api.listApplicableProviderCapabilities).mockResolvedValue({
        items: [refreshed], page: 1, pageSize: 25, total: 1,
      });
      // Act
      fireEvent.click(screen.getByRole('button', { name: 'Review current choices' }));
      const choice = await screen.findByRole('checkbox', { name: /Security monitoring/ });
      fireEvent.click(choice);
      fireEvent.click(choice);
      fireEvent.click(screen.getByRole('button', { name: 'Review responsibilities' }));
      fireEvent.click(screen.getByRole('button', { name: 'Continue to confirmation' }));
      // Assert
      expect(screen.getByRole('button', { name: 'Confirm subscriptions' })).toBeDisabled();
      expect(api.proposeProviderCapabilityAdoption).toHaveBeenCalledTimes(1);
      // Act
      fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
      fireEvent.click(screen.getByRole('button', { name: 'Confirm subscriptions' }));
      await confirmRefreshedSubscriptions();
      // Assert
      expect(await screen.findByRole('heading', { name: 'Associations recorded' })).toBeVisible();
      expect(api.associateProviderRelationship).toHaveBeenCalledTimes(1);
      const next = vi.mocked(api.proposeProviderCapabilityAdoption).mock.calls[1]!;
      expect(next[2]).not.toBe(previous[2]);
      expect(next[1].applicabilityPreviewHash).toBe('new-preview');
    });

  it('never resets an uncertain 503 request or mistakes denied retries for unsaved work', async () => {
    // Arrange
    vi.mocked(api.proposeProviderCapabilityAdoption)
      .mockRejectedValueOnce(new api.ProviderRelationshipError('Projection unavailable.', 'PROVIDER_PROJECTION_UNAVAILABLE', 503))
      .mockRejectedValueOnce(new api.ProviderRelationshipError('Access was revoked.', 'PROVIDER_ACCESS_DENIED', 403));
    mount();
    await confirmStep();
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Associate this allocation' }));
    await confirmRefreshedSubscriptions();
    await screen.findByRole('alert');
    const original = vi.mocked(api.proposeProviderCapabilityAdoption).mock.calls[0]!;
    expect(screen.queryByRole('button', { name: 'Review current choices' })).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry incomplete operations' }));
    // Assert
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Access was revoked.'));
    expect(screen.getByRole('status')).toHaveTextContent('Security monitoring: not yet confirmed by the server');
    expect(screen.queryByRole('button', { name: 'Review current choices' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Associations recorded' })).not.toBeInTheDocument();
    expect(vi.mocked(api.proposeProviderCapabilityAdoption).mock.calls[1]).toEqual(original);
  });

  it('retains selections on Back and never selects an inapplicable release', async () => {
    // Arrange
    vi.mocked(api.listApplicableProviderCapabilities).mockResolvedValue({
      items: [capability, { ...capability, capabilityId: 'blocked', capabilityName: 'Blocked capability', canProposeAdoption: false }],
      page: 1, pageSize: 25, total: 2,
    });
    mount();
    await review();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    // Assert
    expect(await screen.findByRole('checkbox', { name: /Security monitoring/ })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: /Blocked capability/ })).toBeDisabled();
  });

  it('retries an uncertain relationship response with the same key before any adoption', async () => {
    // Arrange
    vi.mocked(api.associateProviderRelationship).mockRejectedValueOnce(new Error('Response interrupted.'));
    mount();
    await confirmStep();
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Associate this allocation' }));
    await confirmRefreshedSubscriptions();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Response interrupted.');
    expect(api.proposeProviderCapabilityAdoption).not.toHaveBeenCalled();
    const first = vi.mocked(api.associateProviderRelationship).mock.calls[0];
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry incomplete operations' }));
    await confirmRefreshedSubscriptions();
    // Assert
    expect(await screen.findByRole('heading', { name: 'Associations recorded' })).toBeVisible();
    expect(vi.mocked(api.associateProviderRelationship).mock.calls[1]).toEqual(first);
  });

  it('pages capability choices while retaining earlier named selections', async () => {
    // Arrange
    vi.mocked(api.listApplicableProviderCapabilities)
      .mockResolvedValueOnce({ items: [capability], page: 1, pageSize: 1, total: 2 })
      .mockResolvedValueOnce({ items: [{ ...capability, capabilityId: 'capability-b', releaseId: 'release-b', capabilityName: 'Identity management' }], page: 2, pageSize: 1, total: 2 });
    mount();
    await chooseHostingScope();
    fireEvent.click(await screen.findByRole('radio', { name: /Harbor hosting/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Choose capabilities' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: /Security monitoring/ }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'More capabilities' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: /Identity management/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Review responsibilities' }));
    // Assert
    expect(screen.getByRole('heading', { name: 'Security monitoring' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'Identity management' })).toBeVisible();
    expect(api.proposeProviderCapabilityAdoption).not.toHaveBeenCalled();
  });

  it('does not lose completed capability results when a later capability fails', async () => {
    // Arrange
    vi.mocked(api.listApplicableProviderCapabilities).mockResolvedValue({
      items: [capability, { ...capability, capabilityId: 'capability-b', releaseId: 'release-b', capabilityName: 'Identity management' }],
      page: 1, pageSize: 25, total: 2,
    });
    vi.mocked(api.proposeProviderCapabilityAdoption).mockResolvedValueOnce(adoption).mockRejectedValueOnce(new Error('Second capability unavailable.'));
    mount();
    await chooseHostingScope();
    fireEvent.click(await screen.findByRole('radio', { name: /Harbor hosting/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Choose capabilities' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: /Security monitoring/ }));
    fireEvent.click(screen.getByRole('checkbox', { name: /Identity management/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Review responsibilities' }));
    fireEvent.click(screen.getByRole('button', { name: 'Continue to confirmation' }));
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Associate this allocation' }));
    await confirmRefreshedSubscriptions();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Second capability unavailable.');
    expect(screen.getByText('1 of 2 capability operations confirmed.')).toBeVisible();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry incomplete operations' }));
    // Assert
    expect(await screen.findByRole('heading', { name: 'Associations recorded' })).toBeVisible();
    expect(vi.mocked(api.proposeProviderCapabilityAdoption).mock.calls.map(call => call[1].capabilityId))
      .toEqual(['capability-a', 'capability-b', 'capability-b']);
  });

  it('exposes accessible steps, names, field groups and confirmation controls', async () => {
    // Arrange
    const { container } = mount();
    await review();
    // Act
    // jsdom has no canvas; the Playwright check covers rendered color contrast.
    const options = { rules: { 'color-contrast': { enabled: false } } };
    const reviewed = await axe(container, options);
    fireEvent.click(screen.getByRole('button', { name: 'Continue to confirmation' }));
    const confirmation = await axe(container, options);
    // Assert
    expect(reviewed.violations).toEqual([]);
    expect(confirmation.violations).toEqual([]);
    expect(screen.getByLabelText('Association progress').querySelector('[aria-current="step"]')).toHaveTextContent('Confirm associations');
  });

  it('does not begin another write after leaving the authorized system workspace', async () => {
    // Arrange
    let resolve!: (value: typeof relationship) => void;
    vi.mocked(api.associateProviderRelationship).mockImplementation(() => new Promise(done => { resolve = done; }));
    const view = mount();
    await confirmStep();
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Associate this allocation' }));
    // Act
    view.unmount();
    await act(async () => resolve(relationship));
    // Assert
    expect(api.proposeProviderCapabilityAdoption).not.toHaveBeenCalled();
  });

  it('lets a Mission Owner read source duties but never bypasses canonical adoption permissions', async () => {
    // Arrange
    vi.mocked(api.listApplicableProviderCapabilities).mockResolvedValue({
      items: [{ ...capability, canProposeAdoption: false }], page: 1, pageSize: 25, total: 1,
    });
    mount();
    await chooseHostingScope();
    fireEvent.click(await screen.findByRole('radio', { name: /Harbor hosting/ }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Choose capabilities' }));
    fireEvent.click(await screen.findByText('View Security monitoring responsibilities'));
    // Assert
    expect(screen.getByText('Customer: investigate alerts')).toBeVisible();
    expect(screen.getByRole('checkbox', { name: /Security monitoring/ })).toBeDisabled();
    expect(screen.getByText(/Mission Owner alone does not grant capability subscription permission/)).toBeVisible();
    expect(screen.getByRole('button', { name: 'Review responsibilities' })).toBeDisabled();
    expect(api.proposeProviderCapabilityAdoption).not.toHaveBeenCalled();
  });

  it('requires renewed review when the server applicability preview changes on Back', async () => {
    // Arrange
    vi.mocked(api.listApplicableProviderCapabilities)
      .mockResolvedValueOnce({ items: [capability], page: 1, pageSize: 25, total: 1 })
      .mockResolvedValueOnce({ items: [{ ...capability, applicabilityPreviewHash: 'new-preview' }], page: 1, pageSize: 25, total: 1 });
    mount();
    await review();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('selected capability changed');
    expect(screen.getByRole('button', { name: 'Review responsibilities' })).toBeDisabled();
    expect(screen.getByRole('checkbox', { name: /Security monitoring/ })).toBeChecked();
  });

  it('plans an applicable release without pre-confirm writes when only association is missing', async () => {
    // Arrange
    vi.mocked(api.listApplicableProviderCapabilities).mockResolvedValue({
      items: [{ ...capability, canProposeAdoption: false, canConfirmResponsibilities: true,
        outstandingDecisions: [...capability.outstandingDecisions, 'MissionAssociationRequired'] }],
      page: 1, pageSize: 25, total: 1,
    });
    mount();
    await confirmStep();
    expect(prepareAdoption).not.toHaveBeenCalled();
    expect(api.associateProviderRelationship).not.toHaveBeenCalled();
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Associate this allocation' }));
    await confirmRefreshedSubscriptions();
    // Assert
    expect(await screen.findByRole('heading', { name: 'Associations recorded' })).toBeVisible();
    expect(vi.mocked(api.associateProviderRelationship).mock.invocationCallOrder[0])
      .toBeLessThan(vi.mocked(prepareAdoption).mock.invocationCallOrder[0]!);
    expect(vi.mocked(prepareAdoption).mock.invocationCallOrder[0])
      .toBeLessThan(vi.mocked(api.proposeProviderCapabilityAdoption).mock.invocationCallOrder[0]!);
  });

  it('requires explicit review again after a source change while retaining the saved association', async () => {
    // Arrange
    vi.mocked(prepareAdoption).mockRejectedValueOnce(new MissionReviewRequiredError());
    mount();
    await confirmStep();
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Associate this allocation' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('changed');
    const updated = { ...capability, customerDuties: ['New customer work'], applicabilityPreviewHash: 'fresh-preview' };
    vi.mocked(api.listApplicableProviderCapabilities).mockResolvedValue({ items: [updated], page: 1, pageSize: 25, total: 1 });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Review current choices' }));
    const choice = await screen.findByRole('checkbox', { name: /Security monitoring/ });
    fireEvent.click(choice);
    fireEvent.click(choice);
    fireEvent.click(screen.getByRole('button', { name: 'Review responsibilities' }));
    // Assert
    expect(screen.getByText('New customer work')).toBeVisible();
    expect(api.proposeProviderCapabilityAdoption).not.toHaveBeenCalled();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Continue to confirmation' }));
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm subscriptions' }));
    // Assert
    expect(await screen.findByRole('heading', { name: 'Associations recorded' })).toBeVisible();
    expect(api.associateProviderRelationship).toHaveBeenCalledTimes(1);
    expect(api.proposeProviderCapabilityAdoption).toHaveBeenCalledTimes(1);
    expect(vi.mocked(api.proposeProviderCapabilityAdoption).mock.calls[0]?.[1].applicabilityPreviewHash).toBe('fresh-preview');
  });

  it('does not adopt after leaving the workspace during the exact source recheck', async () => {
    // Arrange
    let complete!: (value: Awaited<ReturnType<typeof prepareAdoption>>) => void;
    vi.mocked(prepareAdoption).mockReturnValue(new Promise(resolve => { complete = resolve; }));
    const view = mount();
    await confirmStep();
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Associate this allocation' }));
    await waitFor(() => expect(prepareAdoption).toHaveBeenCalledTimes(1));
    // Act
    view.unmount();
    await act(async () => complete({
      capability,
      body: {
        assignmentId: capability.assignmentId, expectedAssignmentRevision: capability.assignmentRevision,
        capabilityId: capability.capabilityId, releaseId: capability.releaseId,
        contextSnapshotHash: capability.applicability.snapshotHash, applicabilityPreviewHash: 'fresh-preview',
      },
    }));
    // Assert
    expect(api.proposeProviderCapabilityAdoption).not.toHaveBeenCalled();
  });

  it('retains a completed capability during rereview without permitting it to be adopted twice', async () => {
    // Arrange
    const second = { ...capability, capabilityId: 'capability-b', releaseId: 'release-b', capabilityName: 'Identity management' };
    vi.mocked(api.listApplicableProviderCapabilities).mockResolvedValue({
      items: [capability, second], page: 1, pageSize: 25, total: 2,
    });
    vi.mocked(api.proposeProviderCapabilityAdoption).mockResolvedValueOnce(adoption)
      .mockRejectedValueOnce(new api.ProviderRelationshipError('Second capability changed.', 'AUTHORIZATION_CONTEXT_STALE', 409));
    mount();
    await chooseHostingScope();
    fireEvent.click(await screen.findByRole('radio', { name: /Harbor hosting/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Choose capabilities' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: /Security monitoring/ }));
    fireEvent.click(screen.getByRole('checkbox', { name: /Identity management/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Review responsibilities' }));
    fireEvent.click(screen.getByRole('button', { name: 'Continue to confirmation' }));
    fireEvent.click(screen.getByRole('checkbox', { name: /I confirm these associations/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Associate this allocation' }));
    await confirmRefreshedSubscriptions();
    await screen.findByRole('alert');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Review current choices' }));
    // Assert
    expect(await screen.findByRole('checkbox', { name: /Security monitoring/ })).toBeDisabled();
    expect(screen.getByRole('checkbox', { name: /Security monitoring/ })).not.toBeChecked();
    expect(screen.getByRole('checkbox', { name: /Identity management/ })).toBeChecked();
    expect(screen.getByRole('status')).toHaveTextContent('Previously recorded capabilities: Security monitoring');
    expect(screen.queryByRole('button', { name: 'Back' })).not.toBeInTheDocument();
    expect(api.proposeProviderCapabilityAdoption).toHaveBeenCalledTimes(2);
  });
});
