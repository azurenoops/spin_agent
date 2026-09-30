import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, useLocation } from 'react-router-dom';
import '../helpers/dialog';
import SystemCapabilityList from '../../features/workspace-operations/system-capabilities/SystemCapabilityList';
import * as api from '../../features/workspace-operations/system-capabilities/systemCapabilityApi';
import type { SystemCapabilityDetail, SystemCapabilityItem, SystemCapabilityPage } from '../../features/workspace-operations/system-capabilities/systemCapabilityTypes';
import { systemSetupOperationFixture } from '../fixtures/systemCapabilityDetailSetup';

vi.mock('../../features/workspace-operations/system-capabilities/systemCapabilityApi', () => ({
  listSystemCapabilities: vi.fn(), getSystemCapability: vi.fn(),
  prepareSystemCapabilitySetup: vi.fn(), getSystemCapabilityOperation: vi.fn(),
  completeSystemCapabilityOperation: vi.fn(),
}));
vi.mock('../../components/layout/SystemLayout', () => ({
  useSystemContext: () => ({ detail: { systemId: 'system-a', name: 'Mission Alpha' } }),
}));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => ({
    workspace: { displayName: 'Example organization', kind: 'organization', tenantId: 'org-a', mode: 'ordinary' },
    identity: { directoryTenantId: 'directory-a', oid: 'actor-a' },
  }),
}));
const permissions = {
  canRead: true, canManage: true, canReviewResponsibilities: false,
  canManageEvidence: false, canAuthorNarratives: false, canReviewNarratives: false,
};
const contributor = {
  source: 'provider' as const, recordType: 'component' as const, recordId: 'component-a',
  name: 'Provider SOC', description: 'Monitoring team', componentType: 'Person', subType: 'Team',
  sourceName: 'Cloud provider', mutationAuthority: 'Provider', sourceRevision: 'r1',
  placements: [
    { id: 'assignment-a', boundaryId: 'boundary-a', boundaryName: 'Operations', state: 'InScope' as const, revision: 'a1' },
    { id: 'assignment-b', boundaryId: 'boundary-b', boundaryName: 'Development', state: 'Excluded' as const, revision: 'a2' },
  ],
  capabilities: [{ source: 'provider' as const, recordType: 'capability' as const, recordId: 'cap-a', name: 'Audit monitoring' }],
};
const capability: SystemCapabilityItem = {
  source: 'provider', recordType: 'capability', recordId: 'cap-a', name: 'Audit monitoring',
  description: 'Collect and review audit records', sourceName: 'Cloud provider', mutationAuthority: 'Provider',
  sourceRevision: 'r1', isApplied: true, isAvailable: true, status: 'Applied',
  componentType: null, subType: null, components: [contributor], capabilities: [], placements: [],
  controlIds: ['AU-2', 'AU-6'], reviewRequiredCount: 1,
};
const page: SystemCapabilityPage = {
  items: [capability], page: 1, pageSize: 25, total: 31, scope: 'applied', grouping: 'capability',
  permissions, boundaries: [{ id: 'boundary-a', name: 'Operations' }, { id: 'boundary-b', name: 'Development' }],
};
const componentDetail: SystemCapabilityDetail = {
  item: { ...capability, ...contributor, components: [], controlIds: [], reviewRequiredCount: 0 },
  permissions, baselineId: null, controls: [], evidence: [], narratives: [],
  relationshipRevision: 'relationship-a', responsibilityReviewUrl: '/systems/system-a/inheritance/subscriptions',
};
const capabilityDetail: SystemCapabilityDetail = {
  item: capability,
  permissions,
  baselineId: 'baseline-a',
  controls: [{
    controlId: 'AU-2',
    providerCoverage: 'Collect audit records',
    organizationDuty: 'Review audit records',
    allocation: 'Shared',
    reviewState: 'PendingReview',
    confirmedSourceRevision: null,
    availableSourceRevision: 'r1',
    reviewRevision: 'review-a',
    sourceSnapshot: null,
    confirmedSourceSnapshot: null,
  }],
  evidence: [],
  narratives: [],
  relationshipRevision: 'relationship-a',
  responsibilityReviewUrl: '/systems/system-a/inheritance/subscriptions',
};
function Location() { const location = useLocation(); return <output aria-label="Route">{location.pathname}{location.search}</output>; }
function mount(route = '/systems/system-a/security-capabilities') {
  return render(<MemoryRouter initialEntries={[route]}>
    <SystemCapabilityList tenantId="org-a" systemId="system-a" systemName="Mission Alpha" />
    <Location />
  </MemoryRouter>);
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.listSystemCapabilities).mockResolvedValue(page);
  vi.mocked(api.getSystemCapability).mockImplementation(async (_tenantId, _systemId, key) =>
    key.recordType === 'component' ? componentDetail : capabilityDetail);
});

