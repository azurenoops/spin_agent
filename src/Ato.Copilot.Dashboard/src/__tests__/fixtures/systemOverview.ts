import type { PackageReadinessWorkspace, PackageReadinessRun, PackageReadinessAction } from '../../api/packageReadiness';
import type { OverviewWork, OverviewWorkGroup } from '../../api/systemOverview';

export const overviewAction: PackageReadinessAction = { canView: true, canEdit: false, path: 'profile/SystemDesign', label: 'Open',
  reason: 'Open the source workflow; this action does not grant edit or approval authority.' };
export const overviewRun: PackageReadinessRun = {
  id: 'run-a', outcome: 'Blocked', startedAt: '2026-10-05T12:00:00Z', evaluatedAt: '2026-10-05T12:00:01Z',
  evaluatedBy: 'reviewer', sourceHash: 'a'.repeat(64), sourceHashAfter: 'a'.repeat(64), ruleVersion: 'v3',
  counts: { total: 2, blocking: 2, followUp: 0, passed: 0, notApplicable: 0, unavailable: 0, requiredUnavailable: 0 },
  recommendedCheckId: 'system-design', failure: null,
  freshness: { state: 'Current', checkedAt: '2026-10-05T12:00:02Z', currentSourceHash: 'a'.repeat(64), reason: null },
};
export function overviewWorkspace(): PackageReadinessWorkspace {
  return {
    systemId: 'a', purpose: 'InitialSubmission', selectionHash: 'b'.repeat(64), retainedContext: null,
    source: { state: 'Available', hash: 'a'.repeat(64), ruleVersion: 'v3', reason: null }, latestRun: overviewRun,
    lastSuccessfulRun: overviewRun,
    permissions: { canValidate: true, validateReason: null, canGenerate: false, generateReason: 'Blocking requirements remain.' },
    progress: ['prepare', 'validate', 'export', 'emass', 'decision'].map(id => ({
      id: id as 'prepare' | 'validate' | 'export' | 'emass' | 'decision',
      state: id === 'prepare' || id === 'validate' ? 'Blocked' : 'NotRecorded',
      description: id === 'emass' ? 'Manual receiving observations; no live connector.' : 'Actual retained records.',
      records: [], totalCount: 0, action: { ...overviewAction, path: 'documents' },
    })),
    documents: [
      { kind: 'ssp', title: 'SSP section records', presence: 'Present', status: 'Draft', reviewState: null,
        sourceState: 'Current', validationOutcome: 'Blocking', recordCount: 1,
        records: [{ kind: 'ssp-section', id: 'section', status: 'Draft', recordedAt: null, purpose: null,
          sourceHash: null, sourceRelationship: 'Unknown', action: { ...overviewAction, path: 'narratives' } }],
        action: { ...overviewAction, path: 'narratives' } },
      { kind: 'sap', title: 'Assessment plans', presence: 'Missing', status: null, reviewState: null,
        sourceState: 'Current', validationOutcome: 'Blocking', recordCount: 0, records: [],
        action: { ...overviewAction, path: 'assessments?tab=plan' } },
    ],
    rmf: { phase: 'Prepare', transitions: [], totalCount: 0, confirmed: false, source: null,
      recordedAt: null, actor: null, canConfirm: true },
  };
}
export function overviewGroups(): OverviewWorkGroup[] {
  return [
    { id: 'design', title: 'Review system design', category: 'system-design', owner: null, action: overviewAction,
      rmfPhases: ['Prepare', 'Implement'], documents: ['System Security Plan'], controls: [],
      total: 1, blocking: 1, warnings: 0,
      priorityReason: 'The evaluated finding requires design review before final SSP generation.',
      findings: { items: [{ id: 'finding-design', severity: 'Error', category: 'system-design', artifactType: 'ssp',
        description: 'Review and approve system design before final SSP generation.', remediation: 'Review system design.',
        controlId: null, recordId: null }], totalCount: 1, limit: 20, offset: 0 } },
    { id: 'ac1', title: 'Review AC-1 requirement responses', category: 'requirement-coverage',
      owner: { personId: 'owner-a', displayName: 'Alex Owner', role: 'Issm', assignmentId: 'assignment-a', scope: 'System' },
      action: { ...overviewAction, path: 'narratives?control=AC-1&statement=policy' },
      rmfPhases: ['Implement'], documents: ['System Security Plan'], controls: ['AC-1'],
      total: 4, blocking: 3, warnings: 1, priorityReason: null,
      findings: { items: Array.from({ length: 4 }, (_, index) => ({
        id: `finding-ac1-${index}`, severity: index === 3 ? 'Warning' as const : 'Error' as const,
        category: 'requirement-coverage', artifactType: 'ssp', description: `Recorded requirement gap ${index}`,
        remediation: 'Review mapped requirements.', controlId: 'AC-1', recordId: `ac-1_smt.a.${index}`,
      })), totalCount: 4, limit: 20, offset: 0 } },
  ];
}
export function overviewWork(): OverviewWork {
  return { systemId: 'a', purpose: 'InitialSubmission', selectionHash: 'b'.repeat(64), runId: 'run-a',
    findingsAvailable: true, counts: { total: 5, blocking: 4, warnings: 1 }, actorPersonId: 'owner-a',
    groups: { items: overviewGroups(), totalCount: 2, limit: 10, offset: 0 }, recommendedGroupId: 'design' };
}
