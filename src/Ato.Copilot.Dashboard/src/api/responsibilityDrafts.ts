import apiClient from './client';

export const responsibilityFields = ['allocation', 'provider', 'providerDuties', 'customer', 'scope', 'exclusions', 'source', 'basis', 'information'] as const;
export type ResponsibilityField = typeof responsibilityFields[number];
export type ResponsibilityValues = Record<ResponsibilityField, string>;
export interface ResponsibilityValue {
  value: string; origin: 'From provider source' | 'From system records' | 'AI proposed';
  sourceIds: string[]; explanation: string; userEdited: boolean; sourceHash: string;
}
export interface DraftSource { id: string; title: string; origin: string; version: string; content: string; href: string | null }
export interface ResponsibilityDraft {
  id: string; revision: number; status: string; sourceHash: string; isStale: boolean;
  generationState: string; generationError: string | null; preparedAt: string; generatedAt: string | null;
  preparedBy: string; reviewedBy: string | null; reviewedAt: string | null;
  values: Record<ResponsibilityField, ResponsibilityValue>;
  suggestion: { values: Record<ResponsibilityField, ResponsibilityValue>; questions: string[]; conflicts: string[] };
  sources: DraftSource[];
  history: { id: string; revision: number; action: string; actor: string; at: string; sourceHash: string }[];
}
export interface ResponsibilityDraftContext {
  systemId: string; controlId: string; baselineId: string; scopeId: string | null; canPrepare: boolean; sourceHash: string;
  scopes: { id: string; name: string; provider: string | null; reviewRequired: boolean }[];
  sources: DraftSource[]; sourceValues: Record<ResponsibilityField, ResponsibilityValue>;
  questions: string[]; conflicts: string[]; draft: ResponsibilityDraft | null;
}
const object = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const texts = (value: unknown): value is string[] => Array.isArray(value) && value.every(x => typeof x === 'string');
function values(value: unknown): boolean {
  return object(value) && responsibilityFields.every(key => {
    const field = value[key];
    return object(field) && typeof field.value === 'string'
      && ['From provider source', 'From system records', 'AI proposed'].includes(String(field.origin))
      && texts(field.sourceIds) && typeof field.explanation === 'string' && typeof field.userEdited === 'boolean'
      && typeof field.sourceHash === 'string';
  });
}
function sources(value: unknown): boolean {
  return Array.isArray(value) && value.every(x => object(x)
    && ['id', 'title', 'origin', 'version', 'content'].every(key => typeof x[key] === 'string')
    && (x.href === null || typeof x.href === 'string' && x.href.startsWith('/systems/')));
}
function isDraft(value: unknown): value is ResponsibilityDraft {
  return object(value) && typeof value.id === 'string' && Number.isInteger(value.revision)
    && ['Proposed', 'ComparisonRequired', 'Accepted'].includes(String(value.status))
    && typeof value.sourceHash === 'string' && typeof value.isStale === 'boolean'
    && values(value.values) && object(value.suggestion) && values(value.suggestion.values)
    && texts(value.suggestion.questions) && texts(value.suggestion.conflicts) && sources(value.sources)
    && Array.isArray(value.history) && value.history.every(x => object(x) && Number.isInteger(x.revision)
      && ['id', 'action', 'actor', 'at', 'sourceHash'].every(key => typeof x[key] === 'string'))
    && ['NotRequested', 'Prepared', 'Failed'].includes(String(value.generationState))
    && typeof value.preparedAt === 'string' && typeof value.preparedBy === 'string'
    && ['generatedAt', 'generationError', 'reviewedBy', 'reviewedAt'].every(key => value[key] === null || typeof value[key] === 'string');
}
function draft(value: unknown): ResponsibilityDraft {
  if (!isDraft(value)) throw new Error('The saved responsibility draft response is incomplete.');
  return value;
}
function isContext(value: unknown): value is ResponsibilityDraftContext {
  return object(value) && typeof value.systemId === 'string' && typeof value.controlId === 'string'
    && typeof value.canPrepare === 'boolean' && typeof value.sourceHash === 'string' && typeof value.baselineId === 'string'
    && (value.scopeId === null || typeof value.scopeId === 'string')
    && Array.isArray(value.scopes) && value.scopes.every(x => object(x) && typeof x.id === 'string'
      && typeof x.name === 'string' && (x.provider === null || typeof x.provider === 'string') && typeof x.reviewRequired === 'boolean')
    && sources(value.sources) && values(value.sourceValues) && texts(value.questions) && texts(value.conflicts)
    && (value.draft === null || isDraft(value.draft));
}
function context(value: unknown, systemId: string, controlId: string, scopeId: string | null): ResponsibilityDraftContext {
  if (!isContext(value) || value.systemId !== systemId || value.controlId !== controlId || value.scopeId !== scopeId)
    throw new Error('The responsibility source context does not match this system, control and scope.');
  return value;
}
export class ResponsibilityDraftError extends Error {
  constructor(message: string, public readonly status?: number, public readonly context?: ResponsibilityDraftContext) { super(message); }
}
async function request<T>(run: () => Promise<T>, checkContext?: (body: unknown) => ResponsibilityDraftContext): Promise<T> {
  try { return await run(); }
  catch (reason) {
    const data = object(reason) && object(reason.response) ? reason.response.data : reason;
    if (object(data)) {
      const title = typeof data.title === 'string' ? data.title : reason instanceof Error ? reason.message : 'The responsibility draft request failed.';
      throw new ResponsibilityDraftError(title, typeof data.status === 'number' ? data.status : undefined,
        checkContext && data.draftContext ? checkContext(data.draftContext) : undefined);
    }
    throw new ResponsibilityDraftError(reason instanceof Error ? reason.message : 'The responsibility draft request failed. Verify saved state before retrying.');
  }
}
const root = (systemId: string) => `/systems/${encodeURIComponent(systemId)}/capability-subscriptions/drafts`;
export const getResponsibilityDraft = (systemId: string, controlId: string, scopeId: string | null, signal?: AbortSignal) =>
  request(async () => context((await apiClient.get(`${root(systemId)}/${encodeURIComponent(controlId)}`, { params: { scopeId }, signal })).data, systemId, controlId, scopeId));
