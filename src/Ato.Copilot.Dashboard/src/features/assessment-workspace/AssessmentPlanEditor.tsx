import { useEffect, useRef, useState } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import SetupDialog from '../workspace-operations/SetupDialog';
import { assessmentWorkspaceError, saveAssessmentPlan, type AssessmentPlanControl, type AssessmentPlanEdit,
  type AssessmentPlanTask, type AssessmentPlanWorkspace, type AssessmentTeamMember, type RetainedAssessmentPlan } from '../../api/assessmentWorkspace';

export interface PlanEditorDraft {
  basisHash: string; basisRevision: number; title: string; leadId: string;
  scopeNotes: string; approach: string; rules: string; start: string; end: string;
  controls: AssessmentPlanControl[]; team: AssessmentTeamMember[];
}
function initialDraft(plan: RetainedAssessmentPlan): PlanEditorDraft {
  return { basisHash: plan.contentHash, basisRevision: plan.revision, title: plan.title,
    leadId: plan.assessmentLeadId ?? '', scopeNotes: plan.scopeNotes ?? '', approach: plan.assessmentApproach ?? '',
    rules: plan.rulesOfEngagement ?? '', start: plan.scheduleStart?.slice(0, 10) ?? '', end: plan.scheduleEnd?.slice(0, 10) ?? '',
    controls: plan.controls.map(c => ({ ...c, methods: [...c.methods] })), team: plan.teamMembers.map(member => ({ ...member })) };
}
const titles: Record<AssessmentPlanTask, string> = { title: 'Assessment plan title', lead: 'Assessment lead',
  scope: 'Assessment scope', approach: 'Assessment approach', team: 'Assessment team', schedule: 'Assessment schedule', procedures: 'Control assessment procedures' };

