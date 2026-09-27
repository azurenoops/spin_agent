import type { ProviderScope, Citation, SnapshotRef } from './types';

export interface HostingExclusion { scope: ProviderScope; rationale: string }
export interface HostingScopeInput {
  expectedOfferingRevision: number;
  predecessorRevisionId: string | null;
  name: string;
  purpose?: string | null;
  changeRationale?: string | null;
  permittedScopes: ProviderScope[];
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
  purpose?: string | null;
  changeRationale?: string | null;
  permittedScopes: ProviderScope[];
  exclusions: HostingExclusion[];
  citations: Citation[];
}
export interface HostingAssignmentInput {
  targetTenantId: string;
  systemId: string;
  hostingScopeRevisionId: string;
  assignedScopes: ProviderScope[];
  references: Citation[];
}
export interface HostingAssignment {
  assignmentId: string;
  revision: number;
  offeringId: string;
  systemId: string;
  hostingScope: SnapshotRef;
  assignedScopes: ProviderScope[];
  relationshipState: string;
  systemName?: string | null;
  targetTenantName?: string | null;
}
