import type { ExternalDecision, OfferingOverviewData } from '../../features/provider-authorizations/types';
import { packageStatus, page } from '../package-imports/fixtures';
import { offering, boundary } from './testData';

export const recordedAuthorization: ExternalDecision = {
  recordId: 'decision-1', offeringId: offering.offeringId, revisionId: 'decision-revision-1', revision: 2,
  snapshotHash: 'technical-decision-hash', boundaryRevisionId: boundary.boundaryRevisionId,
  recordKind: 'ProviderDecision', reference: 'Synthetic existing ATO', issuingAuthority: 'Example authorizing official',
  decisionAsStated: 'Authorization to operate', issuedOn: '2026-01-01', effectiveOn: '2026-02-01',
  expiresOn: '2027-01-01', expiryBasis: 'DateStated', scopeStatement: 'Named provider services only.',
  conditions: ['Annual reassessment required.'], sourceCandidateRefs: [],
  citations: [{ packageId: 'package-1', artifactId: 'artifact-1', archivePath: 'authorization-letter.pdf',
    locator: 'page 2', quote: 'Named provider services only.' }],
  metadataReviewState: 'Recorded', currentStanding: 'CurrentAsRecorded', recordedBy: 'Test reviewer',
  recordedAt: '2026-09-25T12:00:00Z', impactReviewRequired: false,
};

export function offeringOverview(): OfferingOverviewData {
  return {
    offeringId: offering.offeringId, offeringRevision: offering.revision,
    authorizations: { ...page([]), recorded: 0, unconfirmed: 0, rejected: 0 },
    packages: { ...page([{
      package: packageStatus({ name: 'Uploaded authorization documents.zip', processingState: 'NeedsAttention',
        coverage: { total: 27, processed: 26, pending: 1, unsupported: 0, unreadable: 0, failed: 0, excluded: 0 },
        analysisProgress: { completedSegments: 415, totalSegments: 428, modelCalls: 64, modelCallLimit: 64,
          continuingAutomatically: false } }),
      packageVersionId: 'version-1', version: 1, boundaryRevisionId: boundary.boundaryRevisionId,
      awaitingReview: 88, authorizationDetails: 2,
    }]), needsAttention: 1, processing: 0, awaitingReview: 88,
    preferredAuthorizationReview: { packageId: 'package-1', packageName: 'Uploaded authorization documents.zip',
      type: 'AuthorizationDecisionClaim' } },
    capabilities: { proposed: 86, awaitingReview: 80, awaitingApproval: 6, published: 4, archived: 1 },
    hosting: { name: 'Configured DoD hosting', configured: true, scopeCount: 2, assignmentCount: 5, associatedSystemCount: 2 },
  };
}
