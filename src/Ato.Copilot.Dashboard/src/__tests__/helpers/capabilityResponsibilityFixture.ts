import type { CapabilityResponsibilityItem } from '../../api/capabilityResponsibilities';

export function responsibilitySnapshotJson(overrides: Record<string, unknown> = {}): string {
  return JSON.stringify({
    Id: 'capability-a', Name: 'Reviewed access capability', Description: 'Published provider access controls.',
    Status: 0, Controls: ['AC-1', 'AC-2', 'AC-3', 'AC-4', 'AC-5', 'AC-6'],
    Component: {
      CspInheritedComponentId: 'component-a', CspProfileId: 'provider-a', Name: 'Published provider component',
      Description: 'Current component description.', Status: 1, SourceArtifactReference: '[redacted]',
    },
    ...overrides,
  });
}

export function responsibilityItem(state = 'MissingAllocation', controlId = 'AC-1'): CapabilityResponsibilityItem {
  return {
    subscriptionId: 'subscription-a', capabilityId: 'capability-a', componentId: 'component-a', cspProfileId: 'provider-a',
    controlId, sourceRevision: 'source-1', reviewRevision: 'review-1', state, reviewedSourceRevision: null,
    confirmedBy: null, confirmedAt: null, allocation: null, effectiveInheritanceType: null, designationSource: null,
    sourceAvailable: true, sourceSnapshotJson: responsibilitySnapshotJson(), reviewedSourceSnapshotJson: null,
  };
}
