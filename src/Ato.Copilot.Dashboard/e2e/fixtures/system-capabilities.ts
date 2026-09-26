import { expect, type BrowserContext } from '@playwright/test';
import { installWorkspaceFixture } from './workspace-shell';
import type {
  SystemCapabilityDetail, SystemCapabilityItem, SystemCapabilityOperation, SystemCapabilitySelection,
} from '../../src/features/workspace-operations/system-capabilities/systemCapabilityTypes';
import type { NarrativeProposal } from '../../src/api/narrativeLibrary';

export const systemCapabilityRoot = '/workspaces/organizations/org-a/systems/system-a/security-capabilities';
export const providerCapabilityId = '11111111-1111-1111-1111-111111111111';
export const providerComponentId = '22222222-2222-2222-2222-222222222222';
const providerId = '33333333-3333-3333-3333-333333333333';
const sourceRevision = 'a'.repeat(64);
const relationshipRevision = 'b'.repeat(64);
const base = '/api/workspaces/organizations/org-a/systems/system-a/security-capabilities';
const permissions = {
  canRead: true, canManage: true, canReviewResponsibilities: true,
  canManageEvidence: true, canAuthorNarratives: false, canReviewNarratives: true,
};
const providerComponent = {
  source: 'provider' as const, recordType: 'component' as const, recordId: providerComponentId,
  name: 'Microsoft Sentinel', description: 'Provider-operated monitoring service.', componentType: 'Thing',
  subType: 'Service', sourceName: 'Synthetic provider', mutationAuthority: 'Provider', sourceRevision,
  placements: [
    { id: 'placement-a', boundaryId: 'boundary-a', boundaryName: 'Azure workload', state: 'InScope' as const, revision: 'placement-revision-a' },
    { id: 'placement-b', boundaryId: 'boundary-b', boundaryName: 'Organization operations', state: 'Excluded' as const, revision: 'placement-revision-b' },
  ],
  capabilities: [{ source: 'provider' as const, recordType: 'capability' as const, recordId: providerCapabilityId, name: 'Security monitoring' }],
};
const localComponent = {
  ...providerComponent, source: 'local' as const, recordId: 'local-team', name: 'SOC analyst team',
  componentType: 'Person', subType: 'Team', sourceName: 'Organization A', mutationAuthority: 'Organization',
  placements: [{ id: 'placement-local', boundaryId: 'boundary-b', boundaryName: 'Organization operations', state: 'InScope' as const, revision: 'placement-revision-local' }],
};
const providerCapability: SystemCapabilityItem = {
  source: 'provider', recordType: 'capability', recordId: providerCapabilityId, name: 'Security monitoring',
  description: 'Detect and investigate security events.', sourceName: 'Synthetic provider',
  mutationAuthority: 'Provider', sourceRevision, isApplied: true, isAvailable: true, status: 'Review required',
  componentType: null, subType: null, components: [providerComponent, localComponent], capabilities: [],
  placements: [], controlIds: ['AU-2'], reviewRequiredCount: 1,
};
const localCapability: SystemCapabilityItem = {
  ...providerCapability, source: 'local', recordId: 'local-response', name: 'Incident response', sourceName: 'Organization A',
  mutationAuthority: 'Organization', components: [localComponent], reviewRequiredCount: 0, isApplied: false, status: 'Available',
};
const availableProvider: SystemCapabilityItem = {
  ...providerCapability, recordId: '55555555-5555-5555-5555-555555555555', name: 'Boundary logging', isApplied: false, status: 'Available',
  components: [providerComponent], reviewRequiredCount: 0,
};
const snapshot = JSON.stringify({
  Id: providerCapabilityId, Name: 'Security monitoring', Description: 'Collect and analyze audit records.',
  Status: 0, Controls: ['AU-2'], Component: {
    CspInheritedComponentId: providerComponentId, CspProfileId: providerId, Name: 'Microsoft Sentinel',
    Description: 'Provider-operated monitoring service.', Status: 1, SourceArtifactReference: '[redacted]',
  },
});
const reviewItem = {
  subscriptionId: 'subscription-a', capabilityId: providerCapabilityId, componentId: providerComponentId,
  cspProfileId: providerId, controlId: 'AU-2', sourceRevision, reviewRevision: 'review-a', state: 'MissingAllocation',
  reviewedSourceRevision: null, confirmedBy: null, confirmedAt: null, allocation: null,
  effectiveInheritanceType: null, designationSource: null, sourceAvailable: true, sourceSnapshotJson: snapshot,
  reviewedSourceSnapshotJson: null,
};

