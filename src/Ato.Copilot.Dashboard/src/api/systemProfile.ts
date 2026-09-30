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
  UserCategoryReviewRequest,
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
  const path = `/systems/${encodeURIComponent(systemId)}/profile/${encodeURIComponent(sectionType)}`;
  const normalized = request.childItems?.map((row, sortOrder) =>
    row && typeof row === 'object' && !Array.isArray(row) ? { ...row, sortOrder } : row);
  const submitted = normalized === undefined ? request : { ...request, childItems: normalized };
  let { data } = await apiClient.put<ProfileSectionDetail>(path, submitted);
  if (normalized?.length === 0) data = (await apiClient.get<ProfileSectionDetail>(path)).data;
  if (request.childItems !== undefined) {
    const collection = {
      UsersAndAccess: 'userCategories', DataTypes: 'dataTypeEntries',
      PortsProtocolsAndServices: 'ppsEntries', LeveragedAuthorizations: 'leveragedAuthorizations',
    } as const;
    const fields = {
      UsersAndAccess: ['categoryName', 'description', 'approximateCount', 'accessMethod', 'dataSensitivityLevel', 'sortOrder', 'pendingDeletion'],
      DataTypes: ['dataTypeName', 'description', 'sensitivityClassification', 'source', 'destination', 'applicableRegulations', 'sortOrder'],
      PortsProtocolsAndServices: ['portOrRange', 'protocol', 'serviceName', 'direction', 'justification', 'sortOrder'],
      LeveragedAuthorizations: ['providerName', 'authorizationType', 'authorizationDate', 'expirationDate', 'coveredControlFamilies', 'sortOrder'],
    };
    if (sectionType in collection) {
      const type = sectionType as keyof typeof collection;
      const actual: unknown = data?.[collection[type]];
      const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
      const expectedRows = normalized!;
      const submittedIds = new Set(expectedRows.filter(record).map(row => row.id).filter(id => typeof id === 'string'));
      const matched = Array.isArray(actual) ? actual.filter((row: unknown) =>
        type !== 'UsersAndAccess' || !record(row) || typeof row.id !== 'string' || row.pendingDeletion !== true || submittedIds.has(row.id)) : [];
      if (data?.sectionType !== sectionType || !Array.isArray(actual) || matched.length !== expectedRows.length
        || expectedRows.some((expected, index) => {
          const saved: unknown = matched[index];
          return !record(expected) || !record(saved) || typeof saved.id !== 'string' || !saved.id
            || typeof expected.id === 'string' && expected.id.toLowerCase() !== saved.id.toLowerCase()
            || fields[type].some(key => key in expected && (expected[key] ?? '') !== (saved[key] ?? ''));
        })) {
        throw new Error('The server did not confirm the submitted profile rows. Your local draft is retained; verify saved records before reloading.');
      }
    }
  }
  return data;
}

export async function reviewUserCategory(systemId: string, categoryId: string, request: UserCategoryReviewRequest, approvingRemoval = false): Promise<ProfileSectionDetail> {
  const { data } = await apiClient.post<ProfileSectionDetail>(
    `/systems/${encodeURIComponent(systemId)}/profile/UsersAndAccess/user-categories/${encodeURIComponent(categoryId)}/review`, request,
  );
  const row = data.userCategories?.find(item => item.id === categoryId);
  const expected = { submit: 'UnderReview', withdraw: 'Draft', approve: 'Approved', request_revision: 'NeedsRevision' }[request.action];
  const receipt = data.reviewResult;
  if (!receipt || receipt.categoryId !== categoryId || receipt.action !== request.action
    || receipt.revision !== request.expectedRevision + 1 || receipt.governanceStatus !== expected) {
    throw new Error('The server did not confirm the selected user-category review. Refresh the saved records before retrying.');
  }
  if (approvingRemoval && request.action === 'approve' && receipt.pendingDeletion && Array.isArray(data.userCategories) && !row) return data;
  if (!row || row.governanceStatus !== expected || row.revision !== receipt.revision) {
    throw new Error('The server did not confirm the selected user-category review. Refresh the saved records before retrying.');
  }
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
  const { data } = await apiClient.post<{ sectionType: ProfileSectionType; newStatus: string; reviewScope?: string }>(
    `/systems/${encodeURIComponent(systemId)}/profile/${encodeURIComponent(sectionType)}/review`,
    request,
  );
  const expected = request.decision === 'approve' ? 'Approved' : 'NeedsRevision';
  if (data?.sectionType !== sectionType || data.newStatus !== expected
    || sectionType === 'UsersAndAccess' && data.reviewScope !== 'AccessContext') {
    throw new Error('The server did not confirm the requested section review. Refresh the saved records before retrying.');
  }
  return getProfileSection(systemId, sectionType);
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
