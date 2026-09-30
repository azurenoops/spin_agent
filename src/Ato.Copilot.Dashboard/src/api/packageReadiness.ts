import apiClient from './client';
import type { PackagePurpose, RetainedPackageSelection } from './package';

export type { PackagePurpose, RetainedPackageSelection } from './package';

export interface PackageReadinessSelection {
  purpose: PackagePurpose;
  retainedContext?: RetainedPackageSelection | null;
}

export interface PackageReadinessScope {
  systemId: string;
  purpose: PackagePurpose;
  retainedContext: RetainedPackageSelection | null;
  selectionHash: string;
}

export type PackageReadinessCheckOutcome = 'Passed' | 'Blocking' | 'FollowUp' | 'NotApplicable' | 'Unavailable';
export type PackageReadinessRunOutcome = 'Ready' | 'Blocked' | 'Failed' | 'SourceChanged';

export interface PackageReadinessAction {
  canView: boolean;
  canEdit: boolean;
  path: string | null;
  label: 'Open' | null;
  reason: string | null;
}

export interface PackageReadinessSource {
  kind: string;
  recordId: string;
  revision: string | null;
  contentHash: string | null;
  label: string;
}

export interface PackageReadinessCheck {
  id: string;
  ruleId: string;
  title: string;
  outcome: PackageReadinessCheckOutcome;
  category: string;
  required: boolean;
  applicability: 'Applicable' | 'NotApplicable' | 'Undetermined';
  why: string;
  missingSource: string | null;
  sources: PackageReadinessSource[];
  nextSteps: string[];
  expectedRole: string | null;
  recordedOwner: {
    personId: string;
    displayName: string;
    role: string;
    assignmentId: string;
    scope: 'System' | 'Organization' | 'Record';
  } | null;
  action: PackageReadinessAction;
}

export interface PackageReadinessCounts {
  total: number;
  passed: number;
  blocking: number;
  followUp: number;
  notApplicable: number;
  unavailable: number;
  requiredUnavailable: number;
}

export interface PackageReadinessRun {
  id: string;
  outcome: PackageReadinessRunOutcome;
  startedAt: string;
  evaluatedAt: string;
  evaluatedBy: string;
  sourceHash: string | null;
  sourceHashAfter: string | null;
  ruleVersion: string;
  counts: PackageReadinessCounts;
  recommendedCheckId: string | null;
  failure: { code: string; message: string } | null;
  freshness: {
    state: 'Current' | 'Stale' | 'Unavailable';
    checkedAt: string;
    currentSourceHash: string | null;
    reason: string | null;
  };
}

export interface PackageReadinessPage<T> {
  items: T[];
  totalCount: number;
  limit: number;
  offset: number;
}

export interface PackageReadinessRecord {
  kind: string;
  id: string;
  status: string | null;
  recordedAt: string | null;
  purpose: PackagePurpose | null;
  sourceHash: string | null;
  sourceRelationship: 'CurrentSource' | 'Historical' | 'Unknown';
  action: PackageReadinessAction;
}

export interface PackageReadinessProgress {
  id: 'prepare' | 'validate' | 'export' | 'emass' | 'decision';
  state: 'NotChecked' | 'Ready' | 'Blocked' | 'Stale' | 'Failed' | 'Recorded' | 'NotRecorded' | 'Unavailable';
  description: string;
  records: PackageReadinessRecord[];
  totalCount: number;
  action: PackageReadinessAction;
}

export interface PackageReadinessDocument {
  kind: string;
  title: string;
  presence: 'Present' | 'Missing' | 'Unavailable';
  status: string | null;
  reviewState: string | null;
  sourceState: string | null;
  validationOutcome: PackageReadinessCheckOutcome | null;
  recordCount: number | null;
  records: PackageReadinessRecord[];
  action: PackageReadinessAction;
}

export interface PackageReadinessWorkspace extends PackageReadinessScope {
  source: { state: 'Available' | 'Unavailable'; hash: string | null; ruleVersion: string; reason: string | null };
  latestRun: PackageReadinessRun | null;
  permissions: { canValidate: boolean; validateReason: string | null; canGenerate: boolean; generateReason: string | null };
  progress: PackageReadinessProgress[];
  documents: PackageReadinessDocument[];
  rmf: {
    phase: string;
    transitions: { id: string; fromPhase: string; toPhase: string; occurredAt: string; actor: string }[];
    totalCount: number;
  };
}

