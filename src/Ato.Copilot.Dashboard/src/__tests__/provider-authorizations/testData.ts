import type { BoundaryRevision, Offering, PackageReceipt } from '../../features/provider-authorizations/types';
import { packageStatus } from '../package-imports/fixtures';
import type { CommandResult, ProviderScreen, SetupState } from '../../features/csp-onboarding/providerSetupApi';

export function setupState(screen: ProviderScreen = 'p-details'): SetupState {
  return {
    providerId: 'provider-1', profileRevision: 1,
    profile: { cspProfileId: 'provider-1', onboardingState: 'InWizard', currentStep: 'Review',
      identity: { displayName: 'Synthetic provider', legalEntityName: 'Synthetic operator' },
      supportContact: { primarySupportEmail: 'support@example.invalid' },
      classification: { defaultClassificationFloor: 'Unclassified' } },
    draft: { draftId: 'draft-1', revision: 1, currentScreen: screen, savedAt: '2026-09-30T12:00:00Z',
      savedBy: 'Synthetic administrator', committedOfferingId: null, completion: null,
      fields: { currentScreen: screen,
        details: { displayName: 'Synthetic provider', legalEntityName: 'Synthetic operator', serviceContactName: 'Synthetic contact',
          serviceContactEmail: 'support@example.invalid', legacyClassificationDefault: 'Unclassified', confirmLegacyClassificationDefault: true },
        securityContact: { choice: 'Deferred', deferral: { reason: 'Review contact later', ownerRole: 'CSP.Admin' } },
        firstOffering: { choice: 'Deferred', deferral: { reason: 'Service later', ownerRole: 'CSP.Admin' } },
        sources: { choice: 'Deferred', intentIds: [], deferral: { reason: 'Sources later', ownerRole: 'CSP.Admin' } } } },
    access: { state: 'Authorized', checkedAt: '2026-09-30T12:00:00Z', scope: 'Provider',
      actor: { directoryTenantId: 'directory', objectId: 'actor', displayName: 'Synthetic administrator' } },
    handling: { state: 'Known', policyId: 'synthetic-policy', version: 'test-1', approvalReference: 'Synthetic test approval',
      allowedClassifications: ['Unclassified'], allowedMarkings: [], syntheticOnly: true, uploadsPermitted: true,
      analysisPermitted: true, checkedAt: '2026-09-30T12:00:00Z' },
    uploadIntents: [], facts: [],
  };
}
export function setupResult(state: SetupState): CommandResult {
  return { outcome: 'Committed', replayed: false,
    committedOutcome: { commandId: 'command', committedDraftRevision: state.draft!.revision, committedProfileRevision: state.profileRevision! },
    current: { projectionState: 'Available', actor: state.access.actor, access: state.access, state } };
}

export const offering: Offering = {
  offeringId: 'offering-1', providerId: 'provider-1', name: 'Synthetic service', description: 'Test-only offering',
  environments: ['AzureUSGovernment'], revision: 4, lifecycle: 'Draft',
  currentBoundaryRevisionId: 'boundary-1', currentHostingScopeRevisionId: null,
};
export const boundary: BoundaryRevision = {
  offeringId: offering.offeringId, offeringRevision: 4, boundaryRevisionId: 'boundary-1', version: 1,
  name: 'Test service boundary', scopeStatement: 'Synthetic service only; workloads not covered.',
  componentSnapshotIds: [], services: [], includedScopes: [], exclusions: [], providerResponsibilities: [],
  customerResponsibilities: [], citations: [], predecessorRevisionId: null,
  snapshotHash: 'synthetic-boundary-hash', createdAt: '2026-09-24T00:00:00Z',
};
export const receipt: PackageReceipt = {
  package: { ...packageStatus({ processingState: 'Processing' }), association: {
    offeringId: offering.offeringId, packageVersionId: 'version-1', boundaryRevisionId: boundary.boundaryRevisionId,
  } },
  packageVersion: { packageVersionId: 'version-1', offeringId: offering.offeringId, seriesId: 'series-1', version: 1,
    packageId: packageStatus().packageId, boundaryRevisionId: boundary.boundaryRevisionId, previousVersionId: null,
    manifestHash: 'synthetic-manifest', createdAt: '2026-09-24T00:00:00Z' },
};
