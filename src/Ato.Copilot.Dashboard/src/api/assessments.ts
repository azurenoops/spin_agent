import apiClient from './client';

// ─── Types ──────────────────────────────────────────────────────────────────

export interface AssessmentListItem {
  assessmentId: string;
  systemId: string | null;
  systemName: string | null;
  framework: string;
  status: string;
  scanType: string;
  complianceScore: number;
  totalControls: number;
  passedControls: number;
  failedControls: number;
  totalFindings: number;
  assessedAt: string;
  initiatedBy: string;
  hasCategorization: boolean;
}

export interface RunAssessmentResponse {
  assessmentId: string;
  status: string;
  systemId: string;
}

export type AssessmentCloud = 'Commercial' | 'Government';

export interface AssessmentSubscription {
  subscriptionId: string;
  displayName: string;
}

export interface AssessmentReadiness {
  systemId: string;
  isReady: boolean;
  errorCode: string | null;
  message: string;
  suggestion: string | null;
  configurationUrl: string;
  deploymentCloud: AssessmentCloud | null;
  cloudEnvironment: string | null;
  subscriptions: AssessmentSubscription[];
  checkedAt: string;
}

export interface AssessmentEnvironment {
  systemId: string;
  deploymentCloud: AssessmentCloud;
  cloudEnvironment: string | null;
  subscriptionIds: string[];
  availableSubscriptions: (AssessmentSubscription & {
    cloudEnvironment: string;
    isAvailable: boolean;
  })[];
}

export interface UpdateAssessmentEnvironment {
  cloudEnvironment: AssessmentCloud;
  subscriptionIds: string[];
}

export interface AssessmentFamilyResult {
  familyCode: string;
  familyName: string;
  totalControls: number;
  passedControls: number;
  failedControls: number;
  complianceScore: number;
}

export interface AssessmentFinding {
  findingId: string;
  controlId: string | null;
  controlFamily: string;
  title: string;
  description: string;
  severity: string;
  status: string;
  resourceType: string | null;
  resourceId: string | null;
  remediationGuidance: string | null;
  discoveredAt: string;
  deviationId: string | null;
  deviationType: string | null;
}

export interface AssessmentDetail {
  assessmentId: string;
  systemId: string | null;
  systemName: string | null;
  framework: string;
  scanType: string;
  status: string;
  complianceScore: number;
  totalControls: number;
  passedControls: number;
  failedControls: number;
  notAssessedControls: number;
  assessedAt: string;
  completedAt: string | null;
  initiatedBy: string | null;
  executiveSummary: string | null;
  criticalCount: number;
  highCount: number;
  mediumCount: number;
  lowCount: number;
  familyResults: AssessmentFamilyResult[];
  findings: AssessmentFinding[];
}

// ─── API ────────────────────────────────────────────────────────────────────

export async function getAssessments(): Promise<AssessmentListItem[]> {
  const { data } = await apiClient.get<AssessmentListItem[]>('/assessments');
  return data;
}

export async function getAssessmentReadiness(systemId: string): Promise<AssessmentReadiness> {
  const { data } = await apiClient.get<AssessmentReadiness>(`/systems/${encodeURIComponent(systemId)}/assessment-readiness`);
  return data;
}

export async function getAssessmentEnvironment(systemId: string): Promise<AssessmentEnvironment> {
  const { data } = await apiClient.get<AssessmentEnvironment>(`/systems/${encodeURIComponent(systemId)}/assessment-environment`);
  return data;
}

export async function saveAssessmentEnvironment(systemId: string, request: UpdateAssessmentEnvironment): Promise<AssessmentEnvironment> {
  const { data } = await apiClient.put<AssessmentEnvironment>(`/systems/${encodeURIComponent(systemId)}/assessment-environment`, request);
  return data;
}

export async function detachAssessmentEnvironment(systemId: string): Promise<void> {
  await apiClient.delete(`/systems/${encodeURIComponent(systemId)}/assessment-environment`);
}

export async function runAssessment(systemId: string): Promise<RunAssessmentResponse> {
  const { data } = await apiClient.post<RunAssessmentResponse>(`/systems/${encodeURIComponent(systemId)}/run-assessment`);
  return data;
}

export async function getAssessmentDetail(assessmentId: string): Promise<AssessmentDetail> {
  const { data } = await apiClient.get<AssessmentDetail>(`/assessments/${encodeURIComponent(assessmentId)}`);
  return data;
}
