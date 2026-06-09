import axios, { AxiosError } from 'axios';
import { attachAuthInterceptor } from '../../auth/interceptors';
import { getMsalInstance, DEFAULT_API_SCOPES } from '../../auth/msalInstance';

/**
 * Foundational onboarding-wizard API client (Feature 047).
 * Wraps the `/api/onboarding/*` endpoints; downstream user stories add per-step
 * methods (organization context, roles, eMASS, SSP PDF, subscriptions, templates,
 * narrative seeds) to this same client.
 */
const onboardingApi = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL_ONBOARDING || '/api/onboarding',
  headers: { 'Content-Type': 'application/json' },
});

if (import.meta.env.DEV) {
  onboardingApi.interceptors.request.use((config) => {
    // Dev-only role spoofing for the dashboard (FR-048 parity).
    try {
      const raw = localStorage.getItem('ato-dashboard-settings');
      if (raw) {
        const settings = JSON.parse(raw) as { role?: string };
        if (settings.role) {
          config.headers['X-Simulated-Role'] = settings.role;
        }
      }
    } catch {
      // ignore parse errors
    }
    return config;
  });
}

// Feature 051 T053: MSAL bearer injection (silent renewal + 401 retry).
attachAuthInterceptor(onboardingApi, getMsalInstance, DEFAULT_API_SCOPES);

/**
 * Response interceptor — when the backend returns a non-2xx HTTP status with an
 * envelope body (`{ ok: false, errorCode, message, suggestion }`), axios would
 * normally throw an opaque `AxiosError` and callers would lose the `errorCode`
 * (e.g. `WIZARD_ARM_CONSENT_REQUIRED` → never triggers the consent CTA).
 *
 * We re-throw an `envelopeError`-shaped `Error` so callers can branch on
 * `err.errorCode` for any HTTP status, matching the success-path contract used
 * throughout this client.
 */
onboardingApi.interceptors.response.use(
  (response) => response,
  (error: AxiosError<ApiEnvelope<unknown>>) => {
    const data = error.response?.data;
    if (data && typeof data === 'object' && 'ok' in data && data.ok === false) {
      return Promise.reject(envelopeError(data));
    }
    return Promise.reject(error);
  },
);

export type WizardStepName =
  | 'OrganizationContext'
  | 'Roles'
  | 'Emass'
  | 'SspPdf'
  | 'AzureSubscriptions'
  | 'Templates'
  | 'NarrativeSeeds';

export type OnboardingStatus =
  | 'NotStarted'
  | 'InProgress'
  | 'Completed'
  | 'ReRunInProgress';

export type OnboardingStepStatus = 'Completed' | 'Skipped';

export interface OnboardingStepDto {
  step: WizardStepName | string;
  status: OnboardingStepStatus;
  completedAt: string;
  durationMs: number;
}

export interface OnboardingStateDto {
  tenantId: string;
  status: OnboardingStatus;
  lastStep: WizardStepName | string | null;
  startedAt: string | null;
  completedAt: string | null;
  lastReRunAt: string | null;
  steps: OnboardingStepDto[];
}

export interface ApiEnvelope<T> {
  ok: boolean;
  data?: T;
  errorCode?: string;
  message?: string;
  suggestion?: string;
}