describe('applied system security capability views', () => {
  it('lists applied records, actual contributor placements and accurate server totals', async () => {
    // Arrange / Act
    mount();
    // Assert
    expect(await screen.findByRole('link', { name: 'Audit monitoring' })).toHaveAttribute('href', '/systems/system-a/security-capabilities/provider/cap-a');
    expect(screen.getByRole('heading', { name: 'Applied security capabilities' })).toBeVisible();
    expect(screen.getByText(/Mission Alpha/)).toBeVisible();
    expect(api.listSystemCapabilities).toHaveBeenCalledWith('org-a', 'system-a', expect.objectContaining({ scope: 'applied', grouping: 'capability' }), expect.any(AbortSignal));
    expect(screen.getByRole('navigation', { name: 'Pagination' })).toHaveTextContent('31 total records');
    expect(within(screen.getByRole('table')).getByText('Operations')).toBeVisible();
    expect(screen.getByText(/Development.*Excluded/)).toBeVisible();
    expect(screen.getByText(/does not confirm responsibilities/i)).toBeVisible();
    expect(screen.getByRole('link', { name: 'Preview contribution' })).toHaveAttribute(
      'href',
      '/systems/system-a/documents#ssp-sections',
    );
    expect(screen.getByRole('link', { name: 'View package readiness' })).toHaveAttribute(
      'href',
      '/systems/system-a',
    );
  });

  it('opens an applied capability review drawer with real review and placement actions', async () => {
    // Arrange
    mount();

    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Open Audit monitoring' }));

    // Assert
    const drawer = await screen.findByRole('dialog', { name: 'Review applied capability' });
    expect(await within(drawer).findByRole('heading', { name: 'Audit monitoring' })).toBeVisible();
    expect(api.getSystemCapability).toHaveBeenCalledWith('org-a', 'system-a', {
      source: 'provider', recordType: 'capability', recordId: 'cap-a',
    }, expect.any(AbortSignal));
    expect(within(drawer).getByText('Collect audit records')).toBeVisible();
    expect(within(drawer).getByText('Review audit records')).toBeVisible();
    expect(within(drawer).getByRole('link', { name: 'Review responsibilities' })).toHaveAttribute(
      'href',
      '/systems/system-a/inheritance/subscriptions',
    );
    expect(within(drawer).getByRole('link', { name: 'Review evidence and narratives' })).toHaveAttribute(
      'href',
      '/systems/system-a/security-capabilities/provider/cap-a?tab=evidence',
    );
    expect(within(drawer).getByRole('button', { name: 'Manage Provider SOC placement' })).toBeEnabled();
    expect(screen.getByLabelText('Route')).toHaveTextContent('capabilityId=cap-a');
  });

  it('keeps available offerings in a separate add flow and retains inventory tools', async () => {
    // Arrange
    mount();
    await screen.findByRole('link', { name: 'Audit monitoring' });
    // Act
    expect(screen.getByRole('link', { name: 'Add CSP hosting & capabilities' })).toHaveAttribute(
      'href',
      '/systems/system-a/provider-relationships/setup',
    );
    fireEvent.click(screen.getByRole('button', { name: 'Add organization capability' }));
    // Assert
    const drawer = await screen.findByRole('dialog', { name: 'Add organization capability' });
    expect(drawer).toBeVisible();
    expect(drawer).toHaveClass('max-w-3xl');
    expect(within(drawer).getByTestId('capability-options')).toHaveClass('grid-cols-1');
    expect(within(drawer).getByTestId('selection-summary')).toHaveClass('order-first');
    expect(screen.getByLabelText('Route')).toHaveTextContent('/systems/system-a/security-capabilities');
    expect(screen.getByRole('link', { name: 'Manage inventory' })).toHaveAttribute('href', '/systems/system-a/security-capabilities/inventory');
  });

  it('refreshes the applied table only after setup completes on the server', async () => {
    // Arrange
    const added = { ...capability, recordId: 'cap-new', name: 'Identity management', isApplied: false, reviewRequiredCount: 0 };
    let completed = false;
    vi.mocked(api.listSystemCapabilities).mockImplementation(async (_tenantId, _systemId, query) =>
      query.scope === 'available'
        ? { ...page, items: [added], total: 1, scope: 'available' }
        : { ...page, items: completed ? [capability, { ...added, isApplied: true }] : [capability], total: completed ? 2 : 1 });
    vi.mocked(api.prepareSystemCapabilitySetup).mockImplementation(async (_tenantId, _systemId, body) => ({
      existing: false,
      operation: {
        ...systemSetupOperationFixture(),
        tenantId: 'org-a',
        revision: 0,
        idempotencyKey: body.idempotencyKey,
        selections: body.selections,
      },
    }));
    vi.mocked(api.getSystemCapabilityOperation).mockResolvedValue({
      ...systemSetupOperationFixture(),
      tenantId: 'org-a',
      revision: 0,
    });
    vi.mocked(api.completeSystemCapabilityOperation).mockImplementation(async () => {
      completed = true;
      return { ...systemSetupOperationFixture('Setup', 'Completed'), tenantId: 'org-a' };
    });
    mount();
    await screen.findByRole('link', { name: 'Audit monitoring' });

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Add organization capability' }));
    const drawer = await screen.findByRole('dialog', { name: 'Add organization capability' });
    fireEvent.click(within(drawer).getByRole('checkbox', { name: 'Select Identity management' }));
    fireEvent.click(within(drawer).getByRole('button', { name: 'Continue to applicability' }));
    fireEvent.click(within(drawer).getByRole('button', { name: 'Continue to review' }));
    fireEvent.click(await within(drawer).findByRole('checkbox', { name: /reviewed.*exact.*plan/i }));
    fireEvent.click(within(drawer).getByRole('button', { name: 'Add to system' }));

    // Assert
    expect(await screen.findByRole('link', { name: 'Identity management' })).toBeVisible();
    expect(api.completeSystemCapabilityOperation).toHaveBeenCalledOnce();
    expect(api.listSystemCapabilities).toHaveBeenLastCalledWith('org-a', 'system-a',
      expect.objectContaining({ scope: 'applied' }), expect.any(AbortSignal));
  });

  it('includes directly assigned components without fabricating a delivering capability', async () => {
    // Arrange
    const direct: SystemCapabilityItem = {
      ...componentDetail.item, source: 'local', recordId: 'direct-a', name: 'Mission analyst',
      capabilities: [], placements: [{ id: 'system-assignment', boundaryId: null, boundaryName: null, state: 'SystemWide', revision: 'r2' }],
    };
    vi.mocked(api.listSystemCapabilities).mockResolvedValue({ ...page, items: [direct], grouping: 'component', total: 1 });
    // Act
    mount('/systems/system-a/security-capabilities?view=component');
    // Assert
    const row = (await screen.findByRole('button', { name: 'Mission analyst' })).closest('tr')!;
    expect(within(row).getByText('Direct system assignment')).toBeVisible();
    expect(within(row).getByText('System-wide')).toBeVisible();
    expect(screen.getByRole('tab', { name: 'By component' })).toHaveAttribute('aria-selected', 'true');
  });

  it('sends source, type, boundary, sort and paging filters to the authoritative query', async () => {
    // Arrange
    mount();
    await screen.findByRole('link', { name: 'Audit monitoring' });
    // Act
    fireEvent.change(screen.getByLabelText('Source'), { target: { value: 'local' } });
    await waitFor(() => expect(api.listSystemCapabilities).toHaveBeenLastCalledWith('org-a', 'system-a', expect.objectContaining({ source: 'local', page: 1 }), expect.any(AbortSignal)));
    fireEvent.change(screen.getByLabelText('Component type'), { target: { value: 'Person' } });
    fireEvent.change(screen.getByLabelText('Boundary'), { target: { value: 'boundary-a' } });
    fireEvent.change(screen.getByLabelText('Sort'), { target: { value: 'source:asc' } });
    // Assert
    await waitFor(() => expect(api.listSystemCapabilities).toHaveBeenLastCalledWith('org-a', 'system-a',
      expect.objectContaining({ source: 'local', componentType: 'Person', boundaryId: 'boundary-a', sort: 'source', direction: 'asc' }), expect.any(AbortSignal)));
    // Act
    await screen.findByRole('link', { name: 'Audit monitoring' });
    fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    // Assert
    await waitFor(() => expect(api.listSystemCapabilities).toHaveBeenLastCalledWith('org-a', 'system-a', expect.objectContaining({ page: 2, source: 'local', boundaryId: 'boundary-a' }), expect.any(AbortSignal)));
  });

  it('opens a source-qualified provider drawer with actual placements and read-only ownership', async () => {
    // Arrange
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Provider SOC' }));
    // Assert
    const drawer = await screen.findByRole('dialog', { name: 'Component details' });
    await within(drawer).findByRole('heading', { name: 'Provider SOC' });
    expect(api.getSystemCapability).toHaveBeenCalledWith('org-a', 'system-a', {
      source: 'provider', recordType: 'component', recordId: 'component-a',
    }, expect.any(AbortSignal));
    expect(within(drawer).getByText(/source is managed by the provider/i)).toBeVisible();
    expect(within(drawer).getByText('Team')).toBeVisible();
    expect(within(drawer).getByText('Operations')).toBeVisible();
    expect(within(drawer).getByText(/Development.*Excluded/)).toBeVisible();
    expect(within(drawer).queryByRole('button', { name: /edit source/i })).not.toBeInTheDocument();
  });

  it('explains denied setup authority without hiding the applied records', async () => {
    // Arrange
    vi.mocked(api.listSystemCapabilities).mockResolvedValue({ ...page, permissions: { ...permissions, canManage: false } });
    // Act
    mount();
    // Assert
    await screen.findByRole('link', { name: 'Audit monitoring' });
    expect(screen.getByRole('button', { name: 'Add organization capability' })).toBeDisabled();
    expect(screen.getByText(/system-management permission/i)).toBeVisible();
  });

  it('distinguishes an organization-only empty system from a failed request and retries explicitly', async () => {
    // Arrange
    vi.mocked(api.listSystemCapabilities).mockRejectedValueOnce(new Error('System service unavailable'));
    vi.mocked(api.listSystemCapabilities).mockResolvedValue({ ...page, items: [], total: 0 });
    mount();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('System service unavailable');
    expect(screen.queryByText(/No security capabilities applied/)).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    // Assert
    expect(await screen.findByText(/No security capabilities applied/)).toBeVisible();
    expect(screen.getByText(/organization capabilities.*without a provider/i)).toBeVisible();
  });

  it('supports keyboard view switching and ignores an older filter response', async () => {
    // Arrange
    let finish!: (value: SystemCapabilityPage) => void;
    vi.mocked(api.listSystemCapabilities).mockReturnValueOnce(new Promise(resolve => { finish = resolve; }));
    vi.mocked(api.listSystemCapabilities).mockResolvedValue({ ...page, grouping: 'component', items: [componentDetail.item] });
    mount();
    // Act
    fireEvent.keyDown(screen.getByRole('tab', { name: 'By capability' }), { key: 'ArrowRight' });
    await screen.findByRole('button', { name: 'Provider SOC' });
    await act(async () => finish({ ...page, items: [{ ...capability, name: 'Old result' }] }));
    // Assert
    expect(screen.getByRole('tab', { name: 'By component' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.queryByText('Old result')).not.toBeInTheDocument();
    expect(vi.mocked(api.listSystemCapabilities).mock.calls[0]![3]?.aborted).toBe(true);
  });
});
