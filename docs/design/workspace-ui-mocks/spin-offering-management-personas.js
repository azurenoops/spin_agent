'use strict';
(() => {
  const KEY = 'spin.offering-personas.v1', SCHEMA = 1;
  const $ = selector => document.querySelector(selector);
  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' }[c]));
  const now = () => new Date().toISOString();
  const dateAfter = days => { const d = new Date(); d.setDate(d.getDate() + days); return d.toISOString().slice(0,10); };
  const clone = value => JSON.parse(JSON.stringify(value));
  const fingerprint = value => {
    let hash = 2166136261;
    for (const c of JSON.stringify(value)) { hash ^= c.charCodeAt(0); hash = Math.imul(hash, 16777619); }
    return `SIM-${(hash >>> 0).toString(16).padStart(8,'0')}`;
  };
  const users = {
    ssm:{ name:'Blair · SSM', subject:'demo-blair', role:'SSM', offerings:['azure','collab'], systems:['shared','collab-system'], defaults:'overview', scope:'Two assigned offerings; provider boundaries; customer summaries only' },
    isso:{ name:'Dana · ISSO', subject:'demo-dana', role:'ISSO', offerings:['azure'], systems:['shared'], defaults:'controls', scope:'Azure documentation, accepted sources and scoped impact coordination' },
    engineer:{ name:'Alex · Engineer', subject:'demo-alex', role:'Engineer', offerings:['azure'], systems:['shared'], defaults:'work', scope:'Assigned Azure facts; delegated review of Ellis’s unrelated operational facts only; no customer evidence or other offering' },
    owner:{ name:'Ellis · Owner', subject:'demo-ellis', role:'Owner', offerings:['azure'], systems:['shared'], defaults:'operations', scope:'Azure service operations and consented customer dependency summaries' },
    sca:{ name:'Casey · SCA', subject:'demo-casey', role:'SCA', offerings:['azure'], systems:['shared'], defaults:'posture', scope:'Shared platform assessment plan, exact approved sources and findings' },
    ao:{ name:'Finley · AO', subject:'demo-finley', role:'AO', offerings:['azure'], systems:['shared'], defaults:'coverage', scope:'Shared platform decision package only; no customer decision authority' },
    admin:{ name:'Gray · Portal administrator', subject:'demo-gray', role:'Portal administrator', offerings:['azure','collab'], systems:[], defaults:'admin', scope:'Organization, portfolio, invitation and limited access setup only' }
  };
  const sections = [['overview','Overview'],['work','Work deliverables'],['environments','Customer environments'],['posture','Security posture'],['controls','Controls & evidence'],['changes','Changes remediation'],['coverage','Authorization coverage packages'],['operations','Lifecycle operations'],['history','Activity history']];
  function seed() {
    return {
      schema:SCHEMA, revision:0, saved:now(),
      portfolios:['Flank Speed'],
      offerings:[
        { id:'azure', name:'Flank Speed Azure', portfolio:'Flank Speed', cloud:'Microsoft', stage:'InService', publication:'Example catalog release 1', owner:'Ellis', links:['shared','alpha-system','bravo-system'] },
        { id:'collab', name:'Flank Speed Collaboration', portfolio:'Flank Speed', cloud:'Microsoft', stage:'Onboarding', publication:'Draft catalog', owner:'Separate sample owner', links:['shared','collab-system'] }
      ],
      systems:[
        { id:'shared', name:'Shared Platform RMF System', boundary:'Provider platform authorization boundary', custody:'PEO Digital provider custody', decision:'Historical simulated ATO', decisionDate:dateAfter(-120), expiry:dateAfter(80) },
        { id:'alpha-system', name:'Fleet Logistics RMF System', boundary:'Fleet Logistics mission boundary', custody:'Navy Fleet Logistics · independent customer', decision:'Customer decision not shared', expiry:null },
        { id:'bravo-system', name:'Training Mission RMF System', boundary:'Training mission boundary', custody:'Navy Training Command · independent customer', decision:'Not recorded / unresolved', expiry:null },
        { id:'collab-system', name:'Collaboration RMF System', boundary:'Collaboration boundary', custody:'PEO Digital provider custody', decision:'No decision recorded', expiry:null }
      ],
      organizations:[
        { id:'fleet', name:'Navy Fleet Logistics', owner:'Morgan · Mission Owner', grant:'Summary only', active:true },
        { id:'training', name:'Navy Training Command', owner:'Robin · Mission Owner', grant:'Summary only', active:true }
      ],
      environments:[
        {id:'e1', org:'fleet', name:'Fleet production landing zone', subscription:'Fleet mixed-resource subscription', directory:'Fleet cloud directory', appTenant:'Fleet application tenant', resource:'Shared log collector', relationship:'Covered provider component', system:'shared', health:'Finding', responsibility:'Provider logging; customer workload configuration', source:'Simulated diagnostic observation', observed:now()},
        {id:'e2', org:'fleet', name:'Fleet production landing zone', subscription:'Fleet mixed-resource subscription', directory:'Fleet cloud directory', appTenant:'Fleet application tenant', resource:'Logistics application', relationship:'Separate customer boundary', system:'alpha-system', health:'Customer-only finding', responsibility:'Customer application logging', source:'Consented finding summary only', observed:now()},
        {id:'e3', org:'fleet', name:'Fleet production landing zone', subscription:'Fleet mixed-resource subscription', directory:'Fleet cloud directory', appTenant:'Fleet application tenant', resource:'Research sandbox', relationship:'Uncovered', system:null, health:'Permission missing', responsibility:'Customer scope owner review required', source:'No read grant', observed:null},
        {id:'e4', org:'training', name:'Training mission environment', subscription:'Training shared subscription', directory:'Training cloud directory', appTenant:'Training application tenant', resource:'Training workload', relationship:'Unresolved', system:'bravo-system', health:'Unknown', responsibility:'Owner must resolve overlap', source:'No monitoring connection', observed:null},
        {id:'e5', org:'training', name:'Training mission environment', subscription:'Training shared subscription', directory:'Training cloud directory', appTenant:'Training application tenant', resource:'Shared network gateway', relationship:'Shared dependency', system:'shared', health:'Shared-service review', responsibility:'Provider network / customer route responsibility', source:'Simulated change summary', observed:now()},
        {id:'e6', org:'fleet', name:'Fleet isolated analytics', subscription:'Fleet mixed-resource subscription', directory:'Fleet cloud directory', appTenant:'Fleet application tenant', resource:'Isolated analytics enclave', relationship:'Separate customer boundary', system:'alpha-system', health:'Stale', responsibility:'Customer-held isolated scope', source:'Old summary; no fresh observations', observed:dateAfter(-12)},
        {id:'e7', org:'training', name:'Training mission environment', subscription:'Training shared subscription', directory:'Training cloud directory', appTenant:'Training application tenant', resource:'Legacy reporting service', relationship:'Uncovered', system:null, health:'Unknown', responsibility:'Customer transition owner', source:'Manual inventory only', observed:null},
        {id:'e8', org:'fleet', name:'Fleet experimental environment with deliberately long descriptive names for responsive layout verification', subscription:'Fleet mixed-resource subscription', directory:'Fleet cloud directory', appTenant:'Fleet application tenant', resource:'Unreconciled resource discovery candidate', relationship:'Unresolved', system:null, health:'Potential offering impact', responsibility:'Human provider/customer scope review', source:'Simulated discovery candidate', observed:now()}
      ],
      works:[
        {id:'logging', offering:'azure', system:'shared', title:'Logging implementation and retention proof', requirement:'AU-2 / AU-12 · baseline 1 · mandatory change-package requirement', mandatory:true, status:'Draft', owner:null, reviewer:'ssm', due:null, priority:'High', criteria:'Document routing, retention and a verifiable end-to-end sample event.', dependencies:['Owner resourcing obligation (blocks decision, not fact collection)'], facts:'', evidence:'', submissions:[], reviews:[], comments:[], accepted:null, blocked:null, revision:0},
        {id:'retirement', offering:'azure', system:'shared', title:'Optional retirement communications outline', requirement:'Optional operational planning; not an AO gate', mandatory:false, status:'Draft', owner:null, reviewer:'ssm', due:null, priority:'Low', criteria:'Name affected customers and retained evidence responsibilities.', dependencies:[], facts:'', evidence:'', submissions:[], reviews:[], comments:[], accepted:null, blocked:null, revision:0},
        {id:'peer-review', offering:'azure', system:'shared', title:'Independent peer operational fact review', requirement:'Optional operational fact check; delegated review only; not a control assessment', mandatory:false, status:'Submitted', owner:'owner', reviewer:'engineer', due:dateAfter(5), priority:'Low', criteria:'Confirm that the named operations contact and evidence reference match the submitted facts.', dependencies:[], facts:'Ellis recorded the sample gateway operations contact and an operational handoff reference.', evidence:'SIM owner operational-handoff-v1.txt', submissions:[{version:1,author:'demo-ellis',at:now(),facts:'Ellis recorded the sample gateway operations contact and an operational handoff reference.',evidence:'SIM owner operational-handoff-v1.txt',evidenceVersion:1,criteria:'Confirm named operations contact and evidence reference.',system:'shared',baseline:'Reviewed baseline 1',hash:fingerprint({author:'demo-ellis',version:1,evidence:'SIM owner operational-handoff-v1.txt'})}], reviews:[], comments:[], accepted:null, blocked:null, revision:0}
      ],
      document:{revision:0, state:'Not staged', author:null, approvedBy:null, pins:[], history:[]},
      plan:{status:'Draft', version:1, control:'AU-12', scope:'Shared log collector · reviewed baseline 1', methods:'Examine routing and retention evidence; test end-to-end sample events.'},
      assessments:[],
      findings:[
        {id:'observation', title:'Required logging route is not evidenced', status:'Open', source:'SIM diagnostic observation · Shared log collector', owner:'ssm', impact:'Potential offering impact', rationale:'Needs human review; authorization unchanged', poam:'Not yet linked'},
      ],
      operation:{resolved:false, reason:'', evidence:'', revision:0},
      activities:[],
      triggers:[],
      packages:[],
      decisions:[],
      invitations:[],
      roles:[],
      events:[{id:'initial', at:now(), actor:'Sample seed', action:'SIMULATED baseline and historical decision loaded', offering:'azure', system:'shared', category:'baseline', work:null, detail:'Recorded authorization is separate from current posture and package readiness.'}]
    };
  }
  let state, storageFault = '', persona='ssm', offering=null, section='overview', landing='overview';
  let offeringSearch='';
  let filter={search:'',org:'',relationship:'',health:'',page:1}, workFilter='', workSearch='', historySearch='';
  let dirty=false, opener=null, dialogMode=null, dialogTarget=null;
  try {
    const raw=localStorage.getItem(KEY);
    state=raw ? JSON.parse(raw) : seed();
    if (state.schema!==SCHEMA || !Array.isArray(state.works) || !Array.isArray(state.offerings) || !Array.isArray(state.events) || !Array.isArray(state.packages) || !Number.isInteger(state.revision)) throw new Error('Invalid schema or records');
    if (!raw) localStorage.setItem(KEY,JSON.stringify(state));
  } catch(error) {
    storageFault=`Local demo storage unavailable or corrupt: ${error.message}. Writes are disabled. Use Reset demo data to explicitly replace the example namespace.`;
    state=seed();
  }
  const user=() => users[persona];
  const workBy=id => state.works.find(w=>w.id===id);
  const sysBy=id => state.systems.find(s=>s.id===id);
  const offerBy=id => state.offerings.find(o=>o.id===id);
  const latest=w => w?.submissions?.at(-1);
  const accepted=w => w.submissions.find(s=>s.version===w.accepted);
  const canReadWork=w => !!w && user().offerings.includes(w.offering) && user().systems.includes(w.system) && persona!=='admin' && (persona!=='engineer' || w.owner==='engineer' || (w.id==='peer-review'&&w.reviewer==='engineer')) && (!['sca','ao'].includes(persona)||w.mandatory);
  const scopedWorks=()=>state.works.filter(canReadWork).filter(w=>!offering || w.offering===offering);
  const customerSummaries=()=>['ssm','isso','owner'].includes(persona);
  const visibleEnvs=()=>customerSummaries() && offering==='azure' ? state.environments.filter(e=>state.organizations.find(o=>o.id===e.org)?.active) : [];
  const consentedSystem=id=>customerSummaries()&&state.environments.some(e=>e.system===id&&state.organizations.find(o=>o.id===e.org)?.active);
  const sourceSignature=s => fingerprint({doc:s.document, assessment:s.assessments.at(-1)??null, operation:s.operation, work:s.works[0].accepted, workStatus:s.works[0].status, plan:s.plan});
  function readiness(s=state) {
    const w=s.works.find(w=>w.id==='logging'), pin=s.document.pins.at(-1), result=s.assessments.at(-1);
    return [
      {name:'Mandatory logging deliverable', pass:w.status==='Accepted' && !!w.accepted, reason:'Current logging requirement must be independently accepted.', owner:'Blair · SSM', tab:'work'},
      {name:'Approved implementation source', pass:s.document.state==='Approved' && pin?.version===w.accepted && pin?.work==='logging', reason:'ISSO stages the accepted exact version; SSM independently approves.', owner:'Dana · ISSO / Blair · reviewer', tab:'controls'},
      {name:'Independent fresh assessment', pass:result?.result==='Satisfied' && result?.documentRevision===s.document.revision && result?.version===w.accepted, reason:'SCA must reassess corrected evidence; accepted work is not a passing control.', owner:'Casey · SCA', tab:'posture'},
      {name:'Finding / POA&M disposition', pass:s.findings.every(f=>f.status==='Verified closed'), reason:'This sample policy requires verified closure, not a percent or implicit risk acceptance.', owner:'Casey · SCA', tab:'changes'},
      {name:'Operational dependency', pass:s.operation.resolved, reason:'Owner must record resource availability and supporting evidence.', owner:'Ellis · Owner', tab:'operations'}
    ];
  }
  function permission(action,p={}) {
    if (action==='reset') return null;
    const w=p.work ? workBy(p.work) : null;
    if (p.work && (!w || !canReadWork(w))) return 'Denied: work is outside the active identity’s assigned scope.';
    const targetOffering=w?.offering || p.offering || offering || 'azure';
    if (!user().offerings.includes(targetOffering)) return 'Denied: unassigned offering.';
    if (w && p.offering && p.offering!==w.offering) return 'Denied: offering/record mismatch.';
    const adminActions=['addOrganization','addPortfolio','invite','redeem','revokeInvite','grantRole','revokeRole','revokeSummary'];
    if (adminActions.includes(action)) {
      if(persona!=='admin')return 'Denied: portal setup administration required.';
      if(action==='grantRole' && (!['Engineer','Owner'].includes(p.role) || p.subject==='demo-gray'))return 'Denied: admin cannot self-grant or assign protected security/AO roles.';
      return null;
    }
    if (persona==='admin') return 'Denied: portal administrator has no substantive security, assessment, risk or AO authority.';
    if (p.system && (!user().systems.includes(p.system) || (w && w.system!==p.system))) return 'Denied: system/custody outside assigned scope.';
    if (!w && p.system && p.system!=='shared' && ['incorporate','approveDocument','finalizePlan','assess','resolveOperation','lifecycle','impact','trigger','refresh'].includes(action)) return 'Denied: this operation targets the Shared Platform system only.';
    if (p.environment && !visibleEnvs().some(e=>e.id===p.environment)) return 'Denied: no consented customer summary grant.';
    if (p.customer && !customerSummaries()) return 'Denied: customer source is not shared with this identity.';
    if (p.customer && !state.organizations.some(o=>o.id===p.customer&&o.active)) return 'Denied: no active consent for the requested customer.';
    if (p.customer && !['readPrivateEvidence','refresh'].includes(action)) return 'Denied: provider actions cannot mutate another customer’s records through summary consent.';
    if (action==='readPrivateEvidence') return 'Denied: summaries do not grant customer private evidence.';
    if (['assign','reopen','cancel','block','unblock'].includes(action)) return ['ssm','isso'].includes(persona) ? null : 'Denied: scoped work coordinator required.';
    if (['saveFacts','start','submit'].includes(action)) return w?.owner===persona && persona==='engineer' ? null : 'Denied: assigned author only.';
    if (['review','return','accept'].includes(action)) {
      if (latest(w)?.author===user().subject) return 'Denied: author cannot review or accept their own implementation.';
      const delegatedPeer=persona==='engineer'&&w?.id==='peer-review';
      if (!w || w.reviewer!==persona || (!['ssm','isso'].includes(persona)&&!delegatedPeer)) return 'Denied: designated independent reviewer required.';
      return null;
    }
    if (action==='comment') return canReadWork(w) ? null : 'Denied: assigned work scope required.';
    if (action==='incorporate') return persona==='isso' && targetOffering==='azure' ? null : 'Denied: scoped documentation author required.';
    if (action==='approveDocument') return persona==='ssm' && state.document.author!==user().subject && targetOffering==='azure' ? null : 'Denied: independent document reviewer required.';
    if (['finalizePlan','assess'].includes(action)) {
      const lw=workBy('logging'), sub=accepted(lw), review=lw.reviews.find(r=>r.version===lw.accepted && r.result==='Accepted');
      if (sub?.author===user().subject || review?.actor===user().subject || state.document.author===user().subject) return 'Denied: assessor must differ from implementation author and acceptor.';
      if (persona!=='sca' || targetOffering!=='azure') return 'Denied: scoped independent SCA required.';
      return null;
    }
    if (['resolveOperation','lifecycle'].includes(action)) return persona==='owner' && targetOffering==='azure' ? null : 'Denied: assigned operational owner required.';
    if (['impact','trigger','refresh'].includes(action)) return ['ssm','isso'].includes(persona) && targetOffering==='azure' ? null : 'Denied: scoped oversight coordinator required.';
    if (action==='preparePackage') return ['ssm','isso'].includes(persona) && targetOffering==='azure' && (!p.system || p.system==='shared') ? null : 'Denied: exact provider system/package coordinator required.';
    if (['reviewPackage','clarify','authorize'].includes(action)) return persona==='ao' && targetOffering==='azure' && p.system==='shared' ? null : 'Denied: designated AO for this exact system required.';
    return 'Denied: unsupported action; no cloud mutation, risk bypass or implicit grants.';
  }
  function event(s,action,p,detail,category='work') {
    const w=p.work?s.works.find(x=>x.id===p.work):null;
    s.events.push({id:`event-${s.events.length+1}`, at:now(),actor:user().name,subject:user().subject,action,offering:w?.offering||p.offering||offering||'azure',system:w?.system||p.system||null,work:w?.id||null,category,detail});
  }
  function persist(next,reset=false) {
    if (storageFault && !reset) throw new Error(storageFault);
    if (!reset) {
      const stored=JSON.parse(localStorage.getItem(KEY));
      if (stored?.revision!==state.revision) throw new Error('Another tab changed this demo. Reload before saving; input is retained.');
    }
    next.revision=state.revision+1; next.saved=now();
    localStorage.setItem(KEY,JSON.stringify(next));
    state=next; storageFault='';
  }
  function required(value,label,min=3) { if (!String(value??'').trim() || String(value).trim().length<min) throw new Error(`${label} is required (at least ${min} characters).`); return String(value).trim(); }
  function futureDate(value,label='Due date') { if (!/^\d{4}-\d{2}-\d{2}$/.test(value??'') || Number.isNaN(Date.parse(value)) || value<dateAfter(0)) throw new Error(`${label} must be a valid date today or later.`); return value; }
  function dispatch(action,p={}) {
    const deny=permission(action,p);
    if (deny) {
      try {const next=clone(state);event(next,'Denied local action',{},`${action}: ${deny}`,'denial');persist(next);} catch(error) { storageFault=error.message; }
      return {ok:false,error:deny};
    }
    try {
      const next=action==='reset'?seed():clone(state);
      const w=p.work?next.works.find(x=>x.id===p.work):null;
      const targetOffering=w?.offering||p.offering||offering||'azure';
      if (w && p.revision!==undefined && p.revision!==w.revision) throw new Error('Stale work revision. Reopen the record; no version was changed.');
      let detail='Local example updated', category='work';
      switch(action) {
        case 'reset': detail='Explicitly reset the isolated demo namespace';category='setup';break;
        case 'assign':
          if (!['Draft','Assigned'].includes(w.status)) throw new Error('Assignment is only editable before work starts.');
          if (p.owner!=='engineer' || p.reviewer!=='ssm') throw new Error('This example requires Alex as assigned author and Blair as independent reviewer.');
          w.owner=p.owner;w.reviewer=p.reviewer;w.due=futureDate(p.due);w.priority=required(p.priority,'Priority');w.criteria=required(p.criteria,'Acceptance criteria',12);w.status='Assigned';
          detail=`${w.title} assigned to Alex; reviewer Blair; due ${w.due}`;break;
        case 'start':
          if (!['Assigned','Returned','Reopened'].includes(w.status)) throw new Error('Start requires assigned, returned or reopened work.');
          if (w.blocked) throw new Error(`Blocked: ${w.blocked}`);
          w.status='InProgress';detail=`Started ${w.title}`;break;
        case 'saveFacts':
          if (w.status!=='InProgress') throw new Error('Start work before editing facts.');
          w.facts=String(p.facts??'').trim();w.evidence=String(p.evidence??'').trim();detail='Saved local draft facts; pinned submissions unchanged';break;
        case 'submit': {
          if (w.status!=='InProgress' || w.blocked) throw new Error('Submission requires unblocked InProgress work.');
          w.facts=required(p.facts,'Implementation facts',15);w.evidence=required(p.evidence,'Evidence filename / metadata',5);
          const version=w.submissions.length+1;
          const sub={version,author:user().subject,at:now(),facts:w.facts,evidence:w.evidence,evidenceVersion:version,criteria:w.criteria,system:w.system,baseline:'Reviewed baseline 1',hash:fingerprint({version,facts:w.facts,evidence:w.evidence,author:user().subject})};
          w.submissions.push(sub);w.status='Submitted';detail=`Submitted immutable v${version} · ${sub.hash}`;break;
        }
        case 'review':
          if (w.status!=='Submitted') throw new Error('Claim only a submitted version.');
          w.status='InReview';detail=`Claimed review of v${latest(w).version}`;break;
        case 'return':
        case 'accept':
          if (w.status!=='InReview' || w.blocked) throw new Error('A claimed, unblocked review is required.');
          if (Number(p.version)!==latest(w).version) throw new Error('Stale submission: exact current version required.');
          if (action==='accept' && p.criteriaConfirmed!==true) throw new Error('Confirm the acceptance criteria against this exact version.');
          w.reviews.push({version:latest(w).version,result:action==='accept'?'Accepted':'Returned',actor:user().subject,reason:required(p.reason,action==='accept'?'Review rationale':'Return reason',8),at:now(),hash:latest(w).hash});
          w.status=action==='accept'?'Accepted':'Returned';
          if (action==='accept') w.accepted=latest(w).version;
          detail=`${w.status} exact v${latest(w).version}; no assessment or authorization side effect`;break;
        case 'reopen':
          if (w.status!=='Accepted') throw new Error('Only accepted work can be reopened.');
          required(p.reason,'Reopen reason',8);w.status='Reopened';w.blocked=null;
          detail=`Reopened for corrective proof: ${p.reason}; accepted v${w.accepted} preserved`;break;
        case 'cancel':
          if (['Accepted','Cancelled'].includes(w.status)) throw new Error('Cancel only active work; accepted history must be reopened explicitly.');
          w.status='Cancelled';w.blocked=null;detail=`Cancelled: ${required(p.reason,'Cancellation / requirement disposition',8)}; mandatory requirements remain unmet`;break;
        case 'block':
          if (['Accepted','Cancelled','Draft'].includes(w.status)) throw new Error('Block an active assignment, not terminal or draft work.');
          w.blocked=required(p.reason,'Blocker / dependency reason',8);detail=`Blocked overlay on ${w.status}: ${w.blocked}`;break;
        case 'unblock':
          if (!w.blocked) throw new Error('No blocker recorded.');
          required(p.reason,'Resolution reason',8);w.blocked=null;detail=`Blocker cleared; retained ${w.status}`;break;
        case 'comment':
          w.comments.push({actor:user().name,text:required(p.reason,'Comment'),at:now()});detail='Added scoped work comment';break;
        case 'incorporate': {
          const lw=next.works.find(x=>x.id==='logging'), sub=accepted(lw);
          if (lw.status!=='Accepted' || !sub) throw new Error('Only current accepted exact material may be staged.');
          if (Number(p.version)!==sub.version) throw new Error('Stale incorporation source.');
          const d=next.document;
          if (d.pins.at(-1)?.version===sub.version) throw new Error('This accepted version is already incorporated.');
          if (d.revision) d.history.push({revision:d.revision,state:d.state,author:d.author,approvedBy:d.approvedBy,pins:clone(d.pins)});
          d.revision++;d.state='Staged';d.author=user().subject;d.approvedBy=null;
          d.pins=[{work:lw.id,version:sub.version,evidence:sub.evidence,hash:sub.hash,facts:sub.facts,scope:'Shared Platform RMF System'}];
          detail=`ISSO staged document ${d.revision}, exact logging v${sub.version}; independent approval pending`;category='document';break;
        }
        case 'approveDocument':
          if (next.document.state!=='Staged') throw new Error('A staged document requires independent review.');
          if (Number(p.version)!==next.document.revision) throw new Error('Stale document revision.');
          next.document.state='Approved';next.document.approvedBy=user().subject;
          detail=`Approved document ${next.document.revision} / source v${next.document.pins.at(-1).version}; not a control pass`;category='document';break;
        case 'finalizePlan':
          if (next.plan.status!=='Draft') throw new Error('Plan already finalized.');
          next.plan.methods=required(p.methods,'Assessment methods',15);next.plan.status='Finalized';next.plan.actor=user().subject;next.plan.at=now();
          detail='Finalized AU-12 plan v1 on reviewed exact scope';category='assessment';break;
        case 'assess': {
          const lw=next.works.find(x=>x.id==='logging'), d=next.document, pin=d.pins.at(-1), prior=next.assessments.at(-1);
          if (next.plan.status!=='Finalized' || d.state!=='Approved' || lw.status!=='Accepted' || pin?.version!==lw.accepted) throw new Error('Finalized plan and current independently approved source are required.');
          if (!['Failed','Satisfied'].includes(p.result)) throw new Error('Select an explicit determination.');
          if (!prior && p.result!=='Failed') throw new Error('Connected sample begins with a failed retention test; record the finding first.');
          if (prior && prior.version===pin.version) throw new Error('Reassessment requires a fresh corrected source version.');
          const a={version:pin.version,documentRevision:d.revision,planVersion:next.plan.version,result:p.result,rationale:required(p.reason,'Independent test findings',15),proof:required(p.proof,'Fresh test proof',8),actor:user().subject,at:now(),hash:pin.hash};
          next.assessments.push(a);
          if (p.result==='Failed') next.findings.push({id:`assessment-${next.assessments.length}`,title:'AU-12 retention test failed',status:'Reassessment required',source:`Independent SCA result / source v${pin.version}`,owner:'ssm',impact:'Shared-service review',rationale:a.rationale,poam:'SIM POA&M · logging correction'});
          else next.findings.forEach(f=>{f.status='Verified closed';f.verifiedBy=user().subject;f.verificationVersion=pin.version;f.verifiedAt=now();});
          detail=`SCA ${p.result} · exact v${pin.version}; ${p.result==='Failed'?'finding and reassessment required':'independently verified finding closure'}`;category='assessment';break;
        }
        case 'resolveOperation':
          next.operation={resolved:true,reason:required(p.reason,'Owner rationale',12),evidence:required(p.evidence,'Operational evidence',8),revision:next.operation.revision+1,actor:user().subject,at:now()};
          detail='Owner resolved resource dependency with proof; authorization unchanged';category='operations';break;
        case 'lifecycle':
          if (!['Change review','Renewal preparation','Retirement plan'].includes(p.kind)) throw new Error('Choose a lifecycle activity.');
          next.activities.push({kind:p.kind,reason:required(p.reason,'Activity / affected dependencies',15),date:futureDate(p.due,'Review date'),actor:user().subject,at:now(),status:'Staged for independent review',scope:'Azure only; shared boundary and other offering retained'});
          if (p.kind==='Retirement plan') next.offerings.find(o=>o.id==='azure').stage='RetirementPlanned';
          detail=`Staged ${p.kind}; no cloud decommission, AO change or shared-boundary disposal`;category='operations';break;
        case 'impact': {
          const f=next.findings.find(x=>x.id===p.finding);
          if (!f || !['Customer-only','Shared-service review','Potential offering impact','Undetermined'].includes(p.impact)) throw new Error('Select a finding and supported human impact classification.');
          f.impact=p.impact;f.rationale=required(p.reason,'Human impact rationale',12);f.owner=required(p.owner,'Accountable owner');f.poam=p.poam?'SIM POA&M · logging correction':f.poam;
          detail=`Human impact review: ${f.impact}; owner ${f.owner}; no authorization change`;category='impact';break;
        }
        case 'refresh':
          next.environments.filter(e=>next.organizations.find(o=>o.id===e.org)?.active && e.observed && e.health!=='Stale').forEach(e=>{e.observed=now();});
          detail='Simulated read-only observation timestamps refreshed; missing permissions, stale and unknown sources remain explicit';category='observation';break;
        case 'trigger':
          if (!['Shared log collector','Shared network gateway'].includes(p.resource)) throw new Error('Trigger must stay within reviewed provider resources.');
          if (!['Hourly','Daily'].includes(p.frequency)) throw new Error('Choose evaluation frequency.');
          next.triggers.push({source:required(p.source,'Data source'),resource:p.resource,condition:required(p.condition,'Condition',10),severity:required(p.severity,'Severity'),owner:required(p.owner,'Owner'),frequency:p.frequency,followup:required(p.followup,'Follow-up action',10),status:'SIM configured; connectivity unverified',at:now()});
          detail='Recorded exact-resource ConMon trigger; no live connection or cATO assertion';category='observation';break;
        case 'preparePackage': {
          if (readiness(next).some(r=>!r.pass)) throw new Error('Purpose readiness is blocked. Follow the named work, source, assessment and owner links.');
          if (!['Change','Renewal','Initial'].includes(p.purpose)) throw new Error('Select package purpose.');
          const pkg={id:`package-${next.packages.length+1}`,system:'shared',purpose:p.purpose,status:'Prepared',version:next.packages.length+1,signature:sourceSignature(next),pins:clone(next.document.pins),documentRevision:next.document.revision,assessment:clone(next.assessments.at(-1)),operation:clone(next.operation),risks:'No residual risk asserted accepted. Verified findings retained in POA&M history.',conditions:'Maintain logging verification; review source changes.',at:now(),actor:user().subject};
          pkg.hash=fingerprint(pkg);next.packages.push(pkg);detail=`Prepared exact ${p.purpose} package ${pkg.version} · ${pkg.hash}; not submitted or authorized`;category='package';break;
        }
        case 'reviewPackage':
        case 'clarify':
        case 'authorize': {
          const pkg=next.packages.find(x=>x.id===p.package);
          if (!pkg || pkg.system!==p.system) throw new Error('Exact assigned package/system is required.');
          if (pkg.signature!==sourceSignature(next) || readiness(next).some(r=>!r.pass)) throw new Error('Package sources are stale or readiness is blocked; prepare a fresh package.');
          if (p.hash!==pkg.hash) throw new Error('Package pin mismatch; reopen exact package.');
          if (action==='reviewPackage') {
            if (pkg.status!=='Prepared') throw new Error('Review a prepared package.');
            pkg.status='In AO review';pkg.reviewer=user().subject;detail=`AO reviewed exact package ${pkg.hash}`;
          } else if(action==='clarify') {
            if (!['Prepared','In AO review'].includes(pkg.status)) throw new Error('This package is no longer reviewable.');
            pkg.status='Returned for clarification';pkg.clarification=required(p.reason,'Clarification reason',12);detail=`AO returned exact package ${pkg.hash}; SSM/ISSO follow-up required`;
          } else {
            if (pkg.status!=='In AO review') throw new Error('Review the exact package before recording a decision.');
            const rationale=required(p.reason,'Decision rationale',15), conditions=required(p.conditions,'Decision conditions',12), expiry=futureDate(p.expiry,'Expiration');
            if (expiry<=dateAfter(0)) throw new Error('Expiration must be after today.');
            if (p.decision!=='ATO') throw new Error('Choose the explicit simulated ATO decision.');
            const decision={system:pkg.system,package:pkg.id,hash:pkg.hash,kind:'SIMULATED ATO',rationale,conditions,expiry,actor:user().subject,at:now()};
            next.decisions.push(decision);pkg.status='SIM decision recorded';
            const system=next.systems.find(x=>x.id===pkg.system);system.decision=decision.kind;system.expiry=expiry;system.decisionDate=now();
            detail=`SIMULATED ATO on ${system.name} only; customer and other boundary decisions unchanged`;
          }
          category='decision';break;
        }
        case 'addOrganization':
          next.organizations.push({id:`org-${next.organizations.length+1}`,name:required(p.name,'Customer organization'),owner:required(p.owner,'Mission Owner'),grant:'No summary consent',active:false});
          detail='Created local customer setup draft; no customer consent or system authority';category='setup';break;
        case 'addPortfolio':
          if (next.portfolios.includes(p.name)) throw new Error('Portfolio already exists.');
          next.portfolios.push(required(p.name,'Portfolio name'));detail='Added local portfolio; no offering or system grants';category='setup';break;
        case 'invite':
          if (!/^[^@\s]+@[^@\s]+\.invalid$/.test(p.email??'')) throw new Error('Use a simulated .invalid email address.');
          if (!['Engineer','Owner'].includes(p.role)) throw new Error('Only limited Engineer/Owner example invitations are supported. AO/security roles require independent approved policy.');
          if (!next.organizations.some(o=>o.id===p.customer)) throw new Error('Choose an existing sample customer.');
          next.invitations.push({id:`invite-${next.invitations.length+1}`,email:p.email,role:p.role,customer:p.customer,status:'Pending',subject:`example-subject-${next.invitations.length+1}`,expires:futureDate(p.due,'Invitation expiry')});
          detail='Created simulated identity-bound invitation; no email sent';category='setup';break;
        case 'redeem':
        case 'revokeInvite': {
          const invite=next.invitations.find(i=>i.id===p.invitation);
          if (!invite || invite.status!=='Pending') throw new Error('Only a pending, unused invitation can change.');
          if (action==='redeem') {
            if (p.subject!==invite.subject) throw new Error('Wrong sample subject; email alone is not identity.');
            if(invite.expires<dateAfter(0)) throw new Error('Invitation expired.');
            invite.status='Redeemed (SIM identity verified)';
          } else invite.status='Revoked';
          detail=`Invitation ${invite.status}; no live membership or portal authority`;category='setup';break;
        }
        case 'grantRole':
          if (!['Engineer','Owner'].includes(p.role) || p.subject==='demo-gray' || !p.subject?.endsWith('.invalid')) throw new Error('Cannot self-grant or grant protected security/AO roles; use another simulated .invalid subject.');
          if (!next.organizations.some(o=>o.id===p.customer)) throw new Error('Customer setup scope required.');
          next.roles.push({id:`role-${next.roles.length+1}`,subject:p.subject,role:p.role,customer:p.customer,offering:targetOffering,status:'Approved example limited grant'});
          detail='Recorded limited scoped example grant to another identity; fixed demo users unchanged';category='setup';break;
        case 'revokeRole': {
          const role=next.roles.find(r=>r.id===p.roleId);
          if (!role) throw new Error('Unknown limited role record.');
          role.status='Revoked';detail='Revoked example limited role; history retained';category='setup';break;
        }
        case 'revokeSummary': {
          const org=next.organizations.find(o=>o.id===p.customer);
          if (!org || !org.active) throw new Error('No active sample consented summary grant.');
          org.active=false;org.grant='Revoked summary consent';detail='Revoked local oversight summary; later views hide customer resources';category='setup';break;
        }
        default:throw new Error('Unsupported demo operation.');
      }
      if(w) w.revision++;
      event(next,action,p,detail,category);persist(next,action==='reset');
      return {ok:true,message:detail};
    } catch(error) {return {ok:false,error:`Not saved: ${error.message}`};}
  }
  const badge=(text,type='neutral')=>`<span class="badge ${type}">${esc(text)}</span>`;
  const btn=(label,action,data={},primary=false)=>`<button ${primary?'class="primary"':''} data-action="${esc(action)}" ${Object.entries(data).map(([k,v])=>`data-${k}="${esc(v)}"`).join(' ')}>${esc(label)}</button>`;
  const dl=rows=>`<dl>${rows.map(([a,b])=>`<dt>${esc(a)}</dt><dd>${esc(b)}</dd>`).join('')}</dl>`;
  const empty=message=>`<p class="empty">${esc(message)}</p>`;
  const approvedSource=()=>state.document.pins.at(-1);
  const overdue=w=>w.due && w.due<dateAfter(0) && !['Accepted','Cancelled'].includes(w.status);
  function workRow(w) {
    return `<div class="row"><div><h3>${btn(w.title,'work',{id:w.id})}</h3><p class="subtle">${esc(w.requirement)}</p>${badge(w.status,w.status==='Accepted'?'good':w.status==='Returned'?'bad':'neutral')} ${w.blocked?badge('Blocked','warning'):''} ${overdue(w)?badge('Overdue','bad'):''}
      <small>Owner: ${esc(users[w.owner]?.name||'Unassigned')} · reviewer ${esc(users[w.reviewer]?.name||'Unassigned')} · ${w.due?'sample due '+esc(w.due):'No date assigned'}</small></div><div>${w.accepted?badge(`Accepted v${w.accepted}`,'good'):badge(w.mandatory?'Mandatory':'Optional')}${latest(w)?`<small>Latest source v${latest(w).version}</small>`:''}</div></div>`;
  }
  function render() {
    $('#persona').innerHTML=Object.entries(users).map(([id,u])=>`<option value="${id}" ${id===persona?'selected':''}>${esc(u.name)}</option>`).join('');
    $('#workspace-role').textContent=`Provider workspace · ${user().role} · sample identity`;
    $('#storage-error').hidden=!storageFault;$('#storage-error').textContent=storageFault;
    for(const link of document.querySelectorAll('.rail-link')){link.classList.remove('active');link.removeAttribute('aria-current');}
    const active=!offering?(landing==='queue'?(persona==='admin'?'administration':'queue'):landing==='offerings'?'offerings':'home'):({environments:'missions',changes:'changes',history:null}[section]||'offerings');
    if(active){$('#'+active).classList.add('active');$('#'+active).setAttribute('aria-current','page');}
    if (!offering) renderLanding();
    else renderWorkspace();
  }
  function renderLanding() {
    if(landing==='overview'){renderPersonalOverview();return;}
    const queue=landing==='queue';
    $('#main').innerHTML=`<p class="breadcrumbs">Provider / ${queue?persona==='admin'?'Administration':'My work':'Offerings'}</p>
      <div class="heading"><div><p class="eyebrow">Provider operations</p><h1>${queue?persona==='admin'?'Provider administration':'My persona queue':'My Offerings'}</h1><p>Choose a service to manage its scope, operations and assigned work.</p></div></div>
      <section class="card choose-offering"><h2>Choose an offering to view its overview</h2><p>Select an offering name in Service offerings below. Tasks apply only to your assigned offering, not the whole provider.</p></section>
      <div class="landing-columns"><section aria-label="Assigned offerings" class="card offering-register"><div class="register-head"><h2>Service offerings</h2><span class="subtle">My Offerings · sample records</span></div>
        <label class="offering-search">Search assigned offerings<input id="offering-search" type="search" value="${esc(offeringSearch)}" placeholder="Offering or portfolio"></label>
        <table><thead><tr><th>Offering</th><th>Catalog release</th><th>Recorded operating stage</th><th><span class="sr-only">Actions</span></th></tr></thead><tbody>${state.offerings.filter(o=>user().offerings.includes(o.id)&&`${o.name} ${o.portfolio}`.toLowerCase().includes(offeringSearch.toLowerCase())).map(o=>`<tr><td data-label="Offering"><button class="offering-name" data-action="openOffering" data-id="${esc(o.id)}">${esc(o.name)}</button><small>${esc(o.portfolio)} portfolio · ${esc(o.cloud)} cloud</small></td><td data-label="Catalog release">${esc(o.publication)}<small>Publication is not authorization</small></td><td data-label="Recorded operating stage">${esc(o.stage)}<small>Simulated service-management fact</small></td><td data-label="Actions"><button data-action="openOffering" data-id="${esc(o.id)}" aria-label="Open ${esc(o.name)}">Open offering</button></td></tr>`).join('')}</tbody></table>
        ${state.offerings.some(o=>user().offerings.includes(o.id)&&`${o.name} ${o.portfolio}`.toLowerCase().includes(offeringSearch.toLowerCase()))?'':empty('No assigned offerings match this search.')}
      </section><aside class="task-support" aria-label="Current persona task"><h2>${persona==='admin'?'Setup and access':persona==='ao'?'Assigned decision queue':persona==='sca'?'Assigned assessment queue':persona==='engineer'?'Assigned technical work':persona==='owner'?'Operational obligations':'Your next scoped task'}</h2>
        <p>${persona==='admin'?'Administration manages setup and limited access, not security review or authorization.':persona==='engineer'?'Only assigned deliverables appear. Ask the demo SSM to assign logging work first.':persona==='ao'?'Recorded decisions and current package readiness stay separate. Review only exact, ready package versions.':persona==='sca'?'Finalize the plan and independently assess approved implementation versions.':persona==='owner'?'Resolve the resource dependency and review ongoing service changes.':'Logging proof is missing. Coordinate a named owner, deadline and independent review.'}</p><p class="subtle">${esc(user().name)} · ${esc(user().scope)}</p>${persona==='ssm'?'<small>SSM = ISSM-backed demo alias; no new stored role.</small>':''}
      </aside></div>
      ${queue?(persona==='admin'?adminPanel():`<section class="card persona-work"><h2>My scoped work</h2>${persona==='engineer'?scopedWorks().map(workRow).join(''):btn('Open Flank Speed Azure','openOffering',{id:'azure'})}</section>`):''}`;
    $('#offering-search').addEventListener('input',event=>{offeringSearch=event.target.value;const position=event.target.selectionStart;renderLanding();$('#offering-search').focus();if(position!==null)$('#offering-search').setSelectionRange(position,position);});
  }
  function renderPersonalOverview() {
    const work=scopedWorks();
    const pending=work.filter(w=>['Submitted','InReview','Returned'].includes(w.status)||w.blocked||overdue(w));
    const visible=persona==='admin'?[]:(persona==='engineer'?work:work.filter(w=>
      pending.includes(w)||w.mandatory&&!['Accepted','Cancelled'].includes(w.status)));
    const heading={ssm:'Review gaps and returned work',isso:'Coordinate documentation and evidence',engineer:'Complete your assigned technical work',owner:'Resolve operational blockers',sca:'Review your assessment plan',ao:'Inspect decision-package readiness',admin:'Manage setup and access'}[persona];
    const target={ssm:'work',isso:'controls',engineer:'work',owner:'operations',sca:'posture',ao:'coverage',admin:'overview'}[persona];
    const explanation={ssm:'Assign missing proof, review submitted versions and track dependencies across your assigned offerings.',isso:'Incorporate accepted material and keep documentation changes separate from assessment results.',engineer:work.length?'Respond to your recorded assignments and reviewer comments.':'No technical work is assigned yet. The demo SSM can assign the logging deliverable.',owner:state.operation.resolved?'The resource dependency is resolved. Review ongoing service obligations.':'The resource dependency still needs your facts, evidence and rationale.',sca:state.assessments.length?'Review retained assessment results and reassess corrected evidence independently.':'An independent assessment needs a finalized plan and approved implementation evidence.',ao:state.packages.length?'Inspect the exact prepared package; authorization still requires your explicit decision.':'No decision package has been prepared. Readiness blockers remain visible in the offering.',admin:'Manage organizations, invitations and limited access. Administration does not grant security review or AO authority.'}[persona];
    $('#main').innerHTML=`<p class="breadcrumbs">Provider / Overview</p>
      <div class="heading"><div><p class="eyebrow">Provider operations</p><h1>Your provider workspace</h1><p>${esc(user().name)} · What needs attention within your assigned scope.</p></div>${btn('Browse offerings','offerings')}</div>
      <div class="landing-columns"><div>
        <section class="card"><h2>What needs your attention?</h2><div class="row"><div><h3>${esc(heading)}</h3><p>${esc(explanation)}</p></div>${persona==='admin'?btn('Open administration','administration',{},true):btn('Review next action','openSection',{id:'azure',section:target},true)}</div>
          <p class="subtle">This is your personal operational view—not the offering catalog or an authorization readiness percentage.</p></section>
        <section class="card" aria-label="Personal work summary"><div class="register-head"><h2>${persona==='ao'?'Decision handoffs':persona==='sca'?'Assessment handoffs':persona==='admin'?'Administrative work':'Work requiring attention'}</h2>${btn('View my work','queue')}</div>
          ${persona==='admin'?`<p>${state.invitations.length} sample invitations recorded. Access requests and substantive security actions stay separate.</p>`:persona==='ao'?`<p>${state.packages.length} prepared package versions · ${state.decisions.length} explicit simulated decisions.</p>`:persona==='sca'?`<p>Assessment plan: ${esc(state.plan.status)} · ${state.assessments.length} retained evaluations. Work acceptance does not satisfy a control.</p>`:visible.map(workRow).join('')||empty('No work currently requires your attention.')}</section>
        <section class="card" aria-label="Offering shortcuts"><h2>Assigned offering shortcuts</h2><p class="subtle">Open a service for its own Overview. The full searchable register is under Offerings.</p>${state.offerings.filter(o=>user().offerings.includes(o.id)).slice(0,2).map(o=>`<div class="row"><div><h3>${esc(o.name)}</h3><small>${esc(o.portfolio)} portfolio</small></div><button data-action="openOffering" data-id="${esc(o.id)}" aria-label="Open ${esc(o.name)}">Open offering</button></div>`).join('')}</section>
      </div><aside class="task-support" aria-label="Current persona task"><h2>Your active scope</h2><p>${esc(user().scope)}</p><p class="subtle">PEO Digital provider · Flank Speed portfolio</p><p>Recorded authorization, current posture and work completion remain separate in every offering.</p></aside></div>`;
  }
  function renderWorkspace() {
    const o=offerBy(offering);
    if(!o || !user().offerings.includes(offering)){offering=null;renderLanding();return;}
    for(const link of document.querySelectorAll('.rail-link')){link.classList.remove('active');link.removeAttribute('aria-current');}
    const active=({environments:'missions',changes:'changes'}[section]||'offerings');
    $('#'+active).classList.add('active');$('#'+active).setAttribute('aria-current','page');
    $('#main').innerHTML=`<p class="breadcrumbs">${btn('My Offerings','offerings')} / PEO Digital / ${esc(o.portfolio)}</p>
      <div class="heading"><div><p class="eyebrow">Shared offering workspace · Microsoft underlying cloud</p><h1>${esc(o.name)}</h1><p>${esc(user().name)} · ${esc(user().scope)}</p></div><div class="stack">${badge('SIMULATED')}${badge(o.stage)}</div></div>
      <div class="tabs" role="tablist" aria-label="Offering sections">${sections.map(([id,label])=>`<button role="tab" id="tab-${id}" aria-controls="panel" aria-selected="${id===section}" tabindex="${id===section?0:-1}" data-action="tab" data-id="${id}">${esc(label)}</button>`).join('')}</div>
      <section id="panel" role="tabpanel" aria-labelledby="tab-${section}">${panel()}</section>`;
    const selectedTab=$('#tab-'+section),strip=selectedTab.parentElement;
    const tabBounds=selectedTab.getBoundingClientRect(),stripBounds=strip.getBoundingClientRect();
    if(tabBounds.left<stripBounds.left||tabBounds.right>stripBounds.right)strip.scrollLeft+=tabBounds.left-stripBounds.left;
  }
  function panel() {
    if(persona==='admin') return `<section class="card"><h2>Setup scope only</h2><p>Substantive ${esc(sections.find(s=>s[0]===section)?.[1])} records are unavailable to portal administration. Setup and independently approved limited access remain below.</p></section>${section==='history'?historyPanel():adminPanel()}`;
    if(offering!=='azure')return `<section class="card"><h2>${esc(sections.find(s=>s[0]===section)?.[1])} · collaboration</h2><p>Separately assigned offering. It shares the provider platform boundary but does not inherit another boundary's decision. No assigned work or telemetry is recorded for this example.</p>${btn('Inspect shared boundary link','boundary',{id:'shared'})}${btn('Inspect separate collaboration boundary','boundary',{id:'collab-system'})}</section>`;
    switch(section) {
      case 'overview':return overviewPanel();
      case 'work':return workPanel();
      case 'environments':return environmentPanel();
      case 'posture':return posturePanel();
      case 'controls':return controlsPanel();
      case 'changes':return changesPanel();
      case 'coverage':return coveragePanel();
      case 'operations':return operationsPanel();
      case 'history':return historyPanel();
      default:return '';
    }
  }
  function overviewPanel() {
    if(persona==='engineer')return `<section class="card hero"><h2>Your assigned facts and evidence</h2><p>No blanket customer, assessment or decision visibility is granted.</p>${scopedWorks().map(workRow).join('')||empty('No assignments yet. Switch to SSM and assign the logging gap.')}</section>`;
    return `<div class="split"><section class="card hero"><p class="eyebrow">Current purpose · change package readiness</p><h2>${readiness().every(r=>r.pass)?'Ready to prepare a scoped package':'Logging proof and operational dependency need attention'}</h2><p>The simulated logging observation identifies a real gap in this example baseline. Recorded authorization is unchanged.</p>
      ${persona==='ssm'||persona==='isso'?btn('Assign logging deliverable','work',{id:'logging'},true):btn('Open your persona task','tab',{id:user().defaults},true)}
      <div class="actions">${btn('Review current posture','tab',{id:'posture'})}${btn('Inspect boundary coverage','tab',{id:'coverage'})}</div></section>
      <section class="card accent"><p class="eyebrow">Recorded authorization · exact system</p><h2>${esc(sysBy('shared').decision)}</h2><p>Shared Platform RMF System only · sample expiration ${esc(sysBy('shared').expiry)}</p><small>Not a current posture, readiness or cATO certification.</small></section></div>
      <div class="grid"><section class="card"><p class="eyebrow">Operating stage</p><div class="metric">${esc(offerBy('azure').stage)}</div><p>Recorded service fact. ${state.activities.length} staged management activities.</p>${btn('Owner obligations','tab',{id:'operations'})}</section>
      <section class="card"><p class="eyebrow">Work handoffs</p><div class="metric">${state.works.filter(w=>w.status==='Accepted').length} accepted</div><p>Versioned acceptance only; independent documentation and assessment still apply.</p>${btn('Open deliverables','tab',{id:'work'})}</section>
      <section class="card"><p class="eyebrow">Current posture</p><div class="metric">${state.findings.filter(f=>f.status!=='Verified closed').length} open / reassessment</div><p>Unavailable or stale customer telemetry stays unknown, not healthy.</p>${btn('Findings and source freshness','tab',{id:'posture'})}</section></div>
      ${readinessPanel()}<section class="card"><h2>Named scope and outcomes</h2>${dl([['Provider','PEO Digital'],['Portfolio','Flank Speed'],['Offering','Flank Speed Azure'],['Cloud provider','Microsoft'],['Customer custody',customerSummaries()?state.organizations.filter(o=>o.active).map(o=>o.name).join(' / ')||'No active summary grants':'Independent customer custody; summaries not granted to this identity'],['Authorization','Recorded separately on each linked RMF system']])}</section>`;
  }
  function readinessPanel() {
    return `<section class="card"><h2>Purpose-specific readiness · change package</h2><p class="subtle">SIM rule set 1 · no percentage or implicit risk acceptance. Required blockers link to accountable work.</p>${readiness().map(r=>`<div class="row"><div><strong>${esc(r.name)}</strong><small>${esc(r.reason)} Owner: ${esc(r.owner)}</small></div><div>${badge(r.pass?'Passed':'Blocking',r.pass?'good':'warning')} ${btn('Open '+r.name,'tab',{id:r.tab})}</div></div>`).join('')}</section>`;
  }
  function workPanel() {
    const list=scopedWorks().filter(w=>(!workFilter||w.status===workFilter)&&(`${w.title} ${w.criteria}`.toLowerCase().includes(workSearch.toLowerCase())));
    return `<section class="card"><h2>Work deliverables</h2><p>Assignment → frozen submission → independent exact-version acceptance. Blocked is an overlay; overdue is computed, not a replacement status.</p>
      <div class="filters"><label>Search work<input id="work-search" value="${esc(workSearch)}" placeholder="Title or acceptance criteria"></label><label>Work status<select id="work-filter" aria-label="Work status"><option value="">All permitted work</option>${['Draft','Assigned','InProgress','Submitted','InReview','Returned','Accepted','Reopened','Cancelled'].map(x=>`<option ${x===workFilter?'selected':''}>${x}</option>`).join('')}</select></label></div>
      <div id="work-results">${list.map(workRow).join('')||empty('No work matches your scope and filters.')}</div></section>`;
  }
  function environmentPanel() {
    if(!customerSummaries())return `<section class="card"><h2>Customer environments · restricted</h2><p>This identity has no cross-customer summary consent. Assignment or provider membership does not expose private customer evidence.</p>${btn('Inspect your permitted provider boundary','boundary',{id:'shared'})}</section>`;
    let envs=visibleEnvs().filter(e=>(!filter.org||e.org===filter.org)&&(!filter.relationship||e.relationship===filter.relationship)&&(!filter.health||e.health===filter.health)&&`${e.name} ${e.resource} ${e.subscription}`.toLowerCase().includes(filter.search.toLowerCase()));
    const pages=Math.max(1,Math.ceil(envs.length/6));filter.page=Math.min(filter.page,pages);
    return `<section class="card"><div class="heading"><div><h2>Customer environments</h2><p>Consented summary-only projections. Subscription ≠ authorization boundary. Cloud directory ≠ application tenant.</p></div>${['ssm','isso'].includes(persona)?btn('Refresh simulated read-only observations','refresh'):''}</div>
      <div class="filters"><label>Search inventory<input id="env-search" value="${esc(filter.search)}" placeholder="Resource, environment or subscription"></label><label>Customer organization<select id="env-org" aria-label="Customer organization"><option value="">All consented customers</option>${state.organizations.filter(o=>o.active).map(o=>`<option value="${o.id}" ${filter.org===o.id?'selected':''}>${esc(o.name)}</option>`).join('')}</select></label>
      <label>Relationship<select id="env-relationship" aria-label="Relationship"><option value="">All relationships</option>${[...new Set(state.environments.map(e=>e.relationship))].map(v=>`<option ${v===filter.relationship?'selected':''}>${esc(v)}</option>`).join('')}</select></label>
      <label>Collection health<select id="env-health" aria-label="Collection health"><option value="">All health states</option>${[...new Set(state.environments.map(e=>e.health))].map(v=>`<option ${v===filter.health?'selected':''}>${esc(v)}</option>`).join('')}</select></label></div>
      ${envs.length?`<table><thead><tr><th>Resource / customer</th><th>Subscription / relationship</th><th>System / collection health</th><th>Action</th></tr></thead><tbody>${envs.slice((filter.page-1)*6,filter.page*6).map(e=>`<tr><td data-label="Resource"><strong>${esc(e.resource)}</strong><small>${esc(state.organizations.find(o=>o.id===e.org).name)}</small></td><td data-label="Relationship">${esc(e.relationship)}<small>${esc(e.subscription)}</small></td><td data-label="System">${esc(sysBy(e.system)?.name||'Unresolved / no boundary')}<small>${esc(e.health)}</small></td><td>${btn('Inspect '+e.resource,'environment',{id:e.id})}</td></tr>`).join('')}</tbody></table>`:empty('No consented resource summaries match. Missing data is not healthy.')}
      <div class="pager"><span id="inventory-count">${envs.length} resources · page ${filter.page} of ${pages}</span><div class="actions">${btn('Previous resources','envPage',{delta:-1})}${btn('Next resources','envPage',{delta:1})}${btn('Clear inventory filters','clearEnvs')}</div></div></section>`;
  }
  function posturePanel() {
    if(persona==='engineer')return `<section class="card"><h2>Assignment-scoped finding context</h2><p>Logging facts support AU-12 on the Shared Platform boundary. Control determinations and customer telemetry are not granted.</p>${scopedWorks().map(workRow).join('')||empty('No scoped technical assignments.')}</section>`;
    return `<div class="split"><section class="card"><h2>Current security posture</h2><p>Source-qualified findings, not recorded authorization.</p>${state.findings.map(f=>`<div class="row"><div><h3>${esc(f.title)}</h3><p class="subtle">${esc(f.source)}</p>${badge(f.status,f.status==='Verified closed'?'good':'warning')}<small>${esc(f.impact)} · ${esc(f.poam)}</small></div>${btn('Inspect finding','finding',{id:f.id})}</div>`).join('')}</section>
      <section class="card"><h2>Assigned assessment plan</h2>${badge(state.plan.status)}${dl([['Control',state.plan.control],['Scope',state.plan.scope],['Methods',state.plan.methods],['Independence','Alex author / Blair acceptor / Casey assessor']])}
      ${persona==='sca'?btn(state.plan.status==='Draft'?'Finalize assessment plan':'Determine control / reassess','assessment',{},true):btn('Inspect plan and conclusions','assessment')}</section></div>
      <section class="card"><h2>Assessment conclusions</h2>${state.assessments.length?state.assessments.map(a=>`<div class="row"><div><strong>${esc(a.result)} · source v${a.version}</strong><small>Document ${a.documentRevision} · plan ${a.planVersion} · Casey / ${esc(a.at)}</small><p>${esc(a.rationale)}</p><small>Proof: ${esc(a.proof)}</small></div>${badge(a.result,a.result==='Satisfied'?'good':'bad')}</div>`).join(''):empty('Not assessed. Deliverable acceptance does not satisfy a control.')}</section>
      <section class="card"><h2>Telemetry and continuous monitoring</h2><p>Permission missing, stale and unknown customer checks remain explicit. No monitoring connection or cATO status is asserted.</p>${['ssm','isso'].includes(persona)?btn('Define scoped ConMon trigger','trigger'):''}${state.triggers.map(t=>`<div class="row"><div><strong>${esc(t.resource)} · ${esc(t.frequency)}</strong><small>${esc(t.source)} · ${esc(t.condition)} · ${esc(t.severity)} · owner ${esc(t.owner)}</small><p>${esc(t.followup)}</p>${badge(t.status,'warning')}</div></div>`).join('')}</section>`;
  }
  function controlsPanel() {
    if(persona==='engineer')return `<section class="card"><h2>Assigned technical source pins only</h2>${scopedWorks().map(workRow).join('')||empty('No assignments.')}<p>Customer evidence and domain document approval are not granted.</p></section>`;
    const w=workBy('logging'), sub=accepted(w), d=state.document;
    return `<section class="card"><h2>Controls, responsibilities and evidence</h2><p>AU-12 · reviewed baseline 1 · provider collects platform logs; customer configures mission workload routing; shared end-to-end verification needs explicit scope. A selected capability alone is not accepted inheritance.</p>
      ${dl([['Provider duty','Shared log collector routing and retention proof'],['Customer duty','Application sources and workload settings (private evidence not shared)'],['Shared duty','Scope-qualified event continuity and interface verification'],['Adopted source','SIM baseline 1; successor work does not overwrite historical pins']])}
      ${btn('Inspect responsibility and source manifest','manifest')}</section>
      <section class="card hero"><h2>Accepted material → reviewed documentation</h2>${badge(d.state,d.state==='Approved'?'good':'warning')}<p>Current accepted logging source: ${sub?'v'+sub.version+' · '+esc(sub.hash):'None'}. Staging and independent document approval are separate from work acceptance.</p>
      <div class="actions">${persona==='isso'?btn('Incorporate accepted material','incorporation',{},true):''}${persona==='ssm'&&d.state==='Staged'?btn('Approve staged document','approveDocument',{},true):''}${btn('View pinned source manifest','manifest')}</div>
      ${d.pins.map(p=>`<div class="row"><div><strong>Document ${d.revision} · logging v${p.version}</strong><small>${esc(p.evidence)} · ${esc(p.hash)}</small><p>${esc(p.facts)}</p></div></div>`).join('')}
      <details><summary>Historical document versions (${d.history.length})</summary>${d.history.map(h=>`<p>Document ${h.revision} · ${esc(h.state)} · ${h.pins.map(p=>'logging v'+p.version+' / '+esc(p.hash)).join(', ')}</p>`).join('')||'<p>None yet.</p>'}</details></section>`;
  }
  function changesPanel() {
    if(persona==='engineer')return `<section class="card"><h2>Your corrective work</h2>${scopedWorks().map(workRow).join('')||empty('No assignments.')}<p>Manual remediation occurs outside SPIN; submit facts and fresh evidence here.</p></section>`;
    return `<section class="card"><h2>Human change impact and remediation</h2><p>Customer-only, shared-service, potential offering impact and undetermined cases are distinct. No observation automatically changes an ATO.</p>${state.findings.map(f=>`<div class="row"><div><h3>${esc(f.title)}</h3>${badge(f.impact)}<p class="subtle">${esc(f.rationale)}</p><small>Owner: ${esc(users[f.owner]?.name||f.owner)} · ${esc(f.poam)} · ${esc(f.status)}</small></div>${btn('Review impact / finding','finding',{id:f.id})}</div>`).join('')}
      <div class="actions">${['ssm','isso'].includes(persona)&&workBy('logging').status==='Accepted'?btn('Assign corrective logging proof','reopen',{id:'logging'},true):''}${btn('Open corrective deliverable','work',{id:'logging'})}${persona==='sca'?btn('Reassess fresh evidence','assessment'):''}</div></section>
      <section class="card"><h2>POA&M and verification history</h2><p>SIM logging correction links the assessment finding to the same versioned technical work. Acceptance does not close this finding. Casey's fresh, independent reassessment is required.</p>${state.findings.filter(f=>f.poam!=='Not yet linked').map(f=>`<p>${esc(f.poam)} · ${esc(f.status)}${f.verificationVersion?' · verified evidence v'+f.verificationVersion:''}</p>`).join('')||empty('No corrective POA&M link until assessment or human review.')}</section>`;
  }
  function coveragePanel() {
    if(persona==='engineer')return `<section class="card"><h2>Assigned scope, not AO authority</h2><p>Your logging deliverable is for Shared Platform RMF System only.</p>${btn('Inspect assigned boundary','boundary',{id:'shared'})}</section>`;
    return `<section class="card"><h2>Explicit authorization coverage</h2><p>Many-to-many offering links are reviewed scope relationships, not authorization. Shared Platform also supports the separately assigned Collaboration offering.</p>
      ${offerBy('azure').links.filter(id=>user().systems.includes(id)||consentedSystem(id)).map(id=>{const s=sysBy(id);return `<div class="row"><div><h3>${esc(s.name)}</h3><p class="subtle">${esc(s.boundary)} · ${esc(s.custody)}</p>${badge(s.decision,id==='shared'?'neutral':'warning')}<small>${id==='shared'?'Shared log collector and shared network gateway only; link snapshot 1':'Customer boundary ownership retained; private decision/evidence not granted'}</small></div>${btn('Inspect '+s.name,'boundary',{id})}</div>`;}).join('')}
      <p class="banner">${customerSummaries()?'Consented customer inventory shows uncovered and unresolved resources.':'Customer source details are unavailable to this identity.'} Mixed subscription attachment never expands these boundaries.</p></section>
      ${readinessPanel()}
      <section class="card"><h2>Contextual package preparation and AO queue</h2><p>Target: Shared Platform RMF System · change / renewal purpose. No customer package authority is inferred.</p>
      ${['ssm','isso'].includes(persona)?btn('Prepare authorization package','packagePrepare',{},true):''}
      ${state.packages.map(p=>`<div class="row"><div><h3>${esc(p.purpose)} package ${p.version}</h3>${badge(p.status)} ${badge(p.signature===sourceSignature(state)?'Source fresh':'Stale source',p.signature===sourceSignature(state)?'good':'bad')}
      <small>${esc(p.hash)} · logging v${p.pins.at(-1).version} · assessment ${esc(p.assessment.result)}</small>${p.clarification?`<p>AO clarification: ${esc(p.clarification)}</p>`:''}</div>${btn('Review package '+p.version,'package',{id:p.id})}</div>`).join('')||empty('No package prepared. Keep daily offering work moving; readiness is not a decision.')}
      </section><section class="card"><h2>Recorded decision history</h2><p>Historical simulated ATO · Shared Platform only · sample expiration ${esc(dateAfter(80))}. Original decision retained.</p>${state.decisions.map(d=>`<div class="row"><div><strong>${esc(d.kind)} · Shared Platform only</strong><p>${esc(d.rationale)}</p><small>Conditions: ${esc(d.conditions)} · expiration ${esc(d.expiry)} · package ${esc(d.hash)} · Finley</small></div></div>`).join('')}</section>`;
  }
  function operationsPanel() {
    if(persona==='engineer')return `<section class="card"><h2>Technical assignment dependencies</h2><p>Owner resourcing obligation blocks package readiness, not your fact collection. Only the owner may record its operational resolution.</p>${scopedWorks().map(workRow).join('')||empty('No assignments.')}</section>`;
    return `<div class="split"><section class="card hero"><h2>Resource availability obligation</h2>${badge(state.operation.resolved?'Resolved with owner proof':'Blocking · owner proof missing',state.operation.resolved?'good':'warning')}
      <p>Ellis must document logging service resourcing and the dependency on the shared gateway before this sample package can advance.</p>${state.operation.resolved?dl([['Owner rationale',state.operation.reason],['Evidence',state.operation.evidence]]):''}
      ${persona==='owner'?btn('Resolve operational dependency','operation',{},true):btn('Inspect owner dependency','operation')}</section>
      <section class="card"><h2>Operating stage and activities</h2><div class="metric">${esc(offerBy('azure').stage)}</div><p>Publication, operating stage, posture and authorization remain separate.</p>${persona==='owner'?btn('Plan lifecycle activity','lifecycle'):btn('Inspect operational history','tab',{id:'history'})}</section></div>
      <section class="card"><h2>Ongoing change, renewal and retirement</h2><p>Retirement plans name affected customers and surviving shared-boundary dependencies. No automatic decommission, customer deletion or AO withdrawal occurs. Renewal preparation does not extend the historical expiration.</p>
      ${state.activities.map(a=>`<div class="row"><div><strong>${esc(a.kind)} · ${esc(a.status)}</strong><p>${esc(a.reason)}</p><small>Review ${esc(a.date)} · ${esc(a.scope)}</small></div></div>`).join('')||empty('No staged lifecycle activities.')}
      </section>`;
  }
  function historyPanel() {
    const list=state.events.filter(e=>user().offerings.includes(e.offering)&&(!offering||e.offering===offering)&&(persona!=='engineer'||(e.work&&canReadWork(workBy(e.work)))||(e.category==='denial'&&e.subject===user().subject))&&(persona!=='admin'||['setup','denial'].includes(e.category))&&(persona!=='owner'||['operations','baseline','impact'].includes(e.category))&&`${e.actor} ${e.action} ${e.detail}`.toLowerCase().includes(historySearch.toLowerCase())).reverse();
    return `<section class="card"><h2>Activity history · scoped actor and version</h2><label>Search activity<input id="history-search" value="${esc(historySearch)}" placeholder="Actor, action or version"></label>
      <div class="timeline">${list.map(e=>`<p><strong>${esc(e.actor)} · ${esc(e.action)}</strong><small>${esc(e.at)} · ${esc(offerBy(e.offering)?.name||'Setup scope')}</small>${esc(e.detail)}</p>`).join('')||empty('No events match this identity’s scope and filter.')}</div></section>`;
  }
  function adminPanel() {
    return `<section class="card hero"><h2>Portal setup and access administration</h2><p>Gray manages setup, not security acceptance, assessment, risk or authorization. Fixed persona assignments are sample fixtures, not grants made by this panel. Only limited Engineer/Owner example grants to other identities are supported.</p>
      <div class="actions">${btn('Add sample organization','organization',{},true)}${btn('Add portfolio','portfolio')}${btn('Create local invitation','invitation')}${btn('Manage limited scoped role','roleGrant')}</div></section>
      <div class="split"><section class="card"><h2>Independent customer organizations</h2>${state.organizations.map(o=>`<div class="row"><div><strong>${esc(o.name)}</strong><small>${esc(o.owner)} · ${esc(o.grant)}</small></div>${o.active?btn('Revoke summary consent','revokeSummary',{id:o.id}):badge('No active projection','warning')}</div>`).join('')}<p class="subtle">Provider custody is not a customer. Revocation affects future summary views; no customer evidence is displayed here.</p></section>
      <section class="card"><h2>Portfolios</h2>${state.portfolios.map(p=>`<p>${esc(p)}</p>`).join('')}<h3>Protected roles</h3><p>SSM/ISSO/SCA/AO assignment requires separate approved policy. Admin cannot self-grant them or switch hidden substantive roles through this panel.</p></section></div>
      <section class="card"><h2>Simulated invitations</h2>${state.invitations.map(i=>`<div class="row"><div><strong>${esc(i.email)} · ${esc(i.role)}</strong><small>${esc(i.status)} · expires ${esc(i.expires)}</small></div>${i.status==='Pending'?`<div class="actions">${btn('Redeem example invitation','redeem',{id:i.id})}${btn('Revoke invitation','revokeInvite',{id:i.id})}</div>`:''}</div>`).join('')||empty('No invitations; no email service is connected.')}
      <h3>Limited scoped example roles</h3>${state.roles.map(r=>`<div class="row"><div><strong>${esc(r.subject)} · ${esc(r.role)}</strong><small>${esc(r.status)} · ${esc(state.organizations.find(o=>o.id===r.customer)?.name)}</small></div>${r.status!=='Revoked'?btn('Revoke limited role','revokeRole',{id:r.id}):''}</div>`).join('')||'<p>No limited grants.</p>'}</section>`;
  }
  function field(label,name,value='',type='text',help='') {
    return `<label>${esc(label)}<input aria-label="${esc(label)}" name="${esc(name)}" type="${esc(type)}" value="${esc(value)}">${help?`<small>${esc(help)}</small>`:''}</label>`;
  }
  const area=(label,name,value='',help='')=>`<label class="full">${esc(label)}<textarea aria-label="${esc(label)}" name="${esc(name)}">${esc(value)}</textarea>${help?`<small>${esc(help)}</small>`:''}</label>`;
  const select=(label,name,values,current='')=>`<label>${esc(label)}<select aria-label="${esc(label)}" name="${name}">${values.map(v=>{const [key,text]=Array.isArray(v)?v:[v,v];return `<option value="${esc(key)}" ${key===current?'selected':''}>${esc(text)}</option>`;}).join('')}</select></label>`;
  const form=(action,body,submit='Save simulated record',extra={})=>`<form data-command="${esc(action)}" ${Object.entries(extra).map(([k,v])=>`data-${k}="${esc(v)}"`).join(' ')}><div class="fields">${body}</div><div class="actions"><button type="submit" class="primary">${esc(submit)}</button><button type="button" data-action="close">Cancel</button></div></form>`;
  function openDialog(title,body,mode,target=null) {
    opener=document.activeElement;dialogMode=mode;dialogTarget=target;dirty=false;
    $('#drawer-title').textContent=title;$('#dialog-content').innerHTML=body;$('#form-error').textContent='';$('#dirty-guard').hidden=true;
    $('#drawer').showModal();const first=$('#dialog-content input, #dialog-content select, #dialog-content textarea, #dialog-content button');(first||$('#close')).focus();
  }
  function closeDialog(force=false) {
    if(dirty&&!force){$('#dirty-guard').hidden=false;$('#keep').focus();return;}
    $('#drawer').close();dirty=false;$('#dirty-guard').hidden=true;
    if(opener?.isConnected)opener.focus();else $(`#tab-${section}`)?.focus()||$('#home').focus();
  }
  function showWork(id) {
    const w=workBy(id);if(!canReadWork(w)){notify('Denied: work outside assigned scope.',true);return;}
    const sub=latest(w);
    let body=`${badge(w.status)} ${w.blocked?badge('Blocked: '+w.blocked,'warning'):''}${overdue(w)?badge('Overdue','bad'):''}
      ${dl([['Boundary',sysBy(w.system).name],['Requirement',w.requirement],['Responsible owner',users[w.owner]?.name||'Unassigned'],['Reviewer',(users[w.reviewer]?.name||'Unassigned')+' (independent)'],['Sample due',w.due||'Unassigned'],['Priority',w.priority],['Acceptance criteria',w.criteria],['Dependencies',w.dependencies.join('; ')||'None']])}
      <p class="banner">Accepted work is not a satisfied control, finding closure, risk acceptance or authorization.</p>`;
    if(['ssm','isso'].includes(persona)&&['Draft','Assigned'].includes(w.status)) {
      body+=form('assign',select('Responsible owner','owner',[['engineer','Alex · Engineer']])+select('Independent reviewer','reviewer',[['ssm','Blair · SSM']])+field('Sample due date','due',w.due||dateAfter(7),'date')+select('Priority','priority',['High','Medium','Low'],w.priority)+area('Acceptance criteria','criteria',w.criteria),'Assign deliverable',{work:w.id,revision:w.revision});
    }
    if(persona==='engineer' && ['Assigned','Returned','Reopened'].includes(w.status))body+=btn('Start work','start',{id:w.id},true);
    if(persona==='engineer' && w.status==='InProgress')body+=form('submit',area('Implementation facts','facts',w.facts,'Document observed routing, retention and event proof. Manual cloud changes happen outside this prototype.')+field('Evidence filename / simulated metadata','evidence',w.evidence)+field('Select example evidence metadata','file','','file','Filename only: bytes are never read, retained or sent.'),'Submit pinned version',{work:w.id,revision:w.revision})+btn('Save local facts draft','saveFacts',{id:w.id});
    const reviewerAllowed=permission('review',{work:w.id})===null;
    if(reviewerAllowed && w.status==='Submitted')body+=btn('Claim exact-version review','review',{id:w.id},true);
    if(reviewerAllowed && w.status==='InReview')body+=form('accept',field('Exact submission version','version',sub.version,'number')+area('Review rationale / return reason','reason','')+`<label class="full"><input name="criteriaConfirmed" type="checkbox"> I verified all criteria against this exact pinned submission</label>`,'Accept exact version',{work:w.id,revision:w.revision})+btn('Return exact version','return',{id:w.id});
    if(['ssm','isso'].includes(persona))body+=`<div class="actions">${w.status==='Accepted'?btn('Reopen for corrective evidence','reopen',{id:w.id}):!['Draft','Cancelled'].includes(w.status)?`${btn(w.blocked?'Resolve blocker':'Record blocker',w.blocked?'unblock':'block',{id:w.id})}${btn('Cancel active work','cancel',{id:w.id})}`:w.status==='Draft'?btn('Cancel draft work','cancel',{id:w.id}):''}</div>`;
    body+=`<details open><summary>Frozen submissions and reviews (${w.submissions.length})</summary>${w.submissions.map(s=>`<section class="card"><h3>Submission v${s.version} ${w.accepted===s.version?'· accepted pin':''}</h3><p>${esc(s.facts)}</p><small>Evidence v${s.evidenceVersion}: ${esc(s.evidence)} · ${esc(s.at)} · author ${esc(Object.values(users).find(u=>u.subject===s.author)?.name||'Sample subject')}</small><p class="pin">${esc(s.hash)} · criteria: ${esc(s.criteria)}</p>${w.reviews.filter(r=>r.version===s.version).map(r=>`<p><strong>${esc(r.result)}</strong> by ${esc(Object.values(users).find(u=>u.subject===r.actor)?.name||'Sample subject')} · ${esc(r.reason)}<small>${esc(r.at)}</small></p>`).join('')||'<p>Not independently reviewed.</p>'}</section>`).join('')||'<p>No submission pins yet.</p>'}</details>
      <details><summary>Comments (${w.comments.length}) and advanced technical identifiers</summary>${w.comments.map(c=>`<p>${esc(c.actor)}: ${esc(c.text)}</p>`).join('')}<p class="pin">Demo work ${esc(w.id)} · revision ${w.revision} · system ${esc(w.system)}</p></details>
      ${form('comment',area('Scoped comment','reason',''),'Add comment',{work:w.id,revision:w.revision})}`;
    openDialog(w.title,body,'work',id);
  }
  function reasonDialog(action,id) {
    const w=workBy(id);
    openDialog(action==='reopen'?'Assign corrective logging proof':`${action[0].toUpperCase()+action.slice(1)} work`,form(action,area(action==='return'?'Return reason':'Reason / dependency disposition','reason',''),'Record '+action,{work:id,revision:w.revision,version:latest(w)?.version||0}),action,id);
  }
  function showBoundary(id) {
    const s=sysBy(id), permitted=user().systems.includes(id), summary=consentedSystem(id)&&offerBy(offering)?.links.includes(id);
    if (!s||persona==='admin'||(!permitted&&!summary)){notify('Denied: boundary details outside assigned scope.',true);return;}
    openDialog(s.name,dl([['Boundary',s.boundary],['Custody',s.custody],['Relationship',id==='shared'?'Shared across Azure and Collaboration offerings':'Separate boundary; no authorization from offering association'],['Recorded decision',s.decision],['Sample expiry',s.expiry||'Not shared / not recorded'],['Coverage',id==='shared'?'Named Shared log collector / Shared gateway only':'Customer-owned reviewed scope; provider receives summaries only']])+`<p class="banner">A subscription is not this boundary. Uncovered or unresolved resources are not silently included.</p><details><summary>Advanced link identifiers</summary><p class="pin">SIM boundary ${esc(id)} · link snapshot 1 · ${esc(fingerprint(s))}</p></details>`,'boundary',id);
  }
  function showEnvironment(id) {
    const e=visibleEnvs().find(x=>x.id===id);if(!e){notify('Denied: no active customer summary grant.',true);return;}
    openDialog(e.resource,`${badge('SIM · Summary-only consent')}${dl([['Customer',state.organizations.find(o=>o.id===e.org).name],['Mission Owner',state.organizations.find(o=>o.id===e.org).owner],['Environment',e.name],['Cloud subscription',e.subscription],['Cloud directory',e.directory],['Application tenant',e.appTenant],['Boundary',sysBy(e.system)?.name||'Unresolved / uncovered'],['Relationship',e.relationship],['Responsibility',e.responsibility],['Source',e.source],['Health',e.health],['Observed',e.observed||'Unavailable; no healthy result']])}<p class="banner">Customer private files are not shared. Provider receives the named summary only. Discovery is read-only and does not reconcile boundary membership automatically.</p>${btn('Check restricted evidence access','privateEvidence',{id:e.id})}`,'environment',id);
  }
  function showFinding(id) {
    const f=state.findings.find(x=>x.id===id);if(!f||['engineer','admin'].includes(persona)){notify('Denied: finding source unavailable.',true);return;}
    let body=dl([['Source',f.source],['Status',f.status],['Impact',f.impact],['Owner',users[f.owner]?.name||f.owner],['Human rationale',f.rationale],['POA&M',f.poam],['Verification',f.verificationVersion?'SCA verified source v'+f.verificationVersion:'Not verified']])+`<p>Closure requires independent fresh SCA proof. Human classification does not mutate recorded authorization.</p>`;
    if(['ssm','isso'].includes(persona))body+=form('impact',select('Impact classification','impact',['Customer-only','Shared-service review','Potential offering impact','Undetermined'],f.impact)+select('Accountable owner','owner',[['ssm','Blair · coordination'],['owner','Ellis · operations']])+area('Human impact rationale','reason',f.rationale)+`<label><input type="checkbox" name="poam"> Link corrective POA&M / escalation review</label>`,'Record impact review',{finding:id});
    openDialog(f.title,body,'finding',id);
  }
  function showAssessment() {
    if(['engineer','admin'].includes(persona)){notify('Denied: no assessment source grant.',true);return;}
    let body=dl([['Plan',`AU-12 · version ${state.plan.version} · ${state.plan.status}`],['Scope',state.plan.scope],['Approved source',approvedSource()?'Logging v'+approvedSource().version+' · '+approvedSource().hash:'None'],['Independence','Alex / Blair / Casey are three different sample subjects']]);
    if(persona==='sca')body+=state.plan.status==='Draft'?form('finalizePlan',area('Assessment methods','methods',state.plan.methods),'Finalize scoped plan'):form('assess',select('Determination','result',['Failed','Satisfied'])+area('Independent test findings','reason','')+field('Fresh test proof','proof','','text','Explicit assessor proof, not engineer work acceptance.'),'Record independent determination');
    else body+='<p>Read-only plan context. Only the separately scoped SCA can determine effectiveness.</p>';
    openDialog('Independent assessment and reassessment',body,'assessment');
  }
  function showManifest() {
    if(['engineer','admin'].includes(persona)){notify('Denied: no document source grant.',true);return;}
    const d=state.document;
    openDialog('Pinned source manifest',`<p>SIMULATED manifest preview, not an SSP/SAP/SAR file or eMASS export.</p>${dl([['Target','Shared Platform RMF System'],['Document',`Revision ${d.revision} · ${d.state}`],['Author','Dana · ISSO'],['Reviewer',d.approvedBy?'Blair · SSM':'Independent approval pending']])}${d.pins.map(p=>`<section class="card"><h3>Logging source v${p.version}</h3><p>${esc(p.facts)}</p><small>${esc(p.evidence)}</small><p class="pin">${esc(p.hash)} · ${esc(p.scope)}</p></section>`).join('')||empty('No accepted source incorporated.')}<p>Customer private evidence is unavailable beyond grant. History retains old accepted pins when new source versions are staged.</p>`,'manifest');
  }
  function showPackage(id) {
    const p=state.packages.find(x=>x.id===id);
    if(!p||['engineer','admin'].includes(persona)){notify('Denied: package outside substantive scope.',true);return;}
    let body=`${badge(p.status)} ${badge(p.signature===sourceSignature(state)?'Source fresh':'Stale source',p.signature===sourceSignature(state)?'good':'bad')}
      ${dl([['Exact boundary','Shared Platform RMF System'],['Purpose',p.purpose],['Package pin',p.hash],['Source',`Logging v${p.pins.at(-1).version} · document ${p.documentRevision}`],['Assessment',`${p.assessment.result} · Casey · source v${p.assessment.version}`],['Operational proof',p.operation.evidence],['Residual risk / POA&M',p.risks],['Proposed conditions',p.conditions]])}
      <p class="banner">This is preparation only. It neither submits to eMASS nor authorizes any other linked/customer system.</p>`;
    if(persona==='ao') {
      if(p.status==='Prepared')body+=btn('Review exact package completeness','reviewPackage',{id:p.id},true);
      if(['Prepared','In AO review'].includes(p.status))body+=btn('Return for clarification','clarify',{id:p.id});
      if(p.status==='In AO review')body+=form('authorize',select('Simulated decision','decision',['ATO'])+area('Decision rationale','reason','')+area('Decision conditions','conditions','')+field('Sample authorization expiration','expiry',dateAfter(90),'date'),'Record SIMULATED scoped decision',{package:p.id,hash:p.hash,system:p.system});
    }
    openDialog(`${p.purpose} package ${p.version} · AO review`,body,'package',id);
  }
  function notify(message,error=false) {$('#notice').textContent=message;$('#notice').style.color=error?'#952c40':'#24583b';}
  function execute(action,p={},keepOpen=false) {
    const result=dispatch(action,{offering:offering||'azure',...p});
    if(!result.ok){if($('#drawer').open)$('#form-error').textContent=result.error;else notify(result.error,true);renderStorage();return result;}
    dirty=false;closeDialog(true);render();notify('SIMULATED · '+result.message);
    if(keepOpen&&p.work)showWork(p.work);
    if(keepOpen&&action==='reviewPackage')showPackage(p.package);
    return result;
  }
  function renderStorage(){ $('#storage-error').hidden=!storageFault;$('#storage-error').textContent=storageFault; }
  function resetDialog() {openDialog('Reset simulated data?',`<p>This removes only ${esc(KEY)} on this browser/origin. Other prototype and application data are untouched.</p>${btn('Confirm reset simulated data','confirmReset',{},true)}${btn('Keep current demo data','close')}`,'reset');}
  function navigate(action,data={}) {
    switch(action) {
      case 'help':openDialog('About this isolated prototype',`<p>All values, identities, evidence, dates and decisions are simulated. This is not the production portal or authentication.</p><h3>Provider and portfolio</h3><p>PEO Digital is the provider; Flank Speed is its portfolio. Microsoft is the underlying cloud provider. Navy customers retain independent application tenants.</p><h3>Your sample identity</h3><p>${esc(user().name)} · ${esc(user().scope)}</p><p>SSM is an ISSM-backed demo alias, not a new stored role. The seven-person switcher selects fixed scoped example identities, not role grants.</p><h3>Keep the outcomes separate</h3><p>Accepted work ≠ approved documentation ≠ satisfied control ≠ verified finding closure ≠ accepted risk ≠ recorded authorization. Links and subscriptions grant none of those outcomes.</p><p>No API, cloud writes, real mail or eMASS submission. Only this browser's ${esc(KEY)} example records persist. Reset requires confirmation.</p>${btn('Close help','close')}`,'help');return;
      case 'administration':
        if($('#drawer').open){closeDialog();if($('#drawer').open)return;}
        if(persona!=='admin'){openDialog('Administration · restricted','<p>The active sample identity has no portal administration grant. Choose the separate portal administrator persona to inspect setup-only examples; this does not grant security or AO authority. No access or records were changed. '+btn('Close detail','close')+'</p>','restricted');return;}
        offering=null;landing='queue';render();return;
      case 'workspaceSection':
        if($('#drawer').open){closeDialog();if($('#drawer').open)return;}
        if(!offering)offering=state.offerings.find(o=>user().offerings.includes(o.id))?.id;
        if(!offering){notify('No offering assigned to this identity.',true);return;}
        section=data.id;render();return;
      case 'home':offering=null;landing='overview';render();return;
      case 'offerings':offering=null;landing='offerings';render();return;
      case 'openSection':if(!user().offerings.includes(data.id)){notify('Denied: offering not assigned.',true);return;}offering=data.id;section=data.section;render();return;
      case 'queue':offering=null;landing='queue';render();return;
      case 'openOffering':if(!user().offerings.includes(data.id)){notify('Denied: offering not assigned.',true);return;}offering=data.id;section=user().defaults==='admin'?'overview':user().defaults;render();return;
      case 'tab':
        if($('#drawer').open){if(dirty){closeDialog();return;}closeDialog(true);}
        section=data.id;renderWorkspace();$(`#tab-${section}`)?.focus();return;
      case 'work':showWork(data.id);return;
      case 'boundary':showBoundary(data.id);return;
      case 'environment':showEnvironment(data.id);return;
      case 'finding':showFinding(data.id);return;
      case 'assessment':showAssessment();return;
      case 'manifest':showManifest();return;
      case 'close':closeDialog();return;
      case 'confirmReset':execute('reset');offering=null;landing='overview';render();return;
      case 'clearEnvs':filter={search:'',org:'',relationship:'',health:'',page:1};renderWorkspace();return;
      case 'envPage':filter.page=Math.max(1,filter.page+Number(data.delta));renderWorkspace();return;
      case 'start':case 'review':execute(action,{work:data.id,revision:workBy(data.id)?.revision},true);return;
      case 'return':case 'reopen':case 'cancel':case 'block':case 'unblock':dirty=false;closeDialog(true);reasonDialog(action,data.id);return;
      case 'saveFacts':{const f=$('#drawer form[data-command="submit"]');execute('saveFacts',{work:data.id,revision:workBy(data.id)?.revision,facts:f.elements.facts.value,evidence:f.elements.evidence.value},true);return;}
      case 'refresh':execute('refresh');return;
      case 'privateEvidence':{const r=dispatch('readPrivateEvidence',{environment:data.id,customer:visibleEnvs().find(e=>e.id===data.id)?.org});$('#form-error').textContent=r.error;return;}
      case 'incorporation':{
        const w=workBy('logging'),sub=accepted(w);
        openDialog('Incorporate accepted material',`${dl([['Domain target','Shared Platform implementation / change package manifest'],['Selected source',sub?'Logging v'+sub.version+' · '+sub.hash:'No accepted source'],['Document review','Independent SSM approval required after staging']])}${form('incorporate',field('Accepted source version','version',sub?.version||0,'number'),'Stage exact accepted source')}`,'incorporation');return;
      }
      case 'approveDocument':execute('approveDocument',{version:state.document.revision});return;
      case 'operation':openDialog('Operational dependency',persona==='owner'?form('resolveOperation',area('Owner rationale','reason',state.operation.reason)+field('Operational evidence','evidence',state.operation.evidence),'Resolve dependency with owner proof'):`<p>Only the assigned owner can resolve this obligation.</p>${dl([['State',state.operation.resolved?'Resolved':'Blocked'],['Rationale',state.operation.reason||'No owner proof recorded']])}`,'operation');return;
      case 'lifecycle':openDialog('Plan ongoing lifecycle activity',form('lifecycle',select('Activity','kind',['Change review','Renewal preparation','Retirement plan'])+field('Sample review date','due',dateAfter(14),'date')+area('Activity / affected dependencies','reason','','Name both customer dependencies, shared platform surviving use, evidence retention and review.'),'Stage lifecycle plan'),'lifecycle');return;
      case 'trigger':openDialog('Define scoped ConMon trigger',form('trigger',field('Data source','source','Simulated diagnostic observation')+select('Reviewed resource scope','resource',['Shared log collector','Shared network gateway'])+area('Condition','condition','No sample event received within the reviewed evaluation window')+select('Severity','severity',['High','Medium','Low'])+field('Accountable owner','owner','Blair · SSM')+select('Evaluation frequency','frequency',['Hourly','Daily'])+area('Follow-up action','followup','Open scoped impact review and reassessment work; never change authorization automatically.'),'Save simulated trigger'),'trigger');return;
      case 'packagePrepare':openDialog('Prepare exact authorization package',`${readinessPanel()}${form('preparePackage',select('Package purpose','purpose',['Change','Renewal','Initial']),'Prepare scoped local package',{system:'shared'})}`,'packagePrepare');return;
      case 'package':showPackage(data.id);return;
      case 'reviewPackage':{const p=state.packages.find(x=>x.id===data.id);execute('reviewPackage',{package:p.id,system:p.system,hash:p.hash},true);return;}
      case 'clarify':{const p=state.packages.find(x=>x.id===data.id);dirty=false;closeDialog(true);openDialog('Return package for clarification',form('clarify',area('Clarification reason','reason',''),'Return exact package',{package:p.id,system:p.system,hash:p.hash}),'clarify',p.id);return;}
      case 'organization':openDialog('Add sample customer organization',form('addOrganization',field('Customer organization','name','')+field('Mission Owner name','owner',''),'Create local setup draft'),'organization');return;
      case 'portfolio':openDialog('Add portfolio',form('addPortfolio',field('Portfolio name','name',''),'Add local portfolio'),'portfolio');return;
      case 'invitation':openDialog('Create local invitation',form('invite',field('Simulated email (.invalid)','email','example@example.invalid','email')+select('Limited proposed role','role',['Engineer','Owner'])+select('Customer scope','customer',state.organizations.map(o=>[o.id,o.name]))+field('Sample invitation expiry','due',dateAfter(7),'date'),'Create local invitation (no mail)'),'invitation');return;
      case 'redeem':{const i=state.invitations.find(x=>x.id===data.id);openDialog('Redeem sample invitation',`<p>Demo-only identity binding; no real sign-in. Expected sample subject: <strong>${esc(i.subject)}</strong>.</p>${form('redeem',field('Verified sample subject','subject',''),'Redeem once locally',{invitation:i.id})}`,'redeem');return;}
      case 'revokeInvite':execute('revokeInvite',{invitation:data.id});return;
      case 'roleGrant':openDialog('Manage limited scoped role',form('grantRole',field('Other simulated subject (.invalid)','subject','person@example.invalid')+select('Approved limited role','role',['Engineer','Owner'])+select('Customer scope','customer',state.organizations.map(o=>[o.id,o.name])),'Record limited example role'),'roleGrant');return;
      case 'revokeRole':execute('revokeRole',{roleId:data.id});return;
      case 'revokeSummary':openDialog('Revoke customer summary consent?',`<p>This local example removes later provider customer summaries. It does not delete historical customer records or grant cloud writes.</p>${form('revokeSummary','', 'Confirm summary revocation',{customer:data.id})}`,'revokeSummary');return;
      default:notify('Unsupported local action.',true);
    }
  }
  document.addEventListener('click',event=>{
    const b=event.target.closest('[data-action]');if(!b)return;
    event.preventDefault();navigate(b.dataset.action,b.dataset);
  });
  document.addEventListener('submit',event=>{
    if(!event.target.matches('form[data-command]'))return;
    event.preventDefault();const f=event.target, data=Object.fromEntries(new FormData(f));
    for(const key of ['work','revision','version','finding','system','package','hash','invitation','customer'])if(f.dataset[key]!==undefined)data[key]=f.dataset[key];
    if(data.revision!==undefined)data.revision=Number(data.revision);
    data.criteriaConfirmed=f.elements.criteriaConfirmed?.checked??false;data.poam=f.elements.poam?.checked??false;
    execute(f.dataset.command,data,false);
  });
  document.addEventListener('input',event=>{
    if($('#drawer').contains(event.target)) {
      dirty=true;
      if(event.target.name==='file') {
        const file=event.target.files?.[0];if(file)event.target.form.elements.evidence.value=`SIM metadata · ${file.name}`;return;
      }
      return;
    }
    const id=event.target.id;
    if(id==='env-search'){filter.search=event.target.value;filter.page=1;rerenderInput(id);return;}
    if(id==='work-search'){workSearch=event.target.value;rerenderInput(id);return;}
    if(id==='history-search'){historySearch=event.target.value;rerenderInput(id);}
  });
  function rerenderInput(id){const el=$('#'+id),pos=el.selectionStart;renderWorkspace();const replacement=$('#'+id);replacement.focus();replacement.setSelectionRange(pos,pos);}
  document.addEventListener('change',event=>{
    if($('#drawer').contains(event.target)){dirty=true;return;}
    const id=event.target.id;
    if(id==='persona'){persona=event.target.value;offering=null;landing='overview';notify('Presentation switched to a separate SIM identity; no roles granted.');render();return;}
    if(['env-org','env-relationship','env-health'].includes(id)){filter[id.replace('env-','')]=event.target.value;filter.page=1;renderWorkspace();$('#'+id).focus();}
    if(id==='work-filter'){workFilter=event.target.value;renderWorkspace();$('#work-filter').focus();}
  });
  document.addEventListener('keydown',event=>{
    if(!event.target.matches('[role="tab"]')||!['ArrowRight','ArrowLeft','Home','End'].includes(event.key))return;
    event.preventDefault();const i=sections.findIndex(s=>s[0]===event.target.dataset.id);
    const next=event.key==='Home'?0:event.key==='End'?sections.length-1:(i+(event.key==='ArrowRight'?1:-1)+sections.length)%sections.length;
    section=sections[next][0];renderWorkspace();$('#tab-'+section).focus();
  });
  $('#drawer').addEventListener('cancel',event=>{event.preventDefault();closeDialog();});
  $('#drawer').addEventListener('keydown',event=>{
    if(event.key!=='Tab')return;
    const items=[...$('#drawer').querySelectorAll('button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), summary, a[href], [tabindex="0"]')].filter(el=>el.getClientRects().length>0);
    const first=items[0],last=items.at(-1);
    if(event.shiftKey&&document.activeElement===first){event.preventDefault();last?.focus();}
    else if(!event.shiftKey&&document.activeElement===last){event.preventDefault();first?.focus();}
  });
  $('#close').addEventListener('click',()=>closeDialog());
  $('#keep').addEventListener('click',()=>{$('#dirty-guard').hidden=true;$('#dialog-content input, #dialog-content textarea, #dialog-content select')?.focus();});
  $('#discard').addEventListener('click',()=>closeDialog(true));
  $('#reset').addEventListener('click',resetDialog);
  $('#demo-help').addEventListener('click',()=>navigate('help'));
  $('#home').addEventListener('click',()=>navigate('home'));
  $('#queue').addEventListener('click',()=>navigate('queue'));
  $('#brand').addEventListener('click',event=>{event.preventDefault();navigate('home');});
  // DEMO ONLY: permission probes use the same handler as UI commands, not a real security boundary.
  window.spinDemo=Object.freeze({
    key:KEY,
    dispatch:(action,p)=>{const result=dispatch(action,p);render();return result;},
    permission,
    inspect:()=>({persona,subject:user().subject,offerings:state.offerings.filter(o=>user().offerings.includes(o.id)).map(o=>o.name),work:scopedWorks().map(clone),environmentCount:visibleEnvs().length,revision:state.revision}),
  });
  render();
})();
