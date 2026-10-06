import type { SystemProviderScope } from '../../api/systemEnvironments';
import type { ApplicableProviderCapability, ProviderRelationship } from '../provider-relationships/types';

const prerequisites: Record<string, string> = {
  OFFERING_CONTEXT_STALE: 'The offering changed since publication',
  BOUNDARY_CONTEXT_STALE: 'The published boundary needs current source review',
  HOSTING_CONTEXT_STALE: 'The hosting scope changed or no longer matches',
  RELEASE_SUPERSEDED: 'A newer capability release is available',
  PROVIDER_REVIEW_REQUIRED: 'Provider publication review is required',
  DECISION_CONTEXT_STALE: 'The recorded provider decision or its evidence changed',
  PROVIDER_DECISION_REQUIRED: 'A recorded provider decision is required',
  MissionAssociationRequired: 'Connect this offering to the system',
  AuthorizationRelationshipReviewRequired: 'Review the system relationship',
  ResponsibilitiesNotConfirmedByAssociationOrAdoption: 'System responsibilities require separate review',
};
export const prerequisiteLabel = (code: string) => prerequisites[code] ?? 'Unresolved source prerequisite';

export type OfferingAction = 'connect' | 'relationship' | 'applicability' | 'responsibility' | 'source' | 'inspect' | 'refresh';
export interface OfferingReviewPresentation {
  overall: string;
  relationship: string;
  applicability: string;
  adoption: string;
  responsibility: string;
  reason: string;
  action: { kind: OfferingAction; label: string };
  sourceChanged: boolean;
  blockers: { code: string; label: string; capabilityIds: string[] }[];
}

export function offeringReviewPresentation({ item, relationship, capabilities, loading, error }: {
  item: SystemProviderScope; relationship?: ProviderRelationship; capabilities?: ApplicableProviderCapability[];
  loading: boolean; error: string | null;
}): OfferingReviewPresentation {
  const blockers = new Map<string, Set<string>>();
  for (const capability of capabilities ?? []) {
    for (const code of capability.reasonCodes) {
      const affected = blockers.get(code) ?? new Set<string>();
      affected.add(capability.capabilityId); blockers.set(code, affected);
    }
  }
  const captured = item.publishedDuties?.capabilities ?? [];
  const sourceChanged = (capabilities ?? []).some(current => captured.some(source =>
    source.capabilityId === current.capabilityId && (source.releaseId !== current.releaseId
      || source.releaseSnapshotHash !== current.releaseSnapshotHash)));
  const changedSourceBlocker = [...blockers.keys()].some(code =>
    ['OFFERING_CONTEXT_STALE', 'BOUNDARY_CONTEXT_STALE', 'HOSTING_CONTEXT_STALE', 'RELEASE_SUPERSEDED', 'DECISION_CONTEXT_STALE'].includes(code));
  const missingSourceBlocker = blockers.has('PROVIDER_REVIEW_REQUIRED') || blockers.has('PROVIDER_DECISION_REQUIRED');
  const unavailable = loading || !!error || !relationship || !capabilities;
  const review = item.responsibilityReview;
  const value: OfferingReviewPresentation = {
    overall: unavailable ? 'Unavailable' : 'Review required',
    relationship: !relationship ? 'Unavailable' : !relationship.relationshipId ? 'Not recorded'
      : relationship.reviewRequired ? 'Review required' : relationship.reviewedAt ? 'Review recorded' : 'Not recorded',
    applicability: !capabilities ? 'Unavailable' : !capabilities.length ? 'Not recorded'
      : capabilities.every(entry => entry.applicabilityState === 'Applicable') ? 'Published applicable' : 'Review required',
    adoption: !review || review.state === 'Unavailable' ? 'Unavailable' : review.state === 'NotAdopted'
      ? blockers.size || capabilities?.length && capabilities.every(entry => !entry.canProposeAdoption) ? 'Blocked' : 'Not adopted' : 'Current adoption recorded',
    responsibility: !review ? 'Unavailable' : ({
      Unavailable: 'Unavailable', NotAdopted: 'Not recorded', ReviewRequired: 'Review required',
      Reviewed: 'Review recorded', Removed: 'Historical',
    })[review.state],
    action: { kind: 'inspect', label: 'Inspect unresolved review' },
    reason: 'Inspect current records; no permitted review action has been verified.',
    sourceChanged,
    blockers: [...blockers].map(([code, ids]) => ({ code, label: prerequisiteLabel(code), capabilityIds: [...ids] })),
  };
  const action = (kind: OfferingAction, label: string, reason: string) => {
    value.action = { kind, label }; value.reason = reason; return value;
  };
  if (loading) return action('inspect', 'Inspect review status', 'Current review records are loading.');
  if (error) return action('refresh', 'Retry review records', 'Current review records could not be read. Retry before continuing.');
  if (!relationship || !capabilities) return value;
  if (!relationship.relationshipId) return relationship.canAssociate
    ? action('connect', 'Connect offering', 'The current system record has no offering relationship; the server permits association.')
    : action('inspect', 'Inspect unresolved review', 'The server does not permit association for this record and identity. Inspect the retained source and current permissions.');
  if (sourceChanged || changedSourceBlocker) return action('source', 'Review changed source',
    sourceChanged ? 'Current publication differs from the captured release. Compare sources without replacing the selection.'
      : 'Published source prerequisites need review before capability adoption can proceed.');
  if (missingSourceBlocker) return action('source', 'Resolve adoption prerequisites',
    'The server reports missing provider publication review or decision evidence. Inspect the shared prerequisites and affected capabilities.');
  if (relationship.reviewRequired && relationship.canReviewRelationship === true
    && relationship.state !== 'ExplicitlyCoveredByRecordedScope')
    return action('relationship', 'Review relationship', 'The recorded system relationship requires review and the server permits it.');
  if (relationship.reviewRequired) return action('inspect', 'Inspect unresolved review',
    'The relationship requires review, but the server does not permit this determination here. Inspect current permissions and source evidence.');
  if (blockers.size || capabilities.some(entry => entry.applicabilityState !== 'Applicable'))
    return action('inspect', 'Inspect unresolved review', 'Capability applicability has unresolved prerequisites. Inspect their source details.');
  if (review?.state === 'ReviewRequired') return review.canReview
    ? action('responsibility', 'Review responsibilities', review.reason ?? 'Current adopted capabilities require responsibility review.')
    : action('inspect', 'Inspect unresolved review', 'The server does not permit responsibility review for this record and identity.');
  if (review?.state === 'Reviewed') {
    value.overall = 'Review recorded';
    return action('inspect', 'Inspect recorded review', 'The canonical responsibility review is recorded. Inspect it before proposing an update.');
  }
  if (review?.state === 'NotAdopted' && capabilities.some(entry => entry.canProposeAdoption))
    return action('applicability', 'Review applicability', 'Published applicability is available; system adoption and responsibilities are not yet recorded.');
  if (capabilities.length && capabilities.every(entry => !entry.canProposeAdoption))
    return action('inspect', 'Inspect unresolved review', 'The server does not permit capability adoption for the current records and identity.');
  return value;
}

export function offeringWorkflowContext(item: SystemProviderScope) {
  return new URLSearchParams({
    offeringId: item.offeringId, assignmentId: item.assignmentId,
    hostingScopeRevisionId: item.hostingScopeRevisionId,
  }).toString();
}
