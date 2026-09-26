export const allocation = {
  relationshipId: null,
  assignmentId: 'assignment-a', revision: 3, offeringId: 'offering-a',
  offeringName: 'Harbor hosting', systemId: 'system-a', systemName: 'Vanguard',
  assignedScopes: [{
    cloud: 'AzureUSGovernment' as const, directoryTenantId: 'directory-a', subscriptionId: 'subscription-a',
    resourceId: '/subscriptions/subscription-a/resourceGroups/mission',
  }],
  canAssociate: true,
  providerName: 'Harbor provider',
  hostingScopeName: 'Mission resource group',
};

export const allocationResponse = {
  relationshipId: null, revision: 0, assignmentId: allocation.assignmentId, assignmentRevision: allocation.revision,
  offeringId: allocation.offeringId, systemId: allocation.systemId, state: 'Undetermined' as const,
  reviewRequired: true, authorizationRevisionId: null, boundaryRevisionId: null, reviewedBy: null, reviewedAt: null,
  assignedScopes: allocation.assignedScopes, offeringName: allocation.offeringName, providerName: allocation.providerName,
  systemName: allocation.systemName, hostingScopeName: allocation.hostingScopeName, canAssociate: true,
};

export const capability = {
  capabilityId: 'capability-a', capabilityName: 'Security monitoring', offeringName: 'Harbor hosting',
  offeringId: 'offering-a', assignmentId: 'assignment-a', assignmentRevision: 3,
  releaseId: 'release-a', releaseRevision: 2, releaseSnapshotHash: 'release-hash',
  applicability: { revisionId: 'applicability-a', revision: 2, snapshotHash: 'context-hash' },
  applicabilityPreviewHash: 'preview-hash',
  applicabilityState: 'Applicable', reasonCodes: [], authorizationRelationship: 'Undetermined' as const,
  relationshipReviewRequired: true, providerCoverage: ['Provider: operate monitoring'],
  sharedDuties: ['Shared: coordinate incidents'], customerDuties: ['Customer: investigate alerts'],
  outstandingDecisions: ['Review responsibility allocations'],
  sourceReferences: [{ referenceId: 'reference-a', title: 'Harbor SSP', locator: '§ 9.2', canReadContent: false }],
  canProposeAdoption: true, canConfirmResponsibilities: false,
};

export const relationship = {
  relationshipId: 'relationship-a', revision: 1, assignmentId: 'assignment-a',
  state: 'Undetermined' as const,
};

export const adoption = {
  subscription: { id: 'subscription-a', alreadySubscribed: false, unsubscribed: false, created: true,
    responsibilities: { systemId: 'system-a', baselineId: null, canConfirm: false, items: [], pendingImpacts: [] } },
  adoptionSnapshotId: 'adoption-a', releaseId: 'release-a', contextSnapshotHash: 'context-hash',
};
