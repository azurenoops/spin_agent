export type CloudEnvironment = 'AzureCloud' | 'AzureUSGovernment';

export interface AzureScope {
  cloud: CloudEnvironment;
  directoryTenantId: string;
  subscriptionId: string;
  resourceId: string;
}

export interface Citation {
  packageId: string;
  artifactId: string;
  archivePath: string;
  locator: string;
  quote: string;
}

export interface SnapshotRef {
  revisionId: string;
  revision: number;
  snapshotHash: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}

export type RelationshipState =
  | 'Undetermined'
  | 'SeparateBoundaryConsumer'
  | 'ExplicitlyCoveredByRecordedScope';

export interface AssociateRelationshipInput {
  assignmentId: string;
  expectedAssignmentRevision: number;
}

export interface AssociatedRelationship {
  relationshipId: string;
  revision: number;
  assignmentId: string;
  state: RelationshipState;
}

export interface RelationshipPreviewInput {
  expectedRevision: number;
  expectedAssignmentRevision: number;
  relationshipState: RelationshipState;
  authorizationRevisionId?: string;
  boundaryRevisionId?: string;
  evidence: Citation[];
  rationale: string;
}

export interface RelationshipPreview {
  previewId: string;
  previewHash: string;
  revision: number;
  contextSnapshotHash: string;
  blockers: unknown[];
  canReview: boolean;
}

export interface RelationshipReviewInput {
  expectedRevision: number;
  previewId: string;
  previewHash: string;
  rationale: string;
}

export interface ApplicableCapabilitiesQuery {
  page: number;
  assignmentId?: string;
  offeringId?: string;
  environment?: CloudEnvironment;
  capabilityId?: string;
  releaseId?: string;
}

export interface CapabilityAdoptionInput {
  assignmentId: string;
  expectedAssignmentRevision: number;
  capabilityId: string;
  releaseId: string;
  contextSnapshotHash: string;
  applicabilityPreviewHash: string;
}

export interface SystemHostingAllocation {
  relationshipId: string | null;
  assignmentId: string;
  revision: number;
  offeringId: string;
  offeringName: string;
  systemId: string;
  systemName: string;
  assignedScopes: AzureScope[];
  canAssociate: boolean;
  providerName: string | null;
  hostingScopeName: string | null;
}

export interface ProviderRelationship {
  relationshipId: string | null;
  revision: number;
  assignmentId: string;
  assignmentRevision: number;
  offeringId: string;
  systemId: string;
  state: RelationshipState;
  reviewRequired: boolean;
  authorizationRevisionId: string | null;
  boundaryRevisionId: string | null;
  reviewedBy: string | null;
  reviewedAt: string | null;
  assignedScopes: AzureScope[];
  offeringName: string | null;
  providerName: string | null;
  systemName: string | null;
  hostingScopeName: string | null;
  canAssociate: boolean;
}

export interface ApplicableProviderCapability {
  capabilityId: string;
  capabilityName: string | null;
  releaseId: string;
  releaseRevision: number;
  releaseSnapshotHash: string;
  offeringId: string;
  offeringName: string | null;
  assignmentId: string;
  assignmentRevision: number;
  applicability: SnapshotRef;
  applicabilityPreviewHash: string;
  applicabilityState: string;
  reasonCodes: string[];
  authorizationRelationship: RelationshipState;
  relationshipReviewRequired: boolean;
  providerCoverage: string[];
  sharedDuties: string[];
  customerDuties: string[];
  outstandingDecisions: string[];
  sourceReferences: { referenceId: string; title: string; locator: string; canReadContent: boolean }[];
  canProposeAdoption: boolean;
  canConfirmResponsibilities: boolean;
}

export interface CapabilityAdoption {
  subscription: {
    id: string;
    alreadySubscribed: boolean;
    unsubscribed: boolean;
    created: boolean;
    responsibilities: import('../../api/capabilityResponsibilities').CapabilityResponsibilityResponse;
  };
  adoptionSnapshotId: string;
  releaseId: string;
  contextSnapshotHash: string;
}
