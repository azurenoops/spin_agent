import { beforeEach, describe, expect, it, vi } from 'vitest';
import axios from 'axios';
import * as api from '../../features/workspace-operations/system-capabilities/systemCapabilityApi';
import type {
  SystemCapabilityAccess, SystemCapabilityDetail, SystemCapabilityItem, SystemCapabilityOperation,
} from '../../features/workspace-operations/system-capabilities/systemCapabilityTypes';

vi.mock('axios', () => ({ default: { request: vi.fn(), isAxiosError: (error: { isAxiosError?: boolean }) => error?.isAxiosError === true } }));

const permissions: SystemCapabilityAccess = {
  canRead: true, canManage: false, canReviewResponsibilities: false,
  canManageEvidence: false, canAuthorNarratives: false, canReviewNarratives: false,
};
const item: SystemCapabilityItem = {
  source: 'provider', recordType: 'capability', recordId: 'cap-a', name: 'Monitoring',
  description: 'Provider monitoring', sourceName: 'Provider A', mutationAuthority: 'Provider',
  sourceRevision: 'a'.repeat(64), isApplied: true, isAvailable: true, status: 'Applied',
  componentType: null, subType: null, components: [], capabilities: [], placements: [],
  controlIds: ['AU-2'], reviewRequiredCount: 1,
};
const detail: SystemCapabilityDetail = {
  item, permissions, baselineId: 'baseline-a', controls: [], evidence: [], narratives: [],
  relationshipRevision: 'b'.repeat(64), responsibilityReviewUrl: '/systems/system-a/inheritance/subscriptions',
};
const operation: SystemCapabilityOperation = {
  operationId: 'operation-a', idempotencyKey: 'key-a', tenantId: 'org-a', systemId: 'system-a',
  kind: 'Setup', state: 'Prepared', revision: 1, selections: [], plannedWrites: [], outcomes: [],
  lastError: null, createdAt: '2026-09-25T10:00:00Z', updatedAt: '2026-09-25T10:00:00Z',
};
const base = '/api/workspaces/organizations/org-a/systems/system-a/security-capabilities';
function success(data: unknown) { vi.mocked(axios.request).mockResolvedValue({ data: { data }, status: 200 }); }

beforeEach(() => vi.clearAllMocks());

