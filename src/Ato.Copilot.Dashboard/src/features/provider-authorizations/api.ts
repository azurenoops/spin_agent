import { packageRequest } from '../package-imports/request';
import type {
  AssociatedPackage, BoundaryInput, BoundaryRevision, ExternalDecision, ExternalDecisionInput,
  Finding, FindingInput, ImpactInput, ImpactPreview, ImpactReview, ImpactTarget, Offering, OfferingBoundaryOverview, OfferingOverviewData, PackageReceipt, PackageVersion, Page,
} from './types';

const root = '/api/csp/offerings';
export const offeringPath = (id: string) => `${root}/${encodeURIComponent(id)}`;
export const authorizationHref = (id?: string, section = '') =>
  `/workspaces/csp/authorizations${id ? `/offerings/${encodeURIComponent(id)}${section ? `/${section}` : ''}` : ''}`;
export const importHref = '/workspaces/csp/authorizations/import';
export function changeImpactHref(id: string, context: {
  packageVersionId?: string; packageId?: string; boundaryRevisionId?: string; capabilityId?: string; reviewId?: string;
} = {}) {
  const query = new URLSearchParams();
  for (const [name, value] of Object.entries(context)) if (value) query.set(name, value);
  return `${authorizationHref(id, 'impact')}${query.size ? `?${query}` : ''}`;
}
const params = (page: number) => ({ page, pageSize: 25 });
const keyHeader = (key: string) => ({ 'Idempotency-Key': key });

export const listOfferings = (page = 1, search = '', signal?: AbortSignal) =>
  packageRequest<Page<Offering>>({ url: root, params: { ...params(page), search }, signal });
export const getOffering = (id: string, signal?: AbortSignal) =>
  packageRequest<Offering>({ url: offeringPath(id), signal });
export async function getOfferingOverview(id: string, authorizationPage = 1, packagePage = 1, signal?: AbortSignal) {
  const result = await packageRequest<OfferingOverviewData>({
    url: `${offeringPath(id)}/overview`, params: { authorizationPage, packagePage, pageSize: 10 }, signal,
  });
  if (!result || result.offeringId !== id || !Number.isSafeInteger(result.offeringRevision) || result.offeringRevision < 1)
    throw new Error('The overview did not identify the requested offering and current revision. Reload before continuing.');
  return result;
}
export const createOffering = (data: Pick<Offering, 'name' | 'description' | 'environments'>, key: string) =>
  packageRequest<Offering>({ method: 'POST', url: root, data, headers: keyHeader(key) });
export const updateOffering = (id: string, data: Pick<Offering, 'name' | 'description' | 'environments'> & { expectedRevision: number }) =>
  packageRequest<Offering>({ method: 'PATCH', url: offeringPath(id), data });
export const listBoundaries = (id: string, page = 1, signal?: AbortSignal) =>
  packageRequest<Page<BoundaryRevision>>({ url: `${offeringPath(id)}/boundary-revisions`, params: params(page), signal });
export async function getBoundary(id: string, revisionId: string, signal?: AbortSignal) {
  const boundary = await packageRequest<BoundaryRevision>({
    url: `${offeringPath(id)}/boundary-revisions/${encodeURIComponent(revisionId)}`, signal,
  });
  if (!boundary || boundary.offeringId !== id || boundary.boundaryRevisionId !== revisionId)
    throw new Error('The server did not return the selected offering and boundary. Reload before editing.');
  return boundary;
}
export const getBoundaryOverview = (id: string, capabilityPage = 1, missionPage = 1, signal?: AbortSignal) =>
  packageRequest<OfferingBoundaryOverview>({
    url: `${offeringPath(id)}/boundary-overview`, params: { capabilityPage, missionPage, pageSize: 10 }, signal,
  });
export const createBoundary = (id: string, data: BoundaryInput & { expectedOfferingRevision: number; predecessorRevisionId: string | null }, key: string) =>
  packageRequest<BoundaryRevision>({ method: 'POST', url: `${offeringPath(id)}/boundary-revisions`, data, headers: keyHeader(key) });
export const listPackageVersions = (id: string, page = 1, signal?: AbortSignal) =>
  packageRequest<Page<PackageVersion>>({ url: `${offeringPath(id)}/package-versions`, params: params(page), signal });
