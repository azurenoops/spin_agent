import type { PackageStatus } from '../package-imports/types';

export interface Page<T> { items: T[]; page: number; pageSize: number; total: number }
export type Cloud = 'AzureCloud' | 'AzureUSGovernment';
export type OfferingEnvironment = Cloud | 'Microsoft365DoD' | 'ManualService';
export interface AzureScope { kind?: 'Azure'; cloud: Cloud; directoryTenantId: string; subscriptionId: string; resourceId: string }
export interface ServiceScope { kind: 'Service'; serviceId: string; serviceName: string; environment: OfferingEnvironment; tenantReference: string | null }
export type ProviderScope = AzureScope | ServiceScope;
export interface Citation { packageId: string; artifactId: string; archivePath: string; locator: string; quote: string }
export interface SnapshotRef { revisionId: string; revision: number; snapshotHash: string }
export interface Offering {
  offeringId: string; providerId: string; name: string; description: string; environments: OfferingEnvironment[];
  revision: number; lifecycle: 'Draft' | 'Active' | 'Retired';
  currentBoundaryRevisionId: string | null; currentHostingScopeRevisionId: string | null;
  serviceModel?: ServiceModel | null; managementArrangement?: ManagementArrangement | null;
  serviceOwner?: string | null; securityContact?: string | null;
}
export type ServiceModel = 'InfrastructureSharedServices' | 'PlatformService' | 'SoftwareAsAService' | 'BrokeredCloudSpace';
export type ManagementArrangement = 'ProviderManaged' | 'SharedOperations' | 'MissionOwnerManaged';
export type OfferingIdentityInput = Pick<Offering, 'name' | 'description' | 'environments' | 'serviceOwner' | 'securityContact'>
  & { serviceModel?: ServiceModel | '' | null; managementArrangement?: ManagementArrangement | '' | null };