describe('selected-system capability transport', () => {
  it('uses exact source, relationship and placement revisions for boundary writes', async () => {
    // Arrange
    const key = { source: 'provider', recordType: 'component', recordId: 'component-a' } as const;
    const options = { source: 'provider', recordId: 'component-a', sourceRevision: 'source-a', relationshipRevision: 'relationship-a',
      canAssignBoundary: true, assignBlockedReason: null, boundaries: [{ id: 'boundary-a', name: 'Operations' }],
      placements: [{ id: 'placement-a', boundaryId: 'boundary-a', boundaryName: 'Operations', state: 'InScope',
        revision: 'placement-r1', canUnassign: true, unassignBlockedReason: null }] };
    success(options);
    // Act / Assert
    expect(await api.getSystemComponentPlacements('org-a', 'system-a', key)).toEqual(options);
    const body = { boundaryId: 'boundary-a', sourceRevision: 'source-a', relationshipRevision: 'relationship-a' };
    success({ source: 'provider', recordId: 'component-a', placementId: 'placement-a', boundaryId: 'boundary-a', action: 'Assigned', relationshipRevision: 'relationship-b' });
    await api.assignSystemComponentBoundary('org-a', 'system-a', key, body);
    expect(axios.request).toHaveBeenLastCalledWith(expect.objectContaining({
      method: 'POST', url: `${base}/provider/component/component-a/placements/assign`, data: body,
    }));
    const removal = { sourceRevision: 'source-a', relationshipRevision: 'relationship-b', placementRevision: 'placement-r1' };
    success({ source: 'provider', recordId: 'component-a', placementId: 'placement-a', boundaryId: 'boundary-a', action: 'Unassigned', relationshipRevision: 'relationship-c' });
    await api.unassignSystemComponentBoundary('org-a', 'system-a', key, 'placement-a', removal);
    expect(axios.request).toHaveBeenLastCalledWith(expect.objectContaining({
      method: 'POST', url: `${base}/provider/component/component-a/placements/placement-a/unassign`, data: removal,
    }));
  });

  it('rejects mismatched placement identity and incomplete action permissions', async () => {
    // Arrange
    const key = { source: 'provider', recordType: 'component', recordId: 'component-a' } as const;
    success({ source: 'local', recordId: 'component-a', sourceRevision: 'source-a', relationshipRevision: 'relationship-a',
      canAssignBoundary: true, boundaries: [], placements: [] });
    // Act / Assert
    await expect(api.getSystemComponentPlacements('org-a', 'system-a', key)).rejects.toMatchObject({ code: 'INVALID_SYSTEM_CAPABILITY_RESPONSE' });
    success({ source: 'provider', recordId: 'component-a', sourceRevision: 'source-a', relationshipRevision: 'relationship-a',
      canAssignBoundary: true, boundaries: [], placements: [{ id: 'placement-a' }] });
    await expect(api.getSystemComponentPlacements('org-a', 'system-a', key)).rejects.toMatchObject({ code: 'INVALID_SYSTEM_CAPABILITY_RESPONSE' });
  });

  it('binds applied filters and cancellation to the selected organization/system', async () => {
    // Arrange
    const query = { scope: 'applied', grouping: 'component', source: 'local', componentType: 'Person', boundaryId: 'boundary-a', page: 2, pageSize: 25 } as const;
    const signal = new AbortController().signal;
    success({ items: [], page: 2, pageSize: 25, total: 30, scope: 'applied', grouping: 'component', permissions, boundaries: [] });
    // Act
    const result = await api.listSystemCapabilities('org-a', 'system-a', query, signal);
    // Assert
    expect(result.total).toBe(30);
    expect(axios.request).toHaveBeenCalledWith({ method: 'GET', url: base, params: query, signal });
  });

  it('encodes path identities without replacing source qualification', async () => {
    // Arrange
    success({ ...detail, item: { ...item, recordId: 'cap/a' } });
    // Act
    await api.getSystemCapability('org/a', 'system/a', { source: 'provider', recordType: 'capability', recordId: 'cap/a' });
    // Assert
    expect(axios.request).toHaveBeenCalledWith(expect.objectContaining({
      url: '/api/workspaces/organizations/org%2Fa/systems/system%2Fa/security-capabilities/provider/capability/cap%2Fa',
    }));
  });

  it('prepares exact multi-selection intent and completes only the persisted revision', async () => {
    // Arrange
    const body = { idempotencyKey: 'key-a', selections: [{ source: 'provider' as const, recordId: 'cap-a', sourceRevision: item.sourceRevision,
      placements: [{ source: 'provider' as const, componentId: 'contributor-a', boundaryId: 'boundary-a' }],
      supportingCapabilities: [{ recordId: 'local-support', sourceRevision: 'c'.repeat(64) }] }] };
    success({ operation: { ...operation, selections: body.selections }, existing: false });
    // Act
    await api.prepareSystemCapabilitySetup('org-a', 'system-a', body);
    // Assert
    expect(axios.request).toHaveBeenLastCalledWith({ method: 'POST', url: `${base}/setups/prepare`, data: body });
    // Act
    success({ ...operation, state: 'Completed', revision: 3 });
    await api.completeSystemCapabilityOperation('org-a', 'system-a', 'operation-a', { expectedRevision: 2 });
    // Assert
    expect(axios.request).toHaveBeenLastCalledWith({
      method: 'POST', url: `${base}/setups/operation-a/complete`, data: { expectedRevision: 2 },
    });
  });

  it('previews provider removal without deleting a shared provider record', async () => {
    // Arrange
    success({ operation: { ...operation, kind: 'Removal' }, existing: false });
    const body = { idempotencyKey: 'remove-a', sourceRevision: item.sourceRevision, relationshipRevision: detail.relationshipRevision };
    // Act
    await api.prepareSystemCapabilityRemoval('org-a', 'system-a', 'provider', 'cap-a', body);
    // Assert
    expect(axios.request).toHaveBeenCalledWith({
      method: 'POST', url: `${base}/provider/capability/cap-a/removals/prepare`, data: body,
    });
  });

  it('keeps narrative review locked to the selected source and system', async () => {
    // Arrange
    success({ status: 'Accepted' });
    const body = { expectedRevision: 4, decision: 'Accept', note: 'Reviewed the technical change.' };
    // Act
    await api.reviewSystemCapabilityNarrative('org-a', 'system-a', 'provider', 'cap-a', 'proposal-a', body);
    // Assert
    expect(axios.request).toHaveBeenCalledWith({
      method: 'POST', url: `${base}/provider/capability/cap-a/narrative-proposals/proposal-a/review`, data: body,
    });
  });

  it('preserves explicit stale and authorization errors rather than returning empty success', async () => {
    // Arrange
    vi.mocked(axios.request).mockRejectedValue({
      isAxiosError: true, message: 'Conflict', response: { status: 409, data: { error: { code: 'STALE_SOURCE', message: 'Review the changed source.' } } },
    });
    // Act / Assert
    await expect(api.getSystemCapabilityOperation('org-a', 'system-a', 'operation-a')).rejects.toMatchObject({
      status: 409, code: 'STALE_SOURCE', message: 'Review the changed source.',
    });
  });

  it('rejects incomplete permission projections', async () => {
    // Arrange
    success({ items: [], page: 1, pageSize: 25, total: 0, scope: 'applied', grouping: 'capability', boundaries: [] });
    // Act / Assert
    await expect(api.listSystemCapabilities('org-a', 'system-a', {})).rejects.toMatchObject({ code: 'INVALID_SYSTEM_CAPABILITY_RESPONSE' });
  });

  it('never turns library availability or denied read access into an applied system result', async () => {
    // Arrange
    const page = { items: [{ ...item, isApplied: false }], page: 1, pageSize: 25, total: 1,
      scope: 'applied', grouping: 'capability', permissions, boundaries: [] };
    success(page);
    // Act / Assert
    await expect(api.listSystemCapabilities('org-a', 'system-a', {})).rejects.toMatchObject({ code: 'INVALID_SYSTEM_CAPABILITY_RESPONSE' });
    // Arrange
    success({ ...page, items: [item], permissions: { ...permissions, canRead: false } });
    // Act / Assert
    await expect(api.listSystemCapabilities('org-a', 'system-a', {})).rejects.toMatchObject({ code: 'INVALID_SYSTEM_CAPABILITY_RESPONSE' });
  });

  it('does not expose a recovered operation returned for another system', async () => {
    // Arrange
    success({ ...operation, systemId: 'system-b' });
    // Act / Assert
    await expect(api.getSystemCapabilityOperation('org-a', 'system-a', 'operation-a')).rejects.toMatchObject({ code: 'INVALID_SYSTEM_CAPABILITY_RESPONSE' });
  });

  it('rejects source identity mismatches and unprotected evidence links', async () => {
    // Arrange
    success({ ...detail, item: { ...item, source: 'local' } });
    const key = { source: 'provider', recordType: 'capability', recordId: 'cap-a' } as const;
    // Act / Assert
    await expect(api.getSystemCapability('org-a', 'system-a', key)).rejects.toMatchObject({ code: 'INVALID_SYSTEM_CAPABILITY_RESPONSE' });
    // Arrange
    success({ ...detail, evidence: [{ id: 'evidence-a', fileName: 'Policy', openUrl: 'https://storage.example.test/private' }] });
    // Act / Assert
    await expect(api.getSystemCapability('org-a', 'system-a', key)).rejects.toMatchObject({ code: 'INVALID_SYSTEM_CAPABILITY_RESPONSE' });
  });
});
