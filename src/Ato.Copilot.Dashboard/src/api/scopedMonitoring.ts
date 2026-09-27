import api from './client';

export interface MonitoringCondition { field: string; operator: string; value: string }
export interface MonitoringRule {
  id: string; name: string; boundaryDefinitionId: string; baselineReference: string; ownerId: string;
  signal: string; triggerCondition: string; cadenceMinutes: number; severityOverride: string;
  isEnabled: boolean; version: number; lastEvaluatedAt: string | null;
}
export interface RuleInput {
  name: string; boundaryDefinitionId: string; baselineReference: string; ownerId: string;
  signal: string; condition: MonitoringCondition; cadenceMinutes: number; severity: string;
  isEnabled: boolean; expectedVersion?: number;
}
export interface MonitoringEvaluation {
  id: string; ruleId: string; ruleVersion: number; outcome: string; evaluatedAt: string;
  ruleSnapshotJson: string; inputSnapshotJson: string;
}
export interface MonitoringImpact {
  id: string; evaluationId: string; controlId: string | null; ownerId: string; disposition: string;
  rationale: string | null; reviewedBy: string | null; reviewedAt: string | null; version: number;
  affectedRecordsJson: string; narrativeProposalIdsJson: string;
}
export interface MonitoringWorkspace {
  canManageRules: boolean;
  canReviewImpacts: boolean;
  boundaries: { id: string; name: string }[];
  rules: MonitoringRule[];
  coverage: { assignmentId: string; boundaryId: string; resourceId: string | null; providerComponentId: string | null;
    health: string; lastSuccessAt: string | null; error: string | null }[];
  changes: { sourceId: string; kind: string; title: string; controlId: string | null; changeDetails: string | null;
    attribution: string; observedAt: string }[];
  evaluations: MonitoringEvaluation[];
  impacts: MonitoringImpact[];
}
const root = (id: string) => `/systems/${encodeURIComponent(id)}/conmon`;
export const getMonitoringWorkspace = (id: string) => api.get<MonitoringWorkspace>(`${root(id)}/workspace`).then(r => r.data);
export const saveMonitoringRule = (system: string, input: RuleInput, id?: string) =>
  (id ? api.put<MonitoringRule>(`${root(system)}/rules/${id}`, input) : api.post<MonitoringRule>(`${root(system)}/rules`, input)).then(r => r.data);
export const testMonitoringRule = (system: string, id: string) =>
  api.post<MonitoringEvaluation[]>(`${root(system)}/rules/${id}/test`).then(r => r.data);
export const dispositionMonitoringImpact = (system: string, id: string, expectedVersion: number, disposition: string, rationale: string) =>
  api.post<MonitoringImpact>(`${root(system)}/impacts/${id}/disposition`, { expectedVersion, disposition, rationale }).then(r => r.data);
