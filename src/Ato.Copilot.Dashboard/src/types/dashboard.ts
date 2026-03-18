// ─── Enums ─────────────────────────────────────────────────────────────────────

export type AtoSeverity = 'green' | 'yellow' | 'red' | 'expired' | 'none';
export type HeatmapSeverity = 'green' | 'yellow' | 'red' | 'gray';
export type RmfPhaseStatus = 'complete' | 'current' | 'upcoming';
export type ComplianceStatus = 'Satisfied' | 'OtherThanSatisfied' | 'NotAssessed';
export type NarrativeStatus = 'Populated' | 'Empty' | 'Customized';

export type CapabilityStatus = 'Planned' | 'InProgress' | 'Implemented' | 'Deprecated';
export type CapabilityMappingRole = 'Primary' | 'Supporting' | 'Shared';
export type ComponentType = 'Person' | 'Place' | 'Thing' | 'Policy';
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
  acronym: string | null;
  systemType: string;
  missionCriticality: string;
  hostingEnvironment: string;
  description: string | null;
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
  activeDeviations: number;
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
  acronym: string | null;
  systemType: string;
  missionCriticality: string;
  hostingEnvironment: string;
  impactLevel: string;
  baselineLevel: string;
  currentRmfPhase: string;
  rmfPhaseProgress: RmfPhaseProgress[];
  keyMetrics: KeyMetrics;
  recentActivity: RecentActivity[];
  categorization: CategorizationInfo | null;
}

export interface CategorizationInfo {
  confidentiality: string;
  integrity: string;
  availability: string;
  overall: string;
  formalNotation: string;
  dodImpactLevel: string;
  informationTypes: InfoTypeInfo[];
}

export interface InfoTypeInfo {
  name: string;
  confidentiality: string;
  integrity: string;
  availability: string;
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
  boundaryDefinitionId: string | null;
  boundaryDefinitionName: string | null;
  narrativeStatus: NarrativeStatus;
  isManuallyCustomized: boolean;
}

export interface CreateMappingsRequest {
  mappings: { controlId: string; role: CapabilityMappingRole; registeredSystemId?: string; boundaryDefinitionId?: string }[];
}

export interface MappingWarning {
  controlId: string;
  message: string;
}

export interface CreateMappingsResponse {
  created: number;
  warnings: MappingWarning[];
  narrativesGenerated: number;
}

// ─── Gap Analysis (US4) ──────────────────────────────────────────────────────

export interface GapFamilyBreakdown {
  familyCode: string;
  familyName: string;
  totalControls: number;
  coveredControls: number;
  waivedControls: number;
  gapCount: number;
  coveragePercent: number;
  isBelow50: boolean;
  unmappedControls: { controlId: string; controlTitle: string }[];
  waivedControlIds: string[];
}

export interface GapAnalysisResponse {
  systemId: string;
  baselineLevel: string;
  totalBaselineControls: number;
  coveredControls: number;
  waivedControls: number;
  gapCount: number;
  coveragePercent: number;
  familyBreakdown: GapFamilyBreakdown[];
  boundaryComparison?: BoundaryComparisonItem[] | null;
}

export interface BoundaryComparisonItem {
  boundaryId: string;
  boundaryName: string;
  boundaryType: string;
  isPrimary: boolean;
  totalControls: number;
  coveredControls: number;
  waivedControls: number;
  gapCount: number;
  coveragePercent: number;
}

// ─── Components (US5) ─────────────────────────────────────────────────────────

