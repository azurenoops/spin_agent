// Types for Capabilities Hub import/coverage operations (Feature 045)

// ─── CSP Profile Import ────────────────────────────────────────────────────

export interface CspImportRequest {
  profileId: string;
  conflictResolution?: 'skip' | 'overwrite';
  dryRun?: boolean;
}

export interface CspImportResult {
  profileName: string;
  componentsCreated: number;
  componentsReused: number;
  capabilitiesCreated: number;
  capabilitiesReused: number;
  controlMappingsCreated: number;
  orgDefaultsDerived: number;
  systemsAffected: number;
  narrativesGenerated: number;
  conflicts: number;
  skipped: number;
  dryRun: boolean;
}

export interface CspImportPreview {
  profileName: string;
  componentsToCreate: number;
  componentsToReuse: number;
  capabilitiesToCreate: number;
  capabilitiesToReuse: number;
  controlMappingsToCreate: number;
  conflicts: number;
  conflictDetails: ConflictDetail[];
  systemsAffected: number;
  dryRun: boolean;
}

export interface ConflictDetail {
  controlId: string;
  existingRole: string;
  newRole: string;
  resolution: string;
}

// ─── CRM Import ─────────────────────────────────────────────────────────────

export interface CrmColumnMapping {
  controlId: string;
  inheritanceType: string;
  provider: string;
  customerResponsibility: string;
}

export interface CrmImportResult {
  fileName: string;
  rowsParsed: number;
  componentsCreated: number;
  componentsReused: number;
  capabilitiesCreated: number;
  capabilitiesReused: number;
  controlMappingsCreated: number;
  unmatchedRows: number;
  orgDefaultsDerived: number;
  systemsAffected: number;
  narrativesGenerated: number;
  conflicts: number;
  dryRun: boolean;
}

export interface CrmImportPreview {
  fileName: string;
  rowsParsed: number;
  componentsToCreate: number;
  componentsToReuse: number;
  capabilitiesToCreate: number;
  capabilitiesToReuse: number;
  controlMappingsToCreate: number;
  unmatchedRows: number;
  conflicts: number;
  conflictDetails: ConflictDetail[];
  systemsAffected: number;
  dryRun: boolean;
  detectedColumns: string[];
  sampleRows: Record<string, string>[];
}

// ─── Coverage ───────────────────────────────────────────────────────────────

export interface CoverageResponse {
  orgWide: OrgWideCoverage;
  perSystem: SystemCoverage[];
}

export interface OrgWideCoverage {
  totalCapabilities: number;
  mappedControls: number;
  unmappedControls: number | null;
  coveragePercent: number | null;
  baselineLevel: string | null;
  baselineControlCount: number | null;
  perFamily: FamilyCoverage[];
}

export interface FamilyCoverage {
  family: string;
  mapped: number;
  total: number;
  percent: number;
}

export interface SystemCoverage {
  systemId: string;
  systemName: string;
  baselineLevel: string;
  coveragePercent: number;
  mappedControls: number;
  totalControls: number;
}

// ─── Component Linking ──────────────────────────────────────────────────────

export interface LinkComponentCapabilitiesRequest {
  capabilityIds: string[];
}

export interface LinkComponentCapabilitiesResponse {
  componentId: string;
  linksCreated: number;
  linksAlreadyExist: number;
}

// ─── Enhanced Capability (with component badges) ────────────────────────────

export interface LinkedComponent {
  id: string;
  name: string;
  componentType: string;
}
