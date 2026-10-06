export const rmfJourney = [
  { phase: 'Prepare', purpose: 'Establish the mission, system scope, responsibilities and documentation sources.',
    work: 'Review system design, inventory and team responsibilities.', path: 'profile/MissionAndPurpose', documents: 'System Security Plan and boundary records' },
  { phase: 'Categorize', purpose: 'Describe the information processed and its confidentiality, integrity and availability impact.',
    work: 'Review recorded information types and categorization rationale.', path: 'categorization', documents: 'Categorization and System Security Plan' },
  { phase: 'Select', purpose: 'Select and tailor the authoritative baseline, parameters and applicable provider responsibilities.',
    work: 'Review baseline applicability and source-backed responsibility decisions.', path: 'baseline', documents: 'Baseline, tailoring and responsibility records' },
  { phase: 'Implement', purpose: 'Document what this system actually implements and the evidence supporting each requirement.',
    work: 'Map source requirements and review implementation responses.', path: 'narratives', documents: 'System Security Plan and evidence index' },
  { phase: 'Assess', purpose: 'Plan and record assessments using actual evidence, methods and human determinations.',
    work: 'Review assessment scope, procedures, results and unresolved findings.', path: 'assessments?tab=plan', documents: 'Assessment plan, assessment report and POA&M' },
  { phase: 'Authorize', purpose: 'Prepare the reviewed package and documented risk response for the responsible authorization authority.',
    work: 'Review package preparation, handoff records and recorded decisions separately.', path: 'documents?purpose=InitialSubmission', documents: 'Authorization package and recorded decision' },
  { phase: 'Monitor', purpose: 'Maintain evidence and documentation against the reviewed baseline throughout the lifecycle.',
    work: 'Review monitoring scope, fresh evaluations, changes and assigned follow-up.', path: 'conmon', documents: 'Continuous monitoring documentation and package updates' },
] as const;
export type OverviewPhase = typeof rmfJourney[number]['phase'];
export function overviewPhase(value: string | null | undefined): OverviewPhase | null {
  return rmfJourney.find(item => item.phase === value)?.phase ?? null;
}
export function overviewDate(value: string | null | undefined) {
  return value && Number.isFinite(Date.parse(value)) ? new Date(value).toLocaleString() : 'Not recorded';
}