export interface SystemComponentDto {
  id: string;
  name: string;
  componentType: ComponentType;
  subType: string | null;
  description: string | null;
  owner: string | null;
  personName: string | null;
  email: string | null;
  status: ComponentStatus;
  boundaryDefinitionId: string | null;
  boundaryDefinitionName: string | null;
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
  personName?: string;
  email?: string;
  status: ComponentStatus;
  boundaryDefinitionId?: string;
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

// ─── Implementation Roadmap (Feature 031) ──────────────────────────────────────

export interface Roadmap {
  roadmapId: string;
  systemId: string;
  systemName: string;
  status: string;
  baselineLevel: string;
  totalGaps: number;
  totalEstimatedEffortDays: number;
  totalRiskPoints: number;
  overallCompletionPercent: number;
  phases: RoadmapPhase[];
  createdAt: string;
  updatedAt: string;
}

export interface RoadmapPhase {
  phaseId: string;
  name: string;
  displayOrder: number;
  estimatedEffortDays: number;
  riskPoints: number;
  riskReductionPercent: number;
  targetStartWeek: number | null;
  targetEndWeek: number | null;
  status: string;
  completedItemCount: number;
  totalItemCount: number;
  items?: RoadmapItem[];
}

export interface RoadmapItem {
  itemId: string;
  controlId: string;
  controlTitle: string;
  controlFamily: string;
  gapType: string;
  severity: string;
  riskPoints: number;
  estimatedEffortDays: number;
  assignedRole: string;
  dependsOn: string[] | null;
  status: string;
  linkedTaskId: string | null;
}

export interface RoadmapProgress {
  roadmapId: string;
  systemName: string;
  overallCompletionPercent: number;
  itemsCompleted: number;
  itemsTotal: number;
  riskCurve: RiskCurvePoint[];
  phaseProgress: PhaseProgress[];
}

export interface RiskCurvePoint {
  week: number;
  riskPoints: number;
  riskReductionPercent: number;
}

export interface PhaseProgress {
  name: string;
  displayOrder: number;
  completionPercent: number;
  status: string;
  actualRiskReductionPercent: number;
  isOverdue: boolean;
  daysOverdue: number;
}

// ─── Todo List ─────────────────────────────────────────────────────────────────

export interface TodoList {
  systemId: string;
  systemName: string;
  currentPhase: string;
  nextPhase: string | null;
  items: TodoItem[];
}

export interface TodoItem {
  id: string;
  label: string;
  detail: string;
  category: string;
  prompt?: string;
  link?: string;
  /** Present when category === 'deferred' — the deferred prerequisite GUID */
  deferredId?: string;
}

// ─── Boundary Definitions (Feature 033) ────────────────────────────────────────

export type BoundaryDefinitionType = 'Physical' | 'Logical' | 'Hybrid';

export interface BoundaryDefinitionDto {
  id: string;
  registeredSystemId: string;
  name: string;
  boundaryType: BoundaryDefinitionType;
  description: string | null;
  isPrimary: boolean;
  resourceCount: number;
  componentCount: number;
  coveragePercent: number;
  createdAt: string;
}

export interface CreateBoundaryDefinitionRequest {
  name: string;
  boundaryType: string;
  description?: string | null;
}

export interface DeleteBoundaryDefinitionResponse {
  deletedId: string;
  reassignedComponents: number;
  reassignedMappings: number;
  reassignedResources: number;
  primaryBoundaryId: string;
}

// ─── Azure Resource Discovery (Feature 033 US8) ────────────────────────────

export interface AzureDiscoveredResourceDto {
  resourceId: string;
  name: string;
  type: string;
  resourceGroup: string;
  location: string;
  alreadyInBoundary: boolean;
}

export interface AzureSuggestedBoundaryDto {
  resourceGroupName: string;
  boundaryType: string;
  resourceCount: number;
  alreadyExists: boolean;
  resources: AzureDiscoveredResourceDto[];
}

export interface AzureDiscoveryResponse {
  suggestedBoundaries: AzureSuggestedBoundaryDto[];
  nextCursor: string | null;
  totalResourceCount: number;
}

export interface ApplyBoundaryItem {
  resourceGroupName: string;
  name: string;
  boundaryType: string;
  description?: string;
}

export interface ApplyComponentItem {
  boundaryDefinitionId?: string;
  resourceId: string;
  name: string;
  subType?: string;
}

export interface ApplyDiscoveryRequest {
  boundaries: ApplyBoundaryItem[];
  components: ApplyComponentItem[];
}

export interface ApplyDiscoveryResponse {
  boundariesCreated: number;
  componentsCreated: number;
  skipped: number;
}

// ─── Deviations (Feature 035) ──────────────────────────────────────────────────

export type DeviationType = 'FalsePositive' | 'RiskAcceptance' | 'Waiver';
export type DeviationStatus = 'Pending' | 'Approved' | 'Denied' | 'Expired' | 'Revoked';

export interface DeviationListItem {
  id: string;
  deviationType: string;
  controlId: string;
  catSeverity: number;
  status: string;
  justification: string;
  expirationDate: string;
  daysUntilExpiration: number;
  requestedBy: string;
  requestedAt: string;
  reviewedBy: string | null;
  reviewedAt: string | null;
  evidenceCount: number;
  findingId: string | null;
  poamEntryId: string | null;
  boundaryDefinitionId: string | null;
}

export interface DeviationListResponse {
  items: DeviationListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface DeviationSummary {
  total: number;
  pending: number;
  approved: number;
  denied: number;
  expired: number;
  revoked: number;
  expiringWithin30d: number;
  catI: number;
  catII: number;
  catIII: number;
  withoutEvidence: number;
}

export interface DeviationFindingRef {
  id: string;
  controlId: string;
  status: string;
  severity: string;
}

export interface DeviationPoamRef {
  id: string;
  weakness: string;
  status: string;
}

export interface DeviationEvidenceRef {
  scanImportRecordId: string;
  fileName: string;
  scanType: string;
  scanDate: string | null;
  benchmarkTitle: string | null;
}

export interface DeviationAuditEntry {
  eventType: string;
  actor: string;
  timestamp: string;
  summary: string;
}

export interface DeviationDetail {
  id: string;
  deviationType: string;
  controlId: string;
  catSeverity: number;
  status: string;
  justification: string;
  compensatingControls: string | null;
  evidenceReferences: string[];
  expirationDate: string;
  reviewCycle: string;
  requestedBy: string;
  requestedAt: string;
  reviewedBy: string | null;
  reviewedAt: string | null;
  reviewerRole: string | null;
  reviewerComments: string | null;
  issmRecommendation: string | null;
  issmRecommendedBy: string | null;
  issmRecommendedAt: string | null;
  revokedBy: string | null;
  revokedAt: string | null;
  revocationReason: string | null;
  boundaryDefinitionId: string | null;
  boundaryDefinitionName: string | null;
  finding: DeviationFindingRef | null;
  poamEntry: DeviationPoamRef | null;
  evidence: DeviationEvidenceRef[];
  auditTrail: DeviationAuditEntry[];
}

export interface CreateDeviationRequest {
  deviationType: string;
  controlId: string;
  catSeverity: string;
  justification: string;
  compensatingControls?: string;
  evidenceIds?: string[];
  expirationDate: string;
  reviewCycle: string;
  findingId?: string;
  poamEntryId?: string;
  boundaryDefinitionId?: string;
}

export interface ReviewDeviationRequest {
  decision: string;
  comments?: string;
}

export interface RevokeDeviationRequest {
  reason: string;
}

export interface ExtendDeviationRequest {
  newExpirationDate: string;
  justification?: string;
}
