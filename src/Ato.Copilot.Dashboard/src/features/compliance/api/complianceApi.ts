import apiClient from '../../../api/client';

export type ControlValidationLinkType = 'AzureResource' | 'ScanFinding' | 'EvidenceArtifact' | 'ExternalUrl';

export interface ControlValidationLink {
  id: string;
  linkType: ControlValidationLinkType;
  linkTarget: string;
  description: string | null;
  addedBy: string;
  addedAt: string;
  validatedAt: string | null;
  isAutomated: boolean;
}

export interface ControlValidationLinksResponse {
  systemId: string;
  controlId: string;
  total: number;
  links: ControlValidationLink[];
}

export interface AddValidationLinkPayload {
  linkType: ControlValidationLinkType;
  linkTarget: string;
  description?: string;
}

function validationPath(systemId: string, controlId: string): string {
  return `/systems/${encodeURIComponent(systemId)}/controls/${encodeURIComponent(controlId)}/validation`;
}

export async function getControlValidationLinks(
  systemId: string,
  controlId: string,
): Promise<ControlValidationLinksResponse> {
  const response = await apiClient.get<ControlValidationLinksResponse>(validationPath(systemId, controlId));
  return response.data;
}

export async function addValidationLink(
  systemId: string,
  controlId: string,
  payload: AddValidationLinkPayload,
): Promise<ControlValidationLink> {
  const response = await apiClient.post<ControlValidationLink>(validationPath(systemId, controlId), payload);
  return response.data;
}

export async function deleteValidationLink(
  systemId: string,
  controlId: string,
  linkId: string,
): Promise<void> {
  await apiClient.delete(`${validationPath(systemId, controlId)}/${encodeURIComponent(linkId)}`);
}