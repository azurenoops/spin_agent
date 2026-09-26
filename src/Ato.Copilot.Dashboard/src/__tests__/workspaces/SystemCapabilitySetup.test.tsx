import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import SystemCapabilitySetup from '../../features/workspace-operations/system-capabilities/SystemCapabilitySetup';
import { systemCapabilityAccess, systemCapabilityDetailFixture, systemCapabilityItem, systemSetupOperationFixture } from '../fixtures/systemCapabilityDetailSetup';

const api = vi.hoisted(() => ({
  listSystemCapabilities: vi.fn(), getSystemCapability: vi.fn(), prepareSystemCapabilitySetup: vi.fn(),
  getSystemCapabilityOperation: vi.fn(), completeSystemCapabilityOperation: vi.fn(),
}));
vi.mock('../../features/workspace-operations/system-capabilities/systemCapabilityApi', () => api);
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => ({ workspace: { displayName: 'Example organization', kind: 'organization', tenantId: 'tenant-a', mode: 'ordinary' },
    identity: { directoryTenantId: 'directory-a', oid: 'actor-a' } }),
}));
vi.mock('../../components/layout/SystemLayout', () => ({ useSystemContext: () => ({ detail: { name: 'Selected system', systemId: 'system-a' } }) }));

function Location() { return <output aria-label="Current route">{useLocation().search}</output>; }
function mount(search = '') {
  return render(<MemoryRouter initialEntries={[`/systems/system-a/security-capabilities/add${search}`]}>
    <SystemCapabilitySetup tenantId="tenant-a" systemId="system-a" /><Location />
  </MemoryRouter>);
}
const page = (source: 'local' | 'provider' = 'provider', applied = false) => ({
  items: [systemCapabilityItem(source, source === 'provider' ? 'capability-a' : 'local-capability-a', applied)],
  page: 1, pageSize: 25, total: 1, scope: 'available', grouping: 'capability',
  permissions: { ...systemCapabilityAccess }, boundaries: [{ id: 'boundary-a', name: 'Workload boundary' }],
});

