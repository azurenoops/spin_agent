import apiClient from './client';
import type { AssessmentFinding } from './assessments';

export type AssessmentPlanTask = 'title' | 'lead' | 'scope' | 'approach' | 'team' | 'schedule' | 'procedures';
export interface AssessmentPlanControl {
  controlId: string; title: string; family: string; included: boolean;
  exclusionRationale: string | null; methods: string[]; methodRationale: string | null; objectives: string[];
}
export interface AssessmentTeamMember { name: string; organization: string; role: string; contactInfo: string | null }
export interface RetainedAssessmentPlan {
  id: string; title: string; status: string; revision: number; contentHash: string;
  generatedAt: string; updatedAt: string | null; finalizedAt: string | null;
  assessmentLead: string | null; assessmentLeadId: string | null; scopeNotes: string | null;
  assessmentApproach: string | null; rulesOfEngagement: string | null;
  scheduleStart: string | null; scheduleEnd: string | null; scopeCount: number;
  controls: AssessmentPlanControl[]; teamMembers: AssessmentTeamMember[];
}
export interface AssessmentPlanWorkspace {
  systemId: string; systemName: string; baselineLevel: string | null; baselineControlCount: number;
  plan: RetainedAssessmentPlan | null;
  plans: { id: string; title: string; status: string; revision: number; generatedAt: string; finalizedAt: string | null }[];
  leadOptions: { id: string; name: string; kind: string; organization: string | null }[];
  tasks: { key: string; title: string; description: string; complete: boolean; required: boolean; actionLabel: string }[];
  warnings: string[]; finalizationBlockers: string[];
  permissions: { canCreatePlan: boolean; canEditPlan: boolean; canFinalizePlan: boolean;
    createReason: string | null; editReason: string | null; finalizeReason: string | null };
}
export interface AssessmentPlanEdit {
  task: AssessmentPlanTask; expectedContentHash: string; expectedRevision: number;
  title?: string; assessmentLeadId?: string | null; scopeNotes?: string;
  includedControlIds?: string[]; exclusionReasons?: Record<string, string>;
  assessmentApproach?: string; rulesOfEngagement?: string; scheduleStart?: string | null; scheduleEnd?: string | null;
  teamMembers?: AssessmentTeamMember[];
  methodOverrides?: { controlId: string; methods: string[]; rationale: string | null }[];
}
export interface AssessmentPlanPreview { systemId: string; sapId: string; revision: number; contentHash: string; content: string }
export interface AssessmentResultSet {
  id: string; recordId: string; name: string; source: string; method: string;
  collectionStatus: string; reviewStatus: string; recordedAt: string; actor: string | null;
  planId: string | null; planRevision: number | null; planTitle: string | null; planStatusAtCollection: string | null;
  requiresReconciliation: boolean; observedControlCount: number; reviewedControlCount: number;
  scopeControlCount: number | null; revision: string; warnings: string[]; canReview: boolean;
}
export interface AssessmentResultsWorkspace {
  systemId: string; items: AssessmentResultSet[]; totalCount: number; page: number; pageSize: number;
  collection: {
    canRunAzure: boolean; runReason: string | null; canImport: boolean; importReason: string | null;
    canConfigureAzure: boolean; configurationReason: string | null;
    azure: { state: string; message: string; checkedAt: string | null; subscriptions: { id: string; name: string }[]; scopeDescription: string[] };
    importFormats: string[];
  };
  sarReadiness: { canPrepareDraft: boolean; blockers: string[]; warnings: string[]; scopeCount: number | null;
    observedControlCount: number; reviewedControlCount: number; missingControlIds: string[]; selectedResultIds: string[] };
  permissions: { canReview: boolean; reviewReason: string | null; canRemediate: boolean; canRequestDeviation: boolean };
  reports: { id: string; title: string; status: string; createdAt: string }[];
  selectedResults: { id: string; name: string; revision: string }[];
}
export interface AssessmentResultDetail {
  systemId: string; item: AssessmentResultSet; originalScope: string[]; selectedScope: string[];
  observedControls: string[]; missingControls: string[]; outOfScopeControls: string[]; excludedControls: string[]; duplicateControls: string[];
  evidence: { id: string; name: string; contentHash: string | null; downloadUrl: string | null }[];
  findings: AssessmentFinding[]; errors: string[];
  history: { action: string; actor: string | null; at: string; description: string }[];
  permissions: { canReview: boolean; reviewReason: string | null; canReconcile: boolean; reconcileReason: string | null;
    canRemediate: boolean; canRequestDeviation: boolean };
}
export interface AssessmentReport {
  id: string; title: string; status: string; createdAt: string; downloadUrl: string | null;
  sections: { title: string; content: string }[]; sourceResultIds: string[]; warnings: string[];
}
export interface AssessmentResultsQuery { planId: string | null; search: string; page: number; pageSize: number; selectedResultIds: string[] }
export interface AssessmentControlReview {
  expectedResultRevision: string; controlId: string; determination: 'Satisfied' | 'OtherThanSatisfied';
  method: 'Examine' | 'Interview' | 'Test'; notes: string; evidenceIds: string[]; catSeverity: string | null;
}
export interface AssessmentReportInput {
  planId: string | null; expectedPlanHash: string | null; resultIds: string[];
  expectedResultRevisions: Record<string, string>; requestId: string; title: string;
}
const object = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v);
const text = (v: unknown): v is string => typeof v === 'string';
const nullableText = (v: unknown) => v === null || text(v);
const count = (v: unknown) => typeof v === 'number' && Number.isInteger(v) && v >= 0;
const nullableCount = (v: unknown) => v === null || count(v);
const texts = (v: unknown): v is string[] => Array.isArray(v) && v.every(text);
const booleans = (v: Record<string, unknown>, keys: string[]) => keys.every(key => typeof v[key] === 'boolean');
function isPlan(v: unknown): v is RetainedAssessmentPlan {
  return object(v) && ['id', 'title', 'status', 'contentHash', 'generatedAt'].every(k => text(v[k]))
    && count(v.revision) && count(v.scopeCount)
    && ['updatedAt', 'finalizedAt', 'assessmentLead', 'assessmentLeadId', 'scopeNotes', 'assessmentApproach',
      'rulesOfEngagement', 'scheduleStart', 'scheduleEnd'].every(k => nullableText(v[k]))
    && Array.isArray(v.controls) && v.controls.every(c => object(c) && ['controlId', 'title', 'family'].every(k => text(c[k]))
      && typeof c.included === 'boolean' && nullableText(c.exclusionRationale) && nullableText(c.methodRationale)
      && texts(c.methods) && texts(c.objectives))
    && Array.isArray(v.teamMembers) && v.teamMembers.every(m => object(m)
      && ['name', 'organization', 'role'].every(k => text(m[k])) && nullableText(m.contactInfo));
}
function isPlanWorkspace(v: unknown): v is AssessmentPlanWorkspace {
  return object(v) && text(v.systemId) && text(v.systemName) && nullableText(v.baselineLevel) && count(v.baselineControlCount)
    && (v.plan === null || isPlan(v.plan)) && texts(v.warnings) && texts(v.finalizationBlockers)
    && object(v.permissions) && booleans(v.permissions, ['canCreatePlan', 'canEditPlan', 'canFinalizePlan'])
    && ['createReason', 'editReason', 'finalizeReason'].every(k => object(v.permissions) && nullableText(v.permissions[k]))
    && Array.isArray(v.plans) && v.plans.every(p => object(p) && ['id', 'title', 'status', 'generatedAt'].every(k => text(p[k]))
      && count(p.revision) && nullableText(p.finalizedAt))
    && Array.isArray(v.leadOptions) && v.leadOptions.every(l => object(l) && ['id', 'name', 'kind'].every(k => text(l[k])) && nullableText(l.organization))
    && Array.isArray(v.tasks) && v.tasks.every(t => object(t) && ['key', 'title', 'description', 'actionLabel'].every(k => text(t[k])) && booleans(t, ['complete', 'required']));
}
function isResult(v: unknown): v is AssessmentResultSet {
  return object(v) && ['id', 'recordId', 'name', 'source', 'method', 'collectionStatus', 'reviewStatus', 'recordedAt', 'revision'].every(k => text(v[k]))
    && ['actor', 'planId', 'planTitle', 'planStatusAtCollection'].every(k => nullableText(v[k])) && nullableCount(v.planRevision)
    && nullableCount(v.scopeControlCount) && count(v.observedControlCount) && count(v.reviewedControlCount)
    && booleans(v, ['requiresReconciliation', 'canReview']) && texts(v.warnings);
}
function isResultsWorkspace(v: unknown): v is AssessmentResultsWorkspace {
  if (!object(v) || !object(v.collection) || !object(v.collection.azure) || !object(v.sarReadiness) || !object(v.permissions)) return false;
  const c = v.collection, a = v.collection.azure, r = v.sarReadiness;
  return text(v.systemId) && ['totalCount', 'page', 'pageSize'].every(k => count(v[k]))
    && Array.isArray(v.items) && v.items.every(isResult)
    && booleans(c, ['canRunAzure', 'canImport', 'canConfigureAzure'])
    && ['runReason', 'importReason', 'configurationReason'].every(k => nullableText(c[k])) && texts(c.importFormats)
    && text(a.state) && text(a.message) && nullableText(a.checkedAt) && texts(a.scopeDescription)
    && Array.isArray(a.subscriptions) && a.subscriptions.every(s => object(s) && text(s.id) && text(s.name))
    && typeof r.canPrepareDraft === 'boolean' && texts(r.blockers) && texts(r.warnings) && texts(r.missingControlIds) && texts(r.selectedResultIds)
    && nullableCount(r.scopeCount) && count(r.observedControlCount) && count(r.reviewedControlCount)
    && booleans(v.permissions, ['canReview', 'canRemediate', 'canRequestDeviation']) && nullableText(v.permissions.reviewReason)
    && Array.isArray(v.reports) && v.reports.every(s => object(s) && ['id', 'title', 'status', 'createdAt'].every(k => text(s[k])))
    && Array.isArray(v.selectedResults) && v.selectedResults.every(s => object(s) && ['id', 'name', 'revision'].every(k => text(s[k])));
}
function isResultDetail(v: unknown): v is AssessmentResultDetail {
  return object(v) && text(v.systemId) && isResult(v.item)
    && ['originalScope', 'selectedScope', 'observedControls', 'missingControls', 'outOfScopeControls', 'excludedControls', 'duplicateControls', 'errors'].every(k => texts(v[k]))
    && Array.isArray(v.evidence) && v.evidence.every(e => object(e) && text(e.id) && text(e.name) && nullableText(e.contentHash) && nullableText(e.downloadUrl))
    && Array.isArray(v.findings) && v.findings.every(f => object(f) && ['findingId', 'controlFamily', 'title', 'description', 'severity', 'status', 'discoveredAt'].every(k => text(f[k]))
      && ['controlId', 'resourceType', 'resourceId', 'remediationGuidance', 'deviationId', 'deviationType'].every(k => nullableText(f[k])))
    && Array.isArray(v.history) && v.history.every(h => object(h) && ['action', 'at', 'description'].every(k => text(h[k])) && nullableText(h.actor))
    && object(v.permissions) && booleans(v.permissions, ['canReview', 'canReconcile', 'canRemediate', 'canRequestDeviation'])
    && nullableText(v.permissions.reviewReason) && nullableText(v.permissions.reconcileReason);
}
function isReport(v: unknown): v is AssessmentReport {
  return object(v) && ['id', 'title', 'status', 'createdAt'].every(k => text(v[k])) && nullableText(v.downloadUrl)
    && texts(v.sourceResultIds) && texts(v.warnings) && Array.isArray(v.sections)
    && v.sections.every(s => object(s) && text(s.title) && text(s.content));
}
const root = (system: string) => `/systems/${encodeURIComponent(system)}/assessment-workspace`;
const parameters = (query: object) => Object.fromEntries(Object.entries(query).filter(([, value]) => value !== '' && value !== null && value !== undefined));
function planResponse(value: unknown, systemId: string): AssessmentPlanWorkspace {
  if (!isPlanWorkspace(value) || value.systemId !== systemId) throw new Error('Unexpected or mismatched assessment plan response.');
  return value;
}
function resultResponse(value: unknown, systemId: string, id: string): AssessmentResultDetail {
  if (!isResultDetail(value) || value.systemId !== systemId || value.item.id !== id) throw new Error('Unexpected or mismatched result response.');
  return value;
}
export async function getAssessmentPlan(system: string, planId?: string | null, signal?: AbortSignal) {
  const { data } = await apiClient.get<unknown>(`${root(system)}/plan`, { params: parameters({ planId }), signal });
  const value = planResponse(data, system);
  if (planId && value.plan?.id !== planId) throw new Error('The selected plan was not returned.');
  return value;
}
export async function createAssessmentPlan(system: string, input: { requestId: string; previousPlanId: string | null; expectedContentHash: string | null }) {
  return planResponse((await apiClient.post<unknown>(`${root(system)}/plans`, input)).data, system);
}
export async function saveAssessmentPlan(system: string, id: string, input: AssessmentPlanEdit) {
  const value = planResponse((await apiClient.put<unknown>(`${root(system)}/plans/${encodeURIComponent(id)}`, input)).data, system);
  if (value.plan?.id !== id) throw new Error('The saved plan identity changed unexpectedly.');
  return value;
}
export async function finalizeAssessmentPlan(system: string, id: string, input: { expectedContentHash: string; expectedRevision: number }) {
  const value = planResponse((await apiClient.post<unknown>(`${root(system)}/plans/${encodeURIComponent(id)}/finalize`, input)).data, system);
  if (value.plan?.id !== id || value.plan.status !== 'Finalized') throw new Error('Plan finalization was not confirmed.');
  return value;
}
export async function previewAssessmentPlan(system: string, id: string, signal?: AbortSignal): Promise<AssessmentPlanPreview> {
  const { data } = await apiClient.get<unknown>(`${root(system)}/plans/${encodeURIComponent(id)}/preview`, { signal });
  if (!object(data) || data.systemId !== system || data.sapId !== id || !count(data.revision) || !text(data.contentHash) || !text(data.content))
    throw new Error('The saved plan preview could not be verified.');
  return { systemId: system, sapId: id, revision: Number(data.revision), contentHash: data.contentHash, content: data.content };
}
export async function getAssessmentResults(system: string, query: AssessmentResultsQuery, signal?: AbortSignal): Promise<AssessmentResultsWorkspace> {
  const { selectedResultIds, ...filters } = query;
  const { data } = await apiClient.get<unknown>(`${root(system)}/results`, {
    params: { ...parameters(filters), selectedResultIds: selectedResultIds.join(',') }, signal,
  });
  if (!isResultsWorkspace(data) || data.systemId !== system) throw new Error('Unexpected or mismatched assessment results response.');
  return data;
}
export async function getAssessmentResult(system: string, id: string, planId?: string | null, signal?: AbortSignal) {
  return resultResponse((await apiClient.get<unknown>(`${root(system)}/results/${encodeURIComponent(id)}`, { params: parameters({ planId }), signal })).data, system, id);
}
export async function collectAssessmentResults(system: string, input: { planId: string | null; expectedPlanHash: string | null; requestId: string }): Promise<{ status: string; message: string; resultIds: string[] }> {
  const { data } = await apiClient.post<unknown>(`${root(system)}/collect`, input);
  if (!object(data) || !text(data.status) || !text(data.message) || !texts(data.resultIds)) throw new Error('Collection outcome is unavailable. Refresh retained results before retrying.');
  return { status: data.status, message: data.message, resultIds: data.resultIds };
}
export async function reconcileAssessmentResult(system: string, id: string, input: { planId: string; expectedPlanHash: string; expectedResultRevision: string }) {
  return resultResponse((await apiClient.post<unknown>(`${root(system)}/results/${encodeURIComponent(id)}/reconcile`, input)).data, system, id);
}
export async function reviewAssessmentControl(system: string, id: string, input: AssessmentControlReview) {
  return resultResponse((await apiClient.post<unknown>(`${root(system)}/results/${encodeURIComponent(id)}/review`, input)).data, system, id);
}
export async function prepareAssessmentReport(system: string, input: AssessmentReportInput): Promise<AssessmentReport> {
  const { data } = await apiClient.post<unknown>(`${root(system)}/reports`, input);
  if (!isReport(data)) throw new Error('Report preparation was not confirmed. Refresh retained reports before retrying.');
  return data;
}
export async function getAssessmentReport(system: string, id: string, signal?: AbortSignal): Promise<AssessmentReport> {
  const { data } = await apiClient.get<unknown>(`${root(system)}/reports/${encodeURIComponent(id)}`, { signal });
  if (!isReport(data) || data.id !== id) throw new Error('Unexpected or mismatched retained report response.');
  return data;
}
export function assessmentWorkspaceError(reason: unknown): string {
  if (object(reason)) {
    if (text(reason.error)) return reason.error;
    if (object(reason.error) && text(reason.error.message)) return reason.error.message;
    if (object(reason.response)) {
      if (object(reason.response.data)) return assessmentWorkspaceError(reason.response.data);
      if (reason.response.status === 403) return 'Your current workspace does not authorize this assessment action.';
      if (reason.response.status === 404) return 'This assessment record is unavailable in the current system.';
      if (reason.response.status === 409) return 'The retained assessment source changed. Refresh and review before retrying.';
    }
  }
  return reason instanceof Error ? reason.message : 'The assessment operation could not be confirmed. Refresh the retained records and retry.';
}