export interface BoundaryInput {
  name: string; scopeStatement: string; services: string[]; componentSnapshotIds: string[];
  includedScopes: ProviderScope[]; exclusions: { scope: ProviderScope | null; description: string; rationale: string }[];
  providerResponsibilities: string[]; customerResponsibilities: string[]; citations: Citation[];
}
export interface BoundaryRevision extends BoundaryInput {
  offeringId: string; offeringRevision: number; boundaryRevisionId: string; version: number;
  snapshotHash: string; predecessorRevisionId: string | null; createdAt: string;
}
export interface OfferingBoundaryCapability {
  capabilityId: string | null; candidateId: string | null; packageId: string | null; name: string;
  reviewState: string; publicationState: string; releaseId: string | null; boundaryRevisionId: string | null;
  releaseRevision?: number | null;
}
export interface OfferingBoundaryMission {
  assignmentId: string; systemId: string; systemName: string | null; relationshipState: string;
  associated: boolean; adoptedCapabilityCount: number; assignedScopes: ProviderScope[];
  targetTenantId?: string | null; targetTenantName?: string | null;
  adoptedReleases?: {
    capabilityId: string; capabilityName: string; releaseId: string; revision: number;
    currentReleaseRevision: number | null; updateAvailable: boolean;
  }[];
}
export interface OfferingBoundaryOverview {
  offeringId: string; offeringRevision: number;
  capabilities: Page<OfferingBoundaryCapability> & { awaitingReview: number; published: number };
  missionSystems: Page<OfferingBoundaryMission>;
}
export interface OfferingOverviewData {
  offeringId: string; offeringRevision: number;
  /** Explicit pending allocation reviews plus actionable source-backed narrative reviews; null means unavailable. */
  customerActionCount?: number | null;
  openFindingCount?: number | null;
  authorizations: Page<ExternalDecision> & { recorded: number; unconfirmed: number; rejected: number };
  packages: Page<{
    package: PackageStatus; packageVersionId: string | null; version: number | null; boundaryRevisionId: string | null;
    awaitingReview: number; authorizationDetails: number;
  }> & {
    needsAttention: number; processing: number; awaitingReview: number; sourceDocumentCount?: number | null;
    preferredAuthorizationReview: {
      packageId: string; packageName: string; type: 'AuthorizationDecisionClaim' | 'AuthorizationReference';
    } | null;
  };
  capabilities: { proposed: number; awaitingReview: number; awaitingApproval: number; published: number; archived: number;
    publishedReleaseRevisions?: number[] };
  hosting: { name: string | null; configured: boolean; scopeCount: number; assignmentCount: number; associatedSystemCount: number };
}
export interface PackageVersion {
  packageVersionId: string; offeringId: string; seriesId: string; version: number; packageId: string;
  boundaryRevisionId: string; previousVersionId: string | null; manifestHash: string; createdAt: string;
}
export interface PackageAssociation { offeringId: string; packageVersionId: string; boundaryRevisionId: string }
export interface AssociatedPackage extends PackageStatus { association: PackageAssociation | null }
export interface PackageReceipt { package: AssociatedPackage; packageVersion: PackageVersion }
export interface CandidateRef { packageId: string; candidateId: string; revision: number }
export interface ExternalDecisionInput {
  boundaryRevisionId: string; sourceCandidateRefs: CandidateRef[];
  recordKind: 'ProviderDecision' | 'InheritedMicrosoftReference' | 'InheritedProviderReference'; reference: string;
  upstreamProvider?: string | null;
  issuingAuthorityType?: 'person' | 'organization' | null;
  issuingAuthority: string | null; decisionAsStated: string | null; issuedOn: string | null;
  effectiveOn: string | null; expiresOn: string | null; expiryBasis: 'DateStated' | 'NoExpiryStated' | 'NotRecorded';
  scopeStatement: string; conditions: string[]; citations: Citation[];
}
export interface ExternalDecision extends ExternalDecisionInput {
  recordId: string; offeringId: string; revisionId: string; revision: number; snapshotHash: string;
  metadataReviewState: 'Unconfirmed' | 'Recorded' | 'Rejected';
  currentStanding: 'Undetermined' | 'NotYetEffective' | 'CurrentAsRecorded' | 'Expired' | 'Withdrawn' | 'Superseded';
  recordedBy: string | null; recordedAt: string | null; impactReviewRequired: boolean;
}
export interface FindingInput {
  title: string; observation: string; severityAsStated: string | null; sourceCandidateRef?: CandidateRef;
  controlIds: string[]; citations: Citation[];
}
export interface Finding extends FindingInput {
  findingId: string; offeringId: string; revision: number; workflowState: string; createdAt: string;
}
export interface FindingReviewInput {
  expectedRevision: number; evidenceIds: string[]; disposition: 'KeepOpen' | 'AcceptClosure'; rationale: string;
}
export interface FindingReview {
  reviewId: string; findingId: string; offeringId: string; findingRevision: number; evidenceIds: string[];
  disposition: string; rationale: string; reviewedBy: string; reviewedAt: string; workflowState: string;
}
export interface FindingEvidence {
  evidenceRevision?: number;
  evidenceId: string; findingId: string; offeringId: string; findingRevision: number; fileName: string;
  mediaType: string; byteLength: number; sha256: string; description: string; state: string;
  createdAt: string; latestReview: FindingReview | null;
}
export interface ProviderPoam extends PoamInput {
  poamId: string; offeringId: string; revision: number; workflowState: string; createdAt: string;
}
export interface PoamInput {
  title: string; findingIds: string[]; correctiveAction: string; ownerAsStated?: string;
  milestones: { description: string; dueDate: string | null }[]; sourceCandidateRef?: CandidateRef; citations: Citation[];
}
export interface ImpactInput {
  expectedOfferingRevision: number;
  changes: { kind: string; recordId: string; expectedRevision: number | string; proposedSnapshotHash: string }[];
  authorizationRevisionIds: string[]; boundaryRevisionId: string; hostingScopeRevisionId?: string; packageVersionIds: string[];
}
export interface ImpactPreview {
  reviewId: string; revision: number; previewId: string; previewHash: string; contextSnapshotHash: string; expiresAt: string;
  blockers: { code: string; message: string; targetId?: string }[];
  affectedCounts: { components: number; capabilities: number; scopes: number; systems: number };
}
export interface ImpactReview {
  reviewId: string; revision: number; disposition: string; reviewedBy: string | null;
  reviewedAt: string | null; contextSnapshotHash: string; stale: boolean;
  title?: string | null; summary?: string | null; createdAt?: string | null;
  affectedCounts?: ImpactPreview['affectedCounts'] | null;
}
export interface ImpactTarget {
  kind: string; recordId: string; tenantId: string | null; systemId: string | null; reviewState: string;
}
