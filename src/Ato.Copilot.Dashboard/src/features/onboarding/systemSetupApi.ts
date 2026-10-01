import { workspaceRequest } from '../workspace-operations/workspaceRequest';

export const systemSetupSteps = [
  { id: 's-details', label: 'System & objective' },
  { id: 's-team', label: 'Team & ownership' },
  { id: 's-sources', label: 'Existing sources' },
  { id: 's-hosting', label: 'Hosting association' },
  { id: 's-connect', label: 'Monitoring' },
  { id: 's-review', label: 'Review setup' },
  { id: 's-ready', label: 'System ready' },
] as const;
export type SystemSetupScreen = typeof systemSetupSteps[number]['id'];
export interface SystemSetupDraft {
  name: string;
  acronym: string;
  missionPurpose: string;
  objective: 'initialAto' | 'continuePackage' | 'maintainSystem' | '';
  contact?: { personId: string; responsibility: 'preparationContact' } | null;
  sourceChoice: 'blank' | 'sspPdf' | 'emass' | 'deferred';
  hostingChoice: 'allocatedService' | 'organizationManaged' | 'deferred';
  monitoringChoice: 'configureLater' | 'reviewAzureConnection';
  lastScreen: SystemSetupScreen;
}
export interface SystemSetupContact { personId: string; displayName: string }
export interface SystemSetupTask {
  id: string; label: string; detail: string; state: string; contribution: string;
  link: string; ownerRole?: string; canAct: boolean;
}
export interface SystemSetupContext {
  organizationName: string;
  canCreate: boolean;
  contacts: SystemSetupContact[];
}
export interface SystemSetupSummary {
  systemId: string; displayName: string; revision: number; savedAt: string;
  setupState: 'draft' | 'confirmed' | 'legacy' | 'discarded';
  links?: { resume?: string };
}
export interface SystemSourceReceipt {
  sessionId: string; kind: 'emass' | 'ssp-pdf'; systemId: string; fileName: string; sha256: string;
  sourceRevision: number; receiptState: string; analysisState: string; reviewState: string; state: string;
  fields: { field: string; sourceField: string; proposedValue: string | null; supported: boolean }[];
  error: string | null;
}
export interface SystemSourcePreview {
  sessionId: string; systemId: string; sourceRevision: number; sourceHash: string;
  identityRevision: string; previewHash: string;
  fields: { field: string; currentValue: string | null; proposedValue: string | null; supported: boolean }[];
}
export interface SystemSourceApply {
  expectedSourceRevision: number; expectedSystemRevision: string; previewHash: string;
  decisions: { field: string; decision: 'keepCurrent' | 'applyProposed' }[];
}
export interface SystemSetupView {
  systemId: string; tenantId: string; displayName: string; organizationName: string;
  revision: number; identityRevision: string; savedAt: string;
  setupState: 'draft' | 'confirmed' | 'legacy' | 'discarded';
  completedAt: string | null; draft: SystemSetupDraft; canManage: boolean;
  contacts: SystemSetupContact[];
  effectiveTeam: { role: string; displayName: string | null; source: string }[];
  sources: SystemSourceReceipt[];
  tasks: SystemSetupTask[];
  links?: { documents?: string; hosting?: string; capabilities?: string; monitoring?: string };
  monitoring: { configuration: string; access: string; scopeReview: string; collection: string; evaluation: string };
}
const root = (tenantId: string) => `/api/workspaces/organizations/${encodeURIComponent(tenantId)}/systems`;
const setup = (tenantId: string, systemId: string) => `${root(tenantId)}/${encodeURIComponent(systemId)}/setup`;
export const getSystemSetupContext = (tenantId: string) =>
  workspaceRequest<SystemSetupContext>({ method: 'GET', url: `${root(tenantId)}/setup-context` });
export const getSystemSetup = (tenantId: string, systemId: string) =>
  workspaceRequest<SystemSetupView>({ method: 'GET', url: setup(tenantId, systemId) });
export const getSystemSetupAccess = (tenantId: string, signal?: AbortSignal) =>
  workspaceRequest<{ canCreateSystem: boolean }>({ method: 'GET', url: `${root(tenantId)}/setup-access`, signal });
export const listSystemSetupDrafts = (tenantId: string, cursor?: string, signal?: AbortSignal) =>
  workspaceRequest<{ items: SystemSetupSummary[]; nextCursor: string | null }>({
    method: 'GET', url: `${root(tenantId)}/setup-drafts`, params: { cursor, pageSize: 25 }, signal,
  });
export function saveSystemSetup(tenantId: string, draft: SystemSetupDraft, key: string, current?: SystemSetupView) {
  return workspaceRequest<SystemSetupView>({
    method: current ? 'PUT' : 'POST',
    url: current ? setup(tenantId, current.systemId) : `${root(tenantId)}/setup-drafts`,
    data: current ? { ...draft, expectedIdentityRevision: current.identityRevision } : draft,
    headers: { 'Idempotency-Key': key, ...(current ? { 'If-Match': `"setup-${current.revision}"` } : {}) },
  });
}
export function confirmSystemSetup(tenantId: string, current: SystemSetupView, key: string) {
  return workspaceRequest<SystemSetupView>({
    method: 'POST', url: `${setup(tenantId, current.systemId)}/confirm`,
    data: { reviewRevision: current.revision, identityRevision: current.identityRevision, confirmed: true },
    headers: { 'Idempotency-Key': key, 'If-Match': `"setup-${current.revision}"` },
  });
}
const sourcePath = (tenantId: string, systemId: string, kind: string, id: string) =>
  `${root(tenantId)}/${encodeURIComponent(systemId)}/source-imports/${encodeURIComponent(kind)}/${encodeURIComponent(id)}`;
export function uploadSystemSource(tenantId: string, systemId: string, kind: 'emass' | 'ssp-pdf', file: File, key: string) {
  const data = new FormData();
  data.append('file', file);
  return workspaceRequest<SystemSourceReceipt>({
    method: 'POST', url: `${root(tenantId)}/${encodeURIComponent(systemId)}/source-imports/${kind}`,
    data, headers: { 'Idempotency-Key': key },
  });
}
export const getSystemSource = (tenantId: string, systemId: string, kind: string, id: string) =>
  workspaceRequest<SystemSourceReceipt>({ method: 'GET', url: sourcePath(tenantId, systemId, kind, id) });
export const previewSystemSource = (tenantId: string, systemId: string, source: SystemSourceReceipt) =>
  workspaceRequest<SystemSourcePreview>({ method: 'POST',
    url: `${sourcePath(tenantId, systemId, source.kind, source.sessionId)}/review-previews`,
    data: { expectedSourceRevision: source.sourceRevision },
  });
export const applySystemSource = (tenantId: string, systemId: string, source: SystemSourceReceipt, data: SystemSourceApply, key: string) =>
  workspaceRequest<SystemSourceReceipt>({ method: 'POST',
    url: `${sourcePath(tenantId, systemId, source.kind, source.sessionId)}/apply`,
    data, headers: { 'Idempotency-Key': key },
  });
