import type { ImpactDetails, ImpactOption, ImpactOptionKind } from '../../features/provider-authorizations/changeImpactApi';
import type { ImpactInput, ImpactPreview, ImpactReview } from '../../features/provider-authorizations/types';

export const emptyImpactPage = { items: [], page: 1, pageSize: 25, total: 0 };
export const impactOptions: Record<ImpactOptionKind, ImpactOption[]> = {
  Boundary: [
    { id: 'boundary-1', name: 'Government services', version: 'Version 1', summary: 'Recorded service scope.',
      change: { kind: 'Boundary', recordId: 'boundary-1', expectedRevision: 1, proposedSnapshotHash: 'b'.repeat(64) } },
    { id: 'boundary-2', name: 'Expanded government services', version: 'Version 2', summary: 'Revised service scope.',
      change: { kind: 'Boundary', recordId: 'boundary-2', expectedRevision: 2, proposedSnapshotHash: 'c'.repeat(64) } },
  ],
  HostingScope: [],
  Authorization: [{ id: 'decision-revision', name: 'Recorded ATO', version: 'Version 1', summary: 'External authorization record.', change: null }],
  Package: [{ id: 'version-1', name: 'Revised logging package', version: 'Version 2', summary: 'Source package only; not approved coverage.', change: null }],
  Component: [{ id: 'component-1', name: 'Audit service', version: 'Revision 7', summary: 'Saved component.',
    change: { kind: 'Component', recordId: 'component-1', expectedRevision: 7, proposedSnapshotHash: 'd'.repeat(64) } }],
  Capability: [{ id: 'capability-1', name: 'Logging coverage', version: 'Working revision 7', summary: 'Saved capability with changed logging responsibility.',
    change: { kind: 'Capability', recordId: 'capability-1', expectedRevision: 7, proposedSnapshotHash: 'a'.repeat(64) } }],
};
export const impactContext: ImpactInput = {
  expectedOfferingRevision: 4, boundaryRevisionId: 'boundary-1', authorizationRevisionIds: ['decision-revision'],
  packageVersionIds: ['version-1'], changes: [{ kind: 'Capability', recordId: 'capability-1', expectedRevision: 7, proposedSnapshotHash: 'a'.repeat(64) }],
};
export const impactPreview: ImpactPreview = {
  reviewId: 'impact-1', revision: 2, previewId: 'preview-1', previewHash: 'e'.repeat(64),
  contextSnapshotHash: 'c'.repeat(64), expiresAt: '2099-01-01T00:00:00Z', blockers: [],
  affectedCounts: { components: 1, capabilities: 1, scopes: 0, systems: 1 },
};
export const acceptedImpact: ImpactReview = {
  reviewId: 'impact-1', revision: 3, disposition: 'AcceptForPublication', reviewedBy: 'Test reviewer',
  reviewedAt: '2026-09-24T01:00:00Z', contextSnapshotHash: 'c'.repeat(64), stale: false,
  title: 'Logging coverage change', summary: 'Saved capability change supported by the revised package.',
  createdAt: '2026-09-24T00:00:00Z', affectedCounts: impactPreview.affectedCounts,
};
export const pendingImpact: ImpactReview = {
  ...acceptedImpact, disposition: 'PendingReview', revision: 2, reviewedBy: null, reviewedAt: null,
};
export const impactDetails: ImpactDetails = {
  review: pendingImpact, title: 'Logging coverage change', summary: 'Recorded relationships for the proposed logging change.',
  rationale: null, createdAt: '2026-09-24T00:00:00Z', context: impactContext,
  changes: [{ kind: 'Capability', recordId: 'capability-1', expectedRevision: 7, proposedSnapshotHash: 'a'.repeat(64),
    name: 'Logging coverage', summary: 'Review the saved responsibility changes.' }],
  blockers: [],
  affectedCapabilities: { ...emptyImpactPage, total: 1, items: [
    { recordId: 'capability-1', name: 'Logging coverage', kind: 'Capability', summary: 'Saved change included in this review.', reviewState: 'Affected' },
  ] },
  affectedSystems: { ...emptyImpactPage, total: 1, items: [
    { recordId: 'system-1', name: 'Mission Alpha', kind: 'System', summary: 'Associated system with a recorded capability subscription.', reviewState: 'NeedsReview' },
  ] },
};