describe('Three-step selected-system capability setup', () => {
  beforeEach(() => {
    vi.clearAllMocks(); sessionStorage.clear();
    api.listSystemCapabilities.mockImplementation((_tenant, _system, query) => Promise.resolve(page(query.source === 'local' ? 'local' : 'provider')));
    api.prepareSystemCapabilitySetup.mockImplementation((_tenant, _system, body) => Promise.resolve({
      operation: { ...systemSetupOperationFixture(), idempotencyKey: body.idempotencyKey, selections: body.selections }, existing: false,
    }));
    api.getSystemCapabilityOperation.mockResolvedValue(systemSetupOperationFixture());
    const requested = systemCapabilityDetailFixture();
    requested.item.isApplied = false;
    api.getSystemCapability.mockResolvedValue(requested);
    api.completeSystemCapabilityOperation.mockResolvedValue(systemSetupOperationFixture('Setup', 'Completed'));
  });

  it('uses separate available scope and disables already applied records', async () => {
    // Arrange
    api.listSystemCapabilities.mockResolvedValue(page('provider', true));
    // Act
    mount();
    // Assert
    expect(await screen.findByRole('checkbox', { name: /Select Security monitoring/i })).toBeDisabled();
    expect(screen.getByText('Already applied')).toBeVisible();
    expect(api.listSystemCapabilities.mock.calls[0]?.slice(0, 2)).toEqual(['tenant-a', 'system-a']);
    expect(api.listSystemCapabilities.mock.calls[0]?.[2]).toMatchObject({ scope: 'available', grouping: 'capability' });
  });

  it('prepares exact source revisions and component placements only after applicability review', async () => {
    // Arrange
    mount();
    fireEvent.click(await screen.findByRole('checkbox', { name: /Select Security monitoring/i }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Continue to applicability' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Place Provider collector in Workload boundary' }));
    fireEvent.click(screen.getByRole('button', { name: 'Continue to review' }));
    // Assert
    expect(await screen.findByRole('heading', { name: 'Review changes before adding' })).toBeVisible();
    expect(api.prepareSystemCapabilitySetup).toHaveBeenCalledOnce();
    expect(api.prepareSystemCapabilitySetup.mock.calls[0]?.[2]).toMatchObject({
      selections: [{ source: 'provider', recordId: 'capability-a', sourceRevision: 'source-1',
        placements: [{ source: 'provider', componentId: 'component-a', boundaryId: 'boundary-a' }],
        supportingCapabilities: [] }],
    });
    expect(api.completeSystemCapabilityOperation).not.toHaveBeenCalled();
    expect(screen.getByLabelText('Current route')).toHaveTextContent('operationId=operation-a');
    expect(screen.getByText('Subscribe provider capability')).toBeVisible();
    expect(screen.getByText('Provider collector')).toBeVisible();
    expect(screen.getByText('Boundary: Workload boundary')).toBeVisible();
  });

  it('allows local Person contributors only at system scope with an explicit boundary restriction', async () => {
    // Arrange
    mount('?source=local');
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Select Incident response' }));
    fireEvent.click(screen.getByRole('button', { name: 'Continue to applicability' }));
    expect(await screen.findByRole('checkbox', { name: 'Place Response team in Workload boundary' })).toBeDisabled();
    expect(screen.getByText('Person contributors can only be placed system-wide, not in an authorization boundary.')).toBeVisible();
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: 'Place Response team in System-wide' }));
    fireEvent.click(screen.getByRole('button', { name: 'Continue to review' }));
    // Assert
    await waitFor(() => expect(api.prepareSystemCapabilitySetup).toHaveBeenCalledOnce());
    expect(api.prepareSystemCapabilitySetup.mock.calls[0]?.[2].selections[0]).toMatchObject({
      source: 'local', recordId: 'local-capability-a',
      placements: [{ source: 'local', componentId: 'local-component-a', boundaryId: null }],
    });
  });

  it('rejects a recovered draft assigning a local Person to a boundary', async () => {
    // Arrange
    const first = mount('?source=local');
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Select Incident response' }));
    first.unmount();
    const key = sessionStorage.key(0)!;
    const draft = JSON.parse(sessionStorage.getItem(key)!);
    draft.chosen[0].selection.placements.push({ source: 'local', componentId: 'local-component-a', boundaryId: 'boundary-a' });
    sessionStorage.setItem(key, JSON.stringify(draft));
    // Act
    mount('?source=local&step=2');
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(/saved setup draft is invalid/i);
    expect(api.prepareSystemCapabilitySetup).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Continue to applicability' })).toBeDisabled();
  });

  it('resumes only unfinished server work with the same operation ID after partial success', async () => {
    // Arrange
    api.getSystemCapabilityOperation.mockResolvedValue(systemSetupOperationFixture('Setup', 'Partial'));
    mount('?operationId=operation-a&step=3');
    await screen.findByText('Placement write unavailable.');
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: /reviewed.*exact.*plan/i }));
    fireEvent.click(screen.getByRole('button', { name: 'Retry unfinished changes' }));
    // Assert
    await waitFor(() => expect(api.completeSystemCapabilityOperation).toHaveBeenCalledOnce());
    expect(api.completeSystemCapabilityOperation.mock.calls[0]?.slice(0, 4)).toEqual([
      'tenant-a', 'system-a', 'operation-a', { expectedRevision: 2 },
    ]);
    expect(api.prepareSystemCapabilitySetup).not.toHaveBeenCalled();
    expect(await screen.findByText(/Capabilities added to Selected system/)).toBeVisible();
    expect(screen.getByRole('link', { name: 'Review responsibilities' })).toHaveAttribute('href', '/systems/system-a/inheritance/subscriptions');
  });

  it('rejects recovery for a different system without exposing the saved plan', async () => {
    // Arrange
    api.getSystemCapabilityOperation.mockResolvedValue({ ...systemSetupOperationFixture(), systemId: 'system-b' });
    // Act
    mount('?operationId=operation-a');
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(/different.*scope|does not match/i);
    expect(screen.queryByRole('button', { name: 'Add to system' })).not.toBeInTheDocument();
    expect(api.completeSystemCapabilityOperation).not.toHaveBeenCalled();
  });

  it('requires current management permission independently of readable catalog data', async () => {
    // Arrange
    const readonly = page();
    readonly.permissions.canManage = false;
    api.listSystemCapabilities.mockResolvedValue(readonly);
    // Act
    mount();
    // Assert
    expect(await screen.findByRole('checkbox', { name: /Select Security monitoring/i })).toBeDisabled();
    expect(screen.getByText(/System setup permission is required/i)).toBeVisible();
  });

  it('does not retry an uncertain write before reloading durable outcomes', async () => {
    // Arrange
    api.getSystemCapabilityOperation.mockResolvedValueOnce(systemSetupOperationFixture())
      .mockRejectedValueOnce(new Error('Outcome refresh unavailable.'));
    api.completeSystemCapabilityOperation.mockRejectedValue(new Error('Request timed out.'));
    mount('?operationId=operation-a&step=3');
    await screen.findByRole('heading', { name: 'Review changes before adding' });
    fireEvent.click(screen.getByRole('checkbox', { name: /reviewed.*exact.*plan/i }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Add to system' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(/Outcome refresh unavailable/);
    expect(screen.getByRole('button', { name: 'Refresh saved outcomes' })).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Retry unfinished changes' })).not.toBeInTheDocument();
    expect(api.completeSystemCapabilityOperation).toHaveBeenCalledOnce();
  });

  it('keeps source-qualified selection across available library pages', async () => {
    // Arrange
    api.listSystemCapabilities.mockImplementation((_tenant, _system, query) => Promise.resolve({
      ...page(query.page === 2 ? 'local' : 'provider'), page: query.page, total: 26,
      items: [systemCapabilityItem(query.page === 2 ? 'local' : 'provider', 'same-record-id', false)],
    }));
    mount();
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Select Security monitoring' }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Select Incident response' }));
    fireEvent.click(screen.getByRole('button', { name: 'Continue to applicability' }));
    fireEvent.click(screen.getByRole('button', { name: 'Continue to review' }));
    // Assert
    await waitFor(() => expect(api.prepareSystemCapabilitySetup).toHaveBeenCalledOnce());
    expect(api.prepareSystemCapabilitySetup.mock.calls[0]?.[2].selections).toEqual([
      { source: 'provider', recordId: 'same-record-id', sourceRevision: 'source-1', placements: [], supportingCapabilities: [] },
      { source: 'local', recordId: 'same-record-id', sourceRevision: 'source-1', placements: [], supportingCapabilities: [] },
    ]);
  });

  it('recovers the tab-scoped draft and its applicability after reload', async () => {
    // Arrange
    const first = mount();
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Select Security monitoring' }));
    fireEvent.click(screen.getByRole('button', { name: 'Continue to applicability' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Place Provider collector in Workload boundary' }));
    first.unmount();
    // Act
    mount('?step=2');
    // Assert
    expect(await screen.findByRole('checkbox', { name: 'Place Provider collector in Workload boundary' })).toBeChecked();
    expect(api.prepareSystemCapabilitySetup).not.toHaveBeenCalled();
  });

  it('links supporting organization capability contributors without altering provider authorship', async () => {
    // Arrange
    mount();
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Select Security monitoring' }));
    fireEvent.click(screen.getByRole('button', { name: 'Continue to applicability' }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Choose supporting organization capabilities' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Support with Incident response' }));
    fireEvent.click(screen.getByRole('checkbox', { name: 'Place Response team in System-wide' }));
    fireEvent.click(screen.getByRole('button', { name: 'Continue to review' }));
    // Assert
    await waitFor(() => expect(api.prepareSystemCapabilitySetup).toHaveBeenCalledOnce());
    expect(api.prepareSystemCapabilitySetup.mock.calls[0]?.[2].selections[0]).toMatchObject({
      source: 'provider', recordId: 'capability-a',
      supportingCapabilities: [{ recordId: 'local-capability-a', sourceRevision: 'source-1' }],
      placements: [{ source: 'local', componentId: 'local-component-a', boundaryId: null }],
    });
  });

  it('does not allow another execution of a source-stale prepared plan', async () => {
    // Arrange
    api.completeSystemCapabilityOperation.mockRejectedValue(Object.assign(new Error('Source changed.'), { status: 409, code: 'STALE_SOURCE' }));
    mount('?operationId=operation-a');
    await screen.findByRole('heading', { name: 'Review changes before adding' });
    fireEvent.click(screen.getByRole('checkbox', { name: /reviewed.*exact.*plan/i }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Add to system' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Source changed.');
    expect(screen.queryByRole('button', { name: 'Add to system' })).not.toBeInTheDocument();
    expect(screen.getByText(/saved plan is stale/i)).toBeVisible();
  });

  it('fails closed with explicit recovery errors for malformed nested placement data', async () => {
    // Arrange
    const first = mount();
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Select Security monitoring' }));
    first.unmount();
    const key = sessionStorage.key(0)!;
    const draft = JSON.parse(sessionStorage.getItem(key)!);
    draft.chosen[0].selection.placements.push(null);
    sessionStorage.setItem(key, JSON.stringify(draft));
    // Act
    mount();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(/saved setup draft is invalid/i);
    expect(api.prepareSystemCapabilitySetup).not.toHaveBeenCalled();
  });

  it('does not restore another actor draft even if copied to this actor storage key', async () => {
    // Arrange
    const first = mount();
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Select Security monitoring' }));
    first.unmount();
    const key = sessionStorage.key(0)!;
    const draft = JSON.parse(sessionStorage.getItem(key)!);
    draft.identity = 'another-actor';
    sessionStorage.setItem(key, JSON.stringify(draft));
    // Act
    mount();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(/different actor or scope/i);
    expect(screen.getByRole('button', { name: 'Continue to applicability' })).toBeDisabled();
  });

  it('reports storage denial and prevents preparation without durable local recovery', async () => {
    // Arrange
    const denied = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('Storage is disabled.'); });
    mount();
    // Act
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Select Security monitoring' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(/Cannot save recovery state.*Storage is disabled/);
    expect(api.prepareSystemCapabilitySetup).not.toHaveBeenCalled();
    denied.mockRestore();
  });

  it('honors newly revoked management permission in supporting-capability results', async () => {
    // Arrange
    api.listSystemCapabilities.mockImplementation((_tenant, _system, query) => {
      const result = page(query.source === 'local' ? 'local' : 'provider');
      result.permissions.canManage = query.source !== 'local';
      return Promise.resolve(result);
    });
    mount();
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Select Security monitoring' }));
    fireEvent.click(screen.getByRole('button', { name: 'Continue to applicability' }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Choose supporting organization capabilities' }));
    // Assert
    expect(await screen.findByRole('button', { name: 'Support with Incident response' })).toBeDisabled();
    expect(screen.getByText(/current permission does not allow supporting/i)).toBeVisible();
  });

  it('shows exact persisted control and narrative effects before confirmation', async () => {
    // Arrange
    const operation = systemSetupOperationFixture();
    api.getSystemCapabilityOperation.mockResolvedValue({ ...operation, plannedWrites: operation.plannedWrites.map(write =>
      ({ ...write, controlIds: ['AC-1', 'AU-2'], narrativeTypes: ['Policy', 'Technical'] })) });
    // Act
    mount('?operationId=operation-a');
    // Assert
    expect((await screen.findAllByText('Affected controls: AC-1, AU-2')).length).toBeGreaterThan(0);
    expect(screen.getAllByText('Narrative types: Policy, Technical').length).toBeGreaterThan(0);
    expect(api.completeSystemCapabilityOperation).not.toHaveBeenCalled();
  });

  it('selects a legacy route hint only after an exact eligible detail read', async () => {
    // Arrange
    api.listSystemCapabilities.mockResolvedValue({ ...page(), items: [], total: 0 });
    // Act
    mount('?source=provider&recordId=capability-a');
    // Assert
    await waitFor(() => expect(screen.getByRole('button', { name: 'Continue to applicability' })).toBeEnabled());
    expect(api.getSystemCapability).toHaveBeenCalledWith('tenant-a', 'system-a',
      { source: 'provider', recordType: 'capability', recordId: 'capability-a' }, expect.any(AbortSignal));
    expect(screen.getByText('1 capabilities selected')).toBeVisible();
    await waitFor(() => expect(screen.getByLabelText('Current route')).not.toHaveTextContent('recordId='));
    expect(api.prepareSystemCapabilitySetup).not.toHaveBeenCalled();
  });

  it('does not accept an already-applied record from a route hint', async () => {
    // Arrange
    api.getSystemCapability.mockResolvedValue(systemCapabilityDetailFixture());
    // Act
    mount('?source=provider&recordId=capability-a');
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent(/already applied/i);
    expect(screen.getByRole('button', { name: 'Continue to applicability' })).toBeDisabled();
    expect(api.prepareSystemCapabilitySetup).not.toHaveBeenCalled();
  });

  it('ignores selection hints while recovering an authoritative saved operation', async () => {
    // Arrange
    mount('?operationId=operation-a&source=provider&recordId=capability-a');
    // Act
    await screen.findByRole('heading', { name: 'Review changes before adding' });
    // Assert
    expect(api.getSystemCapability).not.toHaveBeenCalled();
    expect(api.prepareSystemCapabilitySetup).not.toHaveBeenCalled();
  });
});
