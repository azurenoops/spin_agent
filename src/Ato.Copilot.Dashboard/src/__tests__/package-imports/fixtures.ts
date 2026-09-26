import type { PackageCandidate, PackageEntry, PackagePage, PackagePreview, PackageReviewState, PackageStatus } from '../../features/package-imports/types';

export const page = <T,>(items: T[], pageNumber = 1, total = items.length): PackagePage<T> =>
  ({ items, page: pageNumber, pageSize: 25, total });

export const packageStatus = (overrides: Partial<PackageStatus> = {}): PackageStatus => ({
  packageId: 'package-1', operationId: 'operation-1', name: 'Synthetic package', revision: 4,
  processingState: 'ReadyForReview', publicationState: 'Unpublished',
  coverage: { total: 1, pending: 0, processed: 1, unsupported: 0, unreadable: 0, failed: 0, excluded: 0 },
  lastError: null, createdAt: '2026-09-23T12:00:00Z', updatedAt: '2026-09-23T12:10:00Z', ...overrides,
});
export const candidate = (overrides: Partial<PackageCandidate> = {}): PackageCandidate => ({
  candidateId: 'candidate-1', type: 'Component', name: 'Synthetic source component', description: 'Source-supported component.',
  componentType: 'Service', classification: 'Unclassified', serviceCategory: 'Security',
  controlDuties: { 'AC-2': 'Provider' },
  contributorIds: [], citations: [{ artifactId: 'artifact-1', archivePath: 'package.json', locator: 'component 1', quote: 'Provider manages the source account lifecycle.' }],
  duplicateMatches: [], duplicateResolution: null, rationale: null, reviewState: 'NeedsReview', revision: 1,
  confidence: 0.99, publishedRecordId: null, ...overrides,
});
export const entry = (overrides: Partial<PackageEntry> = {}): PackageEntry => ({
  entryId: 'entry-1', artifactId: 'artifact-1', fileName: 'package.json', archivePath: 'package.json',
  mediaType: 'application/json', byteLength: 100, sha256: 'synthetic-sha', status: 'Processed',
  reason: null, candidateCount: 1, exclusionReason: null, revision: 1, ...overrides,
});
export const preview = (overrides: Partial<PackagePreview> = {}): PackagePreview => ({
  previewId: 'preview-1', previewHash: 'exact-hash', revision: 4, state: 'Preview',
  candidates: [{ candidateId: 'candidate-1', revision: 1 }], blockers: [], newComponents: 1, newCapabilities: 0,
  ...overrides,
});
export const reviewState = (overrides: Partial<PackageReviewState> = {}): PackageReviewState => ({
  packageId: 'package-1', revision: 4, preview: null, previewIsStale: false, publication: null, ...overrides,
});
