/**
 * Issue #202 — typed API client module for NIST KB overlay documents.
 *
 * NOTE on API URL: apiClient's baseURL is `/api/dashboard`, but the overlay
 * endpoints are served at the absolute path `/api/v1/admin/overlay-documents`.
 * The leading `/api/v1/...` bypasses the baseURL, hitting the correct server
 * route directly.  Do not prepend a relative segment — keep the leading slash.
 */
import apiClient from './client';

export interface OverlayDocumentDto {
  id: string;
  type: string;
  title: string;
  controlId: string;
  content: string;
  sourceReference?: string;
  tenantId?: string;
  isActive: boolean;
  createdBy: string;
  createdAt: string;
  modifiedBy?: string;
  modifiedAt?: string;
}

export interface CreateOverlayDocumentRequest {
  type: string;
  title: string;
  controlId: string;
  content: string;
  sourceReference?: string;
  tenantId?: string;
}

export interface UpdateOverlayDocumentRequest {
  title?: string;
  content?: string;
  sourceReference?: string;
  isActive?: boolean;
}

export async function listOverlayDocuments(params?: {
  controlId?: string;
  type?: string;
  includeInactive?: boolean;
}): Promise<OverlayDocumentDto[]> {
  const res = await apiClient.get<OverlayDocumentDto[]>(
    '/api/v1/admin/overlay-documents',
    { params },
  );
  return res.data;
}

export async function getOverlayDocument(id: string): Promise<OverlayDocumentDto> {
  const res = await apiClient.get<OverlayDocumentDto>(
    `/api/v1/admin/overlay-documents/${id}`,
  );
  return res.data;
}

export async function createOverlayDocument(
  req: CreateOverlayDocumentRequest,
): Promise<OverlayDocumentDto> {
  const res = await apiClient.post<OverlayDocumentDto>(
    '/api/v1/admin/overlay-documents',
    req,
  );
  return res.data;
}

export async function updateOverlayDocument(
  id: string,
  req: UpdateOverlayDocumentRequest,
): Promise<OverlayDocumentDto> {
  const res = await apiClient.put<OverlayDocumentDto>(
    `/api/v1/admin/overlay-documents/${id}`,
    req,
  );
  return res.data;
}

export async function deleteOverlayDocument(id: string): Promise<void> {
  await apiClient.delete(`/api/v1/admin/overlay-documents/${id}`);
}
