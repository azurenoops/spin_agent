import { packageRequest } from '../package-imports/request';
import type { PackageStatus } from '../package-imports/types';
import type { SourceManifestEntry } from '../package-imports/uploadIdentity';
import type { CspOnboardingStateDto } from './api';

export type ProviderScreen = 'p-details' | 'p-access' | 'p-offering' | 'p-authorization' | 'p-sources' | 'p-uncertain' | 'p-review' | 'p-ready';
export interface Deferral { reason: string; ownerRole: 'CSP.Admin' }
export interface ServiceDescription {
  environmentKind: string; environmentLabel?: string; serviceModel?: string; managedBy?: string;
  intendedUse?: string; declaredImpactLevel?: string;
}
export interface SetupDraft {
  currentScreen: ProviderScreen;
  details: { displayName: string; legalEntityName: string; serviceContactName: string; serviceContactEmail: string;
    dodComponent?: string; timeZoneId?: string; supportPhone?: string; logoUrl?: string;
    legacyClassificationDefault?: string; confirmLegacyClassificationDefault?: boolean };
  operationalContact?: { choice: string; displayName?: string; email?: string; deferral?: Deferral };
  securityContact: { choice: string; displayName?: string; email?: string; deferral?: Deferral };
  firstOffering: { choice: string; offeringId?: string; expectedRevision?: number; name?: string; description?: string;
    portfolioName?: string; environments?: string[]; serviceDescription?: ServiceDescription; deferral?: Deferral };
  authorization: { choice: 'Unspecified' | 'ExistingAuthorization' | 'InitialAuthorization' | 'DetermineLater';
    decisionReference?: string; issuingAuthority?: string; decisionDate?: string; expirationDate?: string;
    conditions?: string; statedScope?: string; systemOrBoundaryName?: string; supportingSource?: string };
  sources: { choice: string; intentIds: string[]; packageReference?: string; deferral?: Deferral;
    selection?: { packageName: string; files: SourceManifestEntry[]; offeringHintId: string | null; context: null } };
}
export interface SetupActor { directoryTenantId: string; objectId: string; displayName: string }
export interface SetupAccess { state: string; actor: SetupActor; checkedAt: string; scope: string }
export interface HandlingPolicy {
  state: string; policyId?: string; version?: string; environmentLabel?: string; approvalReference?: string;
  validUntil?: string; allowedClassifications: string[]; allowedMarkings: string[]; syntheticOnly: boolean;
  uploadsPermitted: boolean; analysisPermitted: boolean; checkedAt: string; reasonCode?: string;
}
export interface UploadIntentInput {
  intentId: string; schemaVersion: 1; packageName: string; entryPoint: 'Onboarding' | 'ActivePortal';
  associationMode: 'Unassociated' | 'ExactBoundary'; offeringHintId: string | null;
  context: { offeringId: string; expectedOfferingRevision: number; boundaryRevisionId: string;
    seriesId: string | null; previousVersionId: string | null } | null;
  files: SourceManifestEntry[]; handlingPolicyVersion: string;
  declaredContent: { classification: string; markings: string[]; containsOnlySyntheticData: boolean };
}
export interface ReceiptReconciliation {
  outcome: 'Confirmed' | 'NotObserved' | 'InFlight' | 'Rejected' | 'Conflict' | 'Unknown';
  observedAt: string; receipt?: PackageStatus | null; nextAction: string;
}
export interface UploadIntent {
  intentId: string; intentHash: string; revision: number; savedAt: string; input: UploadIntentInput;
  receipt: PackageStatus | null; reconciliation: ReceiptReconciliation;
}
export interface SetupAction {
  actionId: string; label: string; state: string; ownerRole: string; reasonCode: string;
  reason?: string; destination: { path: string; label: string }; contribution: string;
}
export interface SetupState {
  providerId: string | null; profileRevision: number | null; profile: CspOnboardingStateDto;
  draft: { draftId: string; revision: number; currentScreen: ProviderScreen; fields: SetupDraft;
    savedAt: string; savedBy: string; committedOfferingId: string | null;
    committedPortfolioId?: string | null; committedAuthorizationIntentId?: string | null; completion: object | null } | null;
  access: SetupAccess; handling: HandlingPolicy; uploadIntents: UploadIntent[]; facts: SetupAction[];
}
export interface CommandResult {
  outcome: 'Committed' | 'NotFound'; replayed: boolean;
  committedOutcome: { commandId: string; committedDraftRevision: number; committedProfileRevision: number } | null;
  current: { projectionState: 'Available' | 'Unavailable'; state: SetupState | null;
    actor: SetupActor; access: SetupAccess; error?: { message: string } };
}
const base = '/api/csp/onboarding/setup';
const keyHeader = (key: string) => ({ 'Idempotency-Key': key });
export const getSetup = (signal?: AbortSignal) => packageRequest<SetupState>({ url: base, signal });
export const saveDraft = (expectedRevision: number, draft: SetupDraft, key: string) =>
  packageRequest<CommandResult>({ method: 'PUT', url: `${base}/draft`, data: { expectedRevision, draft }, headers: keyHeader(key) });
export const commitSetup = (expectedRevision: number, expectedProfileRevision: number, section: string, key: string, expectedOfferingRevision?: number) =>
  packageRequest<CommandResult>({ method: 'POST', url: `${base}/commits`, data: { expectedRevision, expectedProfileRevision, section, expectedOfferingRevision }, headers: keyHeader(key) });
export const completeSetup = (expectedRevision: number, expectedProfileRevision: number, acknowledgedUnresolvedIntentIds: string[], key: string) =>
  packageRequest<CommandResult>({ method: 'POST', url: `${base}/completion`,
    data: { expectedRevision, expectedProfileRevision, confirmed: true, acknowledgedUnresolvedIntentIds }, headers: keyHeader(key) });
export const prepareUpload = (expectedSetupRevision: number, intent: UploadIntentInput) =>
  packageRequest<UploadIntent>({ method: 'POST', url: '/api/csp/package-imports/upload-intents',
    data: { expectedSetupRevision, intent }, headers: keyHeader(intent.intentId) });
export const getUploadIntent = (id: string) =>
  packageRequest<UploadIntent>({ url: `/api/csp/package-imports/upload-intents/${encodeURIComponent(id)}` });
export const getHandlingPolicy = () => packageRequest<HandlingPolicy>({ url: '/api/csp/onboarding/handling-policy' });
export const listPortalIntents = (page = 1, offeringHintId?: string) =>
  packageRequest<{ items: UploadIntent[]; page: number; pageSize: number; total: number }>({
    url: '/api/csp/package-imports/upload-intents',
    params: { entryPoint: 'ActivePortal', page, pageSize: 25, ...(offeringHintId ? { offeringHintId } : {}) },
  });
export const reconcileReceipt = (intent: UploadIntent) => packageRequest<ReceiptReconciliation>({
  method: 'POST', url: '/api/csp/package-imports/receipt-reconciliation', data: { requestKey: intent.intentId, intentHash: intent.intentHash },
});
export function uploadSource(intent: UploadIntent, files: File[], active: boolean) {
  const form = new FormData();
  form.append('name', intent.input.packageName);
  files.forEach(file => form.append('files', file, file.name));
  return packageRequest<PackageStatus>({
    method: 'POST', url: active ? '/api/csp/inherited-components/import' : '/api/csp/onboarding/atos/upload',
    data: form, headers: { ...keyHeader(intent.intentId), 'X-Provider-Upload-Intent': intent.intentId, Prefer: 'respond-async' },
  });
}
