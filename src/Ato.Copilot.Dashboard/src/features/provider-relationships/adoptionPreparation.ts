import { listApplicableProviderCapabilities } from './api';
import type { ApplicableProviderCapability, CapabilityAdoptionInput } from './types';

export class MissionReviewRequiredError extends Error {
  constructor() {
    super('The capability source, hosting context, responsibilities or adoption permission changed. The hosting relationship and completed adoptions remain saved. Review current choices before confirming remaining work.');
    this.name = 'MissionReviewRequiredError';
  }
}

export function canPlanAdoption(item: ApplicableProviderCapability): boolean {
  return item.canProposeAdoption || (item.canConfirmResponsibilities
    && item.applicabilityState === 'Applicable' && item.reasonCodes.length === 0
    && item.outstandingDecisions.includes('MissionAssociationRequired'));
}

function reviewedMaterial(item: ApplicableProviderCapability): string {
  const sorted = (values: string[]) => [...values].sort();
  return JSON.stringify({
    capabilityId: item.capabilityId, capabilityName: item.capabilityName,
    releaseId: item.releaseId, releaseRevision: item.releaseRevision, releaseSnapshotHash: item.releaseSnapshotHash,
    offeringId: item.offeringId, offeringName: item.offeringName,
    assignmentId: item.assignmentId, assignmentRevision: item.assignmentRevision,
    applicability: [item.applicability.revisionId, item.applicability.revision, item.applicability.snapshotHash],
    applicabilityState: item.applicabilityState, reasonCodes: sorted(item.reasonCodes),
    authorizationRelationship: item.authorizationRelationship, relationshipReviewRequired: item.relationshipReviewRequired,
    providerCoverage: sorted(item.providerCoverage), sharedDuties: sorted(item.sharedDuties),
    customerDuties: sorted(item.customerDuties),
    outstandingDecisions: sorted(item.outstandingDecisions.filter(value => value !== 'MissionAssociationRequired')),
    sourceReferences: item.sourceReferences.map(source =>
      JSON.stringify([source.referenceId, source.title, source.locator, source.canReadContent])).sort(),
  });
}

export interface PreparedAdoption {
  capability: ApplicableProviderCapability;
  body: CapabilityAdoptionInput;
}

export async function prepareAdoption(systemId: string, reviewed: ApplicableProviderCapability): Promise<PreparedAdoption> {
  const page = await listApplicableProviderCapabilities(systemId, {
    page: 1, assignmentId: reviewed.assignmentId, capabilityId: reviewed.capabilityId, releaseId: reviewed.releaseId,
  });
  const current = page.items.length === 1 && page.total === 1 ? page.items[0] : undefined;
  if (!current || !current.canProposeAdoption || current.outstandingDecisions.includes('MissionAssociationRequired')
    || reviewedMaterial(current) !== reviewedMaterial(reviewed)) {
    throw new MissionReviewRequiredError();
  }
  return {
    capability: current,
    body: {
      assignmentId: current.assignmentId, expectedAssignmentRevision: current.assignmentRevision,
      capabilityId: current.capabilityId, releaseId: current.releaseId,
      contextSnapshotHash: current.applicability.snapshotHash, applicabilityPreviewHash: current.applicabilityPreviewHash,
    },
  };
}
