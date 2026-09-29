import type { RemediationFindingDetail, RemediationTaskDetail, RemediationWorkspace } from '../../api/remediationWorkspace';
import type { PoamWorkspace } from '../../api/poamWorkspace';

export const remediationWorkspace: RemediationWorkspace = {
  systemId: 'system-a', owners: [{ id: 'person-a', name: 'Application team' }],
  permissions: { canCreateFinding: true, canCreateTask: true, canManageRemediation: true, canVerify: true, reason: null,
    canMoveTasks: true, canMoveAnyTasks: true },
  warnings: [],
  findings: [{
    id: 'finding-a', title: 'Verify session timeout correction', description: 'Session timeout exceeds the retained requirement.',
    controlId: 'AC-12', severity: 'Medium', status: 'Open', ownerName: 'Application team', workStatus: 'Ready to verify',
    readyToVerify: true, isClosed: false, source: { resultId: 'assessment:assessment-a', name: 'Azure configuration checks', planId: 'sap-a', planRevision: 1 },
    taskIds: ['task-a', 'task-b'], poamIds: ['poam-a'], revision: 'finding-revision-a',
  }],
  tasks: [{
    id: 'task-a', taskNumber: 'REM-024', title: 'Correct session timeout settings', description: 'Set the configured timeout.',
    controlId: 'AC-12', severity: 'Medium', status: 'InReview', assigneeId: 'person-a', assigneeName: 'Application team',
    dueDate: '2026-10-05', findingId: 'finding-a', poamIds: ['poam-a'], rowVersion: 'task-revision-a',
    affectedResources: ['/resource/a'], validationCriteria: 'Retest the timeout.', remediationScript: null, remediationScriptType: null,
  }, {
    id: 'task-b', taskNumber: 'REM-025', title: 'Verify corrected configuration', description: 'Review retained retest observations.',
    controlId: 'AC-12', severity: 'Medium', status: 'ToDo', assigneeId: 'person-a', assigneeName: 'Application team',
    dueDate: '2026-10-05', findingId: 'finding-a', poamIds: ['poam-a'], rowVersion: 'task-revision-b',
    affectedResources: ['/resource/a'], validationCriteria: null, remediationScript: null, remediationScriptType: null,
  }],
  poams: [{
    id: 'poam-a', weakness: 'Session management', controlId: 'AC-12', status: 'Ongoing', owner: 'Application team',
    scheduledCompletionDate: '2026-10-05', rowVersion: 'poam-revision-a', taskIds: ['task-a', 'task-b'],
  }],
};
export const remediationFindingDetail: RemediationFindingDetail = {
  systemId: 'system-a', finding: remediationWorkspace.findings[0]!, tasks: remediationWorkspace.tasks,
  poams: remediationWorkspace.poams, permissions: remediationWorkspace.permissions,
  exceptions: [{
    id: 'exception-a', type: 'Waiver', status: 'Pending', controlId: 'AC-12',
    justification: 'Temporary request awaiting an authorized decision.', decisionAuthority: null,
    conditions: null, expiresAt: '2026-10-31', reviewedAt: null,
  }],
  evidence: [{ id: 'evidence-a', name: 'Retest observations', collectedAt: '2026-09-29', hash: 'retained-evidence-hash', downloadUrl: null }],
  history: [{ at: '2026-09-29T12:00:00Z', actor: 'Collector', action: 'Recorded', description: 'Retained finding created from assessment observations.' }],
  verificationBlockers: ['Complete the remaining verification task.'],
};
export const remediationTaskDetail: RemediationTaskDetail = {
  systemId: 'system-a', task: remediationWorkspace.tasks[0]!, poams: remediationWorkspace.poams,
  evidence: remediationFindingDetail.evidence, history: remediationFindingDetail.history,
  permissions: remediationWorkspace.permissions, allowedTransitions: ['InProgress', 'Done'], verificationBlockers: [],
};

export const rawRemediationWorkspace: PoamWorkspace = {
  systemId: 'system-a',
  owners: [{ id: 'person-a', name: 'Application team' }, { id: 'person-b', name: 'Security assessor' }],
  permissions: { canManageRemediation: true, canCreateTasks: true, canMoveTasks: true, canMoveAnyTasks: true, reason: null },
  findings: [{
    id: 'finding-a', title: 'Verify session timeout correction', description: 'Session timeout exceeds the retained requirement.',
    controlId: 'AC-12', severity: 'Medium', status: 'Open', source: 'Azure', assessmentId: 'assessment-a',
    importRecordId: null, discoveredAt: '2026-09-29T12:00:00Z', taskIds: ['task-a', 'task-b'], poamIds: ['poam-a'],
    deviationId: null, provenance: { sourceId: 'assessment-a', sourceName: 'Azure configuration checks', sourceType: 'Assessment',
      plan: { id: 'sap-a', revision: 1, hash: 'retained-plan-hash', title: 'Saved assessment plan', status: 'Finalized' } },
  }],
  tasks: remediationWorkspace.tasks.map(task => ({ ...task, boardId: 'board-a', dueDate: task.dueDate!,
    verificationStatus: 'NotVerified', verificationNotes: null, verifiedBy: null, verifiedAt: null,
    evidence: [{ id: 'evidence-a', name: 'Retest observations', contentHash: 'retained-evidence-hash', linkedAt: '2026-09-29T12:00:00Z', linkedBy: 'person-a' }],
    history: [{ id: `history-${task.id}`, eventType: 'Created', oldValue: null, newValue: null, actor: 'Collector',
      at: '2026-09-29T12:00:00Z', details: 'Retained finding created from assessment observations.' }],
    allowedTransitions: ['InProgress', 'Done'],
  })),
  poams: [{
    id: 'poam-a', poamId: 'poam-a', weakness: 'Session management', securityControlNumber: 'AC-12', status: 'Ongoing',
    taskIds: ['task-a', 'task-b'], findingId: 'finding-a', deviationId: null, rowVersion: 'poam-revision-a',
    pointOfContact: 'Application team', scheduledCompletionDate: '2026-10-05',
  }],
  exceptions: [{ id: 'exception-a', type: 'Waiver', status: 'Pending', controlId: 'AC-12',
    justification: 'Temporary request awaiting an authorized decision.', expirationDate: '2026-10-31', isEffective: false,
    findingId: 'finding-a', poamEntryId: 'poam-a', reviewedBy: null, reviewerRole: null, reviewedAt: null, compensatingControls: null }],
};