export async function getAssociatedPackage(id: string, signal?: AbortSignal) {
  const result = await packageRequest<AssociatedPackage>({ url: `/api/csp/package-imports/${encodeURIComponent(id)}`, signal });
  if (!result || result.association === undefined || (result.association !== null
    && (!result.association.offeringId || !result.association.packageVersionId || !result.association.boundaryRevisionId))) {
    throw new Error('The server did not provide package association metadata. Reload after the authorization API is available; ownership cannot be inferred.');
  }
  return result;
}
export async function uploadPackage(id: string, data: {
  name: string; boundaryRevisionId: string; expectedOfferingRevision: number; seriesId?: string; previousVersionId?: string;
}, files: File[], key: string) {
  const form = new FormData();
  Object.entries(data).forEach(([name, value]) => { if (value !== undefined) form.append(name, String(value)); });
  files.forEach(file => form.append('files', file, file.name));
  const receipt = await packageRequest<PackageReceipt>({
    method: 'POST', url: `${offeringPath(id)}/package-versions`, data: form,
    headers: { ...keyHeader(key), Prefer: 'respond-async' },
  });
  if (!receipt.package?.packageId || !receipt.package.operationId || !receipt.packageVersion?.packageVersionId) {
    throw new Error('No durable offering package receipt was returned. Retry the same upload; keep the selected files.');
  }
  if (receipt.packageVersion.offeringId !== id || receipt.packageVersion.boundaryRevisionId !== data.boundaryRevisionId
    || receipt.packageVersion.packageId !== receipt.package.packageId) {
    throw new Error('The persisted receipt does not match the selected offering, boundary and source package. Keep the files and retry the same operation to recover its exact receipt.');
  }
  return receipt;
}
export const associatePackage = (id: string, data: {
  expectedPackageRevision: number; offeringId: string; expectedOfferingRevision: number;
  boundaryRevisionId: string; seriesId?: string; previousVersionId?: string;
}, key: string) => packageRequest<PackageReceipt>({
  method: 'POST', url: `/api/csp/package-imports/${encodeURIComponent(id)}/association`, data, headers: keyHeader(key),
});
export const listDecisions = (id: string, page = 1, signal?: AbortSignal, recordKind?: ExternalDecision['recordKind']) =>
  packageRequest<Page<ExternalDecision>>({ url: `${offeringPath(id)}/authorization-records`,
    params: { ...params(page), ...(recordKind ? { recordKind } : {}) }, signal });
export const listMicrosoftReferences = (id: string, page = 1, signal?: AbortSignal) =>
  packageRequest<Page<ExternalDecision>>({ url: `${offeringPath(id)}/authorization-records`,
    params: { ...params(page), recordKind: 'InheritedMicrosoftReference' }, signal });
export const listDecisionHistory = (id: string, recordId: string, page = 1, signal?: AbortSignal) =>
  packageRequest<Page<ExternalDecision>>({ url: `${offeringPath(id)}/authorization-records/${encodeURIComponent(recordId)}/revisions`, params: params(page), signal });
export const createDecision = (id: string, data: ExternalDecisionInput & { expectedOfferingRevision: number }, key: string) =>
  packageRequest<ExternalDecision>({ method: 'POST', url: `${offeringPath(id)}/authorization-records`, data, headers: keyHeader(key) });
export const reviseDecision = (id: string, recordId: string, data: ExternalDecisionInput & { expectedRevision: number }) =>
  packageRequest<ExternalDecision>({ method: 'PUT', url: `${offeringPath(id)}/authorization-records/${encodeURIComponent(recordId)}/draft`, data });
export const recordDecision = (id: string, decision: ExternalDecision, rationale: string) =>
  packageRequest<ExternalDecision>({ method: 'POST', url: `${offeringPath(id)}/authorization-records/${encodeURIComponent(decision.recordId)}/record`,
    data: { expectedRevision: decision.revision, revisionId: decision.revisionId, snapshotHash: decision.snapshotHash, rationale } });
export const lifecycleDecision = (id: string, recordId: string, data: {
  expectedRevision: number; kind: 'Withdrawn' | 'Superseded'; effectiveOn: string; replacementRevisionId?: string;
  rationale: string; citations: ExternalDecisionInput['citations'];
}, key: string) => packageRequest<{ eventId: string; record: ExternalDecision; impactReviewId: string }>({
  method: 'POST', url: `${offeringPath(id)}/authorization-records/${encodeURIComponent(recordId)}/lifecycle-events`, data, headers: keyHeader(key),
});
export const listFindings = (id: string, page = 1, signal?: AbortSignal) =>
  packageRequest<Page<Finding>>({ url: `${offeringPath(id)}/findings`, params: params(page), signal });
export const createFinding = (id: string, data: FindingInput & { expectedOfferingRevision: number }, key: string) =>
  packageRequest<Finding>({ method: 'POST', url: `${offeringPath(id)}/findings`, data, headers: keyHeader(key) });
export const previewImpact = (id: string, data: ImpactInput, key: string) =>
  packageRequest<ImpactPreview>({ method: 'POST', url: `${offeringPath(id)}/impact-previews`, data, headers: keyHeader(key) });
export const reviewImpact = (id: string, preview: ImpactPreview, disposition: 'AcceptForPublication' | 'RequestChanges' | 'Reject', rationale: string) =>
  packageRequest<ImpactReview>({ method: 'POST', url: `${offeringPath(id)}/impact-reviews/${encodeURIComponent(preview.reviewId)}/review`,
    data: { expectedRevision: preview.revision, previewId: preview.previewId, previewHash: preview.previewHash, disposition, rationale } });
export const listImpactReviews = (id: string, page = 1, signal?: AbortSignal) =>
  packageRequest<Page<ImpactReview>>({ url: `${offeringPath(id)}/impact-reviews`, params: params(page), signal });
export const getImpactReview = (id: string, reviewId: string, signal?: AbortSignal) =>
  packageRequest<ImpactReview>({ url: `${offeringPath(id)}/impact-reviews/${encodeURIComponent(reviewId)}`, signal });
export const listImpactTargets = (id: string, reviewId: string, page = 1, signal?: AbortSignal) =>
  packageRequest<Page<ImpactTarget>>({ url: `${offeringPath(id)}/impact-reviews/${encodeURIComponent(reviewId)}/affected-targets`, params: params(page), signal });