export interface PackageReadinessLatestResponse extends PackageReadinessScope {
  latestRun: PackageReadinessRun | null;
}
export interface PackageReadinessHistoryResponse extends PackageReadinessScope, PackageReadinessPage<PackageReadinessRun> {}
export interface PackageReadinessRunResponse extends PackageReadinessScope {
  run: PackageReadinessRun;
  checks: PackageReadinessPage<PackageReadinessCheck>;
}
export interface PackageReadinessCheckResponse extends PackageReadinessScope {
  runId: string;
  check: PackageReadinessCheck;
}
export interface PackageReadinessPageRequest {
  limit?: number;
  offset?: number;
}
export interface PackageReadinessCheckPageRequest extends PackageReadinessPageRequest {
  outcome?: PackageReadinessCheckOutcome;
}
export interface GeneratePackageFromReadinessRequest extends PackageReadinessSelection {
  readinessRunId: string;
  expectedSourceHash: string;
  evidenceMode: 'Embedded' | 'ManifestOnly';
  includeEvidence: true;
}
export interface PackageReadinessGenerationReceipt {
  systemId: string;
  purpose: PackagePurpose;
  packageId: string;
  status: 'Pending' | 'Generating' | 'Validating' | 'Completed' | 'Failed';
  message: string;
  readinessRunId: string;
  sourceHash: string;
}

const legacy: PackageReadinessSelection = { purpose: 'Legacy' };
const purposes: readonly string[] = ['Legacy', 'InitialSubmission', 'AuthorizedBaselineArchive', 'ChangeSubmission'];
const outcomes: readonly string[] = ['Passed', 'Blocking', 'FollowUp', 'NotApplicable', 'Unavailable'];
const outcomeBuckets: Record<PackageReadinessCheckOutcome, keyof PackageReadinessCounts> = {
  Passed: 'passed', Blocking: 'blocking', FollowUp: 'followUp', NotApplicable: 'notApplicable', Unavailable: 'unavailable',
};
const paths = new Set([
  'documents', 'documents/preview', 'emass/status', 'authorize', 'history', 'boundaries',
  'profile/MissionAndPurpose', 'profile/UsersAndAccess', 'profile/EnvironmentAndDeployment',
  'profile/DataTypes', 'profile/PortsProtocolsAndServices', 'profile/LeveragedAuthorizations',
  'narratives', 'assessments', 'assessments/environment', 'poam', 'evidence', 'baseline',
  'inheritance', 'inheritance/subscriptions', 'security-capabilities', 'security-capabilities/inventory',
  'roles', 'legal', 'conmon',
]);
const root = (systemId: string) => `/systems/${encodeURIComponent(systemId)}/package-readiness`;
const text = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0;
const nullableText = (value: unknown) => value === null || typeof value === 'string';
const hash = (value: unknown): value is string => typeof value === 'string' && /^[a-f0-9]{64}$/i.test(value);
const nullableHash = (value: unknown) => value === null || hash(value);
const date = (value: unknown) => typeof value === 'string' && Number.isFinite(Date.parse(value));
const count = (value: unknown): value is number => Number.isSafeInteger(value) && typeof value === 'number' && value >= 0;

function requireContract(valid: unknown): asserts valid {
  if (!valid) throw new Error('The server returned an invalid package readiness contract or scope. Refresh before continuing.');
}

function selectionParams(selection: PackageReadinessSelection) {
  requireContract(purposes.includes(selection.purpose));
  const retained = selection.retainedContext;
  if (selection.purpose === 'Legacy' || selection.purpose === 'InitialSubmission') {
    requireContract(retained == null);
    return { purpose: selection.purpose };
  }
  requireContract(retained && text(retained.baselinePackageId) && hash(retained.baselineContentHash)
    && text(retained.authorizationDecisionId)
    && (retained.expectedDecisionSnapshotHash == null || hash(retained.expectedDecisionSnapshotHash))
    && (retained.expectedSourceContextHash == null || hash(retained.expectedSourceContextHash)));
  if (selection.purpose === 'ChangeSubmission')
    requireContract(text(retained.changePreviewId) && hash(retained.changeContentHash));
  else requireContract(retained.changePreviewId == null && retained.changeContentHash == null);
  return { purpose: selection.purpose, ...retained };
}

function selectionIdentity(retained: RetainedPackageSelection | null | undefined) {
  return retained == null ? null : [
    retained.baselinePackageId, retained.baselineContentHash?.toLowerCase(), retained.authorizationDecisionId,
    retained.changePreviewId ?? null, retained.changeContentHash?.toLowerCase() ?? null,
  ];
}

function checkScope(data: PackageReadinessScope, systemId: string, selection: PackageReadinessSelection) {
  requireContract(data && data.systemId === systemId && data.purpose === selection.purpose
    && data.retainedContext !== undefined && hash(data.selectionHash)
    && JSON.stringify(selectionIdentity(data.retainedContext)) === JSON.stringify(selectionIdentity(selection.retainedContext)));
}

