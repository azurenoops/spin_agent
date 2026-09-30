import type { BoundaryRevision, Finding, FindingEvidence, Offering, OfferingBoundaryOverview, OfferingOverviewData, Page } from '../../src/features/provider-authorizations/types';
import type { HostingScopeRevision } from '../../src/features/provider-authorizations/hostingTypes';
import { offeringOverview, recordedAuthorization } from '../../src/__tests__/provider-authorizations/overviewFixtures';
import { boundary, offering } from '../../src/__tests__/provider-authorizations/testData';
import { candidate, entry } from '../../src/__tests__/package-imports/fixtures';
import type { ProviderCapabilityDetail } from '../../src/features/workspace-operations/types';

export const paged = <T,>(items: T[]): Page<T> => ({ items, page: 1, pageSize: 25, total: items.length });
export const fixtureOffering: Offering = { ...offering, name: 'Azure IL5 · Synthetic shared services',
  description: 'Isolated browser fixture — no runtime records or connections.',
  serviceModel: 'InfrastructureSharedServices', managementArrangement: 'ProviderManaged',
  serviceOwner: 'Synthetic service operations', securityContact: 'Synthetic provider reviewer',
  currentHostingScopeRevisionId: 'hosting-fixture',
};
export const fixtureHosting: HostingScopeRevision = {
  offeringId: offering.offeringId, offeringRevision: offering.revision, name: 'SYNTHETIC shared hosting · review-package:fixture-recovery-marker',
  purpose: 'Shared platform logging, network protection and identity integration',
  snapshot: { revisionId: 'hosting-fixture', revision: 2, snapshotHash: 'fixture-snapshot' },
  predecessorRevisionId: null, impactReviewId: null, exclusions: [], citations: [],
  permittedScopes: [{ cloud: 'AzureUSGovernment', directoryTenantId: '11111111-1111-1111-1111-111111111111',
    subscriptionId: '22222222-2222-2222-2222-222222222222',
    resourceId: '/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/synthetic-shared' }],
};
export const fixtureBoundary: BoundaryRevision = { ...boundary, name: 'Synthetic shared-services boundary',
  services: ['Platform logging', 'Network protection', 'Identity integration'],
  scopeStatement: 'Shared provider services only. Mission applications remain outside this service boundary.',
  includedScopes: fixtureHosting.permittedScopes,
  exclusions: [{ scope: null, description: 'Mission application code and customer data ownership', rationale: 'Customer-managed workload responsibilities' }],
  providerResponsibilities: ['Operate shared logging and network services'],
  customerResponsibilities: ['Configure application logs and review mission access'],
};
export const fixtureOverview: OfferingOverviewData = { ...offeringOverview(),
  authorizations: { ...paged([recordedAuthorization]), recorded: 1, unconfirmed: 0, rejected: 0 },
  packages: { ...offeringOverview().packages, sourceDocumentCount: 27 },
  capabilities: { proposed: 2, awaitingReview: 1, awaitingApproval: 1, published: 1, archived: 0, publishedReleaseRevisions: [3] },
  openFindingCount: 1,
};
export const fixtureCapabilities: OfferingBoundaryOverview = {
  offeringId: offering.offeringId, offeringRevision: offering.revision,
  capabilities: { ...paged([
    { capabilityId: null, candidateId: 'candidate-audit', packageId: 'package-1', name: 'Centralized audit collection',
      reviewState: 'NeedsReview', publicationState: 'Unpublished', releaseId: null, boundaryRevisionId: boundary.boundaryRevisionId },
    { capabilityId: null, candidateId: 'candidate-network', packageId: 'package-1', name: 'Network boundary protection',
      reviewState: 'Reviewed', publicationState: 'Unpublished', releaseId: null, boundaryRevisionId: boundary.boundaryRevisionId },
    { capabilityId: 'capability-fixture', candidateId: null, packageId: null, name: 'Identity federation',
      reviewState: 'Reviewed', publicationState: 'Published', releaseId: 'release-fixture', releaseRevision: 3, boundaryRevisionId: boundary.boundaryRevisionId },
  ]), published: 1, awaitingReview: 1 },
  missionSystems: paged([{ assignmentId: 'assignment-fixture', systemId: 'system-fixture', systemName: 'Synthetic mission system',
    relationshipState: 'Active', associated: true, adoptedCapabilityCount: 1, assignedScopes: fixtureHosting.permittedScopes }]),
};
export const fixturePublishedCapability: ProviderCapabilityDetail = {
  capability: { source: 'Provider', componentId: 'component-fixture', capabilityId: 'capability-fixture',
    name: 'Identity federation', description: 'Enterprise identity integration', componentName: 'Identity service',
    componentType: 'Service', lifecycle: 'Published', reviewState: 'Reviewed', sourceFormat: 'Manual',
    sourceReference: null, distinctAdoptionCount: 1, workingRevision: 4, releasedRevision: 3 },
  supportingComponents: [], unresolvedContributorIds: [], sourceArtifacts: [], mappedControlIds: ['IA-2', 'AC-2'],
  sourceEvidenceReferences: null, implementationNarrative: null,
};
export const fixtureCandidates = paged([
  candidate({ candidateId: 'candidate-audit', name: 'Centralized audit collection', description: 'Platform events and log retention',
    controlDuties: { 'AU-6': 'Shared', 'AU-11': 'Customer configures additional retention' } }),
  candidate({ candidateId: 'candidate-network', name: 'Network boundary protection', description: 'Shared network inspection service',
    controlDuties: { 'SC-7': 'Shared' }, reviewState: 'Reviewed' }),
]);
export const fixtureEntries = paged([
  entry({ entryId: 'archive', fileName: 'source.zip', mediaType: 'application/zip' }),
  entry({ entryId: 'matrix', fileName: 'responsibility-matrix.json', archivePath: 'responsibility-matrix.json' }),
  entry({ entryId: 'assessment', fileName: 'assessment-summary.json', archivePath: 'assessment-summary.json' }),
]);
export const fixtureFinding: Finding = { findingId: 'finding-fixture', offeringId: offering.offeringId, revision: 2,
  title: 'Synthetic logging evidence delay', observation: 'Synthetic retained observation', severityAsStated: 'Moderate',
  controlIds: ['AU-6'], citations: [], workflowState: 'Open', createdAt: '2026-09-26T12:00:00Z' };
export const fixtureEvidence: FindingEvidence = { evidenceId: 'evidence-fixture', findingId: fixtureFinding.findingId,
  offeringId: offering.offeringId, findingRevision: 2, fileName: 'synthetic-assessment-summary.pdf',
  mediaType: 'application/pdf', byteLength: 1024, sha256: 'synthetic-hash', description: 'Retained shared-services assessment',
  state: 'PendingReview', createdAt: fixtureFinding.createdAt, latestReview: null };
