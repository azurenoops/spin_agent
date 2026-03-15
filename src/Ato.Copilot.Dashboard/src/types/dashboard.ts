// ─── Enums ─────────────────────────────────────────────────────────────────────

export type AtoSeverity = 'green' | 'yellow' | 'red' | 'expired' | 'none';
export type HeatmapSeverity = 'green' | 'yellow' | 'red' | 'gray';
export type RmfPhaseStatus = 'complete' | 'current' | 'upcoming';
export type ComplianceStatus = 'Satisfied' | 'OtherThanSatisfied' | 'NotAssessed';
export type NarrativeStatus = 'Populated' | 'Empty' | 'Customized';

export type CapabilityStatus = 'Planned' | 'InProgress' | 'Implemented' | 'Deprecated';
export type CapabilityMappingRole = 'Primary' | 'Supporting' | 'Shared';
export type ComponentType = 'Person' | 'Place' | 'Thing';
export type ComponentStatus = 'Active' | 'Planned' | 'Decommissioned';

// ─── Common ────────────────────────────────────────────────────────────────────

export interface PaginatedResponse<T> {
  items: T[];
  nextCursor: string | null;
  totalCount: number;
}

export interface ErrorResponse {
  error: string;
  errorCode: string;
  details: string | null;
  suggestion: string | null;
}

// ─── Portfolio (US1) ───────────────────────────────────────────────────────────

export interface PortfolioSystemSummary {
  systemId: string;
  name: string;
  impactLevel: string;
  currentRmfPhase: string;
  complianceScore: number;
  complianceScoreDelta: number;
  atoExpirationDate: string | null;
  atoStatus: string;
  atoDaysRemaining: number | null;
  atoSeverity: AtoSeverity;
  openPoamCount: number;
  overduePoamCount: number;
  catICounts: number;
  catIICounts: number;
  catIIICounts: number;
}

// ─── System Detail (US2) ──────────────────────────────────────────────────────

export interface RmfPhaseProgress {
  phase: string;
  ordinal: number;
  status: RmfPhaseStatus;
  completionPercent: number;
}

export interface KeyMetrics {
  complianceScore: number;
  complianceScoreDelta: number;
  priorScore: number;
  totalOpenPoams: number;
  overduePoams: number;
  atoDaysRemaining: number | null;
  atoSeverity: AtoSeverity;
  atoExpirationDate: string | null;
  atoStatus: string;
  catIFindings: number;
  catIIFindings: number;
  catIIIFindings: number;
  totalFindings: number;
  narrativeCoverage: number;
}

export interface RecentActivity {
  id: string;
  eventType: string;
  timestamp: string;
  actor: string;
  summary: string;
  relatedEntityType: string | null;
  relatedEntityId: string | null;
}

export interface SystemDetailResponse {
  systemId: string;
  name: string;
  impactLevel: string;
  baselineLevel: string;
  currentRmfPhase: string;
  rmfPhaseProgress: RmfPhaseProgress[];
  keyMetrics: KeyMetrics;
  recentActivity: RecentActivity[];
}

// ─── Heatmap (US2) ────────────────────────────────────────────────────────────

export interface HeatmapFamily {
  familyCode: string;
  familyName: string;
  totalControls: number;
  assessedControls: number;
  satisfiedControls: number;
  compliancePercent: number;
  severity: HeatmapSeverity;
}

export interface HeatmapResponse {
  systemId: string;
  baselineLevel: string;
  families: HeatmapFamily[];
}

export interface HeatmapControl {
  controlId: string;
  controlTitle: string;
  complianceStatus: ComplianceStatus;
  hasNarrative: boolean;
  isManuallyCustomized: boolean;
  securityCapabilityName: string | null;
}

export interface HeatmapControlsResponse {
  systemId: string;
  familyCode: string;
  familyName: string;
  controls: HeatmapControl[];
}

// ─── Trends (US6) ──────────────────────────────────────────────────────────────

export interface TrendDataPoint {
  date: string;
  complianceScore: number;
  catICount: number;
  catIICount: number;
  catIIICount: number;
  openPoamCount: number;
  overduePoamCount: number;
  narrativeCoverage: number;
  isSignificantDecline: boolean;
}

export interface TrendResponse {
  systemId: string;
  granularity: string;
  dataPoints: TrendDataPoint[];
}

// ─── Security Capabilities (US3) ──────────────────────────────────────────────

export interface SecurityCapabilityDto {
  id: string;
  name: string;
  provider: string;
  category: string;
  categoryName: string;
  description: string;
  implementationStatus: CapabilityStatus;
  owner: string;
  mappedControlCount: number;
  systemsUsingCount: number;
  createdAt: string;
  modifiedAt: string | null;
}

export interface CreateCapabilityRequest {
  name: string;
  provider: string;
  category: string;
  description: string;
  implementationStatus: CapabilityStatus;
  owner: string;
}

export interface UpdateCapabilityResponse extends SecurityCapabilityDto {
  narrativesUpdated: number;
  narrativesSkipped: number;
}

export interface CapabilityMappingDto {
  id: string;
  controlId: string;
  controlTitle: string;
  controlFamily: string;
  role: CapabilityMappingRole;
  registeredSystemId: string | null;
  registeredSystemName: string | null;
  narrativeStatus: NarrativeStatus;
  isManuallyCustomized: boolean;
}

export interface CreateMappingsRequest {
  mappings: { controlId: string; role: CapabilityMappingRole; registeredSystemId?: string }[];
}

export interface CreateMappingsResponse {
  created: number;
  warnings: string[];
  narrativesGenerated: number;
}

// ─── Gap Analysis (US4) ──────────────────────────────────────────────────────

export interface GapFamilyBreakdown {
  familyCode: string;
  familyName: string;
  totalControls: number;
  coveredControls: number;
  gapCount: number;
  coveragePercent: number;
  isBelow50: boolean;
  unmappedControls: { controlId: string; controlTitle: string }[];
}

export interface GapAnalysisResponse {
  systemId: string;
  baselineLevel: string;
  totalBaselineControls: number;
  coveredControls: number;
  gapCount: number;
  coveragePercent: number;
  familyBreakdown: GapFamilyBreakdown[];
}

// ─── Components (US5) ─────────────────────────────────────────────────────────

export interface SystemComponentDto {
  id: string;
  name: string;
  componentType: ComponentType;
  subType: string | null;
  description: string | null;
  owner: string | null;
  status: ComponentStatus;
  linkedCapabilities: { capabilityId: string; capabilityName: string }[];
  createdAt: string;
  modifiedAt: string | null;
}

export interface CreateComponentRequest {
  name: string;
  componentType: ComponentType;
  subType?: string;
  description?: string;
  owner?: string;
  status: ComponentStatus;
  linkedCapabilityIds?: string[];
}

export interface ComponentSummary {
  personCount: number;
  placeCount: number;
  thingCount: number;
  totalCount: number;
}

export interface DeleteComponentResponse {
  deletedId: string;
  flaggedCapabilities: { capabilityId: string; capabilityName: string; message: string }[];
}
