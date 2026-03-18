import apiClient from './client';
import type {
  EvidenceArtifactDto,
  EvidenceListResponse,
  EvidenceListParams,
  EvidenceSummaryDto,
  ControlEvidenceDto,
  EvidenceUploadParams,
  EvidenceReplaceParams,
  CollectEvidenceResult,
  EvidenceVersionDto,
} from '../types/evidence';

// ─── Evidence API Functions ─────────────────────────────────────────────────

export async function uploadEvidence(params: EvidenceUploadParams): Promise<EvidenceArtifactDto> {
  const formData = new FormData();
  formData.append('file', params.file);
  formData.append('artifactCategory', params.artifactCategory);
  if (params.controlImplementationId) formData.append('controlImplementationId', params.controlImplementationId);
  if (params.securityCapabilityId) formData.append('securityCapabilityId', params.securityCapabilityId);
  if (params.description) formData.append('description', params.description);
  if (params.collectionMethod) formData.append('collectionMethod', params.collectionMethod);

  const { data } = await apiClient.post<EvidenceArtifactDto>(
    `/systems/${params.systemId}/evidence`,
    formData,
    { headers: { 'Content-Type': 'multipart/form-data' } },
  );
  return data;
}

export async function listEvidence(params: EvidenceListParams): Promise<EvidenceListResponse> {
  const { systemId, ...queryParams } = params;
  const { data } = await apiClient.get<EvidenceListResponse>(
    `/systems/${systemId}/evidence`,
    { params: queryParams },
  );
  return data;
}

export async function getEvidence(systemId: string, evidenceId: string): Promise<EvidenceArtifactDto> {
  const { data } = await apiClient.get<EvidenceArtifactDto>(
    `/systems/${systemId}/evidence/${evidenceId}`,
  );
  return data;
}

export async function downloadEvidence(systemId: string, evidenceId: string): Promise<Blob> {
  const { data } = await apiClient.get<Blob>(
    `/systems/${systemId}/evidence/${evidenceId}/download`,
    { responseType: 'blob' },
  );
  return data;
}

export async function deleteEvidence(systemId: string, evidenceId: string): Promise<void> {
  await apiClient.delete(`/systems/${systemId}/evidence/${evidenceId}`);
}

export async function replaceEvidence(params: EvidenceReplaceParams): Promise<EvidenceArtifactDto> {
  const formData = new FormData();
  formData.append('file', params.file);
  if (params.description) formData.append('description', params.description);

  const { data } = await apiClient.put<EvidenceArtifactDto>(
    `/systems/${params.systemId}/evidence/${params.evidenceId}`,
    formData,
    { headers: { 'Content-Type': 'multipart/form-data' } },
  );
  return data;
}

export async function getEvidenceSummary(systemId: string): Promise<EvidenceSummaryDto> {
  const { data } = await apiClient.get<EvidenceSummaryDto>(
    `/systems/${systemId}/evidence/summary`,
  );
  return data;
}

export async function getControlEvidence(systemId: string, controlId: string): Promise<ControlEvidenceDto> {
  const { data } = await apiClient.get<ControlEvidenceDto>(
    `/systems/${systemId}/controls/${controlId}/evidence`,
  );
  return data;
}

export async function collectEvidence(systemId: string, controlId: string): Promise<CollectEvidenceResult> {
  const { data } = await apiClient.post<CollectEvidenceResult>(
    `/systems/${systemId}/controls/${controlId}/collect-evidence`,
  );
  return data;
}

export async function getEvidenceVersions(systemId: string, evidenceId: string): Promise<EvidenceVersionDto[]> {
  const { data } = await apiClient.get<EvidenceVersionDto[]>(
    `/systems/${systemId}/evidence/${evidenceId}/versions`,
  );
  return data;
}

export async function downloadEvidenceVersion(
  systemId: string,
  evidenceId: string,
  versionId: string,
): Promise<Blob> {
  const { data } = await apiClient.get<Blob>(
    `/systems/${systemId}/evidence/${evidenceId}/versions/${versionId}/download`,
    { responseType: 'blob' },
  );
  return data;
}
