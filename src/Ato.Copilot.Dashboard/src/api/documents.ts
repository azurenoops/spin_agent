import apiClient from './client';

// ─── Types ──────────────────────────────────────────────────────────────────

export interface SspDocumentInfo {
  narrativeCompletionPct: number;
  totalNarratives: number;
  completedNarratives: number;
}

export interface SapDocumentInfo {
  sapId: string;
  status: string;
  title: string;
  contentHash: string | null;
  totalControls: number;
  finalizedAt: string | null;
  scheduleStart: string | null;
  scheduleEnd: string | null;
}

export interface AuthDecisionInfo {
  decisionId: string;
  decisionType: string;
  decisionDate: string;
  expirationDate: string | null;
  residualRisk: string;
  issuedBy: string;
  daysUntilExpiration: number | null;
}

export interface PtaDocumentInfo {
  ptaId: string;
  determination: string;
  collectsPii: boolean;
  piiCategories: string[];
  analyzedAt: string;
  analyzedBy: string;
}

export interface PiaDocumentInfo {
  piaId: string;
  status: string;
  version: number;
  approvedBy: string | null;
  approvedAt: string | null;
  expirationDate: string | null;
  daysUntilExpiration: number | null;
}

export interface InterconnectionDocInfo {
  interconnectionId: string;
  targetSystem: string;
  direction: string;
  status: string;
  hasAgreement: boolean;
  agreementType: string | null;
  agreementStatus: string | null;
}

export interface ConMonInfo {
  planId: string;
  frequency: string;
  reportCount: number;
  lastReportDate: string | null;
}

export interface SspSectionInfo {
  sectionNumber: number;
  title: string;
  status: string;
  authoredBy: string | null;
  authoredAt: string | null;
  reviewedBy: string | null;
  reviewedAt: string | null;
  version: number;
}

export interface NarrativeGovernanceInfo {
  totalNarratives: number;
  draft: number;
  inReview: number;
  approved: number;
  needsRevision: number;
  approvalPct: number;
}

export interface ScanImportInfo {
  importId: string;
  importType: string;
  fileName: string;
  importedAt: string;
  totalEntries: number;
  openCount: number;
  passCount: number;
  benchmarkTitle: string | null;
}

export interface SystemDocumentsResponse {
  systemId: string;
  systemName: string;
  currentPhase: string;

  ssp: SspDocumentInfo;
  sap: SapDocumentInfo | null;
  authorization: AuthDecisionInfo | null;
  poamCount: number;
  poamOverdueCount: number;
  hasBaseline: boolean;
  baselineControlCount: number;

  pta: PtaDocumentInfo | null;
  pia: PiaDocumentInfo | null;

  interconnections: InterconnectionDocInfo[];

  conMon: ConMonInfo | null;

  sspSections: SspSectionInfo[];

  narrativeGovernance: NarrativeGovernanceInfo | null;

  importHistory: ScanImportInfo[];

  inventoryItemCount: number;
}

// ─── API Functions ──────────────────────────────────────────────────────────

export async function getSystemDocuments(
  systemId: string,
): Promise<SystemDocumentsResponse> {
  const { data } = await apiClient.get<SystemDocumentsResponse>(
    `/systems/${systemId}/documents`,
  );
  return data;
}