export const onboarding = {
  async getState(): Promise<OnboardingStateDto> {
    const { data } = await onboardingApi.get<ApiEnvelope<OnboardingStateDto>>('/state');
    if (!data.ok || !data.data) throw envelopeError(data);
    return data.data;
  },

  async start(): Promise<OnboardingStateDto> {
    const { data } = await onboardingApi.post<ApiEnvelope<OnboardingStateDto>>('/start');
    if (!data.ok || !data.data) throw envelopeError(data);
    return data.data;
  },

  async skipStep(step: WizardStepName): Promise<void> {
    const { data } = await onboardingApi.post<ApiEnvelope<unknown>>(
      `/steps/${encodeURIComponent(step)}/skip`,
    );
    if (!data.ok) throw envelopeError(data);
  },

  async complete(): Promise<OnboardingStateDto> {
    const { data } = await onboardingApi.post<ApiEnvelope<OnboardingStateDto>>('/complete');
    if (!data.ok || !data.data) throw envelopeError(data);
    return data.data;
  },

  async getJob(jobId: string) {
    const { data } = await onboardingApi.get<ApiEnvelope<WizardJobStatusDto>>(`/jobs/${jobId}`);
    if (!data.ok || !data.data) throw envelopeError(data);
    return data.data;
  },

  async getOrganizationContext(): Promise<OrganizationContextDto | null> {
    const { data } = await onboardingApi.get<ApiEnvelope<OrganizationContextDto | null>>(
      '/organization-context',
    );
    if (!data.ok) throw envelopeError(data);
    return data.data ?? null;
  },

  async upsertOrganizationContext(
    payload: OrganizationContextDto,
  ): Promise<OrganizationContextDto> {
    const { data } = await onboardingApi.put<ApiEnvelope<OrganizationContextDto>>(
      '/organization-context',
      payload,
    );
    if (!data.ok || !data.data) throw envelopeError(data);
    return data.data;
  },

  async listPersons(query?: string): Promise<PersonDto[]> {
    const { data } = await onboardingApi.get<ApiEnvelope<PersonDto[]>>(
      '/persons',
      { params: query ? { query } : undefined },
    );
    if (!data.ok) throw envelopeError(data);
    return data.data ?? [];
  },

  async createPerson(payload: {
    displayName: string;
    email: string;
    phoneNumber?: string;
  }): Promise<PersonDto> {
    const { data } = await onboardingApi.post<ApiEnvelope<PersonDto>>('/persons', payload);
    if (!data.ok || !data.data) throw envelopeError(data);
    return data.data;
  },

  async searchDirectory(query: string): Promise<DirectoryPersonDto[]> {
    const { data } = await onboardingApi.get<ApiEnvelope<DirectoryPersonDto[]>>(
      '/persons/directory',
      { params: { query } },
    );
    if (!data.ok) throw envelopeError(data);
    return data.data ?? [];
  },

  async promotePerson(personId: string, entraObjectId: string): Promise<PersonDto> {
    const { data } = await onboardingApi.post<ApiEnvelope<PersonDto>>(
      `/persons/${personId}/promote`,
      { entraObjectId },
    );
    if (!data.ok || !data.data) throw envelopeError(data);
    return data.data;
  },

  async listRoleAssignments(): Promise<RoleAssignmentDto[]> {
    const { data } = await onboardingApi.get<ApiEnvelope<RoleAssignmentDto[]>>('/role-assignments');
    if (!data.ok) throw envelopeError(data);
    return data.data ?? [];
  },

  async createRoleAssignment(payload: {
    role: OrganizationRole;
    personId: string;
  }): Promise<{ assignment: RoleAssignmentDto; warnings: string[] }> {
    const { data } = await onboardingApi.post<
      ApiEnvelope<RoleAssignmentDto> & { warnings?: string[] }
    >('/role-assignments', payload);
    if (!data.ok || !data.data) throw envelopeError(data);
    return { assignment: data.data, warnings: data.warnings ?? [] };
  },

  async deleteRoleAssignment(assignmentId: string): Promise<void> {
    const { data } = await onboardingApi.delete<ApiEnvelope<null>>(
      `/role-assignments/${assignmentId}`,
    );
    if (!data.ok) throw envelopeError(data);
  },

  async uploadEmass(file: File): Promise<EmassUploadResultDto> {
    const form = new FormData();
    form.append('file', file);
    const { data } = await onboardingApi.post<ApiEnvelope<EmassUploadResultDto>>(
      '/imports/emass/upload',
      form,
      { headers: { 'Content-Type': 'multipart/form-data' } },
    );
    if (!data.ok || !data.data) throw envelopeError(data);
    return data.data;
  },

  async getEmassPreview(sessionId: string): Promise<EmassParseResultDto> {
    const { data } = await onboardingApi.get<ApiEnvelope<EmassParseResultDto>>(
      `/imports/emass/${sessionId}/preview`,
    );
    if (!data.ok || !data.data) throw envelopeError(data);
    return data.data;
  },

  async commitEmass(
    sessionId: string,
    instructions: EmassCommitInstructionDto[],
  ): Promise<{ sessionId: string; commitJobId: string }> {
    const { data } = await onboardingApi.post<
      ApiEnvelope<{ sessionId: string; commitJobId: string }>
    >(`/imports/emass/${sessionId}/commit`, { instructions });
    if (!data.ok || !data.data) throw envelopeError(data);
    return data.data;
  },

  async getEmassLog(sessionId: string): Promise<EmassImportLogDto> {
    const { data } = await onboardingApi.get<ApiEnvelope<EmassImportLogDto>>(
      `/imports/emass/${sessionId}/log`,
    );
    if (!data.ok || !data.data) throw envelopeError(data);
    return data.data;
  },

  async uploadSspPdfBatch(files: File[]): Promise<SspPdfBatchUploadResultDto> {
    const form = new FormData();
    files.forEach((f) => form.append('files', f));
    const { data } = await onboardingApi.post<ApiEnvelope<SspPdfBatchUploadResultDto>>(
      '/imports/ssp-pdf/upload',
      form,
      { headers: { 'Content-Type': 'multipart/form-data' } },
    );
    if (!data.ok || !data.data) throw envelopeError(data);
    return data.data;
  },

  async getSspPdfBatch(batchId: string): Promise<SspPdfSessionSummaryDto[]> {
    const { data } = await onboardingApi.get<ApiEnvelope<SspPdfSessionSummaryDto[]>>(
      `/imports/ssp-pdf/batches/${batchId}/summary`,
    );
    if (!data.ok) throw envelopeError(data);
    return data.data ?? [];
  },

  async getSspPdfExtraction(sessionId: string): Promise<SspPdfExtractionResultDto> {
    const { data } = await onboardingApi.get<ApiEnvelope<SspPdfExtractionResultDto>>(
      `/imports/ssp-pdf/${sessionId}/extraction`,
    );
    if (!data.ok || !data.data) throw envelopeError(data);
    return data.data;
  },

  async putSspPdfCorrections(
    sessionId: string,
    corrections: SspPdfFieldCorrectionDto[],
  ): Promise<void> {
    const { data } = await onboardingApi.put<ApiEnvelope<unknown>>(
      `/imports/ssp-pdf/${sessionId}/corrections`,
      { corrections },
    );
    if (!data.ok) throw envelopeError(data);
  },

  async importSspPdfSystem(sessionId: string): Promise<{ sessionId: string; systemId: string }> {
    const { data } = await onboardingApi.post<
      ApiEnvelope<{ sessionId: string; systemId: string }>
    >(`/imports/ssp-pdf/${sessionId}/import`);
    if (!data.ok || !data.data) throw envelopeError(data);
    return data.data;
  },

  async listAzureSubscriptions(): Promise<AzureSubscriptionInfoDto[]> {
    const { data } = await onboardingApi.get<ApiEnvelope<AzureSubscriptionInfoDto[]>>(
      '/azure/subscriptions',
    );
    if (!data.ok) throw envelopeError(data);
    return (data.data ?? []).map((s) => ({
      ...s,
      environment: typeof s.environment === 'number'
        ? (ENV_BY_ORDINAL[s.environment as number] ?? 'AzureCloud')
        : s.environment,
    }));
  },

  async listAzureRegistrations(): Promise<AzureSubscriptionRegistrationDto[]> {
    const { data } = await onboardingApi.get<ApiEnvelope<AzureSubscriptionRegistrationDto[]>>(
      '/azure/subscriptions/registrations',
    );
    if (!data.ok) throw envelopeError(data);
    return (data.data ?? []).map(normalizeRegistration);
  },

  async putAzureRegistrations(
    subscriptionIds: string[],
  ): Promise<AzureSubscriptionRegistrationDto[]> {
    const { data } = await onboardingApi.put<
      ApiEnvelope<AzureSubscriptionRegistrationDto[]>
    >('/azure/subscriptions/registrations', { subscriptionIds });
    if (!data.ok) throw envelopeError(data);
    return (data.data ?? []).map(normalizeRegistration);
  },

  async removeAzureRegistration(id: string): Promise<void> {
    await onboardingApi.delete(`/azure/subscriptions/registrations/${id}`);
  },

  // ─── Step 6: Custom document templates ───────────────────────────────

  async listTemplates(templateType?: TemplateType): Promise<OrganizationDocumentTemplateDto[]> {
    const { data } = await onboardingApi.get<ApiEnvelope<OrganizationDocumentTemplateDto[]>>(
      '/templates/' + (templateType ? `?templateType=${templateType}` : ''),
    );
    if (!data.ok) throw envelopeError(data);
    return data.data ?? [];
  },

  async uploadTemplate(args: {
    templateType: TemplateType;
    label: string;
    version: string;
    file: File;
    isDefault?: boolean;
  }): Promise<{ template: OrganizationDocumentTemplateDto; warnings: string[] }> {
    const fd = new FormData();
    fd.append('templateType', args.templateType);
    fd.append('label', args.label);
    fd.append('version', args.version);
    if (args.isDefault) fd.append('isDefault', 'true');
    fd.append('file', args.file);
    const { data } = await onboardingApi.post<ApiEnvelope<{ template: OrganizationDocumentTemplateDto; warnings: string[] }>>(
      '/templates/upload', fd,
      { headers: { 'Content-Type': 'multipart/form-data' } },
    );
    if (!data.ok || !data.data) throw envelopeError(data);
    return data.data;
  },

  async patchTemplate(id: string, body: { label?: string; version?: string }): Promise<OrganizationDocumentTemplateDto> {
    const { data } = await onboardingApi.patch<ApiEnvelope<OrganizationDocumentTemplateDto>>(
      `/templates/${id}`, body,
    );
    if (!data.ok || !data.data) throw envelopeError(data);
    return data.data;
  },

  async deleteTemplate(id: string): Promise<void> {
    await onboardingApi.delete(`/templates/${id}`);
  },

  async replaceTemplateFile(id: string, file: File, version?: string): Promise<{ template: OrganizationDocumentTemplateDto; dependentsFlagged: number; suggestedReRunDependencyIds: string[] }> {
    const fd = new FormData();
    fd.append('file', file);
    if (version) fd.append('version', version);
    const { data } = await onboardingApi.post<ApiEnvelope<{ template: OrganizationDocumentTemplateDto; dependentsFlagged: number; suggestedReRunDependencyIds: string[] }>>(
      `/templates/${id}/replace`, fd,
      { headers: { 'Content-Type': 'multipart/form-data' } },
    );
    if (!data.ok || !data.data) throw envelopeError(data);
    return data.data;
  },

  async markTemplateDefault(id: string): Promise<OrganizationDocumentTemplateDto> {
    const { data } = await onboardingApi.post<ApiEnvelope<OrganizationDocumentTemplateDto>>(
      `/templates/${id}/default`,
    );
    if (!data.ok || !data.data) throw envelopeError(data);
    return data.data;
  },

  async clearTemplateDefault(id: string): Promise<void> {
    await onboardingApi.delete(`/templates/${id}/default/clear`);
  },

  // ─── Step 7: Narrative seeds ─────────────────────────────────────────

  async listNarrativeSeeds(): Promise<NarrativeSeedDocumentDto[]> {
    const { data } = await onboardingApi.get<ApiEnvelope<NarrativeSeedDocumentDto[]>>(
      '/narrative-seeds/',
    );
    if (!data.ok) throw envelopeError(data);
    return data.data ?? [];
  },

  async uploadNarrativeSeed(file: File, label: string, tags: string[]): Promise<{ document: NarrativeSeedDocumentDto; indexJobId: string | null }> {
    const fd = new FormData();
    fd.append('file', file);
    fd.append('label', label);
    for (const t of tags) fd.append('tags', t);
    const { data } = await onboardingApi.post<ApiEnvelope<{ document: NarrativeSeedDocumentDto; indexJobId: string | null }>>(
      '/narrative-seeds/', fd,
      { headers: { 'Content-Type': 'multipart/form-data' } },
    );
    if (!data.ok || !data.data) throw envelopeError(data);
    return data.data;
  },

  async deleteNarrativeSeed(id: string, confirmCitations = false): Promise<void> {
    await onboardingApi.delete(`/narrative-seeds/${id}`, { params: { confirmCitations } });
  },
};

