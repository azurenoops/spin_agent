export const claimKinds = ['AuthorizationDecisionClaim', 'BoundaryClaim', 'AssessmentFinding', 'PoamItem'] as const;
export const isClaimKind = (kind: string) => claimKinds.some(value => value === kind);
export interface PackageClaim {
  authorizationDecision: {
    subjectKind: string; subject: string | null; reference: string | null; authority: string | null;
    decisionType: string | null; statusAsStated: string | null; decisionDate: string | null;
    expirationDate: string | null; scope: string | null; conditions: string[]; exclusions: string[];
  } | null;
  boundary: { subject: string | null; relationship: string; scope: string | null; environment: string | null;
    resourceIds: string[]; responsibilities: string[]; decisionReference: string | null } | null;
  assessmentFinding: { sourceFindingId: string | null; observation: string | null; severityAsStated: string | null;
    statusAsStated: string | null; assessor: string | null; assessmentDate: string | null;
    controlIds: string[]; evidenceReferences: string[] } | null;
  poamItem: { sourcePoamId: string | null; correctiveAction: string | null; ownerAsStated: string | null;
    statusAsStated: string | null; milestones: { description: string | null; dueDate: string | null }[];
    requiredClosureEvidence: string[]; submittedEvidenceReferences: string[] } | null;
  fieldSources: { field: string; citationIndexes: number[] }[];
  relationships: { kind: string; targetSourceId: string; targetEntryId?: string | null; resolution: string }[];
  sourceAliases: string[]; qualifications: string[];
}
export interface ClaimResolution { relationshipIndex: number; targetKind: string; targetId: string; expectedTargetRevision: number }
export interface ClaimReviewInput {
  expectedCandidateRevision: number; action: 'Reviewed' | 'Rejected'; rationale: string; resolutions: ClaimResolution[];
}
export interface ClaimReviewReceipt {
  reviewId: string; candidateId: string; candidateRevision: number; reviewState: string;
  unresolvedRelationships: unknown; recordedObject: null;
}