export const prepareResponsibilityDraft = (systemId: string, controlId: string, scopeId: string | null, expectedRevision: number,
  generate = true, signal?: AbortSignal) => request(async () => context((await apiClient.post(
    `${root(systemId)}/${encodeURIComponent(controlId)}/prepare`, { scopeId, expectedRevision, generate }, { signal })).data,
  systemId, controlId, scopeId), data => context(data, systemId, controlId, scopeId));
export const saveResponsibilityDraft = (systemId: string, id: string, expectedRevision: number, edited: ResponsibilityValues,
  applySuggestion = false, signal?: AbortSignal) => request(async () => draft((await apiClient.put(`${root(systemId)}/record/${encodeURIComponent(id)}`,
  { expectedRevision, values: edited, applySuggestion }, { signal })).data));
export const confirmResponsibilityDraft = (systemId: string, saved: ResponsibilityDraft, reviewNotes: string, signal?: AbortSignal) =>
  request(async () => {
    const result = draft((await apiClient.post(`${root(systemId)}/record/${encodeURIComponent(saved.id)}/confirm`, {
      expectedRevision: saved.revision, sourceHash: saved.sourceHash, providerCoverageVerified: true, customerDutiesReviewed: true, reviewNotes,
    }, { signal })).data);
    if (result.id !== saved.id || result.status !== 'Accepted' || !result.reviewedBy || !result.reviewedAt)
      throw new Error('The response did not verify an authorized responsibility confirmation.');
    return result;
  });
export function plainResponsibilityValues(value: Record<ResponsibilityField, ResponsibilityValue>): ResponsibilityValues {
  return Object.fromEntries(responsibilityFields.map(key => [key, value[key].value])) as ResponsibilityValues;
}