export interface WizardJobStatusDto {
  jobId: string;
  tenantId: string;
  jobType: string;
  status: 'Queued' | 'InProgress' | 'Succeeded' | 'Failed' | 'Cancelled';
  percent: number | null;
  message: string | null;
  errorCode: string | null;
  suggestion: string | null;
  enqueuedAt: string | null;
  startedAt: string | null;
  finishedAt: string | null;
  result: string | null;
}

export type BranchAffiliation =
  | 'Army'
  | 'Navy'
  | 'AirForce'
  | 'MarineCorps'
  | 'SpaceForce'
  | 'CoastGuard'
  | 'CivilAgency'
  | 'IndustryPartnerOther';

export type ClassificationPosture = 'Unclassified' | 'CUI' | 'Secret' | 'TopSecret';

export interface OrganizationContextDto {
  organizationName: string;
  branch: BranchAffiliation;
  branchQualifier?: string | null;
  subOrganization?: string | null;
  classificationPosture?: ClassificationPosture | null;
  authoritativeRepositoryUrl?: string | null;
  primaryPocEmail?: string | null;
}

export type OrganizationRole = 'Issm' | 'Isso' | 'Administrator' | 'Assessor';

export interface PersonDto {
  id: string;
  displayName: string;
  email: string;
  phoneNumber?: string | null;
  entraObjectId?: string | null;
  isLinkedToDirectory: boolean;
}