function safePath(path: string) {
  const [route, fragment] = path.split('#');
  if (!route || path.split('#').length > 2) return false;
  const [pathname, query] = route.split('?');
  if (!pathname || !paths.has(pathname) || route.split('?').length > 2) return false;
  if (fragment !== undefined && (fragment !== 'ssp-sections' || pathname !== 'documents'
    || new URLSearchParams(query).get('tab') !== 'records')) return false;
  if (!query) return true;
  const params = new URLSearchParams(query);
  if (new Set(params.keys()).size !== [...params.keys()].length) return false;
  return [...params].every(([key, value]) =>
    key === 'purpose' && pathname === 'documents' && purposes.includes(value)
    || key === 'tab' && (pathname === 'documents' && ['exports', 'records'].includes(value) || pathname === 'assessments' && value === 'plan'
      || pathname === 'security-capabilities/inventory' && value === 'hardware-software'));
}

function checkAction(action: PackageReadinessAction) {
  requireContract(action && typeof action.canView === 'boolean' && typeof action.canEdit === 'boolean'
    && nullableText(action.reason) && (action.path === null || typeof action.path === 'string' && safePath(action.path))
    && (action.label === null || action.label === 'Open')
    && (!action.canEdit || action.canView)
    && (action.canView ? action.path !== null && action.label === 'Open' : action.path === null && action.label === null)
    && (action.canEdit || text(action.reason)));
}

function checkRun(run: PackageReadinessRun) {
  requireContract(run && text(run.id) && ['Ready', 'Blocked', 'Failed', 'SourceChanged'].includes(run.outcome)
    && date(run.startedAt) && date(run.evaluatedAt) && text(run.evaluatedBy) && text(run.ruleVersion)
    && nullableHash(run.sourceHash) && nullableHash(run.sourceHashAfter) && nullableText(run.recommendedCheckId));
  const c = run.counts;
  requireContract(c && [c.total, c.passed, c.blocking, c.followUp, c.notApplicable, c.unavailable, c.requiredUnavailable].every(count)
    && c.total === c.passed + c.blocking + c.followUp + c.notApplicable + c.unavailable
    && c.requiredUnavailable <= c.unavailable);
  const f = run.freshness;
  requireContract(f && ['Current', 'Stale', 'Unavailable'].includes(f.state) && date(f.checkedAt)
    && nullableHash(f.currentSourceHash) && nullableText(f.reason)
    && (f.state === 'Current' ? hash(f.currentSourceHash) && f.currentSourceHash === run.sourceHash : text(f.reason)));
  requireContract(run.failure === null || text(run.failure?.code) && text(run.failure?.message));
  if (run.outcome === 'Failed') requireContract(run.failure !== null);
  if (run.outcome === 'Ready')
    requireContract(hash(run.sourceHash) && run.sourceHash === run.sourceHashAfter && c.total > 0
      && c.blocking === 0 && c.requiredUnavailable === 0 && run.failure === null);
}

function checkCheck(check: PackageReadinessCheck) {
  requireContract(check && [check.id, check.ruleId, check.title, check.category, check.why].every(text)
    && outcomes.includes(check.outcome) && typeof check.required === 'boolean'
    && ['Applicable', 'NotApplicable', 'Undetermined'].includes(check.applicability)
    && nullableText(check.missingSource) && nullableText(check.expectedRole)
    && Array.isArray(check.sources) && check.sources.every(source => source && text(source.kind)
      && text(source.recordId) && text(source.label) && nullableText(source.revision) && nullableHash(source.contentHash))
    && Array.isArray(check.nextSteps) && check.nextSteps.every(text));
  if (check.recordedOwner !== null) {
    const owner = check.recordedOwner;
    requireContract(owner && [owner.personId, owner.displayName, owner.role, owner.assignmentId].every(text)
      && ['System', 'Organization', 'Record'].includes(owner.scope));
  }
  requireContract(check.outcome !== 'Passed' || check.applicability === 'Applicable');
  requireContract(check.outcome !== 'NotApplicable' || check.applicability === 'NotApplicable');
  checkAction(check.action);
}

function checkPage<T>(page: PackageReadinessPage<T>, validate: (item: T) => void, limit: number, offset: number) {
  requireContract(page && Array.isArray(page.items) && count(page.totalCount)
    && page.limit === limit && page.offset === offset && page.items.length <= limit
    && page.items.length <= Math.max(0, page.totalCount - offset));
  page.items.forEach(validate);
}

