import type { PackageClaim } from './claims';
// Wire records: Core/Interfaces/PackageImports/ICspPackageService.cs.
export interface PackagePage<T> { items: T[]; page: number; pageSize: number; total: number }
export interface PackageCoverage {
  total: number; pending: number; processed: number; unsupported: number;
  unreadable: number; failed: number; excluded: number;
}
export interface PackageStatus {
  packageId: string; operationId: string; name: string; revision: number;
  processingState: string; publicationState: string; coverage: PackageCoverage;
  lastError: string | null; createdAt: string; updatedAt: string;
  analysisProfileVersion?: number;
  analysisProgress?: {
    completedSegments: number; totalSegments: number; modelCalls: number;
    modelCallLimit: number; continuingAutomatically: boolean;
  } | null;
}
export interface PackageEntry {
  entryId: string; artifactId: string; fileName: string; archivePath: string; mediaType: string;
  byteLength: number; sha256: string; status: string; reason: string | null;
  candidateCount: number; exclusionReason: string | null; revision: number;
  familyCoverage?: { family: string; status: string; reason: string | null }[];
}
export interface PackageCitation { artifactId: string; archivePath: string; locator: string; quote: string }
export interface PackageDuplicate { recordId: string; name: string; type: string; published: boolean }
export interface PackageAuthorizationReference {
  reference: string; issuer: string | null; issuedAt: string | null; expiresAt: string | null;
}
export interface PackageCandidate {
  candidateId: string; type: string; name: string; description: string; componentType: string;
  classification: string; serviceCategory: string; controlDuties: Record<string, string>;
  contributorIds: string[]; citations: PackageCitation[]; duplicateMatches: PackageDuplicate[];
  duplicateResolution: string | null; rationale: string | null; reviewState: string; revision: number;
  confidence: number | null; publishedRecordId: string | null;
  unresolvedDependencies?: string[] | null;
  authorizationReference?: PackageAuthorizationReference | null;
  claim?: PackageClaim | null;
}
export interface EditPackageCandidate {
  expectedRevision: number; name: string; description: string; componentType: string | null;
  classification: string; serviceCategory: string; controlDuties: Record<string, string>;
  contributorIds: string[]; reviewAction: 'NeedsReview' | 'Reviewed' | 'Rejected';
  rationale: string | null; duplicateResolution: string | null;
  authorizationReference?: PackageAuthorizationReference | null;
}
export interface PackageSelection { candidateId: string; revision: number }
export interface PackagePreviewRequest { expectedRevision: number; candidates: PackageSelection[]; impactReviewIds?: string[] }
export interface PackageDecision { previewId: string; previewHash: string; revision: number }
export interface PackagePreview extends PackageDecision {
  state: string; candidates: PackageSelection[]; blockers: string[]; newComponents: number; newCapabilities: number;
  impactReviewIds?: string[] | null; contextSnapshotHash?: string | null;
}
export interface PackagePublishedRecord { candidateId: string; recordId: string; releaseId: string | null; type: string }
export interface PackagePublication {
  packageId: string; publicationState: string; records: PackagePublishedRecord[]; existing: boolean;
}
export interface PackageReviewState {
  packageId: string; revision: number; preview: PackagePreview | null;
  previewIsStale: boolean; publication: PackagePublication | null;
}
export interface PackageEnrichmentReceipt {
  operationId: string; packageId: string; targetAnalysisProfileVersion: number; state: string; existing: boolean;
}
export interface PackageAnalysisOperation {
  operationId: string; packageId: string; sourceProfileVersion: number; targetAnalysisProfileVersion: number;
  state: string; errorCode?: string | null; message?: string | null;
}
export interface PackageQuery { page: number; pageSize: number; type?: string; reviewState?: string }