export interface DirectoryPersonDto {
  entraObjectId: string;
  displayName: string;
  email: string;
  department?: string | null;
}

export interface RoleAssignmentDto {
  id: string;
  role: OrganizationRole;
  personId: string;
  isPrimary: boolean;
  createdAt: string;
}

export type EmassCommitDecision = 'Skip' | 'Merge' | 'Overwrite';

export interface EmassParsedSystemDto {
  systemIdentifier: string;
  systemName: string;
  controlCount: number;
  poamCount: number;
  malformedReason: string | null;
}

export interface EmassParseResultDto {
  systems: EmassParsedSystemDto[];
  sourceFormat: string;
}

export interface EmassUploadResultDto {
  sessionId: string;
  parseJobId: string;
  status: string;
}

export interface EmassCommitInstructionDto {
  systemIdentifier: string;
  decision: EmassCommitDecision;
}

export interface EmassImportLogEntryDto {
  systemIdentifier: string;
  systemName: string;
  outcome: string;
  registeredSystemId?: string | null;
  reason?: string | null;
}

export interface EmassImportLogDto {
  sessionId: string;
  status: string;
  entries: EmassImportLogEntryDto[];
}

export type SspPdfStatus =
  | 'Uploaded'
  | 'Extracting'
  | 'Extracted'
  | 'Imported'
  | 'Rejected'
  | 'Failed';

