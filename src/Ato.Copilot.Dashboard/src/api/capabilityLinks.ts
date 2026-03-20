import apiClient from './client';
import type { SystemCapabilityLink } from '../types/dashboard';

interface LinkCapabilitiesResponse {
  linkedCount: number;
  items: SystemCapabilityLink[];
}

interface GetCapabilityLinksResponse {
  items: SystemCapabilityLink[];
  totalCount: number;
}

interface DeleteLinkResponse {
  deletedId: string;
  message: string;
}

export async function linkCapabilities(
  systemId: string,
  capabilityIds: string[],
): Promise<LinkCapabilitiesResponse> {
  const { data } = await apiClient.post<LinkCapabilitiesResponse>(
    `/systems/${systemId}/capability-links`,
    { capabilityIds },
  );
  return data;
}

export async function getCapabilityLinks(
  systemId: string,
): Promise<GetCapabilityLinksResponse> {
  const { data } = await apiClient.get<GetCapabilityLinksResponse>(
    `/systems/${systemId}/capability-links`,
  );
  return data;
}

export async function removeCapabilityLink(
  systemId: string,
  linkId: string,
): Promise<DeleteLinkResponse> {
  const { data } = await apiClient.delete<DeleteLinkResponse>(
    `/systems/${systemId}/capability-links/${linkId}`,
  );
  return data;
}
