import { packageRequest } from '../package-imports/request';
import { offeringPath } from './api';
import type { ImpactInput, ImpactReview, Page } from './types';

export type ImpactOptionKind = 'Boundary' | 'HostingScope' | 'Authorization' | 'Package' | 'Component' | 'Capability';
export interface ImpactOption {
  id: string; name: string; version: string; summary: string;
  change: ImpactInput['changes'][number] | null;
}
export interface ImpactAffectedItem {
  recordId: string; name: string | null; kind: string; summary: string; reviewState: string;
}
export interface ImpactDetails {
  review: ImpactReview; title: string; summary: string; rationale: string | null; createdAt: string | null;
  context: ImpactInput | null;
  changes: { kind: string; recordId: string; name: string | null; summary: string; expectedRevision: number | string; proposedSnapshotHash: string }[];
  blockers: { code: string; message: string; targetId?: string }[];
  affectedCapabilities: Page<ImpactAffectedItem>; affectedSystems: Page<ImpactAffectedItem>;
}

const object = (value: unknown): value is Record<string, unknown> =>
  value !== null && typeof value === 'object' && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0;
const nullableText = (value: unknown): value is string | null => value === null || typeof value === 'string';
const integer = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value > 0;
const revision = (value: unknown): value is number | string => integer(value)
  || (typeof value === 'string' && /^[1-9][0-9]{0,18}$/.test(value) && BigInt(value) <= 9223372036854775807n);
const hash = (value: unknown): value is string => typeof value === 'string' && /^[a-fA-F0-9]{64}$/.test(value);
const array = <T>(value: unknown, valid: (item: unknown) => item is T): value is T[] =>
  Array.isArray(value) && value.every(valid);
const date = (value: unknown): value is string | null => value === null || (text(value) && Number.isFinite(Date.parse(value)));
const count = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const counts = (value: unknown): value is NonNullable<ImpactReview['affectedCounts']> => object(value)
  && count(value.components) && count(value.capabilities) && count(value.scopes) && count(value.systems);
const change = (value: unknown): value is ImpactInput['changes'][number] => object(value)
  && ['Boundary', 'HostingScope', 'Component', 'Capability'].includes(String(value.kind))
  && text(value.kind) && text(value.recordId) && revision(value.expectedRevision) && hash(value.proposedSnapshotHash);

function invalid(): never {
  throw new Error('The server did not return complete, exact change-impact data. Reload the read before reviewing; no impact, approval or coverage can be inferred.');
}
function option(value: unknown, kind: ImpactOptionKind): ImpactOption {
  if (!object(value) || !text(value.id) || !text(value.name) || !text(value.version) || !text(value.summary)
    || !(value.change === null || change(value.change))) return invalid();
  if (value.change !== null && (value.change.kind !== kind || value.change.recordId !== value.id
    || kind === 'Authorization' || kind === 'Package')) return invalid();
  return { id: value.id, name: value.name, version: value.version, summary: value.summary, change: value.change };
}
function page<T>(value: unknown, expectedPage: number, parse: (item: unknown) => T): Page<T> {
  if (!integer(expectedPage) || !object(value) || !Array.isArray(value.items)
    || value.page !== expectedPage || value.pageSize !== 25
    || typeof value.total !== 'number' || !Number.isSafeInteger(value.total) || value.total < 0
    || value.items.length > 25 || value.items.length > value.total
    || (value.items.length > 0 && (expectedPage - 1) * 25 + value.items.length > value.total)) return invalid();
  return { items: value.items.map(parse), page: expectedPage, pageSize: 25, total: value.total };
}
function input(value: unknown): ImpactInput | null {
  if (value === null) return null;
  if (!object(value) || !integer(value.expectedOfferingRevision) || !array(value.changes, change)
    || value.changes.length < 1 || value.changes.length > 100 || !array(value.authorizationRevisionIds, text)
    || value.authorizationRevisionIds.length > 100
    || !array(value.packageVersionIds, text) || value.packageVersionIds.length > 100 || !text(value.boundaryRevisionId)
    || !(value.hostingScopeRevisionId === null || value.hostingScopeRevisionId === undefined || text(value.hostingScopeRevisionId))) return invalid();
  return {
    expectedOfferingRevision: value.expectedOfferingRevision, changes: value.changes,
    authorizationRevisionIds: value.authorizationRevisionIds, boundaryRevisionId: value.boundaryRevisionId,
    // The existing input type uses absence for an optional hosting scope; the server writes JSON null.
    ...(typeof value.hostingScopeRevisionId === 'string' ? { hostingScopeRevisionId: value.hostingScopeRevisionId } : {}),
    packageVersionIds: value.packageVersionIds,
  };
}
function review(value: unknown, id: string): ImpactReview {
  if (!object(value) || value.reviewId !== id || !integer(value.revision) || !text(value.disposition)
    || !nullableText(value.reviewedBy) || !date(value.reviewedAt) || typeof value.contextSnapshotHash !== 'string'
    || !(value.contextSnapshotHash === '' || hash(value.contextSnapshotHash)) || typeof value.stale !== 'boolean'
    || (value.title !== undefined && !nullableText(value.title)) || (value.summary !== undefined && !nullableText(value.summary))
    || (value.createdAt !== undefined && !date(value.createdAt))
    || !(value.affectedCounts === undefined || value.affectedCounts === null || counts(value.affectedCounts))) return invalid();
  return { reviewId: id, revision: value.revision, disposition: value.disposition, reviewedBy: value.reviewedBy,
    reviewedAt: value.reviewedAt, contextSnapshotHash: value.contextSnapshotHash, stale: value.stale,
    ...(value.title !== undefined ? { title: value.title } : {}),
    ...(value.summary !== undefined ? { summary: value.summary } : {}),
    ...(value.createdAt !== undefined ? { createdAt: value.createdAt } : {}),
    ...(value.affectedCounts !== undefined ? { affectedCounts: value.affectedCounts } : {}) };
}
function affected(value: unknown): ImpactAffectedItem {
  if (!object(value) || !text(value.recordId) || !nullableText(value.name) || !text(value.kind)
    || !text(value.summary) || !text(value.reviewState)) return invalid();
  return { recordId: value.recordId, name: value.name, kind: value.kind, summary: value.summary, reviewState: value.reviewState };
}

