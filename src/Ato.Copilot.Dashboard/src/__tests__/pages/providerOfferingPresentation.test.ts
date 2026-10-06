import { describe, expect, it } from 'vitest';
import { offeringReviewPresentation, prerequisiteLabel } from '../../features/systems/providerOfferingPresentation';
import type { SystemProviderScope } from '../../api/systemEnvironments';
import type { ApplicableProviderCapability, ProviderRelationship } from '../../features/provider-relationships/types';
import { allocationResponse, capability } from '../provider-relationships/fixtures';

const item: SystemProviderScope = {
  assignmentId: 'assignment-a', assignmentVersion: 3, relationshipId: 'relationship-a',
  offeringId: 'offering-a', offeringName: 'Any offering', providerName: 'Any provider',
  hostingScopeRevisionId: 'hosting-a', hostingScopeName: 'Recorded scope', hostingScopeRevision: 4,
  state: 'Active', relationshipState: 'SeparateBoundaryConsumer', reviewRequired: false,
  assignedScopes: [], selectionVersion: 1,
  responsibilityReview: { state: 'NotAdopted', canReview: true, canConfirm: false, reason: null },
};
const relationship: ProviderRelationship = { ...allocationResponse, relationshipId: 'relationship-a', reviewRequired: false,
  state: 'SeparateBoundaryConsumer' as const, canReviewRelationship: true, reviewedAt: '2026-10-06', reviewedBy: 'reviewer' };
const candidate: ApplicableProviderCapability = { ...capability, relationshipReviewRequired: false, outstandingDecisions: [] };
const present = (scope = item, current = relationship, capabilities = [candidate]) =>
  offeringReviewPresentation({ item: scope, relationship: current, capabilities, loading: false, error: null });

describe('offering review presentation from explicit contracts', () => {
  it.each(['SaaS', 'PaaS', 'IaaS', 'Shared service', 'Other'])('does not branch on the %s offering name or hosting description', name => {
    // Arrange
    const scope = { ...item, offeringName: name, hostingScopeName: `${name} hosting`, providerName: `${name} operator` };
    // Act
    const value = present(scope);
    // Assert
    expect(value.action.kind).toBe('applicability');
    expect(value.action.label).toBe('Review applicability');
    expect(value.adoption).toBe('Not adopted');
  });
  it('connects only when a canonical missing association explicitly permits it', () => {
    // Arrange
    const current = { ...relationship, relationshipId: null, canAssociate: true };
    // Act
    const value = present({ ...item, relationshipId: null }, current);
    // Assert
    expect(value.action.kind).toBe('connect');
    expect(value.relationship).toBe('Not recorded');
  });
  it('does not invent a responsible role or writable action for denied association', () => {
    // Arrange
    const current = { ...relationship, relationshipId: null, canAssociate: false, canReviewRelationship: false };
    // Act
    const value = present({ ...item, relationshipId: null }, current);
    // Assert
    expect(value.action.kind).toBe('inspect');
    expect(value.reason).toMatch(/does not permit/);
    expect(value.reason).not.toMatch(/owner|ISSM|ISSO|AO/);
  });
  it('reviews an unresolved relationship only with current explicit permission', () => {
    // Arrange
    const current = { ...relationship, reviewRequired: true };
    // Act
    const value = present(item, current);
    // Assert
    expect(value.action.kind).toBe('relationship');
  });
  it.each(['HOSTING_CONTEXT_STALE', 'OFFERING_CONTEXT_STALE', 'BOUNDARY_CONTEXT_STALE', 'RELEASE_SUPERSEDED',
    'PROVIDER_REVIEW_REQUIRED', 'DECISION_CONTEXT_STALE', 'PROVIDER_DECISION_REQUIRED'])('translates verified backend blocker %s', code => {
    // Arrange
    const blocked = { ...candidate, reasonCodes: [code], canProposeAdoption: false, applicabilityState: 'ReviewRequired' };
    // Act
    const value = present(item, relationship, [blocked, { ...blocked, capabilityId: 'second' }]);
    // Assert
    expect(value.action.kind).toBe('source');
    expect(value.blockers).toHaveLength(1);
    expect(value.blockers[0]?.capabilityIds).toEqual(['capability-a', 'second']);
    expect(value.blockers[0]?.label).not.toContain(code);
    expect(prerequisiteLabel(code)).not.toContain('_');
    expect(value.adoption).toBe('Blocked');
  });
  it('retains unknown codes as diagnostics without guessing their meaning', () => {
    // Arrange
    const blocked = { ...candidate, reasonCodes: ['NEW_BACKEND_REQUIREMENT'], canProposeAdoption: false };
    // Act
    const value = present(item, relationship, [blocked]);
    // Assert
    expect(value.action.kind).toBe('inspect');
    expect(value.blockers[0]?.label).toBe('Unresolved source prerequisite');
    expect(value.blockers[0]?.code).toBe('NEW_BACKEND_REQUIREMENT');
  });
  it('separates published applicability from pending responsibility acceptance', () => {
    // Arrange
    const scope = { ...item, responsibilityReview: { state: 'ReviewRequired' as const, canReview: true, canConfirm: false, reason: 'Missing baseline' } };
    // Act
    const value = present(scope);
    // Assert
    expect(value.action.kind).toBe('responsibility');
    expect(value.applicability).toBe('Published applicable');
    expect(value.responsibility).toBe('Review required');
    expect(value.reason).toBe('Missing baseline');
  });
  it.each([0, 1, 51])('does not reinterpret %i capability entries as setup or completeness', count => {
    // Arrange
    const scope = { ...item, responsibilityReview: { state: 'Reviewed' as const, canReview: true, canConfirm: false, reason: null } };
    const entries = Array.from({ length: count }, (_, index) => ({ ...candidate, capabilityId: `${index}` }));
    // Act
    const value = present(scope, relationship, entries);
    // Assert
    expect(value.overall).toBe('Review recorded');
    expect(value.action.label).toBe('Inspect recorded review');
  });
  it('does not turn empty publication into not applicable', () => {
    // Arrange / Act
    const value = present(item, relationship, []);
    // Assert
    expect(value.applicability).toBe('Not recorded');
    expect(value.action.kind).toBe('inspect');
  });
  it.each([true, false])('fails closed on missing or failed current records (loading=%s)', loading => {
    // Arrange / Act
    const value = offeringReviewPresentation({ item, loading, error: loading ? null : 'Denied', capabilities: undefined, relationship: undefined });
    // Assert
    expect(value.action.kind).toBe(loading ? 'inspect' : 'refresh');
    expect(value.overall).toBe('Unavailable');
  });
  it('flags a newer release without replacing captured source', () => {
    // Arrange
    const scope = { ...item, publishedDuties: { state: 'Available' as const, reason: null, capabilities: [{
      capabilityId: 'capability-a', capabilityName: 'Pinned', releaseId: 'old-release', releaseRevision: 1,
      releaseSnapshotHash: 'old-hash', contentHash: 'content', applicabilityContextId: 'context',
      description: null, providerControlIds: [], sharedControlIds: [], customerControlIds: [],
    }] } };
    // Act
    const value = present(scope);
    // Assert
    expect(value.action.kind).toBe('source');
    expect(value.sourceChanged).toBe(true);
    expect(scope.publishedDuties.capabilities[0]?.releaseId).toBe('old-release');
  });
});