export type SspPdfRejectReason =
  | 'Encrypted'
  | 'PasswordProtected'
  | 'ImageOnly'
  | 'Unreadable'
  | 'UnknownFramework';

export type SspPdfFieldConfidence = 'High' | 'Medium' | 'Low';

export interface SspPdfFieldDto {
  name: string;
  value: string | null;
  confidence: SspPdfFieldConfidence;
  pageNumber: number | null;
}

export interface SspPdfExtractionResultDto {
  isAccepted: boolean;
  rejectReason: SspPdfRejectReason | null;
  rejectMessage: string | null;
  fields: SspPdfFieldDto[];
  pageCount: number;
}

export interface SspPdfBatchEntryDto {
  sessionId: string;
  extractJobId: string;
  originalFileName: string;
}

export interface SspPdfBatchUploadResultDto {
  batchId: string;
  sessions: SspPdfBatchEntryDto[];
}

export interface SspPdfSessionSummaryDto {
  sessionId: string;
  originalFileName: string;
  status: SspPdfStatus;
  rejectReason: SspPdfRejectReason | null;
  extractJobId: string | null;
  createdSystemId: string | null;
}

export interface SspPdfFieldCorrectionDto {
  fieldName: string;
  value: string | null;
}

export type AzureEnvironment = 'AzureCloud' | 'AzureUSGovernment';
export type SubscriptionStatus = 'Selected' | 'Unavailable';

