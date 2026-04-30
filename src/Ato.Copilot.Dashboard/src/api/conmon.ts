import apiClient from './client';

export interface ConMonPlanDetail {
  planId: string;
  assessmentFrequency: string;
  annualReviewDate: string;
  reportDistribution: string[];
  significantChangeTriggers: string[];
  createdAt: string;
  modifiedAt: string | null;
}

export interface ConMonStatus {
  currentComplianceScore: number;
  authorizedBaselineScore: number | null;
  scoreDelta: number | null;
  openFindings: number;
  resolvedFindings: number;
  openPoamItems: number;
  overduePoamItems: number;
  monitoringEnabled: boolean;
  driftAlertCount: number;
  autoRemediationRuleCount: number;
  lastMonitoringCheck: string | null;
}

export interface ConMonExpiration {
  hasActiveAuthorization: boolean;
  decisionType: string | null;
  decisionDate: string | null;
  expirationDate: string | null;
  daysUntilExpiration: number | null;
  alertLevel: string;
  alertMessage: string;
  isExpired: boolean;
}

export interface ConMonReauthorization {
  isTriggered: boolean;
  triggers: string[];
  unreviewedChangeCount: number;
}

export interface AgreementExpirationInfo {
  itemType: string;
  agreementTitle: string;
  targetSystemName: string | null;
  expirationDate: string | null;
  daysUntilExpiration: number;
  alertLevel: string;
  message: string;
}

export interface SignificantChangeItem {
  id: string;
  changeType: string;
  description: string;
  detectedAt: string;
  detectedBy: string;
  requiresReauthorization: boolean;
  reauthorizationTriggered: boolean;
  reviewedBy: string | null;
  reviewedAt: string | null;
  disposition: string | null;
}

export interface ConMonReportSummary {
  reportId: string;
  reportType: string;
  period: string;
  complianceScore: number;
  authorizedBaselineScore: number | null;
  scoreDelta: number | null;
  newFindings: number;
  resolvedFindings: number;
  openPoamItems: number;
  overduePoamItems: number;
  generatedAt: string;
  generatedBy: string;
}

export interface ConMonOverviewResponse {
  systemId: string;
  systemName: string;
  currentPhase: string;
  plan: ConMonPlanDetail | null;
  status: ConMonStatus;
  expiration: ConMonExpiration;
  reauthorization: ConMonReauthorization;
  agreementAlerts: AgreementExpirationInfo[];
  significantChanges: SignificantChangeItem[];
  reports: ConMonReportSummary[];
}

export async function getConMonOverview(
  systemId: string,
): Promise<ConMonOverviewResponse> {
  const { data } = await apiClient.get<ConMonOverviewResponse>(
    `/systems/${systemId}/conmon`,
  );
  return data;
}

// ─── Write actions ────────────────────────────────────────────────────────────

export interface CreateConMonPlanBody {
  assessmentFrequency: string;
  annualReviewDate: string; // ISO date
  reportDistribution: string[];
  significantChangeTriggers: string[];
}

export interface GenerateConMonReportBody {
  reportType: string;
  period: string; // e.g. "2026-04"
}

export interface ReportSignificantChangeBody {
  changeType: string;
  description: string;
  detectedBy?: string;
}

export interface ReauthorizationCheckBody {
  initiateIfTriggered: boolean;
}

export interface ConMonReportDetail extends ConMonReportSummary {
  reportContent: string;
}

export interface ReauthorizationCheckResult {
  isTriggered: boolean;
  triggers: string[];
  unreviewedChangeCount: number;
  initiated: boolean;
}

export async function createConMonPlan(
  systemId: string,
  body: CreateConMonPlanBody,
): Promise<{ id: string; assessmentFrequency: string; annualReviewDate: string }> {
  const { data } = await apiClient.post(
    `/systems/${systemId}/conmon-plan`,
    body,
  );
  return data;
}

export async function generateConMonReport(
  systemId: string,
  body: GenerateConMonReportBody,
): Promise<{ id: string; reportType: string; period: string; complianceScore: number; scoreDelta: number | null }> {
  const { data } = await apiClient.post(
    `/systems/${systemId}/conmon-report`,
    body,
  );
  return data;
}

export async function getConMonReport(
  systemId: string,
  reportId: string,
): Promise<ConMonReportDetail> {
  const { data } = await apiClient.get<ConMonReportDetail>(
    `/systems/${systemId}/conmon/reports/${reportId}`,
  );
  return data;
}

export async function reportSignificantChange(
  systemId: string,
  body: ReportSignificantChangeBody,
): Promise<{ id: string; changeType: string; requiresReauthorization: boolean; reauthorizationTriggered: boolean }> {
  const { data } = await apiClient.post(
    `/systems/${systemId}/conmon/significant-change`,
    body,
  );
  return data;
}

export async function checkReauthorization(
  systemId: string,
  body: ReauthorizationCheckBody,
): Promise<ReauthorizationCheckResult> {
  const { data } = await apiClient.post<ReauthorizationCheckResult>(
    `/systems/${systemId}/conmon/reauthorization-check`,
    body,
  );
  return data;
}