/** Synthetic responses exercise the actual SPA; backend tests separately prove authorization and persistence. */
export async function installSystemCapabilityFixture(context: BrowserContext, baseURL: string, options: {
  denied?: boolean; partial?: boolean; staleRemoval?: boolean; unavailable?: boolean; organizationOnly?: boolean;
  paginatedLibrary?: boolean;
  proposals?: boolean; narrativeReviewDenied?: boolean;
} = {}) {
  await installWorkspaceFixture(context, baseURL);
  const access = { ...permissions, canManage: !options.denied, canReviewNarratives: !options.narrativeReviewDenied };
  const records = [providerCapability, localCapability, availableProvider].map(item => structuredClone(item));
  if (options.organizationOnly) {
    records[0]!.isApplied = false;
    records[1]!.isApplied = true;
  }
  records.push({ ...structuredClone(localCapability), recordId: 'local-continuity', name: 'Business continuity' });
  if (options.paginatedLibrary) records.push(...Array.from({ length: 26 }, (_, index) => ({
    ...structuredClone(localCapability), recordId: `local-extra-${index}`, name: `Recovery planning ${String(index + 1).padStart(2, '0')}`,
  })));
  const writes: { path: string; body: unknown }[] = [];
  const operations = new Map<string, SystemCapabilityOperation>();
  let nextOperation = 0;
  let partial = options.partial === true;
  let unavailable = options.unavailable === true;
  let staleRemoval = options.staleRemoval === true;
  let confirmed = false;
  let reviewNotes: string | null = null;
  const responsibility = () => ({
    systemId: 'system-a', baselineId: 'baseline-a', canConfirm: true, pendingImpacts: [],
    items: [{ ...reviewItem, state: confirmed ? 'Applied' : 'MissingAllocation', reviewedSourceRevision: confirmed ? sourceRevision : null,
      reviewedSourceSnapshotJson: confirmed ? snapshot : null, allocation: confirmed ? {
        controlId: 'AU-2', inheritanceType: 'Shared', provider: 'Provider logging', customerResponsibility: 'Mission log review',
      } : null, effectiveInheritanceType: confirmed ? 'Shared' : null, designationSource: confirmed ? 'CspSubscription' : null,
      providerCoverageVerified: confirmed ? true : null, customerDutiesReviewed: confirmed ? true : null, reviewNotes }],
  });
  const componentPlacements = structuredClone(providerComponent.placements);
  let placementRevision = 'placement-set-1';
  const placementBoundaries = [
    { id: 'boundary-a', name: 'Azure workload' }, { id: 'boundary-b', name: 'Organization operations' },
    { id: 'boundary-c', name: 'Mission workload' },
  ];
  let proposalAccepted = false;
  const proposal: NarrativeProposal = {
    id: 'proposal-a', controlId: 'AU-2', narrativeType: 'Policy', baseVersion: 3,
    beforeContent: 'Approved policy content retained.', proposedContent: 'Reviewed policy proposal.',
    stateHash: sourceRevision, provenance: { source: 'provider', recordId: providerCapabilityId },
    conflicts: [], missingEvidence: [], status: 'Draft', revision: 7, createdAt: '2026-09-25T10:00:00Z',
    createdBy: 'separate-author', reviewedAt: null, reviewedBy: null, reviewNote: null, acceptedVersion: null,
    isStale: false, canReview: !options.narrativeReviewDenied,
  };
  await context.route('**/hubs/**', route => route.fulfill({ status: 503, body: 'No live hub access in fixture tests.' }));
  await context.route('**/api/dashboard/systems/system-a/workspace-access', route => route.fulfill({ json: { status: 'success', data: {
    systemId: 'system-a', roles: ['MissionOwner', 'ISSO'],
    permissions: { canRead: true, canEditProfile: true, canManageSystem: !options.denied, canAuthorNarratives: false,
      canReviewNarratives: true, canManageEvidence: true, canRunAssessments: false, canManageRemediation: false, canDecideAuthorization: false },
  } } }));
  await context.route(`**${base}{,/**,?*}`, async route => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const success = (data: unknown, status = 200) => route.fulfill({ status, json: { data } });
    expect(request.headers()['x-workspace-kind']).toBe('organization');
    expect(request.headers()['x-workspace-tenant-id']).toBe('org-a');
    if (request.method() !== 'GET') writes.push({ path, body: request.postDataJSON() });
    if (path.endsWith('/responsibilities/confirm')) {
      const body = request.postDataJSON();
      expect(body.providerCoverageVerified).toBe(true);
      expect(body.customerDutiesReviewed).toBe(true);
      expect(body.reviewNotes).toBeTruthy();
      confirmed = true; reviewNotes = body.reviewNotes;
      return success(responsibility());
    }
    if (path.includes(`/provider/component/${providerComponentId}/placements`)) {
      if (request.method() === 'GET') return success({
        source: 'provider', recordId: providerComponentId, sourceRevision, relationshipRevision: placementRevision,
        canAssignBoundary: !options.denied, assignBlockedReason: options.denied ? 'System-management permission is required.' : null,
        boundaries: placementBoundaries, placements: componentPlacements.map(placement => ({ ...placement, canUnassign: !options.denied, unassignBlockedReason: null })),
      });
      const body = request.postDataJSON();
      expect(body.sourceRevision).toBe(sourceRevision);
      expect(body.relationshipRevision).toBe(placementRevision);
      let placementId: string;
      let boundaryId: string;
      const assigning = path.endsWith('/assign');
      if (assigning) {
        placementId = 'placement-new'; boundaryId = body.boundaryId;
        componentPlacements.push({ id: placementId, boundaryId, boundaryName: placementBoundaries.find(boundary => boundary.id === boundaryId)!.name,
          state: 'InScope', revision: 'placement-new-r1' });
      } else {
        placementId = path.split('/').at(-2)!;
        const index = componentPlacements.findIndex(placement => placement.id === placementId);
        expect(index).toBeGreaterThanOrEqual(0);
        expect(body.placementRevision).toBe(componentPlacements[index]!.revision);
        boundaryId = componentPlacements.splice(index, 1)[0]!.boundaryId;
      }
      placementRevision += '-next';
      return success({ source: 'provider', recordId: providerComponentId, placementId, boundaryId,
        action: assigning ? 'Assigned' : 'Unassigned', relationshipRevision: placementRevision });
    }
    if (path.endsWith('/narrative-proposals/proposal-a/review')) {
      expect(request.postDataJSON().expectedRevision).toBe(7);
      proposalAccepted = request.postDataJSON().decision === 'Approve';
      return success({ ...proposal, status: proposalAccepted ? 'Approved' : 'ChangesRequested' });
    }
    if (request.method() === 'GET' && path === base) {
      if (unavailable) {
        unavailable = false;
        return route.fulfill({ status: 503, json: { error: { code: 'SERVICE_UNAVAILABLE', message: 'System capability service unavailable. Retry this request.' } } });
      }
      const scope = url.searchParams.get('scope') ?? 'applied';
      const grouping = url.searchParams.get('grouping') ?? 'capability';
      let items = records.filter(item => (scope === 'available' || item.isApplied) && (!options.organizationOnly || item.source === 'local'));
      if (grouping === 'component') {
        items = [providerComponent, localComponent, { ...localComponent, recordId: 'direct-policy', name: 'Incident response policy', componentType: 'Policy', subType: null, capabilities: [], placements: [] }]
          .filter(item => !options.organizationOnly || item.source === 'local')
          .map(item => ({ ...providerCapability, ...item, components: [], isApplied: true, controlIds: [], reviewRequiredCount: 0 }));
      }
      const source = url.searchParams.get('source');
      const search = url.searchParams.get('search')?.toLowerCase();
      if (source) items = items.filter(item => item.source === source);
      if (search) items = items.filter(item => item.name.toLowerCase().includes(search));
      const page = Number(url.searchParams.get('page') ?? 1);
      const pageSize = Number(url.searchParams.get('pageSize') ?? 25);
      return success({ items: items.slice((page - 1) * pageSize, page * pageSize), total: items.length, page, pageSize, scope, grouping, permissions: access,
        boundaries: [{ id: 'boundary-a', name: 'Azure workload' }, { id: 'boundary-b', name: 'Organization operations' }] });
    }
    if (path.endsWith('/setups/prepare') || path.endsWith('/removals/prepare')) {
      const body = request.postDataJSON();
      const previous = [...operations.values()].find(operation => operation.idempotencyKey === body.idempotencyKey);
      if (previous) return success({ operation: previous, existing: true });
      const removing = path.endsWith('/removals/prepare');
      const selections: SystemCapabilitySelection[] = removing
        ? [{ source: 'provider', recordId: providerCapabilityId, sourceRevision, placements: [], supportingCapabilities: [] }]
        : body.selections;
      const operation: SystemCapabilityOperation = {
        operationId: `operation-${++nextOperation}`, idempotencyKey: body.idempotencyKey, tenantId: 'org-a', systemId: 'system-a',
        kind: removing ? 'Removal' : 'Setup', state: 'Prepared', revision: 1, selections,
        plannedWrites: selections.map(selection => ({
          writeKind: removing ? 'unsubscribe' : selection.source === 'provider' ? 'subscription' : 'system-link',
          writeId: `${selection.source}:${selection.recordId}`, source: selection.source, recordId: selection.recordId,
          componentId: null, boundaryId: null, alreadyExists: false,
          displayLabel: `${removing ? 'Remove' : 'Apply'} "${records.find(record => record.source === selection.source && record.recordId === selection.recordId)?.name}" for this system`,
        })),
        outcomes: [], lastError: null, createdAt: '2026-09-25T10:00:00Z', updatedAt: '2026-09-25T10:00:00Z',
      };
      operations.set(operation.operationId, operation);
      return success({ operation, existing: false }, 201);
    }
    const operationId = path.match(/\/setups\/([^/]+)(?:\/complete)?$/)?.[1];
    if (operationId) {
      const operation = operations.get(operationId);
      if (!operation) return route.fulfill({ status: 404, json: { error: { code: 'SETUP_NOT_FOUND', message: 'No such operation.' } } });
      if (request.method() === 'POST') {
        expect(request.postDataJSON()).toEqual({ expectedRevision: operation.revision });
        if (staleRemoval && operation.kind === 'Removal') {
          staleRemoval = false;
          return route.fulfill({ status: 409, json: { error: { code: 'STALE_RELATIONSHIP', message: 'System relationships changed. Refresh before removal.' } } });
        }
        operation.revision++;
        if (partial && operation.kind === 'Setup') {
          partial = false; operation.state = 'Partial'; operation.lastError = 'One write remains pending.';
          operation.outcomes = operation.plannedWrites.map((write, index) => ({
            writeKind: write.writeKind, writeId: write.writeId, state: index === 0 ? 'Completed' : 'Failed',
            error: index === 0 ? null : 'Synthetic persistence failure', updatedAt: operation.updatedAt,
          }));
          return route.fulfill({ status: 503, json: { error: { code: 'SETUP_WRITE_FAILED', message: 'Some changes saved; recover persisted outcomes.' } } });
        }
        operation.state = 'Completed'; operation.lastError = null;
        operation.outcomes = operation.plannedWrites.map(write => ({
          writeKind: write.writeKind, writeId: write.writeId, state: 'Completed', error: null, updatedAt: operation.updatedAt,
        }));
        for (const selection of operation.selections) {
          const item = records.find(record => record.source === selection.source && record.recordId === selection.recordId);
          if (item) item.isApplied = operation.kind === 'Setup';
        }
      }
      return success(operation);
    }
    const match = path.match(/\/(local|provider)\/(capability|component)\/([^/]+)$/);
    if (request.method() === 'GET' && match) {
      const [, source, recordType, recordId] = match;
      const item = recordType === 'component'
        ? { ...providerCapability, ...(source === 'provider' ? { ...providerComponent, placements: componentPlacements } : localComponent), recordId: recordId!, components: [], recordType: 'component' as const }
        : records.find(record => record.source === source && record.recordId === recordId);
      if (!item) return route.fulfill({ status: 404, json: { error: { code: 'NOT_FOUND', message: 'Source not available.' } } });
      const detail: SystemCapabilityDetail = {
        item, permissions: access, baselineId: 'baseline-a', relationshipRevision,
        responsibilityReviewUrl: '/systems/system-a/inheritance/subscriptions',
        controls: recordType === 'component' ? [] : [{
          controlId: 'AU-2', providerCoverage: 'Collect platform logs', organizationDuty: 'Configure workload log sources',
          allocation: confirmed ? 'Shared' : null, reviewState: confirmed ? 'Applied' : 'MissingAllocation',
          confirmedSourceRevision: confirmed ? sourceRevision : null, availableSourceRevision: sourceRevision, reviewRevision: 'review-a',
          sourceSnapshot: snapshot, confirmedSourceSnapshot: confirmed ? snapshot : null,
        }],
        evidence: recordType === 'component' ? [] : [{
          id: 'evidence-a', fileName: 'Approved monitoring procedure.pdf', owner: 'Organization A', source: 'System repository',
          state: 'Available', controlId: 'AU-2', narrativeType: 'Policy', openUrl: '/api/dashboard/systems/system-a/evidence/evidence-a/download',
        }],
        narratives: recordType === 'component' ? [] : (['Policy', 'Technical'] as const).map(narrativeType => ({
          controlId: 'AU-2', narrativeType, approvedContent: proposalAccepted && narrativeType === 'Policy' ? proposal.proposedContent : `Approved ${narrativeType.toLowerCase()} content retained.`,
          currentContent: proposalAccepted && narrativeType === 'Policy' ? proposal.proposedContent : `Approved ${narrativeType.toLowerCase()} content retained.`, approvalStatus: 'Approved',
          freshness: narrativeType === 'Policy' ? 'Current' : 'SourceChanged', currentVersion: 3,
          proposals: options.proposals && narrativeType === 'Policy' && !proposalAccepted ? [{
            id: proposal.id, revision: proposal.revision, status: proposal.status, isStale: false,
            canReview: !options.narrativeReviewDenied, source: item.source, recordId: item.recordId,
          }] : [],
          canGenerate: false, blockedReason: 'Narrative authoring permission is required.',
        })),
      };
      return success(detail);
    }
    return route.fulfill({ status: 404, json: { error: { code: 'FIXTURE_ROUTE_MISSING', message: `Unexpected fixture route: ${path}` } } });
  });
  await context.route('**/api/dashboard/systems/system-a/capability-subscriptions/**', async route => {
    const request = route.request();
    if (request.method() === 'PUT') {
      writes.push({ path: new URL(request.url()).pathname, body: request.postDataJSON() });
      confirmed = true;
    }
    return route.fulfill({ json: responsibility() });
  });
  await context.route('**/api/dashboard/systems/system-a/evidence/evidence-a/download', route =>
    route.fulfill({ contentType: 'application/pdf', body: '%PDF-1.4\nSynthetic protected evidence fixture\n' }));
  await context.route('**/api/systems/system-a/narrative-library/proposals/proposal-a', route =>
    route.fulfill({ json: proposal }));
  return { writes, records, operations };
}
