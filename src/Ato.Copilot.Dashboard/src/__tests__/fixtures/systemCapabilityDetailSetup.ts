export const systemCapabilityAccess = {
  canRead: true, canManage: true, canReviewResponsibilities: true,
  canManageEvidence: true, canAuthorNarratives: true, canReviewNarratives: true,
};

export function systemCapabilityItem(source: 'local' | 'provider' = 'provider', recordId = 'capability-a', isApplied = true) {
  return {
    source, recordType: 'capability' as const, recordId,
    name: source === 'provider' ? 'Security monitoring' : 'Incident response',
    description: 'Persisted capability description.', sourceName: source === 'provider' ? 'Example provider' : 'Example organization',
    mutationAuthority: source === 'provider' ? 'provider' : 'organization',
    sourceRevision: 'source-1', isApplied, isAvailable: true, status: 'Published',
    componentType: null, subType: null,
    components: [{
      source, recordType: 'component' as const, recordId: source === 'provider' ? 'component-a' : 'local-component-a',
      name: source === 'provider' ? 'Provider collector' : 'Response team', description: 'Recorded contributor.',
      componentType: source === 'provider' ? 'Thing' : 'Person', subType: source === 'provider' ? 'Service' : null,
      sourceName: source === 'provider' ? 'Example provider' : 'Example organization',
      mutationAuthority: source === 'provider' ? 'provider' : 'organization', sourceRevision: 'component-revision',
      placements: [{ id: 'placement-a', boundaryId: 'boundary-a', boundaryName: 'Workload boundary', state: 'InScope' as const, revision: 'placement-revision' }],
      capabilities: [{ source, recordType: 'capability' as const, recordId, name: 'Security monitoring' }],
    }],
    capabilities: [], placements: [], controlIds: ['AC-1'], reviewRequiredCount: 1,
  };
}

export function systemCapabilityDetailFixture(source: 'local' | 'provider' = 'provider') {
  return {
    item: systemCapabilityItem(source), permissions: { ...systemCapabilityAccess }, baselineId: 'baseline-a',
    controls: [{
      controlId: 'AC-1', providerCoverage: 'Collect logs', organizationDuty: 'Review logs',
      allocation: 'Shared', reviewState: 'PendingReview', confirmedSourceRevision: null,
      availableSourceRevision: 'source-1', reviewRevision: 'review-1', sourceSnapshot: null, confirmedSourceSnapshot: null,
    }],
    evidence: [{
      id: 'evidence-a', fileName: 'System-log-review.pdf', owner: 'Example organization',
      source: 'Manual', state: 'Submitted', controlId: 'AC-1', narrativeType: 'Technical',
      openUrl: '/api/dashboard/systems/system-a/evidence/evidence-a/download',
    }],
    narratives: [
      { controlId: 'AC-1', narrativeType: 'Policy' as const, approvedContent: 'Approved policy remains intact.',
        currentContent: 'Approved policy remains intact.', approvalStatus: 'Approved', freshness: 'ReviewRequired',
        currentVersion: 2, proposals: [{ id: 'proposal-a', revision: 3, status: 'Draft', isStale: false,
          canReview: true, source, recordId: 'capability-a' }], canGenerate: true, blockedReason: null },
      { controlId: 'AC-1', narrativeType: 'Technical' as const, approvedContent: 'Approved technical content.',
        currentContent: 'Approved technical content.', approvalStatus: 'Approved', freshness: 'Current',
        currentVersion: 4, proposals: [], canGenerate: false, blockedReason: 'Technical source evidence needs review.' },
    ],
    relationshipRevision: 'relationship-1',
    responsibilityReviewUrl: '/systems/system-a/inheritance/subscriptions',
  };
}

export function systemSetupOperationFixture(kind: 'Setup' | 'Removal' = 'Setup', state: 'Prepared' | 'Partial' | 'Completed' | 'InProgress' = 'Prepared') {
  return {
    operationId: 'operation-a', idempotencyKey: 'request-a', tenantId: 'tenant-a', systemId: 'system-a',
    kind, state, revision: state === 'Prepared' ? 1 : 2,
    selections: [{ source: 'provider' as const, recordId: 'capability-a', sourceRevision: 'source-1',
      placements: [{ source: 'provider' as const, componentId: 'component-a', boundaryId: 'boundary-a' }], supportingCapabilities: [] }],
    plannedWrites: [
      { writeKind: kind === 'Removal' ? 'unsubscribe' : 'subscription', writeId: 'subscription-a',
        source: 'provider', recordId: 'capability-a', componentId: null, boundaryId: null, alreadyExists: false },
      { writeKind: kind === 'Removal' ? 'responsibility-reconciliation' : 'component-placement', writeId: kind === 'Removal' ? 'reconcile-a' : 'placement-a',
        source: 'provider', recordId: kind === 'Removal' ? 'system-a' : 'component-a', componentId: kind === 'Removal' ? null : 'component-a',
        boundaryId: kind === 'Removal' ? null : 'boundary-a', alreadyExists: kind !== 'Removal' },
    ],
    outcomes: state === 'Prepared' ? [] : [
      { writeKind: kind === 'Removal' ? 'unsubscribe' : 'subscription', writeId: 'subscription-a', state: 'Completed', error: null, updatedAt: '2026-09-25T10:00:00Z' },
      { writeKind: kind === 'Removal' ? 'responsibility-reconciliation' : 'component-placement', writeId: kind === 'Removal' ? 'reconcile-a' : 'placement-a', state: state === 'Completed' ? 'Completed' : 'Failed',
        error: state === 'Completed' ? null : 'Placement write unavailable.', updatedAt: '2026-09-25T10:00:00Z' },
    ],
    lastError: state === 'Partial' ? 'Placement write unavailable.' : null,
    createdAt: '2026-09-25T10:00:00Z', updatedAt: '2026-09-25T10:00:00Z',
  };
}
