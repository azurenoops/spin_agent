import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, useLocation } from 'react-router-dom';
import '../helpers/dialog';
import SystemCapabilityList from '../../features/workspace-operations/system-capabilities/SystemCapabilityList';
import * as api from '../../features/workspace-operations/system-capabilities/systemCapabilityApi';
import type { SystemCapabilityDetail, SystemCapabilityItem, SystemCapabilityOperation, SystemCapabilityPage } from '../../features/workspace-operations/system-capabilities/systemCapabilityTypes';
import { systemSetupOperationFixture } from '../fixtures/systemCapabilityDetailSetup';
import { appliedResponsibilityContext } from '../helpers/appliedResponsibilityContext';
import { getResponsibilityDraft } from '../../api/responsibilityDrafts';
import { getSystemDesign, getApprovedSystemDesign } from '../../api/systemDesign';
import { componentDesignFixture } from '../fixtures/componentReview';

vi.mock('../../features/workspace-operations/system-capabilities/systemCapabilityApi', () => ({
  listSystemCapabilities: vi.fn(), getSystemCapability: vi.fn(),
  prepareSystemCapabilitySetup: vi.fn(), getSystemCapabilityOperation: vi.fn(),
  completeSystemCapabilityOperation: vi.fn(),
  getSystemComponentPlacements: vi.fn(),
}));
vi.mock('../../api/systemDesign', () => ({ getSystemDesign: vi.fn(), getApprovedSystemDesign: vi.fn() }));
vi.mock('../../components/layout/SystemLayout', () => ({
  useSystemContext: () => ({ detail: { systemId: 'system-a', name: 'Mission Alpha' } }),
}));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => ({
    workspace: { displayName: 'Example organization', kind: 'organization', tenantId: 'org-a', mode: 'ordinary' },
    identity: { directoryTenantId: 'directory-a', oid: 'actor-a' },
  }),
}));
vi.mock('../../features/workspace-operations/system-capabilities/ResponsibilityDraftEditor', () => ({
  default: ({ controlId }: { controlId: string }) => <section aria-label={`Prepared draft for ${controlId}`} />,
}));
vi.mock('../../api/responsibilityDrafts', () => ({ getResponsibilityDraft: vi.fn() }));
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
  vi.mocked(getResponsibilityDraft).mockImplementation(async (system, control) => appliedResponsibilityContext(system, control));
  vi.mocked(getSystemDesign).mockResolvedValue(componentDesignFixture('org-a', 'system-a'));
  vi.mocked(getApprovedSystemDesign).mockResolvedValue(null);
  vi.mocked(api.getSystemComponentPlacements).mockResolvedValue({
    source: 'provider', recordId: 'component-a', sourceRevision: 'r1', relationshipRevision: 'rel',
    canAssignBoundary: true, assignBlockedReason: null, boundaries: [],
    placements: contributor.placements.map(placement => ({ ...placement, canUnassign: true, unassignBlockedReason: null })),
  });
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
    expect(within(drawer).getByText(/1 responsibility needs review/)).toBeVisible();
    expect(within(drawer).getByRole('tab', { name: /Overview/ })).toHaveAttribute('aria-selected', 'true');
    fireEvent.click(within(drawer).getByRole('tab', { name: /Responsibilities/ }));
    expect(within(drawer).getByText('Collect audit records')).toBeVisible();
    expect(within(drawer).getByText('Review audit records')).toBeVisible();
    expect(within(drawer).getAllByText('Review responsibility')[0]).toBeVisible();
    expect(within(drawer).getByRole('link', { name: 'Review AU-2 responsibility' })).toHaveAttribute(
      'href',
      '/systems/system-a/security-capabilities/provider/cap-a?tab=coverage&control=AU-2',
    );
    expect(within(drawer).getByRole('link', { name: 'Review evidence and narratives' })).toHaveAttribute(
      'href',
      '/systems/system-a/security-capabilities/provider/cap-a?tab=evidence&control=AU-2',
    );
    fireEvent.click(within(drawer).getByRole('tab', { name: /Where it applies/ }));
    expect(within(drawer).getByRole('button', { name: 'Change location for Provider SOC' })).toBeEnabled();
    expect(screen.getByLabelText('Route')).toHaveTextContent('capabilityId=cap-a');
  });

  it('counts missing and excluded placements without inventing drafts or coverage', async () => {
    // Arrange
    vi.mocked(api.getSystemCapability).mockResolvedValue({
      ...capabilityDetail,
      item: { ...capability, reviewRequiredCount: 99, components: [
        { ...contributor, recordId: 'missing', name: 'Archive', placements: [] },
        { ...contributor, recordId: 'excluded', name: 'Collector', placements: [contributor.placements[1]!] },
      ] },
      controls: [{ ...capabilityDetail.controls[0]!, reviewState: 'MissingAllocation' }],
    });
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Open Audit monitoring' }));
    const drawer = await screen.findByRole('dialog', { name: 'Review applied capability' });
    // Assert
    expect(await within(drawer).findByText(/2 scope gaps/)).toBeVisible();
    expect(within(drawer).getByText(/1 responsibility needs review/)).toBeVisible();
    expect(within(drawer).queryByText(/99/)).not.toBeInTheDocument();
    fireEvent.click(within(drawer).getByRole('tab', { name: /Where it applies/ }));
    expect(within(drawer).getByRole('button', { name: 'Assign location for Archive' })).toBeEnabled();
    expect(within(drawer).getByRole('button', { name: 'Assign location for Collector' })).toBeEnabled();
    expect(within(drawer).getAllByText(/No in-scope system location is recorded/)).toHaveLength(2);
    fireEvent.click(within(drawer).getByRole('tab', { name: /Responsibilities/ }));
    expect(within(drawer).getAllByText('Responsibility not confirmed')[0]).toBeVisible();
    expect(within(drawer).queryByText('MissingAllocation')).not.toBeInTheDocument();
  });

  it('keeps read-only scope actions and selected evidence context accurate', async () => {
    // Arrange
    vi.mocked(api.getSystemCapability).mockResolvedValue({
      ...capabilityDetail, permissions: { ...permissions, canManage: false },
      controls: [
        { ...capabilityDetail.controls[0]!, reviewState: 'Applied' },
        { ...capabilityDetail.controls[0]!, controlId: 'AU-6', reviewState: 'PendingReview' },
      ],
    });

    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Open Audit monitoring' }));
    const drawer = await screen.findByRole('dialog', { name: 'Review applied capability' });
    fireEvent.click(await within(drawer).findByRole('tab', { name: /Responsibilities/ }));
    fireEvent.click(within(drawer).getByRole('button', { name: 'AU-6' }));
    // Assert
    expect(within(drawer).getByRole('link', { name: 'Review AU-6 responsibility' })).toHaveAttribute(
      'href', '/systems/system-a/security-capabilities/provider/cap-a?tab=coverage&control=AU-6');
    expect(within(drawer).getByRole('link', { name: 'Review evidence and narratives' })).toHaveAttribute(
      'href', '/systems/system-a/security-capabilities/provider/cap-a?tab=evidence&control=AU-6');
    fireEvent.click(within(drawer).getByRole('tab', { name: /Where it applies/ }));
    expect(within(drawer).queryByRole('button', { name: /location for/ })).not.toBeInTheDocument();
  });

  it('counts stale canonical drafts without treating their saved state as accepted coverage', async () => {
    // Arrange
    const context = appliedResponsibilityContext('system-a', 'AU-2');
    vi.mocked(getResponsibilityDraft).mockResolvedValue({ ...context, draft: {
      id: 'draft-a', revision: 2, status: 'Proposed', sourceHash: 'old-source', isStale: true,
      generationState: 'NotRequested', generationError: null, preparedAt: '2026-10-01',
      generatedAt: null, preparedBy: 'reviewer', reviewedBy: null, reviewedAt: null,
      values: context.sourceValues, suggestion: { values: context.sourceValues, questions: [], conflicts: [] },
      sources: [], history: [],
    } });
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Open Audit monitoring' }));
    const drawer = await screen.findByRole('dialog', { name: 'Review applied capability' });
    const summary = await within(drawer).findByText('Source statement, version, evidence and limitations');
    fireEvent.click(summary);
    // Assert
    expect(await within(drawer).findByText(/1 saved draft needs review/)).toBeVisible();
    expect(within(drawer).getByText(/Choosing no provider scope uses system records/)).toBeVisible();
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
    const added = { ...capability, source: 'local' as const, recordId: 'cap-new', name: 'Identity management', isApplied: false, reviewRequiredCount: 0 };
    let completed = false;
    let operation: SystemCapabilityOperation = { ...systemSetupOperationFixture(), tenantId: 'org-a', revision: 0 };
    vi.mocked(api.listSystemCapabilities).mockImplementation(async (_tenantId, _systemId, query) =>
      query.scope === 'available'
        ? { ...page, items: [added], total: 1, scope: 'available' }
        : { ...page, items: completed ? [capability, { ...added, isApplied: true }] : [capability], total: completed ? 2 : 1 });
    vi.mocked(api.prepareSystemCapabilitySetup).mockImplementation(async (_tenantId, _systemId, body) => {
      operation = { ...operation, idempotencyKey: body.idempotencyKey, selections: body.selections };
      return { existing: false, operation };
    });
    vi.mocked(api.getSystemCapabilityOperation).mockImplementation(async () => operation);
    vi.mocked(api.completeSystemCapabilityOperation).mockImplementation(async () => {
      completed = true;
      return { ...operation, state: 'Completed', revision: 2 };
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
    expect(within(drawer).getByText('Managed by Cloud provider')).toBeVisible();
    expect(within(drawer).getByText('Team')).toBeVisible();
    fireEvent.click(within(drawer).getByText('Provider source and technical details'));
    expect(within(drawer).getByText(/Source is read-only here/i)).toBeVisible();
    fireEvent.click(within(drawer).getByRole('tab', { name: 'System scope' }));
    fireEvent.click(within(drawer).getByText('Existing infrastructure placements'));
    await within(drawer).findByText('Operations');
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
