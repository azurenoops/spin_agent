import apiClient from './client';
import type {
  ProfileOverview,
  ProfileSectionDetail,
  ProfileCompletenessResponse,
  ProfileTodoResponse,
  SaveProfileSectionRequest,
  SubmitSectionsRequest,
  ReviewSectionRequest,
  ProfileSectionType,
} from '../types/dashboard';

// ─── Profile Overview & Section Detail ──────────────────────────────────────

export async function getProfileOverview(
  systemId: string,
): Promise<ProfileOverview> {
  const { data } = await apiClient.get<ProfileOverview>(
    `/systems/${encodeURIComponent(systemId)}/profile`,
  );
  return data;
}

export async function getProfileSection(
  systemId: string,
  sectionType: ProfileSectionType,
): Promise<ProfileSectionDetail> {
  const { data } = await apiClient.get<ProfileSectionDetail>(
    `/systems/${encodeURIComponent(systemId)}/profile/${encodeURIComponent(sectionType)}`,
  );
  return data;
}

// ─── Save Draft ─────────────────────────────────────────────────────────────

export async function saveProfileSection(
  systemId: string,
  sectionType: ProfileSectionType,
  request: SaveProfileSectionRequest,
): Promise<ProfileSectionDetail> {
  const { data } = await apiClient.put<ProfileSectionDetail>(
    `/systems/${encodeURIComponent(systemId)}/profile/${encodeURIComponent(sectionType)}`,
    request,
  );
  return data;
}

// ─── Submit & Withdraw ──────────────────────────────────────────────────────

export async function submitSections(
  systemId: string,
  request: SubmitSectionsRequest,
): Promise<{ submittedSections: string[]; skippedSections: { sectionType: string; reason: string }[] }> {
  const { data } = await apiClient.post(
    `/systems/${encodeURIComponent(systemId)}/profile/submit`,
    request,
  );
  return data;
}

export async function withdrawSections(
  systemId: string,
  sectionTypes?: ProfileSectionType[],
): Promise<{ withdrawnSections: string[]; skippedSections: { sectionType: string; reason: string }[] }> {
  const { data } = await apiClient.post(
    `/systems/${encodeURIComponent(systemId)}/profile/submit`,
    { action: 'withdraw', sectionTypes },
  );
  return data;
}

// ─── Review ─────────────────────────────────────────────────────────────────

export async function reviewSection(
  systemId: string,
  sectionType: ProfileSectionType,
  request: ReviewSectionRequest,
): Promise<ProfileSectionDetail> {
  const { data } = await apiClient.post<ProfileSectionDetail>(
    `/systems/${encodeURIComponent(systemId)}/profile/${encodeURIComponent(sectionType)}/review`,
    request,
  );
  return data;
}

export async function batchApproveProfile(
  systemId: string,
): Promise<{ approvedSections: string[]; approvedCount: number }> {
  const { data } = await apiClient.post(
    `/systems/${encodeURIComponent(systemId)}/profile/batch-approve`,
  );
  return data;
}

// ─── Completeness & Todos ───────────────────────────────────────────────────

export async function getProfileCompleteness(
  systemId: string,
): Promise<ProfileCompletenessResponse> {
  const { data } = await apiClient.get<ProfileCompletenessResponse>(
    `/systems/${encodeURIComponent(systemId)}/profile/completeness`,
  );
  return data;
}

export async function getProfileTodos(
  systemId: string,
): Promise<ProfileTodoResponse> {
  const { data } = await apiClient.get<ProfileTodoResponse>(
    `/systems/${encodeURIComponent(systemId)}/profile/todos`,
  );
  return data;
}
