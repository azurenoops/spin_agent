import apiClient from './client';
import type {
  SystemDetailResponse,
  HeatmapResponse,
  HeatmapControlsResponse,
} from '../types/dashboard';

export async function getSystemDetail(systemId: string): Promise<SystemDetailResponse> {
  const { data } = await apiClient.get<SystemDetailResponse>(`/systems/${systemId}`);
  return data;
}

export async function getHeatmap(systemId: string): Promise<HeatmapResponse> {
  const { data } = await apiClient.get<HeatmapResponse>(`/systems/${systemId}/heatmap`);
  return data;
}

export async function getHeatmapControls(
  systemId: string,
  familyCode: string,
): Promise<HeatmapControlsResponse> {
  const { data } = await apiClient.get<HeatmapControlsResponse>(
    `/systems/${systemId}/heatmap/${familyCode}/controls`,
  );
  return data;
}

export interface AdvanceRmfStepResponse {
  success: boolean;
  previousStep: string;
  newStep: string;
  wasForced?: boolean;
  error?: string;
  gateResults?: {
    gateName: string;
    passed: boolean;
    message: string;
    severity: string;
  }[];
}

export async function advanceRmfStep(
  systemId: string,
  targetStep: string,
  force = false,
): Promise<AdvanceRmfStepResponse> {
  const { data } = await apiClient.post<AdvanceRmfStepResponse>(
    `/systems/${systemId}/advance-rmf-step`,
    { targetStep, force },
  );
  return data;
}

// ─── Phase Readiness ──────────────────────────────────────────────────────

export interface GateResult {
  gateName: string;
  passed: boolean;
  message: string;
  severity: string;
}

export interface PhaseReadinessResponse {
  currentPhase: string;
  nextPhase: string | null;
  ready: boolean;
  gateResults: GateResult[];
}

export async function getPhaseReadiness(systemId: string): Promise<PhaseReadinessResponse> {
  const { data } = await apiClient.get<PhaseReadinessResponse>(
    `/systems/${systemId}/phase-readiness`,
  );
  return data;
}

export interface CreatePtaRequest {
  collectsPii: boolean;
  maintainsPii: boolean;
  disseminatesPii: boolean;
  piiCategories?: string[];
  estimatedRecordCount?: number;
  purpose?: string;
}

export interface CreatePtaResponse {
  ptaId: string;
  determination: string;
  collectsPii: boolean;
  piiCategories: string[];
  rationale: string;
}

export async function createPta(
  systemId: string,
  body: CreatePtaRequest,
): Promise<CreatePtaResponse> {
  const { data } = await apiClient.post<CreatePtaResponse>(
    `/systems/${systemId}/pta`,
    body,
  );
  return data;
}

export interface AddInterconnectionRequest {
  remoteSystem: string;
  hostname?: string;
  direction: string;
  type?: string;
  protocol?: string;
  port?: string;
  dataClassification?: string;
}

export interface AddInterconnectionResponse {
  interconnectionId: string;
  targetSystemName: string;
  direction: string;
  status: string;
}

export async function addInterconnection(
  systemId: string,
  body: AddInterconnectionRequest,
): Promise<AddInterconnectionResponse> {
  const { data } = await apiClient.post<AddInterconnectionResponse>(
    `/systems/${systemId}/interconnections`,
    body,
  );
  return data;
}

export async function certifyNoInterconnections(
  systemId: string,
): Promise<{ certified: boolean }> {
  const { data } = await apiClient.post<{ certified: boolean }>(
    `/systems/${systemId}/certify-no-interconnections`,
  );
  return data;
}

export interface GenerateApprovePiaResponse {
  piaId: string;
  status: string;
  expirationDate: string | null;
}

export async function generateAndApprovePia(
  systemId: string,
): Promise<GenerateApprovePiaResponse> {
  const { data } = await apiClient.post<GenerateApprovePiaResponse>(
    `/systems/${systemId}/generate-approve-pia`,
  );
  return data;
}

// ─── Categorization ───────────────────────────────────────────────────────

export interface InfoTypeInput {
  sp80060Id: string;
  name: string;
  category?: string;
  confidentialityImpact: string;
  integrityImpact: string;
  availabilityImpact: string;
  usesProvisional?: boolean;
  adjustmentJustification?: string;
}

export interface SetCategorizationRequest {
  isNationalSecuritySystem?: boolean;
  justification?: string;
  informationTypes: InfoTypeInput[];
}

export interface SetCategorizationResponse {
  id: string;
  overallCategorization: string;
  confidentialityImpact: string;
  integrityImpact: string;
  availabilityImpact: string;
  dodImpactLevel: string;
  nistBaseline: string;
  informationTypeCount: number;
}

export async function setCategorization(
  systemId: string,
  body: SetCategorizationRequest,
): Promise<SetCategorizationResponse> {
  const { data } = await apiClient.post<SetCategorizationResponse>(
    `/systems/${systemId}/categorization`,
    body,
  );
  return data;
}

// ─── Select Baseline ───────────────────────────────────────────────────────

export interface SelectBaselineRequest {
  applyOverlay: boolean;
  overlayName?: string;
}

export interface SelectBaselineResponse {
  baselineId: string;
  baselineLevel: string;
  totalControls: number;
  overlayApplied: string | null;
}

export async function selectBaseline(
  systemId: string,
  body: SelectBaselineRequest,
): Promise<SelectBaselineResponse> {
  const { data } = await apiClient.post<SelectBaselineResponse>(
    `/systems/${systemId}/baseline`,
    body,
  );
  return data;
}
