import type { AssessmentPlanWorkspace, AssessmentResultsWorkspace, AssessmentResultDetail, AssessmentReport } from '../../api/assessmentWorkspace';

export const planWorkspace: AssessmentPlanWorkspace = {
  systemId: 'system-a', systemName: 'SPIN Demo System', baselineLevel: 'Moderate', baselineControlCount: 339,
  plan: {
    id: 'sap-a', title: 'SPIN Demo System · Assessment plan', status: 'Draft', revision: 2, contentHash: 'plan-hash',
    generatedAt: '2026-09-28T10:00:00Z', updatedAt: '2026-09-28T11:00:00Z', finalizedAt: null,
    assessmentLead: null, assessmentLeadId: null, scopeNotes: '', assessmentApproach: '', rulesOfEngagement: '',
    scheduleStart: null, scheduleEnd: null, scopeCount: 2,
    controls: ['AC-1', 'AC-2'].map(controlId => ({ controlId, title: controlId === 'AC-1' ? 'Policy and procedures' : 'Account management',
      family: 'AC', included: true, exclusionRationale: null, methods: ['Examine', 'Interview', 'Test'], methodRationale: null, objectives: ['Examine the recorded implementation.'] })),
    teamMembers: [],
  },
  plans: [{ id: 'sap-a', title: 'SPIN Demo System · Assessment plan', status: 'Draft', revision: 2, generatedAt: '2026-09-28T10:00:00Z', finalizedAt: null }],
  leadOptions: [{ id: 'person-a', name: 'Alex Assessor', kind: 'System member', organization: 'SPIN organization' }],
  tasks: [
    { key: 'lead', title: 'Assessment lead', description: 'Choose who will coordinate the assessment.', complete: false, required: false, actionLabel: 'Choose lead' },
    { key: 'scope', title: 'Assessment scope', description: 'Review included controls and exclusions.', complete: true, required: false, actionLabel: 'Define scope' },
    { key: 'approach', title: 'Assessment approach', description: 'Describe methods, procedures and responsibilities.', complete: false, required: false, actionLabel: 'Add approach' },
    { key: 'team', title: 'Assessment team', description: 'Record the planned assessment team.', complete: false, required: false, actionLabel: 'Edit team' },
    { key: 'schedule', title: 'Assessment schedule', description: 'Record the assessment dates.', complete: false, required: false, actionLabel: 'Set schedule' },
  ],
  warnings: ['No assessment team members assigned.', 'Assessment schedule start and/or end dates not set.'],
  finalizationBlockers: [],
  permissions: { canCreatePlan: true, canEditPlan: true, canFinalizePlan: true, createReason: null, editReason: null, finalizeReason: null },
};
export const resultsWorkspace: AssessmentResultsWorkspace = {
  systemId: 'system-a', items: [], totalCount: 0, page: 1, pageSize: 25,
  collection: {
    canRunAzure: false, runReason: 'Your assignments do not authorize Azure assessment execution.',
    canImport: true, importReason: null, canConfigureAzure: false, configurationReason: 'Configuration access required.',
    azure: { state: 'Denied', message: 'Azure assessment access required.', checkedAt: null, subscriptions: [], scopeDescription: [] },
    importFormats: ['CKL', 'XCCDF', 'Nessus'],
  },
  sarReadiness: { canPrepareDraft: false, blockers: ['Select result sets to prepare a report.'], warnings: [],
    scopeCount: 2, observedControlCount: 0, reviewedControlCount: 0, missingControlIds: ['AC-1', 'AC-2'], selectedResultIds: [] },
  permissions: { canReview: false, reviewReason: 'Assessor permission required.', canRemediate: false, canRequestDeviation: false },
  reports: [], selectedResults: [],
};
export const resultDetail: AssessmentResultDetail = {
  systemId: 'system-a',
  item: { id: 'assessment:run-a', recordId: 'run-a', name: 'Azure configuration checks', source: 'Azure',
    method: 'Examine', collectionStatus: 'Completed', reviewStatus: 'Not reviewed', recordedAt: '2026-09-28T12:00:00Z',
    actor: 'Collector', planId: 'sap-a', planRevision: 1, planTitle: 'SPIN Demo System · Assessment plan',
    planStatusAtCollection: 'Draft', requiresReconciliation: true, observedControlCount: 1, reviewedControlCount: 0,
    scopeControlCount: 2, revision: 'result-revision', warnings: ['Plan changed after collection.'], canReview: true },
  originalScope: ['AC-1', 'AC-2'], selectedScope: ['AC-1', 'AC-2'], observedControls: ['AC-1'], missingControls: ['AC-2'],
  outOfScopeControls: [], excludedControls: [], duplicateControls: [],
  evidence: [{ id: 'evidence-a', name: 'Configuration observation', contentHash: 'evidence-hash', downloadUrl: null }],
  findings: [{ findingId: 'finding-a', controlId: 'AC-1', controlFamily: 'AC', title: 'Access observation', description: 'Requires investigation.',
    severity: 'High', status: 'Open', resourceType: 'Resource', resourceId: '/subscriptions/example/resources/a',
    remediationGuidance: 'Review the configured access.', discoveredAt: '2026-09-28T12:00:00Z', deviationId: null, deviationType: null }],
  errors: [], history: [],
  permissions: { canReview: true, reviewReason: null, canReconcile: true, reconcileReason: null, canRemediate: false, canRequestDeviation: false },
};
export const report: AssessmentReport = { id: 'sar-a', title: 'Draft security assessment report', status: 'Draft',
  createdAt: '2026-09-28T13:00:00Z', downloadUrl: '/api/v1/systems/system-a/sar/sar-a/export',
  sections: [{ title: 'Assessment scope', content: 'Retained scope AC-1, AC-2. One control requires review.' }],
  sourceResultIds: ['assessment:run-a'], warnings: ['Coverage gap: AC-2.'] };