export interface AzureSubscriptionInfoDto {
  subscriptionId: string;
  displayName: string;
  parentTenantId: string;
  environment: AzureEnvironment;
}

export interface AzureSubscriptionRegistrationDto {
  id: string;
  tenantId: string;
  subscriptionId: string;
  displayName: string;
  parentTenantId: string;
  environment: AzureEnvironment;
  status: SubscriptionStatus;
  lastSeenVisibleAt: string;
}

function envelopeError(env: ApiEnvelope<unknown>): Error {
  const e = new Error(env.message || 'Onboarding API failure');
  // @ts-expect-error attach diagnostics for callers
  e.errorCode = env.errorCode;
  // @ts-expect-error
  e.suggestion = env.suggestion;
  return e;
}

// The MCP server currently serializes C# enums as numeric ordinals. Normalize
// them to the documented string union at the API boundary so consumers (the
// onboarding wizard, the Component Library) can safely compare to literals
// like `'Selected'` and `'AzureCloud'`.
const STATUS_BY_ORDINAL: SubscriptionStatus[] = ['Selected', 'Unavailable'];
const ENV_BY_ORDINAL: AzureEnvironment[] = ['AzureCloud', 'AzureUSGovernment'];

function normalizeRegistration(r: AzureSubscriptionRegistrationDto): AzureSubscriptionRegistrationDto {
  const status = (typeof r.status === 'number'
    ? STATUS_BY_ORDINAL[r.status as number]
    : r.status) ?? 'Unavailable';
  const environment = (typeof r.environment === 'number'
    ? ENV_BY_ORDINAL[r.environment as number]
    : r.environment) ?? 'AzureCloud';
  return { ...r, status, environment };
}

// ─── Step 6: Templates ───────────────────────────────────────────────

export type TemplateType = 'Ssp' | 'Sar' | 'Sap' | 'Crm' | 'HwSwInventory';
export type TemplateFileFormat = 'Docx' | 'Xlsx';
export type TemplateValidationStatus = 'Pending' | 'Compliant' | 'FlaggedNonCompliant';
export type TemplateStatus = 'Active' | 'Superseded' | 'Deleted';

export interface OrganizationDocumentTemplateDto {
  id: string;
  tenantId: string;
  templateType: TemplateType;
  label: string;
  version: string;
  originalFileName: string;
  storageBlobKey: string;
  fileFormat: TemplateFileFormat;
  fileSizeBytes: number;
  contentChecksumSha256: string;
  isDefault: boolean;
  validationStatus: TemplateValidationStatus;
  validationWarnings: string | null;
  status: TemplateStatus;
  createdAt: string;
  updatedAt: string;
}

// ─── Step 7: Narrative seeds ─────────────────────────────────────────

export type NarrativeSeedIndexingStatus = 'Pending' | 'Indexed' | 'Failed';
export type NarrativeSeedStatus = 'Active' | 'Deleted';

export interface NarrativeSeedDocumentDto {
  id: string;
  tenantId: string;
  label: string;
  tags: string;
  evidenceArtifactId: string;
  indexingStatus: NarrativeSeedIndexingStatus;
  indexJobId: string | null;
  status: NarrativeSeedStatus;
  createdAt: string;
  updatedAt: string;
}

export default onboardingApi;
