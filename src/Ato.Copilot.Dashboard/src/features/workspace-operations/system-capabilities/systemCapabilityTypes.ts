export type SystemCapabilitySource = 'local' | 'provider';

export interface SystemCapabilityRecordKey {
  source: SystemCapabilitySource;
  recordType: 'capability' | 'component';
  recordId: string;
}

export interface SystemCapabilityAccess {
  canRead: boolean;
  canManage: boolean;
  canReviewResponsibilities: boolean;
  canManageEvidence: boolean;
  canAuthorNarratives: boolean;
  canReviewNarratives: boolean;
}

export interface SystemCapabilityPlacement {
  id: string;
  boundaryId: string | null;
  boundaryName: string | null;
  state: 'InScope' | 'Excluded' | 'SystemWide' | 'Unassigned';
  revision: string;
}

export interface SystemComponentPlacementOptions {
  source: SystemCapabilitySource;
  recordId: string;
  sourceRevision: string;
  relationshipRevision: string;
  canAssignBoundary: boolean;
  assignBlockedReason: string | null;
  boundaries: { id: string; name: string }[];
  placements: (SystemCapabilityPlacement & { canUnassign: boolean; unassignBlockedReason: string | null })[];
}

export interface SystemComponentPlacementResult {
  source: SystemCapabilitySource;
  recordId: string;
  placementId: string;
  boundaryId: string;
  action: 'Assigned' | 'Unassigned';
  relationshipRevision: string;
}

export interface SystemCapabilityRecordReference extends SystemCapabilityRecordKey {
  name: string;
}

export interface SystemCapabilityComponent extends SystemCapabilityRecordKey {
  name: string;
  description: string;
  componentType: string;
  subType: string | null;
  sourceName: string;
  mutationAuthority: string;
  sourceRevision: string;
  placements: SystemCapabilityPlacement[];
  capabilities: SystemCapabilityRecordReference[];
}

export interface SystemCapabilityItem extends SystemCapabilityRecordKey {
  name: string;
  description: string;
  sourceName: string;
  mutationAuthority: string;
  sourceRevision: string;
  isApplied: boolean;
  isAvailable: boolean;
  status: string;
  componentType: string | null;
  subType: string | null;
  components: SystemCapabilityComponent[];
  capabilities: SystemCapabilityRecordReference[];
  placements: SystemCapabilityPlacement[];
  controlIds: string[];
  reviewRequiredCount: number;
}

export interface SystemCapabilityQuery {
  scope?: 'applied' | 'available';
  grouping?: 'capability' | 'component';
  source?: SystemCapabilitySource;
  search?: string;
  componentType?: 'Person' | 'Place' | 'Thing' | 'Policy';
  boundaryId?: string;
  sort?: 'name' | 'source' | 'status' | 'componentType';
  direction?: 'asc' | 'desc';
  page?: number;
  pageSize?: number;
}

export interface SystemCapabilityPage {
  items: SystemCapabilityItem[];
  page: number;
  pageSize: number;
  total: number;
  scope: 'applied' | 'available';
  grouping: 'capability' | 'component';
  permissions: SystemCapabilityAccess;
  boundaries: { id: string; name: string }[];
}

export interface SystemCapabilityDetail {
  item: SystemCapabilityItem;
  permissions: SystemCapabilityAccess;
  baselineId: string | null;
  controls: {
    controlId: string;
    providerCoverage: string | null;
    organizationDuty: string | null;
    allocation: string | null;
    reviewState: string;
    confirmedSourceRevision: string | null;
    availableSourceRevision: string;
    reviewRevision: string | null;
    sourceSnapshot: string | null;
    confirmedSourceSnapshot: string | null;
  }[];
  evidence: {
    id: string;
    fileName: string;
    owner: string;
    source: string;
    state: string;
    controlId: string | null;
    narrativeType: string;
    openUrl: string;
  }[];
  narratives: {
    controlId: string;
    narrativeType: 'Policy' | 'Technical';
    approvedContent: string | null;
    currentContent: string | null;
    approvalStatus: string;
    freshness: string;
    currentVersion: number;
    proposals: {
      id: string;
      revision: number;
      status: string;
      isStale: boolean;
      canReview: boolean;
      source: string;
      recordId: string;
    }[];
    canGenerate: boolean;
    blockedReason: string | null;
  }[];
  relationshipRevision: string;
  responsibilityReviewUrl: string;
}

export interface SystemCapabilitySelection {
  source: SystemCapabilitySource;
  recordId: string;
  sourceRevision: string;
  placements: { source: SystemCapabilitySource; componentId: string; boundaryId: string | null }[];
  supportingCapabilities: { recordId: string; sourceRevision: string }[];
}

export interface SystemCapabilityPlannedWrite {
  writeKind: string;
  writeId: string;
  source: string;
  recordId: string;
  componentId: string | null;
  boundaryId: string | null;
  alreadyExists: boolean;
  controlIds?: string[] | null;
  narrativeTypes?: string[] | null;
  displayLabel?: string | null;
}

export interface SystemCapabilityOperation {
  operationId: string;
  idempotencyKey: string;
  tenantId: string;
  systemId: string;
  kind: 'Setup' | 'Removal';
  state: 'Prepared' | 'Partial' | 'Completed' | 'InProgress';
  revision: number;
  selections: SystemCapabilitySelection[];
  plannedWrites: SystemCapabilityPlannedWrite[];
  outcomes: { writeKind: string; writeId: string; state: string; error: string | null; updatedAt: string }[];
  lastError: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface SystemCapabilityPrepared {
  operation: SystemCapabilityOperation;
  existing: boolean;
}
