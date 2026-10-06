import type { SystemCapabilityDetail } from '../../features/workspace-operations/system-capabilities/systemCapabilityTypes';
import type { SystemDesignGraph } from '../../api/systemDesign';

export function componentDesignFixture(tenantId = 'org', systemId = 'system'): SystemDesignGraph {
  return {
    tenantId, systemId, systemName: 'Synthetic system', revision: 2, governanceStatus: 'Draft', sourcesStale: false,
    sourceFingerprint: 'graph-source', componentScopes: [], nodes: [], edges: [], groups: [], gaps: [], proposals: [],
    contributions: [], baselineChanges: [], completenessPercentage: 0, sspReadiness: 'Unapproved',
    discoveryState: 'Unavailable', monitoringState: 'Unavailable',
    actions: { canEdit: true, canSubmit: true, canWithdraw: false, canReview: false, canReconcile: true },
  };
}

export function componentReviewFixture(): SystemCapabilityDetail {
  return {
    item: { source: 'provider', recordType: 'component', recordId: 'backup', name: 'Azure Backup',
      description: 'Synthetic demonstration source. Recovery Services vault backs up saved recovery points.',
      sourceName: 'Flankspeed', mutationAuthority: 'provider', sourceRevision: 'r1',
      componentType: 'Thing', subType: 'Service', isApplied: true, isAvailable: true, status: 'Applied',
      components: [], capabilities: [{ source: 'provider', recordType: 'capability', recordId: 'cap', name: 'Backup and recovery' }],
      placements: [{ id: '', boundaryId: null, boundaryName: null, state: 'Unassigned', revision: '' }],
      controlIds: ['CP-9'], reviewRequiredCount: 1 },
    permissions: { canRead: true, canManage: true, canManageEvidence: false,
      canReviewResponsibilities: false, canAuthorNarratives: false, canReviewNarratives: false },
    baselineId: 'baseline', controls: [], evidence: [], narratives: [], relationshipRevision: 'rel',
    responsibilityReviewUrl: '/systems/system/inheritance/subscriptions',
  };
}
