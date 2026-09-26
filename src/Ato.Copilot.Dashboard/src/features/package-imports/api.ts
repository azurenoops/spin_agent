import { packageRequest } from './request';
import type { ClaimReviewInput, ClaimReviewReceipt } from './claims';
import type {
  EditPackageCandidate, PackageAnalysisOperation, PackageCandidate, PackageDecision, PackageEnrichmentReceipt, PackageEntry, PackagePage,
  PackagePreview, PackagePreviewRequest, PackagePublication, PackageQuery, PackageReviewState, PackageStatus,
} from './types';

const base = '/api/csp/package-imports';
const path = (id: string) => `${base}/${encodeURIComponent(id)}`;
export const packageImportHref = (id?: string) =>
  `/workspaces/csp/security-capabilities/imports${id ? `/${encodeURIComponent(id)}` : ''}`;
export const packageArtifactUrl = (id: string, artifactId: string) =>
  `${path(id)}/artifacts/${encodeURIComponent(artifactId)}/content`;

export function listPackages(page = 1, signal?: AbortSignal) {
  return packageRequest<PackagePage<PackageStatus>>({ method: 'GET', url: base, params: { page, pageSize: 25 }, signal });
}
export function getPackageStatus(id: string, signal?: AbortSignal) {
  return packageRequest<PackageStatus>({ method: 'GET', url: path(id), signal });
}
export function getPackageReviewState(id: string, signal?: AbortSignal) {
  return packageRequest<PackageReviewState>({ method: 'GET', url: `${path(id)}/review-state`, signal });
}
export function getPackageEntries(id: string, page = 1, signal?: AbortSignal) {
  return packageRequest<PackagePage<PackageEntry>>({ method: 'GET', url: `${path(id)}/entries`, params: { page, pageSize: 25 }, signal });
}
export function getPackageCandidates(id: string, query: PackageQuery, signal?: AbortSignal) {
  return packageRequest<PackagePage<PackageCandidate>>({ method: 'GET', url: `${path(id)}/candidates`, params: query, signal });
}
export function editPackageCandidate(id: string, candidateId: string, data: EditPackageCandidate) {
  return packageRequest<PackageCandidate>({ method: 'PATCH', url: `${path(id)}/candidates/${encodeURIComponent(candidateId)}`, data });
}
export function reviewPackageClaim(id: string, candidateId: string, data: ClaimReviewInput, key: string) {
  return packageRequest<ClaimReviewReceipt>({ method: 'POST', url: `${path(id)}/candidates/${encodeURIComponent(candidateId)}/claim-reviews`,
    data, headers: { 'Idempotency-Key': key } });
}
export function excludePackageEntry(id: string, entryId: string, expectedRevision: number, rationale: string) {
  return packageRequest<PackageEntry>({ method: 'PATCH', url: `${path(id)}/entries/${encodeURIComponent(entryId)}`, data: { expectedRevision, rationale } });
}
export function retryPackage(id: string, key: string) {
  return packageRequest<PackageStatus>({ method: 'POST', url: `${path(id)}/retry`, headers: { 'Idempotency-Key': key } });
}
export function enrichPackage(id: string, data: { expectedRevision: number; targetAnalysisProfileVersion: 2 }, key: string) {
  return packageRequest<PackageEnrichmentReceipt>({ method: 'POST', url: `${path(id)}/enrich`, data, headers: { 'Idempotency-Key': key } });
}
export function getAnalysisOperation(id: string, operationId: string, signal?: AbortSignal) {
  return packageRequest<PackageAnalysisOperation>({ method: 'GET', url: `${path(id)}/analysis-operations/${encodeURIComponent(operationId)}`, signal });
}
export function previewPackage(id: string, data: PackagePreviewRequest) {
  return packageRequest<PackagePreview>({ method: 'POST', url: `${path(id)}/approval-previews`, data });
}
export function approvePackage(id: string, data: PackageDecision) {
  return packageRequest<PackagePreview>({ method: 'POST', url: `${path(id)}/approve`, data });
}
export function publishPackage(id: string, data: PackageDecision, key: string) {
  return packageRequest<PackagePublication>({ method: 'POST', url: `${path(id)}/publish`, data, headers: { 'Idempotency-Key': key } });
}
export async function receivePackage(files: File[], key: string, onboarding = false) {
  const form = new FormData();
  files.forEach(file => form.append('files', file, file.name));
  const receipt = await packageRequest<PackageStatus>({
    method: 'POST', url: onboarding ? '/api/csp/onboarding/atos/upload' : '/api/csp/inherited-components/import',
    data: form, headers: { 'Content-Type': 'multipart/form-data', Prefer: 'respond-async', 'Idempotency-Key': key },
  });
  if (!receipt || typeof receipt.packageId !== 'string' || !receipt.packageId
    || typeof receipt.operationId !== 'string' || !receipt.operationId) {
    throw new Error('The server did not return a durable package receipt. Retry with the same upload key.');
  }
  return receipt;
}
