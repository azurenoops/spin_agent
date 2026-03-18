// ─── Evidence Repository Types (Feature 038) ──────────────────────────────────

export type ArtifactCategory =
  | 'Screenshot'
  | 'ScanResult'
  | 'ConfigurationExport'
  | 'PolicyDocument'
  | 'AuditLog'
  | 'TestResult'
  | 'Other';

export type CollectionMethod =
  | 'Manual'
  | 'AutomatedScan'
  | 'ApiExport'
  | 'Other';

export type EvidenceSource = 'Manual' | 'Automated';

// ─── DTOs ──────────────────────────────────────────────────────────────────────

export interface EvidenceArtifactDto {
  id: string;
  source: EvidenceSource;
  fileName: string | null;
  contentType: string | null;
  fileSizeBytes: number | null;
  artifactCategory: string;
  collectionMethod?: CollectionMethod;
  controlId: string | null;
  controlImplementationId: string | null;
  securityCapabilityId: string | null;
  capabilityName?: string | null;
  description: string | null;
  uploadedBy: string;
  uploadedAt: string;
  contentHash: string;
  storagePath?: string;
  versions?: EvidenceVersionDto[];
}

export interface EvidenceVersionDto {
  id: string;
  fileName: string;
  fileSizeBytes: number;
  contentHash: string;
  replacedBy: string;
  replacedAt: string;
  purgeAfter?: string;
  isFilePurged: boolean;
}

export interface EvidenceSummaryDto {
  totalCount: number;
  manualCount: number;
  automatedCount: number;
  controlsWithEvidence: number;
  totalControls: number;
  coveragePercentage: number;
}

export interface ControlEvidenceDto {
  direct: EvidenceArtifactDto[];
  inherited: (EvidenceArtifactDto & { inheritedFromCapability: string })[];
  automated: EvidenceArtifactDto[];
}

export interface EvidenceListResponse {
  items: EvidenceArtifactDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// ─── Request Types ─────────────────────────────────────────────────────────────

export interface EvidenceUploadParams {
  systemId: string;
  file: File;
  artifactCategory: ArtifactCategory;
  controlImplementationId?: string;
  securityCapabilityId?: string;
  description?: string;
  collectionMethod?: CollectionMethod;
}

export interface EvidenceListParams {
  systemId: string;
  page?: number;
  pageSize?: number;
  search?: string;
  controlFamily?: string;
  category?: ArtifactCategory;
  source?: EvidenceSource;
  dateFrom?: string;
  dateTo?: string;
  sortBy?: 'uploadedAt' | 'fileName' | 'controlId' | 'category';
  sortOrder?: 'asc' | 'desc';
}

export interface EvidenceReplaceParams {
  systemId: string;
  evidenceId: string;
  file: File;
  description?: string;
}

export interface CollectEvidenceResult {
  evidenceId: string;
  controlId: string;
  evidenceType: string;
  collectedAt: string;
  contentHash: string;
}
