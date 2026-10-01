import type { DesignLayout, SystemDesignGraph } from '../../api/systemDesign';

export function designFixture(): SystemDesignGraph {
  return {
    tenantId: 'org-a', systemId: 'system-a', systemName: 'Synthetic Mission System', revision: 2, approvedRevision: 1,
    governanceStatus: 'Draft', sourceFingerprint: 'source-v1', sourcesStale: false,
    synchronizedAt: '2026-09-30T10:00:00Z', lastEditor: 'Synthetic Owner', reviewer: 'Synthetic Reviewer',
    nodes: [
      { id: 'system', label: 'Synthetic Mission System', kind: 'System', boundaryDisposition: 'InBoundary', projectionStatus: 'Projected',
        reviewState: 'Reviewed', sspImpact: 'System description', properties: {},
        source: { type: 'System', id: 'system-a', version: '1', provenance: 'Canonical', precedence: 3, reviewState: 'Reviewed', resolutionUrl: '/systems/system-a/profile/MissionAndPurpose' } },
      { id: 'storage', label: 'Mission records', kind: 'Component', boundaryDisposition: 'Undetermined', projectionStatus: 'Projected',
        reviewState: 'Unapproved', sspImpact: 'Boundary', properties: {}, environment: 'Government', networkZone: 'Data',
        source: { type: 'SystemComponent', id: 'storage', version: '1', provenance: 'Canonical', precedence: 3, reviewState: 'Reviewed', resolutionUrl: '/systems/system-a/boundaries' } },
    ],
    edges: [{ id: 'flow', sourceNodeId: 'system', targetNodeId: 'storage', relationshipType: 'DataFlow', direction: 'Outbound',
      purpose: 'Retain mission records', informationType: 'Mission records', classification: 'CUI', port: '443', protocol: 'TCP',
      service: 'HTTPS', protection: 'TLS', encryptionState: 'Encrypted', boundaryCrossing: 'Unknown',
      reviewState: 'Unapproved', projectionStatus: 'Draft' }],
    groups: [{ id: 'inside', label: 'Authorization boundary — included', kind: 'Boundary', nodeIds: ['system'] }],
    gaps: [{ id: 'gap-storage', severity: 'High', explanation: 'Boundary disposition requires review.', recordId: 'storage',
      view: 'Boundary', sspImpact: 'Authorization boundary', owner: 'System Owner', resolutionUrl: '/systems/system-a/boundaries' }],
    proposals: [], contributions: ['Mission', 'Users', 'Environment & hosting', 'Data', 'Inventory & boundary', 'Ports & interconnections', 'Azure']
      .map(section => ({ section, recordCount: 1, state: 'Reviewed', explanation: 'Synthetic source contribution', resolutionUrl: '/systems/system-a/profile/MissionAndPurpose' })),
    baselineChanges: [{ kind: 'Modified', recordId: 'storage', before: 'Original label', after: 'Mission records' }],
    actions: { canEdit: true, canSubmit: true, canWithdraw: false, canReview: false, canReconcile: true },
    completenessPercentage: 50, sspReadiness: 'Unapproved', discoveryState: 'NotCollected', monitoringState: 'NotVerified',
  };
}

export const designLayoutFixture = (view = 'Context'): DesignLayout => ({
  version: 0, view, positions: {}, collapsedGroups: [], edgeRouting: {}, visibility: {}, viewport: { x: 0, y: 0, zoom: 1 },
});