export default function AssessmentPlanEditor({ systemId, workspace, task, restored, onDraft, onClose, onSaved, onReload }: {
  systemId: string; workspace: AssessmentPlanWorkspace & { plan: RetainedAssessmentPlan }; task: AssessmentPlanTask;
  restored?: PlanEditorDraft; onDraft: (draft: PlanEditorDraft | null) => void; onClose: () => void;
  onSaved: (value: AssessmentPlanWorkspace) => void; onReload: () => void;
}) {
  const [values, setValues] = useState(() => restored ?? initialDraft(workspace.plan));
  const [original] = useState(() => JSON.stringify(initialDraft(workspace.plan)));
  const [search, setSearch] = useState('');
  const [controlId, setControlId] = useState(workspace.plan.controls[0]?.controlId ?? '');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const dirty = JSON.stringify(values) !== original;
  const allowed = workspace.permissions.canEditPlan && workspace.plan.status === 'Draft';
  useEffect(() => { onDraft(dirty ? values : null); }, [values, dirty, onDraft]);
  useEffect(() => {
    if (!dirty) return;
    const beforeUnload = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ''; };
    window.addEventListener('beforeunload', beforeUnload);
    return () => window.removeEventListener('beforeunload', beforeUnload);
  }, [dirty]);
  const close = () => {
    if (busy) return;
    if (dirty && !window.confirm('Discard these unsaved planning changes? The saved plan will not change.')) return;
    onDraft(null); onClose();
  };
  const save = async () => {
    if (!allowed || busy) { setError(workspace.permissions.editReason ?? 'Editing this plan is not authorized.'); return; }
    const input: AssessmentPlanEdit = { task, expectedContentHash: values.basisHash, expectedRevision: values.basisRevision };
    if (task === 'title') {
      if (!values.title.trim()) { setError('A plan title is required.'); return; }
      input.title = values.title.trim();
    } else if (task === 'lead') input.assessmentLeadId = values.leadId || null;
    else if (task === 'scope') {
      if (values.controls.some(c => !c.included && !c.exclusionRationale?.trim())) {
        setError('Explain each excluded control before saving the scope.'); return;
      }
      input.scopeNotes = values.scopeNotes;
      input.includedControlIds = values.controls.filter(c => c.included).map(c => c.controlId);
      input.exclusionReasons = Object.fromEntries(values.controls.filter(c => !c.included).map(c => [c.controlId, c.exclusionRationale ?? '']));
    } else if (task === 'approach') {
      input.assessmentApproach = values.approach; input.rulesOfEngagement = values.rules;
    } else if (task === 'team') {
      if (values.team.some(member => !member.name.trim() || !member.organization.trim() || !member.role.trim())) {
        setError('Each team member needs a name, organization and planning role.'); return;
      }
      input.teamMembers = values.team;
    } else if (task === 'schedule') {
      if (values.start && values.end && values.end < values.start) { setError('The end date cannot precede the start date.'); return; }
      input.scheduleStart = values.start ? `${values.start}T00:00:00Z` : null;
      input.scheduleEnd = values.end ? `${values.end}T00:00:00Z` : null;
    } else input.methodOverrides = values.controls.map(c => ({ controlId: c.controlId, methods: c.methods, rationale: c.methodRationale }));
    setBusy(true); setError(null);
    try {
      const saved = await saveAssessmentPlan(systemId, workspace.plan.id, input);
      if (!mounted.current) return;
      onDraft(null); onSaved(saved);
    } catch (reason) { if (mounted.current) setError(assessmentWorkspaceError(reason)); }
    finally { if (mounted.current) setBusy(false); }
  };
  const field = (key: 'title' | 'leadId' | 'scopeNotes' | 'approach' | 'rules' | 'start' | 'end', value: string) => setValues(previous => ({ ...previous, [key]: value }));
  const selected = values.controls.find(c => c.controlId === controlId);
  const updateControl = (id: string, fields: Partial<AssessmentPlanControl>) =>
    setValues(previous => ({ ...previous, controls: previous.controls.map(c => c.controlId === id ? { ...c, ...fields } : c) }));

  return <SetupDialog placement="right" title={titles[task]} description={`${workspace.systemName} · Saved revision ${values.basisRevision}`}
    busy={busy} onClose={close}>
    <form className="aw-dialog aw-editor" onSubmit={event => { event.preventDefault(); void save(); }}>
      <div className="aw-editor-main">
        {!allowed && <p className="aw-warning">{workspace.permissions.editReason ?? 'Finalized plans are read-only. Start a new draft revision to propose changes.'}</p>}
        {error && <div className="aw-error" role="alert"><p>{error}</p><p>Your edits are retained. Reloading will discard them.</p>
          <button type="button" disabled={busy} onClick={() => {
            if (!dirty || window.confirm('Discard edits and reload the saved plan?')) { onDraft(null); onReload(); }
          }}>Reload saved plan</button></div>}
        <fieldset disabled={busy || !allowed} className="aw-fields">
          {task === 'title' && <label>Assessment title<input autoFocus maxLength={500} value={values.title} onChange={e => field('title', e.target.value)} /></label>}
          {task === 'lead' && <>
            <h3>Who will coordinate the assessment?</h3>
            <label>Assessment lead<select autoFocus value={values.leadId} onChange={e => field('leadId', e.target.value)}>
              <option value="">Not selected</option>{workspace.leadOptions.map(person => <option key={person.id} value={person.id}>
                {person.name}{person.organization ? ` · ${person.organization}` : ''} · {person.kind}
              </option>)}</select></label>
            {!workspace.leadOptions.length && <p>No authorized named lead records are available. Review the system team first.</p>}
            {workspace.plan.assessmentLead && !workspace.plan.assessmentLeadId && <p>Previously recorded lead: {workspace.plan.assessmentLead} (not linked to a named record).</p>}
            <p className="aw-muted">This records the assessment lead only. It does not assign authorization roles or change the assessment team.</p>
            <Link to={`/systems/${systemId}/roles`}>Review team &amp; permissions</Link>
          </>}
          {task === 'scope' && <>
            <h3>What will this assessment cover?</h3>
            <p>Choose from this plan's retained control entries. Scope notes describe the selection; they do not replace it.</p>
            <label>Scope notes<textarea rows={4} maxLength={4000} value={values.scopeNotes} onChange={e => field('scopeNotes', e.target.value)} /></label>
            <small className="aw-counter">{values.scopeNotes.length}/4000</small>
            <label>Find a planned control<input value={search} onChange={e => setSearch(e.target.value)} /></label>
            <p>{values.controls.filter(c => c.included).length} included · {values.controls.filter(c => !c.included).length} excluded</p>
            <div className="aw-control-choices">{values.controls.filter(c => `${c.controlId} ${c.title}`.toLowerCase().includes(search.toLowerCase())).map(control =>
              <article key={control.controlId}><label className="aw-check"><input type="checkbox"
                aria-label={`Include ${control.controlId} ${control.title}`} checked={control.included}
                onChange={e => updateControl(control.controlId, { included: e.target.checked })} />
                <span><strong>{control.controlId}</strong> {control.title}</span></label>
                {!control.included && <label>Exclusion reason for {control.controlId}<textarea rows={2}
                  value={control.exclusionRationale ?? ''} onChange={e => updateControl(control.controlId, { exclusionRationale: e.target.value })} /></label>}
              </article>)}</div>
          </>}
          {task === 'approach' && <>
            <h3>How will the team assess this system?</h3>
            <label>Approach and procedures<textarea autoFocus rows={7} maxLength={4000} value={values.approach}
              placeholder="Describe the assessment methods, procedures, and who performs them."
              onChange={e => field('approach', e.target.value)} /></label>
            <small className="aw-counter">{values.approach.length}/4000</small>
            <details className="aw-disclosure" open><summary>Writing guidance</summary>
              <p>Consider: examine records, interview responsible staff, and test relevant controls. Describe responsibilities and how observations will be reviewed.</p>
            </details>
            <details className="aw-disclosure"><summary>Rules of engagement</summary>
              <label>Constraints and engagement rules<textarea rows={4} maxLength={4000} value={values.rules} onChange={e => field('rules', e.target.value)} /></label>
              <small className="aw-counter">{values.rules.length}/4000</small>
            </details>
            <details className="aw-disclosure"><summary>Related assessment records</summary>
              <p>{workspace.plan.scopeCount} retained control entries are in scope. Per-control methods and objectives are available in the procedures editor.</p>
            </details>
          </>}
          {task === 'team' && <>
            <h3>Who will perform the planned work?</h3>
            <p>These are planning assignments, not system authorization grants.</p>
            {values.team.map((member, index) => <section className="aw-team-member" key={index}>
              {(['name', 'organization', 'role', 'contactInfo'] as const).map(key => <label key={key}>
                {key === 'contactInfo' ? 'Contact information' : key === 'role' ? 'Planning role' : key.charAt(0).toUpperCase() + key.slice(1)}
                <input maxLength={key === 'contactInfo' ? 500 : key === 'role' ? 50 : 200} value={member[key] ?? ''}
                  onChange={e => setValues(previous => ({ ...previous, team: previous.team.map((m, i) => i === index ? { ...m, [key]: e.target.value } : m) }))} />
              </label>)}
              <button type="button" onClick={() => setValues(previous => ({ ...previous, team: previous.team.filter((_, i) => i !== index) }))}>Remove team member</button>
            </section>)}
            <button type="button" onClick={() => setValues(previous => ({ ...previous,
              team: [...previous.team, { name: '', organization: '', role: 'Assessor', contactInfo: null }] }))}>Add team member</button>
          </>}
          {task === 'schedule' && <>
            <h3>When will the assessment occur?</h3>
            <label>Assessment start<input type="date" value={values.start} onChange={e => field('start', e.target.value)} /></label>
            <label>Assessment end<input type="date" value={values.end} onChange={e => field('end', e.target.value)} /></label>
          </>}
          {task === 'procedures' && <>
            <label>Control procedure<select value={controlId} onChange={e => setControlId(e.target.value)}>
              {values.controls.map(control => <option key={control.controlId} value={control.controlId}>{control.controlId} · {control.title}</option>)}
            </select></label>
            {selected && <>
              <h3>Assessment methods</h3>
              {['Examine', 'Interview', 'Test'].map(method => <label className="aw-check" key={method}>
                <input type="checkbox" checked={selected.methods.includes(method)} onChange={e => updateControl(selected.controlId,
                  { methods: e.target.checked ? [...selected.methods, method] : selected.methods.filter(m => m !== method) })} />{method}
              </label>)}
              <label>Method rationale<textarea rows={3} value={selected.methodRationale ?? ''}
                onChange={e => updateControl(selected.controlId, { methodRationale: e.target.value })} /></label>
              <details className="aw-disclosure" open><summary>Retained assessment objectives</summary>
                {selected.objectives.length ? <ul>{selected.objectives.map((objective, index) => <li key={index}>{objective}</li>)}</ul>
                  : <p>No assessment objectives were retained for this control. Review the source catalog; none are invented here.</p>}
              </details>
            </>}
          </>}
        </fieldset>
      </div>
      <div className="aw-info">After saving, return to the checklist and preview the updated retained draft.</div>
      <footer className="aw-dialog-footer"><small>Saving updates the draft. It does not finalize the plan.</small>
        <button type="button" disabled={busy} onClick={close}>Cancel</button>
        <button type="submit" className="aw-primary" disabled={busy || !allowed}>{busy ? 'Saving…' : 'Save draft'}</button></footer>
    </form>
  </SetupDialog>;
}
