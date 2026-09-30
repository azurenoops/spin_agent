import { packageRequest } from '../package-imports/request';
import { offeringPath } from './api';

export type ProviderMonitoringSignal = 'AuthorizationExpiry' | 'AuthorizationWithdrawal' | 'EvidenceFreshness' | 'PublishedReleaseChange';
export interface ProviderRuleCondition { field: string; operator: string; value: string }
export interface ProviderMonitoringSource {
  sourceId: string; signal: ProviderMonitoringSignal; name: string; collectionHealth: string;
  sourceRevision: string; field: string; value: string | null; sourceTimestamp: string | null; snapshotJson: string;
}
export interface ProviderMonitoringRule {
  id: string; offeringId: string; revision: number; name: string; signal: ProviderMonitoringSignal;
  sourceId: string; conditionJson: string; ownerId: string; response: string; cadenceMinutes: number;
  isEnabled: boolean; baselineJson: string; baselineHash: string; lastEvaluatedAt: string | null;
}
export interface ProviderMonitoringEvaluation {
  id: string; ruleId: string; ruleRevision: number; outcome: string; collectionHealth: string;
  createdAt: string; ruleSnapshotJson: string; sourceSnapshotJson: string; impactReviewId: string | null;
}
export interface ProviderMonitoringWorkspace {
  offeringId: string; offeringName: string; sources: ProviderMonitoringSource[];
  rules: ProviderMonitoringRule[]; evaluations: ProviderMonitoringEvaluation[];
}
export interface ProviderMonitoringRuleInput {
  expectedRevision: number | null; name: string; signal: ProviderMonitoringSignal; sourceId: string;
  condition: ProviderRuleCondition; cadenceMinutes: number; ownerId: string;
  response: 'CreateProviderImpactReview'; isEnabled: boolean; expectedSourceRevision: string | null;
}
const root = (id: string) => `${offeringPath(id)}/monitoring`;
export const getProviderMonitoring = (id: string, signal?: AbortSignal) =>
  packageRequest<ProviderMonitoringWorkspace>({ url: root(id), signal });
export const saveProviderMonitoringRule = (offeringId: string, data: ProviderMonitoringRuleInput, key: string, ruleId?: string) =>
  packageRequest<ProviderMonitoringRule>({ url: `${root(offeringId)}/rules${ruleId ? `/${ruleId}` : ''}`,
    method: ruleId ? 'PUT' : 'POST', data, headers: { 'Idempotency-Key': key } });
export const testProviderMonitoringRule = (offeringId: string, ruleId: string) =>
  packageRequest<ProviderMonitoringEvaluation>({ url: `${root(offeringId)}/rules/${ruleId}/test`, method: 'POST' });
export const evaluateProviderMonitoringRule = (offeringId: string, ruleId: string, expectedRevision: number, key: string) =>
  packageRequest<ProviderMonitoringEvaluation>({ url: `${root(offeringId)}/rules/${ruleId}/evaluate`,
    method: 'POST', data: { expectedRevision }, headers: { 'Idempotency-Key': key } });