function pageParams(page: PackageReadinessPageRequest, defaultLimit: number, max: number) {
  const limit = page.limit ?? defaultLimit;
  const offset = page.offset ?? 0;
  requireContract(count(limit) && limit >= 1 && limit <= max && count(offset));
  return { limit, offset };
}

function checkRecords(records: PackageReadinessRecord[], total: number | null) {
  requireContract(Array.isArray(records) && records.length <= 5
    && (total === null ? records.length === 0 : count(total) && total >= records.length));
  records.forEach(record => {
    requireContract(record && text(record.kind) && text(record.id) && nullableText(record.status)
      && (record.recordedAt === null || date(record.recordedAt))
      && (record.purpose === null || purposes.includes(record.purpose)) && nullableHash(record.sourceHash)
      && ['CurrentSource', 'Historical', 'Unknown'].includes(record.sourceRelationship));
    checkAction(record.action);
  });
}

export async function getPackageReadinessWorkspace(
  systemId: string, selection: PackageReadinessSelection = legacy, signal?: AbortSignal,
): Promise<PackageReadinessWorkspace> {
  const { data } = await apiClient.get<PackageReadinessWorkspace>(root(systemId), { params: selectionParams(selection), signal });
  checkScope(data, systemId, selection);
  requireContract(data.source && ['Available', 'Unavailable'].includes(data.source.state)
    && text(data.source.ruleVersion) && nullableText(data.source.reason)
    && (data.source.state === 'Available' ? hash(data.source.hash) : data.source.hash === null && text(data.source.reason)));
  if (data.latestRun !== null) checkRun(data.latestRun);
  const p = data.permissions;
  requireContract(p && typeof p.canValidate === 'boolean' && typeof p.canGenerate === 'boolean'
    && nullableText(p.validateReason) && nullableText(p.generateReason)
    && (p.canValidate || text(p.validateReason)) && (p.canGenerate || text(p.generateReason)));
  if (p.canGenerate) requireContract(data.latestRun?.outcome === 'Ready' && data.latestRun.freshness.state === 'Current'
    && data.source.state === 'Available' && data.source.hash === data.latestRun.sourceHash
    && data.source.ruleVersion === data.latestRun.ruleVersion);
  requireContract(Array.isArray(data.progress) && data.progress.length === 5
    && new Set(data.progress.map(item => item?.id)).size === 5);
  data.progress.forEach(item => {
    requireContract(item && ['prepare', 'validate', 'export', 'emass', 'decision'].includes(item.id)
      && ['NotChecked', 'Ready', 'Blocked', 'Stale', 'Failed', 'Recorded', 'NotRecorded', 'Unavailable'].includes(item.state)
      && text(item.description) && count(item.totalCount));
    checkRecords(item.records, item.totalCount);
    checkAction(item.action);
  });
  requireContract(Array.isArray(data.documents) && data.documents.length <= 20);
  data.documents.forEach(document => {
    requireContract(document && text(document.kind) && text(document.title)
      && ['Present', 'Missing', 'Unavailable'].includes(document.presence)
      && [document.status, document.reviewState, document.sourceState].every(nullableText)
      && (document.validationOutcome === null || outcomes.includes(document.validationOutcome)));
    checkRecords(document.records, document.recordCount);
    checkAction(document.action);
  });
  requireContract(data.rmf && text(data.rmf.phase) && Array.isArray(data.rmf.transitions)
    && data.rmf.transitions.length <= 5 && count(data.rmf.totalCount) && data.rmf.totalCount >= data.rmf.transitions.length
    && data.rmf.transitions.every(t => t && [t.id, t.fromPhase, t.toPhase, t.actor].every(text) && date(t.occurredAt)));
  return data;
}

export async function getLatestPackageReadinessRun(
  systemId: string, selection: PackageReadinessSelection = legacy, signal?: AbortSignal,
): Promise<PackageReadinessLatestResponse> {
  const { data } = await apiClient.get<PackageReadinessLatestResponse>(`${root(systemId)}/runs/latest`, { params: selectionParams(selection), signal });
  checkScope(data, systemId, selection);
  if (data.latestRun !== null) checkRun(data.latestRun);
  return data;
}

export async function listPackageReadinessRuns(
  systemId: string, selection: PackageReadinessSelection = legacy, page: PackageReadinessPageRequest = {}, signal?: AbortSignal,
): Promise<PackageReadinessHistoryResponse> {
  const paging = pageParams(page, 20, 100);
  const { data } = await apiClient.get<PackageReadinessHistoryResponse>(`${root(systemId)}/runs`, {
    params: { ...selectionParams(selection), ...paging }, signal,
  });
  checkScope(data, systemId, selection);
  checkPage(data, checkRun, paging.limit, paging.offset);
  return data;
}

