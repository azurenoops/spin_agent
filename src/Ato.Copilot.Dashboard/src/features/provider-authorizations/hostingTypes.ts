import type { AzureScope, Citation, SnapshotRef } from './types';

export interface HostingExclusion { scope: AzureScope; rationale: string }
export interface HostingScopeInput {
  expectedOfferingRevision: number;
  predecessorRevisionId: string | null;
  name: string;
  permittedScopes: AzureScope[];
  exclusions: HostingExclusion[];
  citations: Citation[];
}
export interface HostingScopeRevision {
  offeringId: string;
  offeringRevision: number;
  snapshot: SnapshotRef;
  impactReviewId: string | null;
  predecessorRevisionId: string | null;
  name: string;
  permittedScopes: AzureScope[];
  exclusions: HostingExclusion[];
  citations: Citation[];
}
export interface HostingAssignmentInput {
  targetTenantId: string;
  systemId: string;
  hostingScopeRevisionId: string;
  assignedScopes: AzureScope[];
  references: Citation[];
}
export interface HostingAssignment {
  assignmentId: string;
  revision: number;
  offeringId: string;
  systemId: string;
  hostingScope: SnapshotRef;
  assignedScopes: AzureScope[];
  relationshipState: string;
}