export async function listImpactOptions(offeringId: string, kind: ImpactOptionKind, selectedPage = 1, signal?: AbortSignal): Promise<Page<ImpactOption>> {
  const result = await packageRequest<unknown>({
    url: `${offeringPath(offeringId)}/impact-options`, params: { kind, page: selectedPage, pageSize: 25 }, signal,
  });
  return page(result, selectedPage, item => option(item, kind));
}
export async function getImpactOption(offeringId: string, kind: ImpactOptionKind, id: string, signal?: AbortSignal): Promise<ImpactOption> {
  const result = option(await packageRequest<unknown>({
    url: `${offeringPath(offeringId)}/impact-options/${encodeURIComponent(kind)}/${encodeURIComponent(id)}`, signal,
  }), kind);
  if (result.id !== id) return invalid();
  return result;
}
export async function getImpactDetails(offeringId: string, reviewId: string, capabilityPage = 1, systemPage = 1, signal?: AbortSignal): Promise<ImpactDetails> {
  const result = await packageRequest<unknown>({
    url: `${offeringPath(offeringId)}/impact-reviews/${encodeURIComponent(reviewId)}/details`,
    params: { capabilityPage, systemPage, pageSize: 25 }, signal,
  });
  if (!object(result) || !text(result.title) || !text(result.summary) || !nullableText(result.rationale)
    || !date(result.createdAt) || !Array.isArray(result.changes) || !Array.isArray(result.blockers)) return invalid();
  const retainedInput = input(result.context);
  const retainedReview = review(result.review, reviewId);
  const changes = result.changes.map(item => {
    if (!object(item) || !nullableText(item.name) || !text(item.summary)) return invalid();
    const name = item.name;
    const summary = item.summary;
    if (!change(item)) return invalid();
    return { kind: item.kind, recordId: item.recordId, expectedRevision: item.expectedRevision,
      proposedSnapshotHash: item.proposedSnapshotHash, name, summary };
  });
  if (retainedInput !== null && (changes.length !== retainedInput.changes.length
    || changes.some((item, index) => {
      const original = retainedInput.changes[index];
      return original === undefined || item.kind !== original.kind || item.recordId !== original.recordId
        || String(item.expectedRevision) !== String(original.expectedRevision) || item.proposedSnapshotHash !== original.proposedSnapshotHash;
    }) || !hash(retainedReview.contextSnapshotHash))) return invalid();
  const blockers = result.blockers.map(item => {
    if (!object(item) || !text(item.code) || !text(item.message)
      || !(item.targetId === undefined || item.targetId === null || text(item.targetId))) return invalid();
    return { code: item.code, message: item.message, ...(typeof item.targetId === 'string' ? { targetId: item.targetId } : {}) };
  });
  return { review: retainedReview, title: result.title, summary: result.summary, rationale: result.rationale,
    createdAt: result.createdAt, context: retainedInput, changes, blockers,
    affectedCapabilities: page(result.affectedCapabilities, capabilityPage, affected),
    affectedSystems: page(result.affectedSystems, systemPage, affected) };
}