function checkRunResponse(data: PackageReadinessRunResponse, systemId: string, selection: PackageReadinessSelection,
  limit: number, offset: number, runId?: string, outcome?: PackageReadinessCheckOutcome) {
  checkScope(data, systemId, selection);
  checkRun(data.run);
  requireContract(runId === undefined || data.run.id === runId);
  checkPage(data.checks, checkCheck, limit, offset);
  requireContract(data.checks.totalCount <= data.run.counts.total
    && new Set(data.checks.items.map(check => check.id)).size === data.checks.items.length
    && (outcome ? data.checks.items.every(check => check.outcome === outcome) : data.checks.totalCount === data.run.counts.total));
  if (outcome) requireContract(data.checks.totalCount === data.run.counts[outcomeBuckets[outcome]]);
  if (!outcome && offset === 0 && data.checks.items.length === data.run.counts.total) {
    for (const [value, bucket] of Object.entries(outcomeBuckets))
      requireContract(data.checks.items.filter(check => check.outcome === value).length === data.run.counts[bucket]);
    requireContract(data.checks.items.filter(check => check.outcome === 'Unavailable' && check.required).length === data.run.counts.requiredUnavailable);
    requireContract(data.run.recommendedCheckId === null || data.checks.items.some(check => check.id === data.run.recommendedCheckId));
  }
}

export async function getPackageReadinessRun(
  systemId: string, runId: string, selection: PackageReadinessSelection = legacy,
  page: PackageReadinessCheckPageRequest = {}, signal?: AbortSignal,
): Promise<PackageReadinessRunResponse> {
  const paging = pageParams(page, 50, 200);
  requireContract(text(runId) && (page.outcome === undefined || outcomes.includes(page.outcome)));
  const { data } = await apiClient.get<PackageReadinessRunResponse>(`${root(systemId)}/runs/${encodeURIComponent(runId)}`, {
    params: { ...selectionParams(selection), ...paging, ...(page.outcome ? { outcome: page.outcome } : {}) }, signal,
  });
  checkRunResponse(data, systemId, selection, paging.limit, paging.offset, runId, page.outcome);
  return data;
}

export async function getPackageReadinessCheck(
  systemId: string, runId: string, checkId: string, selection: PackageReadinessSelection = legacy, signal?: AbortSignal,
): Promise<PackageReadinessCheckResponse> {
  requireContract(text(runId) && text(checkId));
  const { data } = await apiClient.get<PackageReadinessCheckResponse>(
    `${root(systemId)}/runs/${encodeURIComponent(runId)}/checks/${encodeURIComponent(checkId)}`,
    { params: selectionParams(selection), signal },
  );
  checkScope(data, systemId, selection);
  requireContract(data.runId === runId && data.check?.id === checkId);
  checkCheck(data.check);
  return data;
}

export async function validatePackageReadiness(
  systemId: string, selection: PackageReadinessSelection = legacy, signal?: AbortSignal,
): Promise<PackageReadinessRunResponse> {
  selectionParams(selection);
  const { data } = await apiClient.post<PackageReadinessRunResponse>(`${root(systemId)}/runs`, {
    purpose: selection.purpose, ...(selection.retainedContext ? { retainedContext: selection.retainedContext } : {}),
  }, { signal });
  checkRunResponse(data, systemId, selection, 50, 0);
  return data;
}

export async function generatePackageFromReadiness(
  systemId: string, request: GeneratePackageFromReadinessRequest, signal?: AbortSignal,
): Promise<PackageReadinessGenerationReceipt> {
  selectionParams(request);
  requireContract(text(request.readinessRunId) && hash(request.expectedSourceHash)
    && ['Embedded', 'ManifestOnly'].includes(request.evidenceMode) && request.includeEvidence === true);
  const { data } = await apiClient.post<PackageReadinessGenerationReceipt>(`/systems/${encodeURIComponent(systemId)}/packages`, {
    purpose: request.purpose, ...(request.retainedContext ? { retainedContext: request.retainedContext } : {}),
    readinessRunId: request.readinessRunId, expectedSourceHash: request.expectedSourceHash,
    evidenceMode: request.evidenceMode, includeEvidence: true,
  }, { baseURL: '/api/v1', signal });
  requireContract(data && data.systemId === systemId && data.purpose === request.purpose
    && data.readinessRunId === request.readinessRunId && data.sourceHash === request.expectedSourceHash
    && text(data.packageId) && text(data.message) && ['Pending', 'Generating', 'Validating', 'Completed', 'Failed'].includes(data.status));
  return data;
}
