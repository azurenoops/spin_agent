// ─── Enums & Constants ──────────────────────────────────────────────────────

export type InheritanceType = 'Inherited' | 'Shared' | 'Customer';
export type InheritanceTypeOrUndesignated = InheritanceType | 'Undesignated';
export type InheritanceChangeSource = 'Manual' | 'BulkUpdate' | 'ProfileApply' | 'CrmImport' | 'OrgDerived' | 'OrgPropagation';

export type DesignationSource = 'OrgDerived' | 'Manual' | 'ProfileApply' | 'CrmImport' | 'BulkUpdate';
export type DesignationSourceFilter = 'org' | 'override' | 'undesignated';

// ─── Org-Level Defaults ─────────────────────────────────────────────────────

export interface OrgDefaultInfo {
  id: string;
  inheritanceType: string;
  provider: string;
  sourceCapabilities: string;
  mappingRole: string;
}

export interface OrgInheritanceDefault {
  id: string;
  controlId: string;
  inheritanceType: string;
  provider: string;
  sourceCapabilityIds: string;
  sourceCapabilityNames: string;
  mappingRole: string;
  derivedAt: string;
}

export interface OrgDefaultsQuery {
  family?: string;
  inheritanceType?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

export interface OrgDefaultsListResult {
  items: OrgInheritanceDefault[];
  totalCount: number;
  summary: {
    inheritedCount: number;
    sharedCount: number;
    totalControls: number;
  };
}

export interface OrgDerivationResult {
  derivedCount: number;
  inheritedCount: number;
  sharedCount: number;
  removedCount: number;
  affectedSystems: number;
  derivedAt: string;
}

export interface RevertResult {
  revertedCount: number;
  skipped: { controlId: string; reason: string }[];
}

// ─── List / Table ───────────────────────────────────────────────────────────

export interface InheritanceDesignation {
  id: string;
  controlId: string;
  family: string;
  inheritanceType: InheritanceTypeOrUndesignated;
  provider: string | null;
  customerResponsibility: string | null;
  designationSource: DesignationSource | null;
  orgDefault: OrgDefaultInfo | null;
  setBy: string;
  setAt: string;
}

export interface SourceBreakdown {
  orgDerived: number;
  manual: number;
  profileApply: number;
  crmImport: number;
  bulkUpdate: number;
  undesignated: number;
}

export interface InheritanceSummary {
  totalControls: number;
  inheritedCount: number;
  sharedCount: number;
  customerCount: number;
  undesignatedCount: number;
  inheritancePercentage: number;
  orgDefaultCount: number;
  systemOverrideCount: number;
  sourceBreakdown: SourceBreakdown;
}

export interface InheritanceListResponse {
  items: InheritanceDesignation[];
  totalItems: number;
  page: number;
  pageSize: number;
  summary: InheritanceSummary;
}

export interface InheritanceListQuery {
  family?: string;
  inheritanceType?: string;
  source?: DesignationSourceFilter;
  search?: string;
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDirection?: 'asc' | 'desc';
}

// ─── Set / Update ───────────────────────────────────────────────────────────

export interface DesignationInput {
  controlId: string;
  inheritanceType: InheritanceType;
  provider?: string;
  customerResponsibility?: string;
}

export interface SetInheritanceRequest {
  designations: DesignationInput[];
  changeSource: InheritanceChangeSource;
}

export interface SetInheritanceResponse {
  controlsUpdated: number;
  inheritedCount: number;
  sharedCount: number;
  customerCount: number;
  skippedControls: string[];
  narrativesAutoUpdated: number;
  summary: InheritanceSummary;
}

// ─── CRM ────────────────────────────────────────────────────────────────────

export interface CrmEntry {
  controlId: string;
  inheritanceType: InheritanceTypeOrUndesignated;
  provider: string | null;
  customerResponsibility: string | null;
}

export interface CrmFamilyGroup {
  family: string;
  familyName: string;
  controls: CrmEntry[];
}

export interface CrmResult {
  systemId: string;
  systemName: string;
  baselineLevel: string;
  totalControls: number;
  inheritedControls: number;
  sharedControls: number;
  customerControls: number;
  undesignatedControls: number;
  inheritancePercentage: number;
  familyGroups: CrmFamilyGroup[];
}

export type CrmExportFormat = 'csv' | 'excel';
export type CrmExportLayout = 'custom' | 'fedramp' | 'emass';

// ─── Audit ──────────────────────────────────────────────────────────────────

export interface AuditEntry {
  id: string;
  actor: string;
  previousInheritanceType: string | null;
  newInheritanceType: string;
  previousProvider: string | null;
  newProvider: string | null;
  previousCustomerResponsibility: string | null;
  newCustomerResponsibility: string | null;
  changeSource: InheritanceChangeSource;
  timestamp: string;
}

export interface AuditHistoryResponse {
  controlId: string;
  entries: AuditEntry[];
}

// ─── CSP Profiles ───────────────────────────────────────────────────────────

export interface CspProfile {
  profileId: string;
  name: string;
  provider: string;
  baselineLevel: string;
  description: string;
  controlCount: number;
  version: string;
}

export interface CspProfilesResponse {
  profiles: CspProfile[];
}

export interface ApplyProfileRequest {
  profileId: string;
  conflictResolution: 'skip' | 'overwrite';
  preview: boolean;
}

export interface ApplyProfilePreview {
  preview: true;
  profileName: string;
  matchedControls: number;
  unmatchedControls: number;
  willSetInherited: number;
  willSetShared: number;
  willSetCustomer: number;
  willSkipExisting: number;
  conflicts: number;
}

export interface ApplyProfileResult {
  applied: true;
  controlsUpdated: number;
  controlsSkipped: number;
  narrativesAutoUpdated: number;
  summary: InheritanceSummary;
}

// ─── CRM Import ─────────────────────────────────────────────────────────────

export interface ImportPreview {
  fileName: string;
  fileType: string;
  totalRows: number;
  detectedColumns: string[];
  suggestedMapping: Record<string, string>;
  sampleRows: Record<string, string>[];
  previewToken: string;
}

export interface ImportApplyRequest {
  previewToken: string;
  columnMapping: {
    controlId: string;
    inheritanceType: string;
    provider: string;
    customerResponsibility: string;
  };
  conflictResolution: 'skip' | 'overwrite';
}

export interface ImportApplyResult {
  applied: true;
  controlsImported: number;
  controlsSkipped: number;
  controlsNotFound: number;
  notFoundControlIds: string[];
  duplicatesOverwritten: number;
  narrativesAutoUpdated: number;
  summary: InheritanceSummary;
}
