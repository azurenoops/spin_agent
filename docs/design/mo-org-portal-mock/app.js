'use strict';
const $ = id => document.getElementById(id);
const pill = (text, tone = '') => `<span class="pill ${tone}">${text}</span>`;
const goBtn = (text, target, primary = false) =>
  `<button ${target.startsWith('!') ? `data-action="${target.slice(1)}"` : `data-go="${target}"`} class="${primary ? 'primary' : ''}">${text}</button>`;
const metric = (label, value, caption) =>
  `<div class="metric"><span>${label}</span><strong>${value}</strong><small>${caption}</small></div>`;
const table = (heads, rows, rowClass = []) =>
  `<div class="tablewrap"><table><thead><tr>${heads.map(h => `<th scope="col">${h}</th>`).join('')}</tr></thead><tbody>${
    rows.map((r, i) => `<tr class="${rowClass[i] || ''}">${r.map(c => `<td>${c}</td>`).join('')}</tr>`).join('')
  }</tbody></table></div>`;
const panelish = (title, body, action = '') =>
  `<section class="panel"><div class="panelhead"><h2>${title}</h2>${action}</div>${body}</section>`;
const steps = (labels, active) =>
  `<div class="flowsteps">${labels.map((x, i) => `<span class="${i === active ? 'current' : ''}"><b>${i < active ? '✓' : i + 1}</b>${x}</span>`).join('')}</div>`;

const seed = [
  { id: 'planning', name: 'Mission Planning', org: '14th CSW', mode: 'attached', offering: 'Secure Enclave IL4',
    adopted: 2, current: 3, package: 'prep', ao: 'Not recorded', next: 'Confirm residual IA-2',
    residual: 18, inherited: 42, conmon: 'Collection unavailable' },
  { id: 'training', name: 'Training Cloud', org: 'AETC', mode: 'attached', offering: 'Secure Enclave IL4',
    adopted: 3, current: 3, package: 'export', ao: 'Not recorded', next: 'Export residual package',
    residual: 12, inherited: 48, conmon: 'Last collection 2h ago' },
  { id: 'ops', name: 'Wing Ops', org: '97th Wing', mode: 'standalone', offering: null,
    adopted: null, current: null, package: 'prep', ao: 'Not recorded', next: 'Author AC-2 narrative',
    residual: 60, inherited: 0, conmon: 'Rules not configured' },
  { id: 'legacy', name: 'Legacy C2', org: 'NGB', mode: 'imported', offering: null,
    adopted: null, current: null, package: 'recorded', ao: 'ATO · eMASS 12 Jan 2025', next: 'Review missing narratives',
    residual: 60, inherited: 0, conmon: 'Not in recorded scope' },
  { id: 'isr', name: 'ISR Ground Station', org: '551st ISRW', mode: 'standalone', offering: null,
    adopted: null, current: null, package: 'prep', ao: 'Not recorded', next: 'Request offering access',
    residual: 60, inherited: 0, conmon: 'Rules not configured' },
];

const duties = [
  { id: 'AC-2', name: 'Account management', kind: 'shared', narrative: 'open' },
  { id: 'IA-2', name: 'Identification and authentication', kind: 'customer', narrative: 'open' },
  { id: 'AU-2', name: 'Event logging', kind: 'inherited', narrative: 'cite' },
  { id: 'SC-7', name: 'Boundary protection', kind: 'inherited', narrative: 'cite' },
  { id: 'CM-6', name: 'Configuration settings', kind: 'customer', narrative: 'draft' },
  { id: 'SA-9', name: 'External system services', kind: 'shared', narrative: 'open' },
];

const pages = {
  home: { title: 'Systems', eyebrow: 'Organization', desc: 'Next residual documentation task. Not a compliance score.' },
  create: { title: 'Create system', eyebrow: 'Intake', desc: 'New authorization boundary. Does not attach a CSP or issue an ATO.' },
  import: { title: 'eMASS import', eyebrow: 'Intake', desc: 'Working copy. eMASS stays the system of record where required.' },
  overview: { title: 'A clear path to your ATO package', eyebrow: 'Overview', desc: 'Follow the RMF journey and finish the work that builds a reviewable, evidence-backed package.' },
  'overview-monitor': { title: 'Keep your documented system current', eyebrow: 'Overview', desc: 'Review actual coverage, evaluations and changes against the versioned reviewed baseline.' },
  mission: { title: 'Mission & purpose', eyebrow: 'System definition', desc: 'Describe what this system does, who depends on it, and why it matters.' },
  users: { title: 'Users & access', eyebrow: 'System definition', desc: 'Document user populations and their access needs. Assign application roles in Team & permissions.' },
  definition: { title: 'Environment & hosting', eyebrow: 'System definition', desc: 'Describe the deployment and confirm the provider scope associated with this system.' },
  attach: { title: 'Provider hosting', eyebrow: 'System definition', desc: 'Request offering access, declare use, then allocate and associate. Access is not inheritance. A sub in the CSP tenant is not an ATO.' },
  data: { title: 'Data types & sensitivity', eyebrow: 'System definition', desc: 'Describe the information handled by the system. This does not approve categorization.' },
  boundary: { title: 'Components & system scope', eyebrow: 'System definition', desc: 'Record inclusion decisions, operators and rationale. Save scope draft does not change the approved baseline.' },
  design: { title: 'System design', eyebrow: 'System definition', desc: 'Architecture assembled from recorded system relationships. Review what is known and resolve what is missing.' },
  ports: { title: 'Ports & interconnections', eyebrow: 'System definition', desc: 'Record permitted communication and the external systems this system depends on.' },
  baseline: { title: 'Categorization & control baseline', eyebrow: 'Controls & evidence', desc: 'Review information impacts and the resulting control set before assigning implementation work.' },
  capabilities: { title: 'Applied capabilities', eyebrow: 'Controls & evidence', desc: 'Capabilities used by this system. A provider capability is not accepted inheritance.' },
  residual: { title: 'Responsibilities', eyebrow: 'Controls & evidence', desc: 'Customer and remaining shared work. Inherited stay cited to the adopted release.' },
  adopt: { title: 'Control inheritance summary', eyebrow: 'Controls & evidence', desc: 'Adopt a pinned release: CRM + evidence summary + recorded offering authorization cites.' },
  narratives: { title: 'Narratives', eyebrow: 'Controls & evidence', desc: 'Residual statements are authored here. Inherited cite the adopted release.' },
  evidence: { title: 'Evidence', eyebrow: 'Controls & evidence', desc: 'Local customer evidence plus shares the CSP allowed.' },
  policies: { title: 'Policies', eyebrow: 'Controls & evidence', desc: 'Policy references that support this system. A reference is not an ATO.' },
  sap: { title: 'Assessment plan', eyebrow: 'Assessment & risk', desc: 'Plan how this system’s residual controls will be assessed. CSP SAR is not yours.' },
  sar: { title: 'Assessments & results', eyebrow: 'Assessment & risk', desc: 'Collect and record results for this system. Collection is not an authorization decision.' },
  'assess-env': { title: 'Azure assessment configuration', eyebrow: 'Assessment & risk', desc: 'Assessments reuse the environment attached to this system. A provider relationship is not required.' },
  findings: { title: 'Findings & remediation', eyebrow: 'Assessment & risk', desc: 'Track weaknesses and remediation tasks. A finding is not a POA&M until committed.' },
  poam: { title: 'Track remediation commitments', eyebrow: 'Assessment & risk', desc: 'Keep weaknesses, milestones and verified outcomes connected.' },
  exceptions: { title: 'Risk decisions & exceptions', eyebrow: 'Assessment & risk', desc: 'Waivers, risk acceptance and false-positive claims. Submission does not approve or change the AO decision.' },
  package: { title: 'Readiness', eyebrow: 'ATO Readiness', desc: 'Preparation, export, submission, and AO decision as four facts.' },
  conmon: { title: 'Coverage & health', eyebrow: 'Continuous monitoring', desc: 'This system’s recorded resources only. Not CSP offering ConMon.' },
};

const orgPages = ['home', 'create', 'import'];

const groups = [
  { label: 'Overview', items: [{ id: 'overview', label: 'Readiness' }] },
  { label: 'System definition', items: [
    { id: 'mission', label: 'Mission' },
    { id: 'users', label: 'Users' },
    { id: 'definition', label: 'Environment & hosting' },
    { id: 'data', label: 'Data' },
    { id: 'boundary', label: 'Components & system scope' },
    { id: 'ports', label: 'Ports & interconnections' },
    { id: 'design', label: 'System design' },
  ] },
  { label: 'Controls & evidence', items: [
    { id: 'baseline', label: 'Categorization & baseline' },
    { id: 'capabilities', label: 'Applied capabilities' },
    { id: 'residual', label: 'Responsibilities' },
    { id: 'narratives', label: 'Narratives' },
    { id: 'evidence', label: 'Evidence' },
    { id: 'policies', label: 'Policies' },
  ] },
  { label: 'Assessment & risk', items: [
    { id: 'sap', label: 'Assessment plan' },
    { id: 'sar', label: 'Assessments & results' },
    { id: 'findings', label: 'Findings & remediation' },
    { id: 'poam', label: 'POA&M' },
    { id: 'exceptions', label: 'Exceptions' },
  ] },
  { label: 'ATO Readiness', items: [
    { id: 'package', label: 'Readiness' },
    { id: 'stub:preview', label: 'Document previews' },
    { id: 'stub:exports', label: 'Export packages' },
    { id: 'stub:emass', label: 'eMASS reconciliation' },
    { id: 'stub:authorize', label: 'Recorded decisions' },
  ] },
  { label: 'Continuous monitoring', items: [
    { id: 'conmon', label: 'Coverage & health' },
    { id: 'stub:rules', label: 'Rules' },
    { id: 'stub:changes', label: 'Detected changes' },
    { id: 'stub:impacts', label: 'Impact reviews' },
    { id: 'stub:reports', label: 'Reports' },
  ] },
  { label: 'Team & permissions', items: [{ id: 'stub:team', label: 'System team' }] },
  { label: 'Activity & history', items: [{ id: 'stub:history', label: 'Audit history' }] },
];

function groupOf(id) {
  if (id === 'attach') return groups.find(g => g.label === 'System definition');
  if (id === 'adopt') return groups.find(g => g.label === 'Controls & evidence');
  if (id === 'overview-monitor') return groups.find(g => g.label === 'Overview');
  if (id === 'assess-env') return groups.find(g => g.label === 'Assessment & risk');
  return groups.find(g => g.items.some(item => item.id === id));
}
function firstOf(group) { return group.items[0].id; }
function tabsFor(id) {
  const group = groupOf(id);
  if (!group) return [];
  if (group.label === 'Overview') {
    return [{ id: 'overview', label: 'Package preparation' }, { id: 'overview-monitor', label: 'Monitoring & follow-up' }];
  }
  const items = group.items.slice();
  if (group.label === 'System definition' && (id === 'definition' || id === 'attach')) {
    items.push({ id: 'attach', label: 'Provider hosting' });
  }
  if (group.label === 'Controls & evidence' && ['capabilities', 'residual', 'adopt'].includes(id)) {
    items.push({ id: 'adopt', label: 'Control inheritance summary' });
  }
  if (group.label === 'Assessment & risk' && ['sap', 'sar', 'assess-env'].includes(id)) {
    items.push({ id: 'assess-env', label: 'Assessment environment' });
  }
  return items;
}

const sim = {
  state: 'ready', page: 'home', selected: 'planning', filter: 'all',
  createStep: 0, createName: 'New mission system',
  importStage: 0, importApplied: false,
  attachStep: 0, attachStatus: 'none',
  declare: { offering: 'enclave', offeringName: 'Secure Enclave IL4', il: 'IL4', data: 'CUI as published', interconnect: 'None', existingSub: true, env: 'Dev', authPath: 'IATT' },
  adoptedBy: {},
  stayRationale: '',
  confirmed: {},
  evidenceView: 'all',
  mission: {
    statement: 'Coordinate mission planning for 14th CSW and keep operational records available to authorized users.',
    purpose: 'Provide a documented planning workspace for wing operations, not a weapons system.',
    justification: 'Disruption delays mission support coordination across supported wings.',
    functions: 'Planning requests, schedule deconfliction, status reporting.',
  },
  users: [
    { id: 'u1', name: 'Mission operators', need: 'Use the application in the documented environment', auth: 'CAC/PIV' },
    { id: 'u2', name: 'System administrators', need: 'Operate and patch in-boundary components', auth: 'CAC/PIV + MFA' },
    { id: 'u3', name: 'External partners', need: 'Limited interconnection if recorded under Ports', auth: 'None until ISA' },
  ],
  dataTypes: [
    { id: 'd1', name: 'Mission operational records', sensitivity: 'CUI', privacy: 'Not PII' },
    { id: 'd2', name: 'Authentication events', sensitivity: 'CUI', privacy: 'Not PII' },
    { id: 'd3', name: 'Personnel identifiers', sensitivity: 'CUI', privacy: 'PII candidate' },
  ],
  components: [
    { id: 'c1', name: 'Mission Planning application', placement: 'In scope', note: 'This system' },
    { id: 'c2', name: 'Mission data store', placement: 'In scope', note: 'Recorded resource' },
    { id: 'c3', name: 'Secure Enclave control plane', placement: 'Out of scope', note: 'CSP offering — not this boundary' },
  ],
  interfaces: [
    { id: 'n1', service: 'Application gateway', port: '443', protocol: 'TLS', direction: 'Inbound' },
    { id: 'n2', service: 'Org IdP', port: '443', protocol: 'TLS', direction: 'Outbound' },
  ],
  interconnects: [
    { id: 'i1', target: 'Org Entra / CAC', owner: '14th CSW', classification: 'CUI', direction: 'Outbound', agreement: 'Not recorded' },
  ],
  architecture: 'Users reach Mission Planning through the application gateway in the associated Secure Enclave. Identity is org Entra/CAC. Mission data stays in the in-scope data store. The CSP control plane is out of this boundary.',
  flows: [
    { id: 'f1', from: 'Mission operator workstation', to: 'Application gateway', data: 'CUI operational records', protection: 'TLS 1.2+' },
    { id: 'f2', from: 'Application', to: 'Mission data store', data: 'CUI operational records', protection: 'TLS in-enclave' },
    { id: 'f3', from: 'Application', to: 'Org Entra / CAC', data: 'Authentication events', protection: 'TLS + CAC/PIV' },
  ],
  diagrams: [
    { id: 'g1', name: 'Authorization boundary (logical)', kind: 'Boundary diagram', status: 'Draft' },
    { id: 'g2', name: 'CUI data flow', kind: 'Data-flow diagram', status: 'Draft' },
  ],
  dataContext: 'CUI in Azure Government. No classified data. Privacy screening incomplete.',
  hosting: { model: 'CSP-hosted', cloud: 'Azure Government', zones: 'Enclave, Application tier', location: 'CONUS' },
  drawer: null,
  designView: 'Context',
  designGov: 'Draft',
  designRevision: 1,
  baseline: { overall: 'Moderate', c: 'Moderate', i: 'Moderate', a: 'Low', overlay: 'CNSSI 1253', controls: 60 },
  capabilities: [
    { id: 'k1', name: 'Identity & access', source: 'organization', status: 'Applied' },
    { id: 'k2', name: 'Secure Enclave logging', source: 'provider', status: 'Associated — not inherited' },
    { id: 'k3', name: 'Boundary protection', source: 'provider', status: 'Inherited after adopt' },
  ],
  policies: [
    { id: 'p1', name: 'Org access control policy', status: 'Referenced', source: 'Organization' },
    { id: 'p2', name: 'CSP AU policy summary', status: 'Shared summary', source: 'Provider' },
  ],
  statements: [
    { id: 'IA-2', source: 'customer', text: 'Customer implements MFA on mission accounts.' },
    { id: 'AC-2', source: 'shared', text: 'Customer half: joiner/leaver tickets. CSP half cited from adopted release.' },
    { id: 'AU-2', source: 'inherited', text: 'Cite Secure Enclave inheritance release. Do not rewrite the CSP statement.' },
  ],
  sap: { title: 'Initial assessment — residual set', lead: 'SCA (assigned)', scope: 'Customer and remaining shared controls. Inherited cite the pinned release and are not re-tested as unimplemented.', schedule: 'Draft — not executed' },
  results: [
    { id: 'r1', control: 'IA-2', result: 'Other than satisfied', note: 'Residual MFA evidence incomplete' },
    { id: 'r2', control: 'AC-2', result: 'Satisfied', note: 'Customer joiner/leaver path demonstrated' },
    { id: 'r3', control: 'AU-2', result: 'Not assessed here', note: 'Inherited — cite CSP SAR, do not copy it' },
  ],
  findings: [
    { id: 'n1', title: 'MFA not complete for mission operators', severity: 'CAT II', state: 'Open', poam: 'Not committed' },
    { id: 'n2', title: 'Shared AC-2 customer half overdue review', severity: 'CAT III', state: 'In remediation', poam: 'Not committed' },
  ],
  poams: [
    { id: 'm1', title: 'Complete mission-operator MFA', milestone: '30 days', state: 'Open', overdue: false },
  ],
  exceptions: [
    { id: 'x1', type: 'Risk acceptance', control: 'AC-2', status: 'Submitted', authority: 'Not approved' },
  ],
  rmfView: 'Implement',
  rmfConfirmed: null,
  readinessStatus: 'Stale — check again',
  blocking: 4,
  warnings: 2,
};
const rmfJourney = [
  { phase: 'Prepare', purpose: 'Establish the mission, system scope, responsibilities and documentation sources.', work: 'Review system design, inventory and team.', path: 'mission', documents: 'SSP and boundary records' },
  { phase: 'Categorize', purpose: 'Describe the information processed and its C/I/A impact.', work: 'Review information types and categorization.', path: 'baseline', documents: 'Categorization and SSP' },
  { phase: 'Select', purpose: 'Select and tailor the baseline and provider responsibilities.', work: 'Review baseline and source-backed duties.', path: 'baseline', documents: 'Baseline and responsibility records' },
  { phase: 'Implement', purpose: 'Document what this system implements and supporting evidence.', work: 'Residual narratives and evidence.', path: 'narratives', documents: 'SSP and evidence index' },
  { phase: 'Assess', purpose: 'Plan and record assessments using actual evidence.', work: 'SAP, results, unresolved findings.', path: 'sap', documents: 'SAP, SAR and POA&M' },
  { phase: 'Authorize', purpose: 'Prepare the reviewed package for the authorization authority.', work: 'Preparation, export, eMASS, recorded decision stay separate.', path: 'package', documents: 'Authorization package' },
  { phase: 'Monitor', purpose: 'Maintain evidence against the reviewed baseline.', work: 'Coverage, changes, follow-up.', path: 'conmon', documents: 'ConMon documentation' },
];
const designViews = [
  { id: 'Context', label: 'System context' },
  { id: 'Boundary', label: 'Authorization boundary' },
  { id: 'Logical', label: 'Logical architecture' },
  { id: 'DataFlows', label: 'Data flows' },
  { id: 'Network', label: 'Network architecture' },
  { id: 'AzureDeployment', label: 'Azure deployment' },
];

function systems() {
  return seed.map(s => {
    let row = { ...s };
    if (s.id === 'planning' && sim.adoptedPlanning) row = { ...row, adopted: 3, next: 'Confirm residual IA-2' };
    if (sim.adoptedBy[s.id]) row = { ...row, adopted: sim.adoptedBy[s.id], next: 'Confirm residual IA-2' };
    const req = loadAccess()[s.id];
    if (req && req.associated) {
      row = {
        ...row,
        mode: 'attached',
        offering: req.offeringName || 'Secure Enclave IL4',
        current: 2,
        next: row.adopted ? row.next : 'Adopt pinned release',
      };
    }
    return row;
  });
}
function current() { return systems().find(s => s.id === sim.selected) || systems()[0]; }
function drifted(s) { return s.mode === 'attached' && s.adopted != null && s.adopted !== s.current; }
function modePill(s) {
  if (s.mode === 'standalone') return pill('Standalone');
  if (s.mode === 'imported') return pill('eMASS imported', 'amber');
  if (s.mode === 'attached' && !s.adopted) return pill('Associated — not inherited', 'amber');
  return drifted(s) ? pill('Attached · drifted', 'amber') : pill('Attached', 'green');
}
function packageFact(s) {
  return {
    prep: ['Preparation', 'Incomplete residual package'],
    export: ['Export', 'Ready to export — not submitted'],
    recorded: ['AO decision', 'Recorded external fact'],
  }[s.package] || ['Preparation', s.package];
}
function canWrite() { return sim.state !== 'restricted'; }

const ACCESS_KEY = 'spin.mock.offeringAccess';
function loadAccess() {
  try {
    return JSON.parse(localStorage.getItem(ACCESS_KEY) || '{}') || {};
  } catch (err) {
    // Design mock only: a corrupt preview store must not blank the screen.
    console.warn('access store unreadable', err);
    return {};
  }
}
function putAccess(id, patch) {
  const all = loadAccess();
  all[id] = { ...(all[id] || {}), ...patch, id, moSystemId: id };
  localStorage.setItem(ACCESS_KEY, JSON.stringify(all));
  return all[id];
}
function accessReqForCurrent() { return loadAccess()[current().id] || null; }
function cspWhatIfLink(req) {
  if (!req) return '';
  return `<p class="caption"><a href="../csp-home-impact-mock/index.html?access=${req.id}#whatif">Open CSP what-if / Access requests</a> · They decide access. This tab does not inherit.</p>`;
}

const CORRECTION_KEY = 'spin.mock.declaredUseCorrections';
function loadCorrections() {
  try {
    return JSON.parse(localStorage.getItem(CORRECTION_KEY) || '{}') || {};
  } catch (err) {
    // Design mock only: a corrupt preview store must not blank the screen.
    console.warn('correction store unreadable', err);
    return {};
  }
}
function putCorrection(id, patch) {
  const all = loadCorrections();
  all[id] = { ...(all[id] || {}), ...patch, cspMissionId: id };
  localStorage.setItem(CORRECTION_KEY, JSON.stringify(all));
  return all[id];
}
function correctionForCurrent() {
  return Object.values(loadCorrections()).find(c => c.moSystemId === current().id) || null;
}
function applyAccessQuery() {
  const q = new URLSearchParams(location.search);
  const id = q.get('access');
  const status = q.get('accessStatus');
  if (id && seed.some(s => s.id === id)) sim.selected = id;
  if (!id || !status) return;
  const cur = loadAccess()[id] || {};
  const s = seed.find(x => x.id === id) || {};
  putAccess(id, {
    org: cur.org || s.org,
    system: cur.system || s.name,
    offering: cur.offering || 'enclave',
    offeringName: cur.offeringName || 'Secure Enclave IL4',
    status,
    il: cur.il || 'IL4',
    data: cur.data || 'CUI as published',
    interconnect: cur.interconnect || 'None',
    env: cur.env || 'Dev',
    authPath: cur.authPath || 'IATT',
    allocated: cur.allocated || false,
    associated: cur.associated || false,
    allocation: cur.allocation || '',
  });
}
function applyCorrectionQuery() {
  const id = new URLSearchParams(location.search).get('correction');
  if (!id) return;
  if (loadCorrections()[id]) return;
  const specimen = {
    csw: {
      moSystemId: 'planning',
      org: '14th CSW',
      system: 'Mission Planning',
      offering: 'Secure Enclave IL4',
      offeringVersion: 'v2.1',
      message: 'Declared use is outside the published boundary. Bring impact level, data types, and extra paths inside Secure Enclave IL4 v2.1.',
      status: 'requested',
      authorized: 'IL4 CUI · enclave compute/storage · peering to CSP hub only',
      declared: 'IL5 mission data · same enclave · additional path to a sister IL5 system',
    },
    aetc: {
      moSystemId: 'training',
      org: 'AETC',
      system: 'Training Cloud',
      offering: 'Secure Enclave IL4',
      offeringVersion: 'v2.1',
      message: 'Document the extra interconnection as customer duty or remove it from declared use.',
      status: 'requested',
      authorized: 'IL4 CUI · enclave · hub peering only',
      declared: 'IL4 CUI · extra interconnection to sister training system',
    },
  };
  if (specimen[id]) putCorrection(id, specimen[id]);
}

function statusline() {
  return `<div class="statusline"><span>Draft · Mission Owner edits / ISSM reviews</span><span>Sample records · Design only</span></div>`;
}
function support(contrib, guidance, next) {
  return `<div class="support-col">
      <p class="eyebrow">Used in your package</p>
      <h3>${contrib}</h3>
      <p>${guidance}</p>
      <p class="eyebrow">Review &amp; ownership</p>
      <p>Reviewed records supply the SSP. A saved draft does not publish the document and is not an ATO.</p>
      ${next || ''}
    </div>`;
}
function workspace(main, contrib, guidance, next) {
  return statusline() + `<div class="workspace-grid"><div>${main}</div>${support(contrib, guidance, next)}</div>`;
}
function openDrawer(spec) {
  sim.drawer = spec;
  $('drawerTitle').textContent = spec.title;
  $('drawerDesc').textContent = spec.desc;
  $('drawerBody').innerHTML = spec.body;
  $('drawerActions').innerHTML = spec.actions;
  $('drawer').hidden = false;
  $('drawerBackdrop').hidden = false;
}
function hideDrawer() {
  sim.drawer = null;
  $('drawer').hidden = true;
  $('drawerBackdrop').hidden = true;
}
function val(id) { return (document.getElementById(id) || {}).value || ''; }

function homeReady() {
  const list = systems().filter(s => sim.filter === 'all' || s.mode === sim.filter);
  const chips = ['all', 'standalone', 'attached', 'imported'].map(id =>
    `<button data-action="filter:${id}" class="${sim.filter === id ? 'active' : ''}">${id === 'all' ? 'All' : id[0].toUpperCase() + id.slice(1)}</button>`).join('');
  return `<div class="metrics">
      ${metric('Systems', String(systems().length), 'This organization')}
      ${metric('Standalone', String(systems().filter(s => s.mode === 'standalone').length), 'No cloud offering')}
      ${metric('Attached', String(systems().filter(s => s.mode === 'attached').length), 'Pinned release, residual remains')}
      ${metric('Drifted', String(systems().filter(drifted).length), 'Adopted ≠ CSP current')}
    </div>
    <div class="filterchips" role="group" aria-label="Filter by mode">${chips}</div>
    ${panelish('Systems', table(['System', 'Mode', 'Inheritance', 'Next residual task', 'Package fact', ''],
      list.map(s => [s.name + `<small>${s.org}</small>`, modePill(s),
        s.mode === 'attached' ? `r${s.adopted}${drifted(s) ? ` · CSP r${s.current}` : ''}` : '—',
        s.next, packageFact(s)[0], goBtn('Open', 'overview:' + s.id)]),
      list.map(s => `mode-${s.mode}`)))}
    <p class="caption">Coverage % and ATO counts are not this home. Four package facts stay separate.</p>`;
}

function createReady() {
  const labels = ['Identity', 'Source', 'Hosting', 'Review'];
  const bodies = [
    `<label class="field">System name<input value="${sim.createName}" data-action="create-name"></label>
     <p class="caption">Standalone by default. Saving a draft is not an ATO.</p>`,
    `<p>Start blank, or leave an eMASS / SSP source review task. Upload does not change facts.</p>
     ${goBtn('Switch to eMASS import', 'import')}`,
    `<p>Use an allocated CSP service, document org-managed hosting, or defer. Defer keeps this standalone.</p>
     <p>${pill('Hosting deferred')} ${pill('No offering attached')}</p>`,
    `<p><b>${sim.createName || 'Unnamed'}</b> will be a standalone draft. Residual work is the full selected baseline until a pinned release is adopted.</p>`,
  ];
  return steps(labels, sim.createStep) + panelish(labels[sim.createStep], bodies[sim.createStep]);
}

function importReady() {
  const labels = ['Retain source', 'Preview', 'Decisions', 'Applied'];
  const stage = sim.importApplied ? 3 : sim.importStage;
  const bodies = [
    `<p>One digital SSP PDF or single-system XLSX. Analysis proposes identity fields only.</p>
     <p>${pill('legacy-c2.xlsx')} ${pill('Receipt retained')}</p>`,
    table(['Field', 'Current', 'Proposed', 'Supported'], [
      ['Name', 'Not recorded', 'Legacy C2', 'Yes'],
      ['Acronym', 'Not recorded', 'LC2', 'Yes'],
      ['Categorization', 'Not recorded', 'Moderate', 'Yes'],
      ['Narratives', 'Not recorded', 'Not extracted', 'No'],
      ['Authorization', 'Not recorded', 'ATO 12 Jan 2025 (external cite only)', 'Record as fact — do not issue'],
    ]),
    `<p>Keep current or apply proposed identity fields. Approved narratives, controls, roles, and authorization decisions are not overwritten.</p>
     <p>${pill('Name: apply')} ${pill('Narratives: keep missing')}</p>`,
    `<div class="banner"><div><b>Source decisions recorded.</b><p>Original bytes and field-level review are retained. Document approval and authorization remain separate. eMASS was not replaced.</p></div></div>`,
  ];
  return steps(labels, stage) + panelish(labels[stage], bodies[stage]);
}

function overviewReady() {
  const s = current();
  const drift = drifted(s);
  const viewing = rmfJourney.find(p => p.phase === sim.rmfView) || rmfJourney[3];
  const journey = `<div class="sd-canvas rmf-journey" role="navigation" aria-label="Explore RMF phases">${rmfJourney.map((p, i) =>
    `<button class="sd-node${p.phase === sim.rmfView ? ' active' : ''}" data-action="rmf-view:${p.phase}"><small>${i + 1}</small><b>${p.phase}</b>${sim.rmfConfirmed === p.phase ? '<small>Current phase</small>' : ''}</button>`).join('')}</div>`;
  const work = [
    { title: s.next, who: 'Mission Owner', path: s.mode === 'attached' ? 'residual' : 'narratives' },
    { title: 'Review categorization inputs from Data', who: 'ISSM', path: 'baseline' },
    { title: 'Confirm residual IA-2 narrative', who: 'ISSO', path: 'narratives' },
  ];
  const docs = [
    ['System Security Plan', 'Gaps', 'Working records, not approved export'],
    ['Assessment plan', 'Draft', 'SAP is not executed'],
    ['Evidence index', 'Present', 'Unavailable share ≠ healthy'],
    ['POA&M', '1 open', 'Not eMASS receipt'],
  ];
  const milestones = [
    ['Preparation', s.package === 'prep' ? 'Incomplete residual package' : 'Ready'],
    ['Export', s.package === 'export' ? 'File generated' : 'Not exported'],
    ['Actual eMASS submission', 'Not observed'],
    ['Authorization decision', s.ao],
  ];
  return workspace(
    (drift ? `<div class="banner amber"><div><b>Adopted r${s.adopted} · CSP published r${s.current}.</b>
      <p>You are drifted. Never silent upgrade. Inheritance is not this overview’s job.</p></div>${goBtn('Control inheritance summary', 'adopt', true)}</div>` : '')
      + `<p>${modePill(s)} ${s.offering ? pill(s.offering) : ''} ${s.mode === 'attached' && s.adopted ? pill('Adopted r' + s.adopted) : ''}</p>`
      + panelish('Current RMF phase: ' + (sim.rmfConfirmed || 'Not confirmed'),
        `<p class="caption">${sim.rmfConfirmed ? 'Explicitly recorded. Browsing does not change it.' : 'An initial or legacy phase value is not an explicitly confirmed phase.'}</p>
         ${journey}
         <h3>Viewing phase: ${viewing.phase}</h3>
         <p>${viewing.purpose}</p>
         <p class="caption">${viewing.work} Supports ${viewing.documents}.</p>
         <p>${goBtn('Explore ' + viewing.phase + ' work', viewing.path)} ${goBtn('Confirm recorded phase', '!confirm-rmf')}</p>`,
        '')
      + panelish('Package preparation', `<p><b>${sim.readinessStatus}</b> · Initial submission</p>
         <p>${sim.blocking} blocking requirements · ${sim.warnings} warnings</p>
         <p class="caption">Finding counts do not indicate RMF phase completion. A check does not approve documents, accept inheritance, submit eMASS or authorize the system.</p>
         <p>${goBtn('Check again', '!check-ready', true)}</p>
         ${table(['Work', 'Responsible', ''], work.map(w => [w.title, w.who, goBtn('Open', w.path)]))}`),
    'Your ATO package',
    'Gaps, document presence and recorded reviews are separate facts. A readiness finding is not document approval. Working preview is not an approved export.',
    panelish('Package documents', table(['Document', 'State', 'Note'], docs))
      + panelish('Package milestones', table(['Milestone', 'State'], milestones))
      + goBtn('Preview working SSP', 'stub:preview') + goBtn('ATO Readiness', 'package'));
}
function overviewMonitorReady() {
  const s = current();
  return workspace(
    definitionBanner()
      + panelish('Keep your documented system current', `<p>Monitoring configuration: ${s.conmon.includes('2h') ? 'Enabled' : 'Not enabled / collection unavailable'}</p>
         <p>Open findings from assessment: ${sim.findings.length} · Overdue POA&amp;M: ${sim.poams.filter(p => p.overdue).length}</p>
         <p class="caption">Monitoring enabled, monitoring healthy, cATO readiness and authorization are separate states. Unavailable telemetry is not zero findings and does not flip the recorded AO decision.</p>
         <p>${goBtn('Review monitoring coverage', 'conmon', true)} ${goBtn('Review monitoring documentation', 'stub:reports')}</p>`)
      + panelish('Coverage and connectivity gaps', `<p>Configuration does not prove collector permissions or complete resource visibility.</p>
         <p>${s.conmon}. Attachment is not a monitoring feed. CSP offering ConMon is not this screen.</p>`),
    'ConMon · Monitoring evidence',
    'This system’s recorded resources only. Proposed follow-up does not replace the reviewed baseline.',
    goBtn('Coverage & health', 'conmon') + goBtn('Package preparation', 'overview'));
}

function attachWalkStep(req) {
  if (!req || !req.status || req.status === 'none') return Math.min(sim.attachStep, 1);
  if (req.status === 'requested' || req.status === 'refused') return 2;
  if (req.status === 'approved' && !req.allocated) return 3;
  if (req.allocated && !req.associated) return 4;
  return 4;
}
function attachReady() {
  const s = current();
  const corr = correctionForCurrent();
  const req = accessReqForCurrent();
  if (s.mode === 'attached' && s.adopted) {
    const declared = (corr && corr.declared) || (req && `${req.il} · ${req.data} · ${req.interconnect}`)
      || 'IL5 mission data · same enclave · additional path to a sister IL5 system';
    const authorized = (corr && corr.authorized) || 'IL4 CUI · enclave compute/storage · peering to CSP hub only';
    const correction = !corr ? ''
      : corr.status === 'requested'
        ? `<div class="banner amber"><div><b>CSP asked you to correct declared use</b><p>${corr.message || 'Bring declared use inside the published offering.'}</p><p class="caption">Access stays approved. Scope fit stays out of scope until you resubmit and the CSP re-scores. This is not an SSP rewrite.</p></div></div>`
        : `<div class="banner"><div><b>Declared use resubmitted</b><p>Waiting on the CSP to re-score scope fit. Access unchanged. Resubmit is not inheritance and not an ATO.</p>
            <p class="caption"><a href="../csp-home-impact-mock/index.html?resubmitted=${corr.cspMissionId}#mo-impact:${corr.cspMissionId}">Return to CSP impact record</a></p></div></div>`;
    return statusline() + correction
      + panelish('Current association', `<div class="kv"><span>Offering</span><span>${s.offering}</span></div>
          <div class="kv"><span>Adopted release</span><span>r${s.adopted}${drifted(s) ? ` · CSP current r${s.current}` : ''}</span></div>
          <div class="kv"><span>Access</span><span>Approved</span></div>
          <p class="caption">Association is not inheritance. Adopted release is on Control inheritance summary.</p>`)
      + `<div class="compare">
          <section class="panel"><h3>Published authorized use</h3><p>${authorized}</p><p class="caption">What the CSP recorded for the offering. You do not edit this here.</p></section>
          <section class="panel"><h3>Your declared use</h3><p>${declared}</p><p class="caption">Subscription declaration. Not inferred from Mission, Data, or the SSP body.</p></section>
        </div>`
      + (corr && corr.status === 'requested'
        ? `<p>${goBtn('Resubmit declared use', '!resubmit-declared', true)}</p>`
        : `<p class="caption">${goBtn('Open Control inheritance summary', 'adopt')}</p>`);
  }
  if (s.mode === 'attached' && !s.adopted) {
    return statusline()
      + `<div class="banner"><div><b>Access approved · hosting associated · not inherited</b>
         <p>A sub in the CSP tenant and an approved offering relationship are not a pinned inheritance release.</p></div></div>`
      + panelish('Current association', `<div class="kv"><span>Offering</span><span>${s.offering}</span></div>
          <div class="kv"><span>Azure allocation</span><span>${(req && req.allocation) || 'Bound'}</span></div>
          <div class="kv"><span>Adopted release</span><span>None</span></div>
          <p class="caption">Next: adopt a pinned release. Do not subtract customer controls yet.</p>`)
      + `<p>${goBtn('Adopt pinned release', 'adopt', true)}</p>`;
  }
  const labels = ['Request', 'Declare use', 'CSP decide', 'Allocate', 'Associate'];
  const step = attachWalkStep(req);
  const d = sim.declare;
  const bodies = [
    `<p>Ask the CSP for <b>offering access</b>. This creates no Azure allocation, association, or inheritance.</p>
     <p>${pill('Secure Enclave IL4')}</p>
     <p class="caption">The CSP catalog is not where you request. This tab is.</p>`,
    `<p>Authorization path, env lane, IL, data, and extra paths the CSP may score. This is not an SSP body share and not an IATT or ATO.</p>
     <div class="fields">
       <label class="field">Authorization path<select data-action="decl-auth"><option ${d.authPath === 'IATT' ? 'selected' : ''}>IATT</option><option ${d.authPath === 'ATO' ? 'selected' : ''}>ATO</option></select></label>
       <label class="field">Declared env<select data-action="decl-env"><option ${d.env === 'Dev' ? 'selected' : ''}>Dev</option><option ${d.env === 'Test' ? 'selected' : ''}>Test</option><option ${d.env === 'Prod' ? 'selected' : ''}>Prod</option></select></label>
       <label class="field">Impact level<select data-action="decl-il"><option ${d.il === 'IL4' ? 'selected' : ''}>IL4</option><option ${d.il === 'IL5' ? 'selected' : ''}>IL5</option></select></label>
       <label class="field">Data types<select data-action="decl-data"><option ${d.data === 'CUI as published' ? 'selected' : ''}>CUI as published</option><option ${d.data.includes('IL5') ? 'selected' : ''}>IL5 mission data</option></select></label>
       <label class="field full">Extra interconnection<select data-action="decl-path"><option ${d.interconnect === 'None' ? 'selected' : ''}>None</option><option ${d.interconnect !== 'None' ? 'selected' : ''}>Sister IL5 system</option></select></label>
     </div>
     <p class="caption">IATT is a test decision. ATO is operational. Dev/Test/Prod are hosting lanes. IATT in Test because Dev is locked is an exception, not “closer to ATO.”</p>`,
    req && req.status === 'refused'
      ? `<div class="banner amber"><div><b>CSP refused offering access</b><p>No subscription. No allocation. No inheritance. You may correct declared use and send again.</p></div></div>${cspWhatIfLink(req)}`
      : req && req.status === 'requested'
        ? `<div class="banner amber"><div><b>Waiting on CSP what-if</b><p>Access is not approved. There is no inheritance and no bound sub from this request.</p></div></div>${cspWhatIfLink(req)}`
        : req && req.status === 'approved'
          ? `<div class="banner green"><div><b>CSP approved offering access</b><p>Approve is not allocate Azure and not adopt. Continue to bind hosting.</p></div></div>`
          : `<p>Send declared use first. The CSP Access requests queue is their inbox.</p>`,
    `<p>Bind where this system runs. Dev/Test/Prod lanes are <b>hosting</b>, not IATT or ATO.</p>
     <label class="field">Lane<select data-action="decl-env"><option ${d.env === 'Dev' ? 'selected' : ''}>Dev</option><option ${d.env === 'Test' ? 'selected' : ''}>Test</option><option ${d.env === 'Prod' ? 'selected' : ''}>Prod</option></select></label>
     <label class="field">Azure allocation<select data-action="decl-sub">
       <option ${d.existingSub ? 'selected' : ''} value="existing">Existing sub in CSP tenant</option>
       <option ${!d.existingSub ? 'selected' : ''} value="none">No sub yet — document later</option>
     </select></label>
     <p class="caption">Recording a ${d.env} sub does not issue IATT or ATO and does not adopt a release. Binding Test while IATT was declared for Dev is hosting drift.</p>`,
    `<p>Record that <b>${s.name}</b> runs on ${d.offeringName}. Association is still not inheritance.</p>
     <p>${pill('Access approved', 'green')} ${pill(d.existingSub ? 'Sub bound' : 'Hosting deferred')} ${pill('Not inherited', 'amber')}</p>
     <p class="caption">After associate, adopt a pinned release on Control inheritance summary.</p>`,
  ];
  return statusline() + steps(labels, step) + panelish(labels[step], bodies[step]);
}

function dutyRows() {
  const s = current();
  const adopted = s.mode === 'attached' && s.adopted;
  if (!adopted) return duties.map(d => ({ ...d, kind: 'customer', narrative: d.narrative === 'cite' ? 'open' : d.narrative }));
  return duties;
}
function baselineReady() {
  const b = sim.baseline;
  const s = current();
  return workspace(
    definitionBanner() + panelish('Security categorization', `<div class="kv"><span>Overall</span><span>${b.overall}</span></div>
      <div class="kv"><span>C / I / A</span><span>${b.c} / ${b.i} / ${b.a}</span></div>
      <div class="kv"><span>Overlay</span><span>${b.overlay}</span></div>
      <div class="kv"><span>Selected controls</span><span>${b.controls} in baseline</span></div>
      <p class="caption">${s.mode === 'attached' && s.adopted ? 'Adopted inheritance does not shrink this baseline; residual work is on Responsibilities.' : 'No controls are inherited until a pinned release is adopted.'}</p>`,
      goBtn('Review categorization', '!edit-baseline', true)),
    'SSP · Security categorization / control baseline',
    'Information types on Data feed this review. Selecting a baseline is not implementation and not an ATO.',
    goBtn('Applied capabilities', 'capabilities'));
}
function capabilitiesReady() {
  const s = current();
  const rows = sim.capabilities.map(k => {
    let status = k.status;
    if (k.source === 'provider' && !(s.mode === 'attached' && s.adopted)) status = 'Associated — not inherited';
    if (k.source === 'provider' && s.mode === 'attached' && s.adopted && k.id === 'k3') status = 'Inherited · r' + s.adopted;
    return [k.name, k.source === 'provider' ? pill('Provider') : pill('Organization'), status, goBtn('Open', '!edit-cap:' + k.id)];
  });
  return workspace(
    definitionBanner() + panelish('Capabilities used by this system', table(['Capability', 'Source', 'State', ''], rows),
      goBtn('Add organization capability', '!add-cap', true)),
    'SSP · Control implementation / CRM',
    'A provider capability on this list is not accepted inheritance. Adopt a pinned release on Control inheritance summary.',
    goBtn('Responsibilities', 'residual') + goBtn('Control inheritance summary', 'adopt'));
}
function adoptReady() {
  const s = current();
  if (s.mode !== 'attached') {
    return workspace(
      definitionBanner() + panelish('No pinned release', `<p>Association or “we use this offering” is not inheritance. Request Provider hosting first, then adopt.</p>
        <p>${goBtn('Provider hosting', 'attach', true)}</p>`),
      'SSP · Leveraged authorization / inheritance cites',
      'Do not subtract customer work until a release is adopted.',
      goBtn('Responsibilities', 'residual'));
  }
  if (!s.adopted) {
    return workspace(
      definitionBanner()
        + `<div class="banner amber"><div><b>Associated — not inherited</b>
           <p>Offering access and a bound Azure sub are not a pinned inheritance release. Adopt r2 to cite CRM + evidence + recorded authorization.</p></div></div>`
        + panelish('If you adopt r2', table(['Duty', 'Count'],
          [['Inherited (cite release)', '42'], ['Shared (customer half remains)', '8'], ['Customer-implemented', '10']])),
      'SSP · Inheritance cites',
      'Adopt is inheritance. Access, allocation, and association are not.',
      goBtn('Provider hosting', 'attach'));
  }
  const adopted = sim.adoptedPlanning && s.id === 'planning' ? 3 : s.adopted;
  const currentRel = s.current || 3;
  return workspace(
    definitionBanner()
      + (drifted({ ...s, adopted }) ? `<div class="banner amber"><div><b>Adopted r${adopted} · CSP published r${currentRel}.</b><p>Never silent upgrade. Adopt r${currentRel} or stay on r${adopted} with rationale.</p></div></div>` : '')
      + `<div class="compare">
          <div class="panel"><div class="panelhead"><h2>Adopted r${adopted}</h2></div><p>CRM + evidence summary + recorded offering authorization cites.</p>${adopted === currentRel ? pill('Current', 'green') : pill('Drifted', 'amber')}</div>
          <div class="panel"><div class="panelhead"><h2>CSP published r${currentRel}</h2></div><p>Newest publication. Does not replace what you adopted until you adopt it.</p></div>
        </div>`
      + panelish('If you adopt the current publication', table(['Duty', 'Count'],
        [['Inherited (cite release)', '42'], ['Shared (customer half remains)', '8'], ['Customer-implemented', '10']]))
      + (sim.stayRationale ? `<div class="banner amber"><div><b>Staying on r${adopted}.</b><p>${sim.stayRationale}</p></div></div>` : ''),
    'SSP · Inheritance cites',
    'Adopt is inheritance. Attach, allocation, and applied provider capabilities are not.',
    goBtn('Responsibilities', 'residual'));
}
function residualReady() {
  const s = current();
  const rows = dutyRows();
  return workspace(
    definitionBanner()
      + `<div class="metrics">${metric('Customer', String(rows.filter(d => d.kind === 'customer').length), 'Implement, evidence, assess')}
         ${metric('Shared', String(rows.filter(d => d.kind === 'shared').length), 'Confirm the customer half')}
         ${metric('Inherited', String(rows.filter(d => d.kind === 'inherited').length), s.adopted ? 'Cite adopted r' + s.adopted : 'None until adopt')}</div>`
      + panelish('Responsibility allocation', table(['Control', 'Duty', 'State', ''],
        rows.map(d => {
          const done = sim.confirmed[d.id];
          return [d.id + `<small>${d.name}</small>`, pill(d.kind, d.kind === 'inherited' ? 'green' : d.kind === 'shared' ? 'amber' : ''),
            d.kind === 'inherited' ? 'Cite r' + (s.adopted || 2) : done ? 'Confirmed' : d.narrative,
            d.kind === 'inherited' ? goBtn('View cite', '!edit-nar:' + d.id) : goBtn(done ? 'Open narrative' : 'Confirm duty', '!confirm:' + d.id)];
        }),
        rows.map(d => d.kind === 'inherited' ? 'duty-inherited' : ''))),
    'SSP · Control implementation / CRM',
    'Residual work is the product after adopt. Do not treat inherited controls as unimplemented without cause. Assess residual only.',
    goBtn('Narratives', 'narratives'));
}
function narrativesReady() {
  const s = current();
  const adopted = s.mode === 'attached' && s.adopted;
  const rows = sim.statements.map(st => {
    const source = !adopted && st.source === 'inherited' ? 'customer' : st.source;
    const text = source === 'inherited' ? st.text : (source === 'customer' && !adopted && st.id === 'AU-2' ? 'Event logging narrative is ours — no CSP cite.' : st.text);
    return [st.id, source === 'inherited' ? pill('Inherited', 'green') : source === 'shared' ? pill('Shared', 'amber') : pill('Customer'),
      text, goBtn('Open', '!edit-nar:' + st.id)];
  });
  return workspace(
    definitionBanner() + panelish('Implementation statements', table(['Control', 'Source', 'Statement', ''], rows)),
    'SSP · Implementation statements',
    'Do not pull the CSP SSP body. Inherited statements cite the pinned release. eMASS import today does not fill narratives.',
    goBtn('Evidence', 'evidence'));
}
function evidenceReady() {
  const view = sim.evidenceView;
  const catalog = [
    { id: 'e1', name: 'MFA screenshot', view: 'system', age: '12 days', share: true },
    { id: 'e2', name: 'Account workflow SOP', view: 'system', age: '3 days', share: true },
    { id: 'e3', name: 'CSP AU logging summary', view: 'provider', age: '41 days', share: true },
    { id: 'e4', name: 'CSP SC-7 boundary summary', view: 'provider', age: 'Unavailable share', share: false },
  ].filter(i => view === 'all' || i.view === view);
  const chips = ['all', 'system', 'provider'].map(id =>
    `<button data-action="ev:${id}" class="${view === id ? 'active' : ''}">${id === 'all' ? 'All' : id === 'system' ? 'System' : 'Provider shared'}</button>`).join('');
  return workspace(
    definitionBanner() + `<div class="filterchips">${chips}</div>`
      + panelish('Catalog', table(['Artifact', 'View', 'Age / share', ''],
        catalog.map(i => [i.name, i.view === 'provider' ? 'Provider shared' : 'System',
          i.share === false ? pill('Unavailable — not healthy', 'amber') : i.age, goBtn('Open', '!edit-ev:' + i.id)]))),
    'SSP · Supporting evidence',
    'No silent CSP private files. Unavailable share is not “healthy, no findings.” Provider shared summaries are not MO-sub telemetry.',
    goBtn('Policies', 'policies'));
}
function policiesReady() {
  return workspace(
    definitionBanner() + panelish('Policy references', table(['Policy', 'Source', 'Status', ''],
      sim.policies.map(p => [p.name, p.source, p.status, goBtn('Open', '!edit-pol:' + p.id)])),
      goBtn('Add policy reference', '!add-pol', true)),
    'SSP · Policies / legal-regulatory',
    'A referenced policy supports implementation. It is not an authorization decision.',
    goBtn('ATO Readiness', 'package'));
}

function sapReady() {
  const p = sim.sap;
  const s = current();
  return workspace(
    definitionBanner() + panelish('Security assessment plan', `<div class="kv"><span>Title</span><span>${p.title}</span></div>
      <div class="kv"><span>Lead</span><span>${p.lead}</span></div>
      <div class="kv"><span>Scope</span><span>${s.mode === 'attached' && s.adopted ? p.scope : 'Full selected baseline — no inherited set until a pinned release is adopted.'}</span></div>
      <div class="kv"><span>Schedule</span><span>${p.schedule}</span></div>`,
      goBtn('Edit plan', '!edit-sap', true) + goBtn('Finalize plan', '!finalize-sap')),
    'SAP / SAR · Assessment scope',
    'Plan residual controls for this system. Inherited controls cite the pinned release; do not treat them as unimplemented. CSP SAR is not yours.',
    goBtn('Assessments & results', 'sar'));
}
function sarReady() {
  return workspace(
    definitionBanner() + panelish('Results', table(['Control', 'Determination', 'Note', ''],
      sim.results.map(r => [r.control, r.result, r.note, goBtn('Open', '!edit-result:' + r.id)])),
      goBtn('Collect results', '!collect-sar', true) + goBtn('Prepare SAR', '!prep-sar')),
    'SAP / SAR · Assessment results',
    'Collection and a prepared SAR are not an authorization decision. Do not paste the CSP SAR as this system’s SAR.',
    goBtn('Findings & remediation', 'findings'));
}
function assessEnvReady() {
  return workspace(
    definitionBanner() + panelish('System subscriptions', `<p>There is no separate assessment subscription picker. Entitlement, reviewed resource scope, collector access, and execution permission are independent checks.</p>
      <p>A provider relationship is not required. Attachment is not a monitoring feed.</p>
      <p>${goBtn('Environment & hosting', 'definition')} ${goBtn('Check prerequisites', '!assess-prereq', true)}</p>`),
    'SAP / SAR · Assessment environment',
    'Assessments reuse the environment attached to this system. Live route: assessments/environment.',
    goBtn('Assessment plan', 'sap'));
}
function findingsReady() {
  return workspace(
    definitionBanner() + panelish('Findings queue', table(['Finding', 'Severity', 'State', 'POA&M', ''],
      sim.findings.map(f => [f.title, f.severity, f.state, f.poam, goBtn('Open', '!edit-finding:' + f.id)])),
      goBtn('Add finding', '!add-finding', true)),
    'SAR / POA&M · Assessment and risk records',
    'A finding is not a POA&M until an authorized person commits it. Remediation tasks are not the AO decision.',
    goBtn('POA&M', 'poam'));
}
function poamReady() {
  const overdue = sim.poams.filter(p => p.overdue).length;
  return workspace(
    definitionBanner()
      + (overdue ? `<div class="banner amber"><div><b>${overdue} commitment overdue.</b><p>Review overdue work. Overdue is not a withdrawn ATO.</p></div></div>` : '')
      + panelish('POA&M register', table(['Item', 'Milestone', 'State', ''],
        sim.poams.map(p => [p.title, p.milestone, p.state, goBtn('Open', '!edit-poam:' + p.id)])),
        goBtn('Add POA&M', '!add-poam', true)),
    'POA&M · Remediation commitments',
    'Keep weaknesses, milestones and verified outcomes connected. Export is not eMASS receipt.',
    goBtn('Exceptions', 'exceptions'));
}
function exceptionsReady() {
  return workspace(
    definitionBanner() + panelish('Exceptions', table(['Type', 'Control', 'Status', 'Authority', ''],
      sim.exceptions.map(x => [x.type, x.control, x.status, x.authority, goBtn('Open', '!edit-exc:' + x.id)])),
      goBtn('Request exception', '!add-exc', true)),
    'Risk register / Package supporting decisions',
    'Waivers, risk acceptance, and false-positive claims stay separate. Submission does not approve a claim or change the recorded AO decision.',
    goBtn('Recorded decisions', 'stub:authorize'));
}

function packageReady() {
  const s = current();
  return `<div class="facts">
      <div class="fact${s.package === 'prep' ? ' warn' : ''}"><span class="caption">1. Preparation</span><strong>${s.package === 'prep' ? 'Incomplete' : 'Ready'}</strong><p>Residual gaps, not inherited gaps.</p></div>
      <div class="fact"><span class="caption">2. Export</span><strong>${s.package === 'export' ? 'File generated' : 'Not exported'}</strong><p>Export is not eMASS receipt.</p></div>
      <div class="fact"><span class="caption">3. Submission</span><strong>Not observed</strong><p>This portal does not mark eMASS received.</p></div>
      <div class="fact"><span class="caption">4. AO decision</span><strong>${s.ao}</strong><p>Recorded if entered. Not issued from save.</p></div>
    </div>
    ${panelish('Actions', `<p>${goBtn('Check residual readiness', '!readiness')} ${goBtn('Record external decision', '!record')}</p>
      <p class="caption">There is no Issue Authorization in this overlay. Recorded decisions stay attributed to the AO or eMASS.</p>`)}`;
}

function conmonReady() {
  const s = current();
  return `<div class="metrics">
      ${metric('Scope', 'Recorded resources', 'Attachment is not a monitoring feed')}
      ${metric('Health', s.conmon, 'Unavailable ≠ zero findings')}
      ${metric('Last collection', s.conmon.includes('2h') ? '2 hours ago' : 'None', 'Stale does not flip the AO decision')}
    </div>
    ${panelish('Rules on this system', table(['Rule', 'Owner', 'Status'], [
      ['Boundary drift', 'ISSO', s.conmon.includes('unavailable') || s.conmon.includes('not') ? pill('Collection unavailable', 'amber') : pill('Healthy', 'green')],
      ['Customer-control evidence age', 'ISSO', pill('Needs review', 'amber')],
    ]))}
    <p class="caption">CSP offering ConMon lives on the CSP portal. This screen does not monitor MO-sub telemetry for the CSP.</p>`;
}

function definitionBanner() {
  const s = current();
  return `<p>${modePill(s)} ${pill('Draft · not approved SSP')} ${s.offering ? pill(s.offering) : pill('No offering attached')}</p>`;
}
function openMissionDrawer() {
  const m = sim.mission;
  openDrawer({
    title: 'Edit mission & purpose',
    desc: 'Saving creates a draft. It does not approve the SSP or issue an ATO.',
    body: `<div class="fields">
      <label class="field full">Mission statement<textarea id="f-statement" rows="4">${m.statement}</textarea></label>
      <label class="field full">Business purpose<textarea id="f-purpose" rows="4">${m.purpose}</textarea></label>
      <label class="field full">Operational justification<textarea id="f-just" rows="3">${m.justification}</textarea></label>
      <label class="field full">Business functions<textarea id="f-fn" rows="3">${m.functions}</textarea></label>
    </div>`,
    actions: goBtn('Cancel', '!drawer-close') + goBtn('Save draft', '!save-mission', true),
  });
}
function openUserDrawer(id) {
  const u = sim.users.find(x => x.id === id) || { id: '', name: '', need: '', auth: 'CAC/PIV' };
  sim.editId = id || '';
  openDrawer({
    title: id ? 'Edit user population' : 'Add user population',
    desc: 'A category is not an account and does not assign ISSM/ISSO roles.',
    body: `<div class="fields">
      <label class="field full">Population name<input id="f-name" value="${u.name}"></label>
      <label class="field full">Access need<textarea id="f-need" rows="3">${u.need}</textarea></label>
      <label class="field full">Authentication methods (documented, not granted)<input id="f-auth" value="${u.auth}"></label>
    </div>`,
    actions: goBtn('Cancel', '!drawer-close') + goBtn('Save draft', '!save-user', true),
  });
}
function openDataDrawer(id) {
  const d = sim.dataTypes.find(x => x.id === id) || { id: '', name: '', sensitivity: 'CUI', privacy: 'Not PII' };
  sim.editId = id || '';
  openDrawer({
    title: id ? 'Edit information type' : 'Add information type',
    desc: 'Recording a type does not approve FIPS 199 or a PTA/PIA.',
    body: `<div class="fields">
      <label class="field full">Information type<input id="f-name" value="${d.name}"></label>
      <label class="field">Sensitivity<select id="f-sens"><option ${d.sensitivity==='CUI'?'selected':''}>CUI</option><option ${d.sensitivity==='Unclassified'?'selected':''}>Unclassified</option><option ${d.sensitivity==='PII candidate'?'selected':''}>PII candidate</option></select></label>
      <label class="field">Privacy screening<input id="f-priv" value="${d.privacy}"></label>
    </div>`,
    actions: goBtn('Cancel', '!drawer-close') + goBtn('Save draft', '!save-data', true),
  });
}
function openContextDrawer() {
  openDrawer({
    title: 'System-wide information handling context',
    desc: 'This context describes the whole data profile, not one type. It is not a privacy determination.',
    body: `<label class="field full">Handling context<textarea id="f-ctx" rows="5">${sim.dataContext}</textarea></label>`,
    actions: goBtn('Cancel', '!drawer-close') + goBtn('Save context', '!save-context', true),
  });
}
function openHostDrawer() {
  const h = sim.hosting;
  openDrawer({
    title: 'Edit environment & hosting',
    desc: 'Scope association documents where the system runs. It does not adopt inheritance.',
    body: `<div class="fields">
      <label class="field">Hosting model<select id="f-model"><option ${h.model==='CSP-hosted'?'selected':''}>CSP-hosted</option><option ${h.model==='Organization-managed cloud'?'selected':''}>Organization-managed cloud</option><option ${h.model==='On-Premises'?'selected':''}>On-Premises</option><option ${h.model==='Hybrid'?'selected':''}>Hybrid</option></select></label>
      <label class="field">Cloud environment<input id="f-cloud" value="${h.cloud}"></label>
      <label class="field full">Network zones<input id="f-zones" value="${h.zones}"></label>
      <label class="field full">Geographic locations<input id="f-loc" value="${h.location}"></label>
    </div>`,
    actions: goBtn('Cancel', '!drawer-close') + goBtn('Save draft', '!save-host', true),
  });
}
function openComponentDrawer(id) {
  const c = sim.components.find(x => x.id === id) || { id: '', name: '', placement: 'In scope', note: '' };
  sim.editId = id || '';
  openDrawer({
    title: id ? 'Edit boundary record' : 'Add boundary record',
    desc: 'Attachment does not put the CSP control plane in this system’s boundary.',
    body: `<div class="fields">
      <label class="field full">Component / resource<input id="f-name" value="${c.name}"></label>
      <label class="field">Placement<select id="f-place"><option ${c.placement==='In scope'?'selected':''}>In scope</option><option ${c.placement==='Out of scope'?'selected':''}>Out of scope</option></select></label>
      <label class="field">Note<input id="f-note" value="${c.note}"></label>
    </div>`,
    actions: goBtn('Cancel', '!drawer-close') + goBtn('Save draft', '!save-component', true),
  });
}
function openIfDrawer(id) {
  const n = sim.interfaces.find(x => x.id === id) || { id: '', service: '', port: '', protocol: 'TLS', direction: 'Inbound' };
  sim.editId = id || '';
  openDrawer({
    title: id ? 'Network interface · ' + n.service : 'Add network interface',
    desc: 'Saving does not approve connectivity or change firewall rules.',
    body: `<div class="fields">
      <label class="field full">Service / interface name<input id="f-name" value="${n.service}"></label>
      <label class="field">Ports or range<input id="f-port" value="${n.port}"></label>
      <label class="field">Protocol<input id="f-proto" value="${n.protocol}"></label>
      <label class="field full">Direction<select id="f-dir"><option>Inbound</option><option ${n.direction==='Outbound'?'selected':''}>Outbound</option><option>Both</option></select></label>
    </div>`,
    actions: goBtn('Cancel', '!drawer-close') + goBtn('Save draft', '!save-if', true),
  });
}
function openIxDrawer(id) {
  const x = sim.interconnects.find(i => i.id === id) || { id: '', target: '', owner: '', classification: 'CUI', direction: 'Outbound', agreement: 'Not recorded' };
  sim.editId = id || '';
  openDrawer({
    title: id ? 'Interconnection · ' + x.target : 'Add interconnection',
    desc: 'Saving does not approve an agreement or grant authorization to connect.',
    body: `<div class="fields">
      <label class="field full">External system<input id="f-name" value="${x.target}"></label>
      <label class="field">Owner<input id="f-owner" value="${x.owner}"></label>
      <label class="field">Classification<input id="f-class" value="${x.classification}"></label>
      <label class="field full">Data flow direction<select id="f-dir"><option ${x.direction==='Outbound'?'selected':''}>Outbound</option><option>Inbound</option><option>Both</option></select></label>
      <label class="field full">Agreement status<input id="f-ag" value="${x.agreement}"></label>
    </div>`,
    actions: goBtn('Cancel', '!drawer-close') + goBtn('Save draft', '!save-ix', true),
  });
}
function openArchDrawer() {
  openDrawer({
    title: 'Edit architecture narrative',
    desc: 'Feeds SSP illustrated architecture. A narrative is not an authorized design.',
    body: `<label class="field full">Network / logical architecture<textarea id="f-arch" rows="7">${sim.architecture}</textarea></label>`,
    actions: goBtn('Cancel', '!drawer-close') + goBtn('Save draft', '!save-arch', true),
  });
}
function openFlowDrawer(id) {
  const f = sim.flows.find(x => x.id === id) || { id: '', from: '', to: '', data: 'CUI operational records', protection: 'TLS 1.2+' };
  sim.editId = id || '';
  openDrawer({
    title: id ? 'Edit data flow' : 'Add data flow',
    desc: 'OSCAL data-flow contribution. Not a live network path and not authorization to connect.',
    body: `<div class="fields">
      <label class="field full">From<input id="f-from" value="${f.from}"></label>
      <label class="field full">To<input id="f-to" value="${f.to}"></label>
      <label class="field">Data type<input id="f-data" value="${f.data}"></label>
      <label class="field">Protection<input id="f-prot" value="${f.protection}"></label>
    </div>`,
    actions: goBtn('Cancel', '!drawer-close') + goBtn('Save draft', '!save-flow', true),
  });
}
function openDiagramDrawer(id) {
  const g = sim.diagrams.find(x => x.id === id) || { id: '', name: '', kind: 'Boundary diagram', status: 'Draft' };
  sim.editId = id || '';
  openDrawer({
    title: id ? 'Edit diagram record' : 'Add diagram record',
    desc: 'Retain a named architecture artifact. This mock does not render visio/png. Inclusion is not AO acceptance.',
    body: `<div class="fields">
      <label class="field full">Title<input id="f-name" value="${g.name}"></label>
      <label class="field">Kind<select id="f-kind"><option ${g.kind==='Boundary diagram'?'selected':''}>Boundary diagram</option><option ${g.kind==='Data-flow diagram'?'selected':''}>Data-flow diagram</option><option ${g.kind==='Network diagram'?'selected':''}>Network diagram</option></select></label>
      <label class="field">Status<select id="f-status"><option>Draft</option><option ${g.status==='Under review'?'selected':''}>Under review</option></select></label>
    </div>`,
    actions: goBtn('Cancel', '!drawer-close') + goBtn('Save draft', '!save-diagram', true),
  });
}
function missionReady() {
  const m = sim.mission;
  const s = current();
  return workspace(
    definitionBanner() + panelish('Mission record', `<div class="kv"><span>System</span><span>${s.name}</span></div>
      <div class="kv"><span>Mission statement</span><span>${m.statement}</span></div>
      <div class="kv"><span>Business purpose</span><span>${m.purpose}</span></div>
      <div class="kv"><span>Operational justification</span><span>${m.justification}</span></div>
      <div class="kv"><span>Business functions</span><span>${m.functions}</span></div>`,
      goBtn('Edit mission', '!edit-mission', true)),
    'SSP · System description',
    'Describe what this system does, who depends on it, and why it matters.',
    goBtn('Users', 'users'));
}
function usersReady() {
  return workspace(
    definitionBanner() + panelish('User populations', table(['Population', 'Access need', 'Documented auth', ''],
      sim.users.map(u => [u.name, u.need, u.auth, goBtn('Open', '!edit-user:' + u.id)])),
      goBtn('Add population', '!add-user', true)),
    'SSP · Users and access',
    'A documented category does not grant application access. Use System team for RMF role assignments.',
    goBtn('Environment & hosting', 'definition'));
}
function definitionReady() {
  const s = current();
  const h = sim.hosting;
  return workspace(
    definitionBanner() + panelish('Deployment', `<div class="kv"><span>Hosting model</span><span>${h.model}</span></div>
      <div class="kv"><span>Cloud environment</span><span>${h.cloud}</span></div>
      <div class="kv"><span>Network zones</span><span>${h.zones}</span></div>
      <div class="kv"><span>Locations</span><span>${h.location}</span></div>
      <div class="kv"><span>Offering association</span><span>${s.offering || 'None — standalone / deferred'}</span></div>
      <div class="kv"><span>Azure allocation</span><span>${(accessReqForCurrent() && accessReqForCurrent().allocation) || (s.mode === 'attached' ? 'Bound with offering' : 'Not bound')}</span></div>
      <p class="caption">A sub in the CSP tenant is hosting. It is not offering access and not inheritance.</p>`,
      goBtn('Edit deployment', '!edit-host', true) + goBtn('Provider hosting', 'attach')),
    'SSP · Environment and hosting scope',
    'Association records where the system runs. Review applied capabilities and customer duties separately. Provider hosting is an extra tab, not left nav.',
    goBtn('Inventory & boundary', 'boundary'));
}
function dataReady() {
  return workspace(
    definitionBanner() + panelish('Information types', table(['Type', 'Sensitivity', 'Privacy screening', ''],
      sim.dataTypes.map(d => [d.name, d.sensitivity, d.privacy, goBtn('Open', '!edit-data:' + d.id)])),
      goBtn('Add information type', '!add-data', true) + goBtn('Handling context', '!edit-context')),
    'SSP · Information types / Privacy screening',
    'Recording types does not approve categorization or a privacy determination.',
    goBtn('Inventory & boundary', 'boundary') + goBtn('Categorization & baseline', 'stub:baseline'));
}
function boundaryReady() {
  return workspace(
    definitionBanner() + panelish('Authorization boundary', table(['Record', 'Placement', 'Note', ''],
      sim.components.map(c => [c.name, pill(c.placement, c.placement === 'In scope' ? 'green' : 'amber'), c.note, goBtn('Open', '!edit-comp:' + c.id)])),
      goBtn('Add record', '!add-comp', true)),
    'SSP · Boundary description and inventory',
    'Attachment does not put the CSP’s entire cloud in this system. ConMon later binds only to recorded in-scope resources.',
    goBtn('Ports & interconnections', 'ports'));
}
function designReady() {
  const v = sim.designView;
  const chips = designViews.map(x =>
    `<button data-action="design-view:${x.id}" class="${v === x.id ? 'active' : ''}">${x.label}</button>`).join('');
  const nodes = [
    { id: 'sys', label: current().name, kind: 'System' },
    { id: 'ops', label: 'Mission operators', kind: 'Actor' },
    { id: 'gw', label: 'Application gateway', kind: 'Component' },
    { id: 'db', label: 'Mission data store', kind: 'Component' },
    { id: 'idp', label: 'Org Entra / CAC', kind: 'External' },
  ];
  const viewCopy = {
    Context: 'Actors, this system, and external systems. Not a network diagram.',
    Boundary: 'What is inside the authorization boundary vs shared/external. Attach does not swallow the CSP cloud.',
    Logical: 'In-scope components and how they relate. Built from Components & system scope.',
    DataFlows: 'CUI/auth flows between recorded elements. Not live telemetry.',
    Network: 'Zones and permitted paths. PPS remains the source for ports.',
    AzureDeployment: 'Recorded Azure/SACA zones from attached scope. Does not invent a reference architecture.',
  };
  const canvas = `<div class="sd-canvas" aria-label="${v} canvas">${nodes.map(n =>
    `<button class="sd-node" data-action="inspect:${n.id}"><b>${n.label}</b><small>${n.kind}</small></button>`).join('')}</div>`;
  const gaps = [
    ['Users', 'Actor counts are documented; access grants are not implied'],
    ['Ports', 'Gateway 443 is recorded; interconnection agreement is not'],
    ['Components', 'Enclave control plane stays out of scope'],
  ];
  return workspace(
    definitionBanner()
      + `<div class="sd-gov"><div><h3>Design status</h3><p>${pill(sim.designGov, 'amber')} Working revision <b>v${sim.designRevision}</b> · Approved baseline <b>Not approved</b></p>
         <p class="caption">Save draft is not approval. Completeness describes the saved revision.</p></div>
         <div class="metrics">${metric('Completeness', '62%', 'Saved revision')} ${metric('Open gaps', '3', 'Resolve before submit')} ${metric('View', designViews.find(x => x.id === v).label, viewCopy[v])}</div></div>`
      + `<div class="filterchips" role="tablist" aria-label="Design views">${chips}</div>`
      + panelish(designViews.find(x => x.id === v).label, `<p class="caption">${viewCopy[v]}</p>${canvas}
         <p>${goBtn('Build from recorded information', '!build-design')} ${goBtn('Save draft', '!save-design', true)} ${goBtn('Submit for review', '!submit-design')}</p>`)
      + panelish('Open gaps', table(['Source tab', 'Gap'], gaps.map(g => [g[0], g[1]]))),
    'SSP · Illustrated architecture / data flow',
    'Live route: profile/SystemDesign. Assembled from Mission, Users, Environment, Data, Components & system scope, and Ports. A canvas is not an authorized design. Compare-to-baseline and package preview stay separate from AO decision.',
    goBtn('Components & system scope', 'boundary') + goBtn('Ports & interconnections', 'ports'));
}
function portsReady() {
  return workspace(
    definitionBanner()
      + panelish('Network interfaces', table(['Service', 'Ports', 'Protocol', 'Direction', ''],
        sim.interfaces.map(n => [n.service, n.port, n.protocol, n.direction, goBtn('Open', '!edit-if:' + n.id)])),
        goBtn('Add interface', '!add-if', true))
      + panelish('Interconnection register', table(['External system', 'Classification', 'Flow', 'Agreement', ''],
        sim.interconnects.map(x => [x.target + `<small>${x.owner}</small>`, x.classification, x.direction, x.agreement, goBtn('Open', '!edit-ix:' + x.id)])),
        goBtn('Add interconnection', '!add-ix', true)),
    'SSP · Network interfaces / Interconnection register',
    'A port row is not authorization to connect. An external connection may still need a reviewed agreement.',
    goBtn('System design', 'design'));
}

function stubReady(label) {
  return `${panelish(label, `<p>This page already exists on the Systems workspace. This overlay does not replace it.</p>
    <p>RMF work stays on this system. It does not move to the CSP portal.</p>
    <p>${goBtn('Back to readiness', 'overview', true)}</p>`)}
    <p class="caption">Left nav matches live SystemSidebar groups. Section pages are tabs, as in SystemTaskNavigation.</p>`;
}

function emptyHome() {
  return `<div class="banner"><div><b>No systems in this organization.</b><p>Create a standalone system or import an eMASS working copy. Completing ATO with no CSP remains valid.</p></div>
    ${goBtn('Create system', 'create', true)}</div>`;
}
function errorView(title) {
  return `<div class="banner amber"><div><b>${title} is unavailable.</b><p>This is not “no residual work” and not a healthy package.</p></div>${goBtn('Retry (simulated)', '!retry')}</div>`;
}
function restrictedView() {
  return `<div class="banner amber"><div><b>Read-only preview.</b><p>SCA / restricted access can see residual status. Adopt, apply source, and record decision stay with ISSM / Mission Owner.</p></div></div>
    ${panelish('Visible without write', '<p>Mode, adopted release, package facts, and next residual task remain visible. Mutation controls are omitted.</p>')}`;
}

function render(raw) {
  let id = raw || 'home';
  let extra = '';
  if (id.startsWith('stub:')) {
    extra = id.slice(5);
  } else if (id.includes(':') && !id.startsWith('stub')) {
    [id, extra] = id.split(':');
  }
  if (extra && seed.some(s => s.id === extra)) sim.selected = extra;
  sim.page = raw || id;
  const s = current();
  const inSystem = !orgPages.includes(id) && !(raw || id).startsWith('stub:org');
  const group = groupOf(raw && raw.startsWith('stub:') ? raw : id);
  const meta = pages[id] || { title: extra ? extra[0].toUpperCase() + extra.slice(1) : 'System page', eyebrow: group ? group.label : 'Systems', desc: 'Existing Systems screen. This overlay does not replace it.' };
  const screenEl = $('screen');
  if ([...screenEl.options].some(o => o.value === id)) screenEl.value = id;
  $('eyebrow').textContent = inSystem ? s.name : meta.eyebrow;
  $('title').textContent = meta.title;
  $('description').textContent = meta.desc;
  document.body.classList.toggle('org-home', !inSystem);
  $('breadcrumb').textContent = inSystem ? `Systems / ${s.name} / ${group ? group.label : ''}` : 'Systems';
  $('orgtops').innerHTML = [
    ['home', 'Portfolio'],
    ['home', 'Systems'],
    ['stub:org-capabilities', 'Security Capabilities'],
    ['stub:org-kb', 'Knowledge Base'],
  ].map(([nid, label], i) => `<button data-go="${nid}" class="${label === 'Systems' && (inSystem || id === 'home' || id === 'create' || id === 'import') ? 'active' : ''}">${label}</button>`).join('');
  const actions = {
    home: goBtn('Add system', 'create', true) + goBtn('Import from eMASS', 'import'),
    create: (sim.createStep > 0 ? goBtn('Back', '!create-back') : '') + goBtn(sim.createStep < 3 ? 'Save & continue' : 'Confirm setup', '!create-next', true),
    import: sim.importApplied ? goBtn('Open Legacy C2', 'overview:legacy', true)
      : goBtn(sim.importStage < 2 ? 'Continue' : 'Apply identity fields', '!import-next', true),
    overview: goBtn('Check again', '!check-ready', true) + goBtn('ATO Readiness', 'package'),
    'overview-monitor': goBtn('Review monitoring coverage', 'conmon', true) + goBtn('Package preparation', 'overview'),
    mission: goBtn('Save draft', '!save-def') + goBtn('Users', 'users', true),
    users: goBtn('Save draft', '!save-def') + goBtn('Environment & hosting', 'definition', true),
    definition: goBtn('Save draft', '!save-def') + goBtn('Provider hosting', 'attach', true),
    data: goBtn('Save draft', '!save-def') + goBtn('Inventory & boundary', 'boundary', true),
    boundary: goBtn('Save draft', '!save-def') + goBtn('Ports & interconnections', 'ports', true),
    ports: goBtn('Save draft', '!save-def') + goBtn('System design', 'design', true),
    design: goBtn('Save draft', '!save-design', true) + goBtn('Submit for review', '!submit-design'),
    attach: (() => {
      const s = current();
      const corr = correctionForCurrent();
      const req = accessReqForCurrent();
      if (s.mode === 'attached' && corr && corr.status === 'requested') {
        return goBtn('Resubmit declared use', '!resubmit-declared', true) + goBtn('Environment & hosting', 'definition');
      }
      if (s.mode === 'attached' && s.adopted) {
        return goBtn('Control inheritance summary', 'adopt', true) + goBtn('Environment & hosting', 'definition');
      }
      if (s.mode === 'attached' && !s.adopted) {
        return goBtn('Adopt pinned release', 'adopt', true) + goBtn('Environment & hosting', 'definition');
      }
      const step = attachWalkStep(req);
      if (step === 0) return goBtn('Continue to declare use', '!attach-next', true);
      if (step === 1) return goBtn('Back', '!attach-back') + goBtn('Send to CSP', '!attach-send', true);
      if (step === 2 && req && req.status === 'requested') {
        return goBtn('Refresh status', '!attach-refresh', true) + goBtn('Environment & hosting', 'definition');
      }
      if (step === 2 && req && req.status === 'refused') return goBtn('Revise declared use', '!attach-revise', true);
      if (step === 2 && req && req.status === 'approved') return goBtn('Allocate Azure hosting', '!attach-next', true);
      if (step === 3) return goBtn('Save allocation', '!attach-allocate', true);
      if (step === 4) return goBtn('Associate hosting', '!attach-associate', true);
      return goBtn('Back', '!attach-back');
    })(),
    baseline: goBtn('Review categorization', '!edit-baseline', true) + goBtn('Applied capabilities', 'capabilities'),
    capabilities: goBtn('Responsibilities', 'residual', true) + goBtn('Control inheritance summary', 'adopt'),
    adopt: current().mode === 'attached' && !current().adopted
      ? goBtn('Adopt r2', '!adopt-r2', true) + goBtn('Provider hosting', 'attach')
      : goBtn('Adopt r3', '!adopt-r3', true) + goBtn('Stay on r2', '!stay'),
    residual: goBtn('Narratives', 'narratives', true) + goBtn('Evidence', 'evidence'),
    narratives: goBtn('Evidence', 'evidence', true) + goBtn('Responsibilities', 'residual'),
    evidence: goBtn('Policies', 'policies', true) + goBtn('Narratives', 'narratives'),
    policies: goBtn('ATO Readiness', 'package', true),
    sap: goBtn('Edit plan', '!edit-sap', true) + goBtn('Assessments & results', 'sar'),
    sar: goBtn('Findings & remediation', 'findings', true) + goBtn('Assessment plan', 'sap'),
    'assess-env': goBtn('Check prerequisites', '!assess-prereq', true) + goBtn('Assessment plan', 'sap'),
    findings: goBtn('POA&M', 'poam', true) + goBtn('Assessments & results', 'sar'),
    poam: goBtn('Exceptions', 'exceptions', true) + goBtn('Add POA&M', '!add-poam'),
    exceptions: goBtn('Request exception', '!add-exc', true) + goBtn('ATO Readiness', 'package'),
    package: goBtn('Coverage & health', 'conmon') + goBtn('Readiness', 'overview'),
    conmon: goBtn('Back to readiness', 'overview', true),
  };
  $('actions').innerHTML = canWrite() ? (actions[id] || goBtn('Back to readiness', 'overview')) : goBtn('Back to Systems', 'home');
  const shownTabs = inSystem ? tabsFor(id) : [];
  $('tabs').innerHTML = shownTabs.length > 1 ? shownTabs.map(item =>
    `<button data-go="${item.id}" class="${(raw || id) === item.id || id === item.id ? 'active' : ''}">${item.label}</button>`).join('') : '';
  $('sidebar').innerHTML = inSystem
    ? `<small>${s.name}</small>${groups.map(g =>
        `<button data-go="${firstOf(g)}" class="${g === group ? 'active' : ''}">${g.label}</button>`).join('')}`
    : '';

  let html;
  const detailed = ['home', 'overview', 'residual', 'package', 'adopt', 'import'];
  if (sim.state === 'error' && detailed.includes(id)) html = errorView(meta.title);
  else if (sim.state === 'restricted' && ['adopt', 'import', 'package', 'create'].includes(id)) html = restrictedView();
  else if (sim.state === 'empty' && id === 'home') html = emptyHome();
  else if (id === 'home') html = homeReady();
  else if (id === 'create') html = createReady();
  else if (id === 'import') html = importReady();
  else if (id === 'overview') html = overviewReady();
  else if (id === 'overview-monitor') html = overviewMonitorReady();
  else if (id === 'mission') html = missionReady();
  else if (id === 'users') html = usersReady();
  else if (id === 'definition') html = definitionReady();
  else if (id === 'data') html = dataReady();
  else if (id === 'boundary') html = boundaryReady();
  else if (id === 'design') html = designReady();
  else if (id === 'ports') html = portsReady();
  else if (id === 'attach') html = attachReady();
  else if (id === 'baseline') html = baselineReady();
  else if (id === 'capabilities') html = capabilitiesReady();
  else if (id === 'adopt') html = adoptReady();
  else if (id === 'residual') html = residualReady();
  else if (id === 'narratives') html = narrativesReady();
  else if (id === 'evidence') html = evidenceReady();
  else if (id === 'policies') html = policiesReady();
  else if (id === 'sap') html = sapReady();
  else if (id === 'sar') html = sarReady();
  else if (id === 'assess-env') html = assessEnvReady();
  else if (id === 'findings') html = findingsReady();
  else if (id === 'poam') html = poamReady();
  else if (id === 'exceptions') html = exceptionsReady();
  else if (id === 'package') html = packageReady();
  else if (id === 'conmon') html = conmonReady();
  else html = stubReady(meta.title);
  $('content').innerHTML = html;
  history.replaceState(null, '', '#' + (id === 'overview' ? `overview:${sim.selected}` : (raw || id)));
  $('main').focus({ preventScroll: true });
}

function toast(msg) { const t = $('toast'); t.textContent = msg; t.classList.add('show'); setTimeout(() => t.classList.remove('show'), 2400); }
function modal(title, body, actions) {
  $('dialogTitle').textContent = title;
  $('dialogBody').innerHTML = body;
  $('dialogActions').innerHTML = actions.join('');
  $('dialog').showModal();
}
function closeDialog() { $('dialog').close(); }

function onAction(name) {
  if (name === 'close') return closeDialog();
  if (name === 'retry') { toast('Retry is simulated. Still unavailable in this preview state.'); return; }
  if (name.startsWith('filter:')) { sim.filter = name.slice(7); render('home'); return; }
  if (name.startsWith('ev:')) { sim.evidenceView = name.slice(3); render('evidence'); return; }
  if (name.startsWith('confirm:')) { sim.confirmed[name.slice(8)] = true; toast('Residual duty confirmed. Narrative still required.'); render('residual'); return; }
  if (name === 'edit-baseline') {
    const b = sim.baseline;
    openDrawer({
      title: 'Review categorization',
      desc: 'SP 800-60 types feed FIPS 199. Saving does not implement controls or issue an ATO.',
      body: `<div class="fields">
        <label class="field">Confidentiality<select id="f-c"><option>${b.c}</option><option>Low</option><option>Moderate</option><option>High</option></select></label>
        <label class="field">Integrity<select id="f-i"><option>${b.i}</option><option>Low</option><option>Moderate</option><option>High</option></select></label>
        <label class="field">Availability<select id="f-a"><option>${b.a}</option><option>Low</option><option>Moderate</option><option>High</option></select></label>
        <label class="field full">Overlay<input id="f-ov" value="${b.overlay}"></label>
      </div>`,
      actions: goBtn('Cancel', '!drawer-close') + goBtn('Save draft', '!save-baseline', true),
    }); return;
  }
  if (name === 'save-baseline') {
    if (!canWrite()) return;
    sim.baseline = { ...sim.baseline, c: val('f-c'), i: val('f-i'), a: val('f-a'), overlay: val('f-ov'), overall: val('f-c') };
    hideDrawer(); toast('Categorization draft saved. Baseline is not implementation.'); render('baseline'); return;
  }
  if (name.startsWith('edit-cap:')) {
    const k = sim.capabilities.find(x => x.id === name.slice(9)) || sim.capabilities[0];
    openDrawer({
      title: 'Capability · ' + k.name,
      desc: 'Provider source does not mean inherited. Adopt a pinned release separately.',
      body: `<div class="kv"><span>Source</span><span>${k.source}</span></div><div class="kv"><span>State</span><span>${k.status}</span></div>`,
      actions: goBtn('Close', '!drawer-close', true),
    }); return;
  }
  if (name === 'add-cap') { toast('Organization capability catalog is the live add flow. Provider capabilities come from association, not this button.'); return; }
  if (name.startsWith('edit-nar:')) {
    const st = sim.statements.find(x => x.id === name.slice(9)) || { id: name.slice(9), source: 'customer', text: '' };
    sim.editId = st.id;
    openDrawer({
      title: 'Narrative · ' + st.id,
      desc: st.source === 'inherited' ? 'Cite the pinned release. Do not rewrite the CSP SSP body.' : 'Author the residual statement. Saving is not approval.',
      body: `<label class="field full">Implementation statement<textarea id="f-nar" rows="6">${st.text}</textarea></label>`,
      actions: goBtn('Cancel', '!drawer-close') + (st.source === 'inherited' ? goBtn('Close', '!drawer-close', true) : goBtn('Save draft', '!save-nar', true)),
    }); return;
  }
  if (name === 'save-nar') {
    if (!canWrite()) return;
    sim.statements = sim.statements.map(st => st.id === sim.editId ? { ...st, text: val('f-nar') } : st);
    hideDrawer(); toast('Narrative draft saved. Not an approved SSP statement.'); render('narratives'); return;
  }
  if (name.startsWith('edit-ev:')) {
    openDrawer({
      title: 'Evidence record',
      desc: 'Unavailable share is not healthy. This is not MO-sub telemetry for the CSP.',
      body: `<p>Synthetic artifact ${name.slice(8)}. Classification and provenance would appear here.</p>`,
      actions: goBtn('Close', '!drawer-close', true),
    }); return;
  }
  if (name === 'add-pol') {
    openDrawer({
      title: 'Add policy reference',
      desc: 'A reference supports implementation. It is not an authorization decision.',
      body: `<label class="field full">Policy name<input id="f-name" value=""></label><label class="field full">Source<input id="f-src" value="Organization"></label>`,
      actions: goBtn('Cancel', '!drawer-close') + goBtn('Save draft', '!save-pol', true),
    }); return;
  }
  if (name.startsWith('edit-pol:')) {
    const p = sim.policies.find(x => x.id === name.slice(9)) || sim.policies[0];
    sim.editId = p.id;
    openDrawer({
      title: 'Policy · ' + p.name,
      desc: 'Referenced policy. Not an ATO.',
      body: `<div class="kv"><span>Source</span><span>${p.source}</span></div><div class="kv"><span>Status</span><span>${p.status}</span></div>`,
      actions: goBtn('Close', '!drawer-close', true),
    }); return;
  }
  if (name === 'save-pol') {
    if (!canWrite()) return;
    sim.policies.push({ id: 'p' + Date.now(), name: val('f-name') || 'Untitled policy', status: 'Referenced', source: val('f-src') || 'Organization' });
    hideDrawer(); toast('Policy reference saved. Not an authorization.'); render('policies'); return;
  }
  if (name === 'edit-sap') {
    const p = sim.sap;
    openDrawer({
      title: 'Edit assessment plan',
      desc: 'Saving a SAP draft is not execution and not an authorization.',
      body: `<div class="fields">
        <label class="field full">Title<input id="f-name" value="${p.title}"></label>
        <label class="field full">Lead<input id="f-lead" value="${p.lead}"></label>
        <label class="field full">Scope<textarea id="f-scope" rows="4">${p.scope}</textarea></label>
      </div>`,
      actions: goBtn('Cancel', '!drawer-close') + goBtn('Save draft', '!save-sap', true),
    }); return;
  }
  if (name === 'save-sap') {
    if (!canWrite()) return;
    sim.sap = { ...sim.sap, title: val('f-name'), lead: val('f-lead'), scope: val('f-scope') };
    hideDrawer(); toast('SAP draft saved. Not executed, not an ATO.'); render('sap'); return;
  }
  if (name === 'finalize-sap') { toast('Plan finalized as a working SAP. Not an authorization decision.'); return; }
  if (name === 'collect-sar') { toast('Collection simulated. Results are not an AO decision and not CSP offering ConMon.'); return; }
  if (name === 'prep-sar') { toast('SAR prepared from this system’s residual results. CSP SAR was not copied.'); return; }
  if (name === 'assess-prereq') { toast('Prerequisites checked against recorded environment. Attachment is not a monitoring feed.'); return; }
  if (name.startsWith('edit-result:')) {
    const r = sim.results.find(x => x.id === name.slice(12)) || sim.results[0];
    openDrawer({
      title: 'Result · ' + r.control,
      desc: 'Inherited controls are not re-tested as unimplemented. CSP SAR is not this SAR.',
      body: `<div class="kv"><span>Determination</span><span>${r.result}</span></div><p>${r.note}</p>`,
      actions: goBtn('Close', '!drawer-close', true),
    }); return;
  }
  if (name === 'add-finding') {
    openDrawer({
      title: 'Add finding',
      desc: 'A finding is not a POA&M until committed.',
      body: `<label class="field full">Title<input id="f-name"></label><label class="field">Severity<select id="f-sev"><option>CAT I</option><option selected>CAT II</option><option>CAT III</option></select></label>`,
      actions: goBtn('Cancel', '!drawer-close') + goBtn('Save draft', '!save-finding', true),
    }); return;
  }
  if (name.startsWith('edit-finding:')) {
    const f = sim.findings.find(x => x.id === name.slice(13)) || sim.findings[0];
    openDrawer({
      title: 'Finding',
      desc: 'Create a POA&M separately. This is not the AO decision.',
      body: `<p>${f.title}</p><div class="kv"><span>Severity</span><span>${f.severity}</span></div><div class="kv"><span>POA&M</span><span>${f.poam}</span></div>`,
      actions: goBtn('Close', '!drawer-close', true) + goBtn('Commit POA&M', '!add-poam'),
    }); return;
  }
  if (name === 'save-finding') {
    if (!canWrite()) return;
    sim.findings.push({ id: 'n' + Date.now(), title: val('f-name') || 'Untitled finding', severity: val('f-sev') || 'CAT II', state: 'Open', poam: 'Not committed' });
    hideDrawer(); toast('Finding recorded. Not a POA&M.'); render('findings'); return;
  }
  if (name === 'add-poam') {
    openDrawer({
      title: 'Add POA&M',
      desc: 'A commitment is not an authorization decision.',
      body: `<label class="field full">Weakness<input id="f-name"></label><label class="field">Milestone<input id="f-ms" value="30 days"></label>`,
      actions: goBtn('Cancel', '!drawer-close') + goBtn('Save draft', '!save-poam', true),
    }); return;
  }
  if (name.startsWith('edit-poam:')) {
    const p = sim.poams.find(x => x.id === name.slice(10)) || sim.poams[0];
    openDrawer({
      title: 'POA&M',
      desc: 'Milestones and verified outcomes. Export is not eMASS receipt.',
      body: `<p>${p.title}</p><div class="kv"><span>Milestone</span><span>${p.milestone}</span></div><div class="kv"><span>State</span><span>${p.state}</span></div>`,
      actions: goBtn('Close', '!drawer-close', true),
    }); return;
  }
  if (name === 'save-poam') {
    if (!canWrite()) return;
    sim.poams.push({ id: 'm' + Date.now(), title: val('f-name') || 'Untitled commitment', milestone: val('f-ms') || '30 days', state: 'Open', overdue: false });
    hideDrawer(); toast('POA&M draft saved. Not an ATO and not eMASS receipt.'); render('poam'); return;
  }
  if (name === 'add-exc') {
    openDrawer({
      title: 'Request exception',
      desc: 'Submission does not approve the claim or change the recorded AO decision.',
      body: `<label class="field">Type<select id="f-type"><option>Risk acceptance</option><option>Waiver</option><option>False positive</option></select></label>
        <label class="field">Control<input id="f-ctl" value="AC-2"></label>`,
      actions: goBtn('Cancel', '!drawer-close') + goBtn('Submit request', '!save-exc', true),
    }); return;
  }
  if (name.startsWith('edit-exc:')) {
    const x = sim.exceptions.find(e => e.id === name.slice(9)) || sim.exceptions[0];
    openDrawer({
      title: 'Exception',
      desc: 'Submitted is not approved. Not an authorization change.',
      body: `<div class="kv"><span>Type</span><span>${x.type}</span></div><div class="kv"><span>Control</span><span>${x.control}</span></div><div class="kv"><span>Status</span><span>${x.status}</span></div>`,
      actions: goBtn('Close', '!drawer-close', true),
    }); return;
  }
  if (name === 'save-exc') {
    if (!canWrite()) return;
    sim.exceptions.push({ id: 'x' + Date.now(), type: val('f-type') || 'Risk acceptance', control: val('f-ctl') || 'AC-2', status: 'Submitted', authority: 'Not approved' });
    hideDrawer(); toast('Exception submitted. Not approved and not an AO decision.'); render('exceptions'); return;
  }
  if (name === 'create-back') { sim.createStep = Math.max(0, sim.createStep - 1); render('create'); return; }
  if (name === 'create-next') {
    if (!canWrite()) return;
    if (sim.createStep < 3) { sim.createStep += 1; render('create'); return; }
    toast('Draft saved. Ready for preparation — not authorization or eMASS submission.');
    sim.selected = 'ops';
    render('overview');
    return;
  }
  if (name === 'import-next') {
    if (!canWrite()) return;
    if (sim.importStage < 2) { sim.importStage += 1; render('import'); return; }
    sim.importApplied = true;
    toast('Identity fields applied. Narratives and AO decision were not overwritten.');
    sim.selected = 'legacy';
    render('import');
    return;
  }
  if (name === 'attach-back') { sim.attachStep = Math.max(0, sim.attachStep - 1); render('attach'); return; }
  if (name === 'attach-next') {
    sim.attachStep = Math.min(4, sim.attachStep + 1);
    render('attach');
    return;
  }
  if (name === 'attach-send') {
    if (!canWrite()) return;
    const s = current();
    const d = sim.declare;
    putAccess(s.id, {
      org: s.org,
      system: s.name,
      offering: d.offering,
      offeringName: d.offeringName,
      status: 'requested',
      il: d.il,
      data: d.data,
      interconnect: d.interconnect,
      env: d.env,
      authPath: d.authPath,
      allocated: false,
      associated: false,
      allocation: '',
      requestedAt: new Date().toISOString(),
    });
    sim.attachStep = 2;
    toast('Sent to CSP Access requests. Access, allocation, and inheritance did not change.');
    render('attach');
    return;
  }
  if (name === 'attach-refresh') { render('attach'); toast('Status refreshed from the shared mock store.'); return; }
  if (name === 'attach-revise') {
    const s = current();
    const req = accessReqForCurrent();
    if (req) putAccess(s.id, { ...req, status: 'none' });
    sim.attachStep = 1;
    render('attach');
    return;
  }
  if (name === 'attach-allocate') {
    if (!canWrite()) return;
    const s = current();
    const req = accessReqForCurrent();
    if (!req || req.status !== 'approved') { toast('CSP must approve offering access before you allocate Azure.'); return; }
    const allocation = sim.declare.existingSub
      ? `Existing CSP-tenant ${sim.declare.env} sub`
      : 'Hosting deferred — no sub bound';
    putAccess(s.id, { ...req, allocated: true, allocation });
    toast('Hosting recorded. Not inheritance.');
    render('attach');
    return;
  }
  if (name === 'attach-associate') {
    if (!canWrite()) return;
    const s = current();
    const req = accessReqForCurrent();
    if (!req || !req.allocated) { toast('Allocate Azure hosting first.'); return; }
    putAccess(s.id, { ...req, associated: true });
    toast('Associated. Still not inherited. Adopt a pinned release next.');
    render('attach');
    return;
  }
  if (name === 'adopt-r2') {
    if (!canWrite()) return;
    sim.adoptedBy[current().id] = 2;
    toast('Adopted r2. Residual customer/shared work remains open. Not an MO ATO.');
    render('residual');
    return;
  }
  if (name === 'resubmit-declared') {
    if (!canWrite()) return;
    const corr = correctionForCurrent();
    if (!corr) { toast('No CSP correction on this system.'); return; }
    openDrawer({
      title: 'Resubmit declared use',
      desc: 'Updates the subscription declaration the CSP scores. Not an SSP save. Does not flip scope fit or issue an ATO.',
      body: `<label class="field">Impact level<select id="declIl"><option>IL4</option><option>IL5</option></select></label>
        <label class="field">Data types<input id="declData" value="CUI as published for Secure Enclave IL4"></label>
        <label class="field">Extra interconnection<select id="declPath"><option>None</option><option>Sister-system path</option></select></label>
        <p class="caption">CSP asked: ${corr.message || 'Bring declared use inside the published offering.'}</p>`,
      actions: goBtn('Cancel', '!drawer-close') + goBtn('Send to CSP', '!save-declared', true),
    });
    return;
  }
  if (name === 'save-declared') {
    if (!canWrite()) return;
    const corr = correctionForCurrent();
    if (!corr || !corr.cspMissionId) { toast('No CSP correction on this system.'); return; }
    const il = val('declIl') || 'IL4';
    const data = val('declData') || 'CUI as published';
    const path = val('declPath') || 'None';
    const declared = `${il} · ${data} · ${path === 'None' ? 'hub peering only' : path}`;
    putCorrection(corr.cspMissionId, {
      ...corr,
      status: 'resubmitted',
      resubmittedAt: new Date().toISOString(),
      declared,
      proposedIl: il,
      proposedData: data,
      proposedPath: path,
    });
    hideDrawer();
    toast('Sent to CSP. Scope fit stays until they re-score. Access unchanged.');
    render('attach');
    return;
  }
  if (name === 'adopt-r3') {
    if (!canWrite()) return;
    sim.adoptedPlanning = true;
    sim.stayRationale = '';
    toast('Adopted r3. Residual customer/shared work remains open. Not an MO ATO.');
    render('residual');
    return;
  }
  if (name === 'stay') {
    if (!canWrite()) return;
    modal('Stay on adopted r2',
      '<p>CSP published r3. Staying is allowed with rationale. You remain drifted. This does not edit the CSP CRM.</p><label class="field">Rationale<textarea id="stayText">Annual review scheduled; r3 CRM change is documentation-only.</textarea></label>',
      [goBtn('Cancel', '!close'), goBtn('Stay on r2', '!stay-save', true)]);
    return;
  }
  if (name === 'stay-save') {
    sim.stayRationale = (document.getElementById('stayText') || {}).value || 'Staying on r2.';
    closeDialog();
    toast('Staying on r2. Drift remains visible.');
    render('adopt');
    return;
  }
  if (name === 'drawer-close') { hideDrawer(); return; }
  if (name === 'edit-mission') { openMissionDrawer(); return; }
  if (name === 'save-mission') {
    if (!canWrite()) return;
    sim.mission = { statement: val('f-statement'), purpose: val('f-purpose'), justification: val('f-just'), functions: val('f-fn') };
    hideDrawer(); toast('Mission draft saved. Not an approved SSP and not an ATO.'); render('mission'); return;
  }
  if (name === 'add-user') { openUserDrawer(''); return; }
  if (name.startsWith('edit-user:')) { openUserDrawer(name.slice(10)); return; }
  if (name === 'save-user') {
    const rec = { id: sim.editId || ('u' + Date.now()), name: val('f-name'), need: val('f-need'), auth: val('f-auth') };
    if (sim.editId) sim.users = sim.users.map(u => u.id === sim.editId ? rec : u); else sim.users.push(rec);
    hideDrawer(); toast('User population draft saved. This does not grant access.'); render('users'); return;
  }
  if (name === 'edit-host') { openHostDrawer(); return; }
  if (name === 'save-host') {
    sim.hosting = { model: val('f-model'), cloud: val('f-cloud'), zones: val('f-zones'), location: val('f-loc') };
    hideDrawer(); toast('Hosting draft saved. Association is not inheritance.'); render('definition'); return;
  }
  if (name === 'add-data') { openDataDrawer(''); return; }
  if (name.startsWith('edit-data:')) { openDataDrawer(name.slice(10)); return; }
  if (name === 'save-data') {
    const rec = { id: sim.editId || ('d' + Date.now()), name: val('f-name'), sensitivity: val('f-sens'), privacy: val('f-priv') };
    if (sim.editId) sim.dataTypes = sim.dataTypes.map(d => d.id === sim.editId ? rec : d); else sim.dataTypes.push(rec);
    hideDrawer(); toast('Information type saved. Categorization is not approved.'); render('data'); return;
  }
  if (name === 'edit-context') { openContextDrawer(); return; }
  if (name === 'save-context') {
    sim.dataContext = val('f-ctx'); hideDrawer(); toast('Handling context saved. Not a PTA/PIA.'); render('data'); return;
  }
  if (name === 'add-comp') { openComponentDrawer(''); return; }
  if (name.startsWith('edit-comp:')) { openComponentDrawer(name.slice(10)); return; }
  if (name === 'save-component') {
    const rec = { id: sim.editId || ('c' + Date.now()), name: val('f-name'), placement: val('f-place'), note: val('f-note') };
    if (sim.editId) sim.components = sim.components.map(c => c.id === sim.editId ? rec : c); else sim.components.push(rec);
    hideDrawer(); toast('Boundary record saved. Not a published SSP boundary.'); render('boundary'); return;
  }
  if (name === 'add-if') { openIfDrawer(''); return; }
  if (name.startsWith('edit-if:')) { openIfDrawer(name.slice(8)); return; }
  if (name === 'save-if') {
    const rec = { id: sim.editId || ('n' + Date.now()), service: val('f-name'), port: val('f-port'), protocol: val('f-proto'), direction: val('f-dir') };
    if (sim.editId) sim.interfaces = sim.interfaces.map(n => n.id === sim.editId ? rec : n); else sim.interfaces.push(rec);
    hideDrawer(); toast('Network interface saved. Not a firewall change.'); render('ports'); return;
  }
  if (name === 'add-ix') { openIxDrawer(''); return; }
  if (name.startsWith('edit-ix:')) { openIxDrawer(name.slice(8)); return; }
  if (name === 'save-ix') {
    const rec = { id: sim.editId || ('i' + Date.now()), target: val('f-name'), owner: val('f-owner'), classification: val('f-class'), direction: val('f-dir'), agreement: val('f-ag') };
    if (sim.editId) sim.interconnects = sim.interconnects.map(x => x.id === sim.editId ? rec : x); else sim.interconnects.push(rec);
    hideDrawer(); toast('Interconnection saved. Not authorization to connect.'); render('ports'); return;
  }
  if (name.startsWith('design-view:')) { sim.designView = name.slice(12); render('design'); return; }
  if (name.startsWith('inspect:')) {
    const id = name.slice(8);
    const labels = { sys: current().name, ops: 'Mission operators', gw: 'Application gateway', db: 'Mission data store', idp: 'Org Entra / CAC' };
    openDrawer({
      title: 'Edit design element',
      desc: 'Stage a governed correction. Renaming changes the design label, not the canonical source. Saving a draft is not approval.',
      body: `<div class="fields">
        <label class="field full">Label<input id="f-name" value="${labels[id] || id}"></label>
        <label class="field full">Does this belong to this system?<select id="f-place"><option>Included in this system</option><option>Outside this system</option><option>Needs confirmation</option></select></label>
      </div>`,
      actions: goBtn('Cancel', '!drawer-close') + goBtn('Save draft', '!save-design-el', true),
    });
    return;
  }
  if (name === 'build-design') { toast('Assembled from recorded Mission, Users, Environment, Data, Components, and Ports. Not an authorized design.'); render('design'); return; }
  if (name === 'save-design' || name === 'save-design-el') {
    if (!canWrite()) return;
    hideDrawer(); toast('Design draft saved. Completeness describes this working revision, not AO approval.'); render('design'); return;
  }
  if (name === 'submit-design') {
    if (!canWrite()) return;
    sim.designGov = 'UnderReview'; toast('Submitted for review. Not an authorization decision.'); render('design'); return;
  }
  if (name === 'edit-arch') { openArchDrawer(); return; }
  if (name === 'save-arch') {
    if (!canWrite()) return;
    sim.architecture = val('f-arch'); hideDrawer(); toast('Architecture narrative saved. Not an authorized design.'); render('design'); return;
  }
  if (name === 'add-flow') { openFlowDrawer(''); return; }
  if (name.startsWith('edit-flow:')) { openFlowDrawer(name.slice(10)); return; }
  if (name === 'save-flow') {
    if (!canWrite()) return;
    const rec = { id: sim.editId || ('f' + Date.now()), from: val('f-from'), to: val('f-to'), data: val('f-data'), protection: val('f-prot') };
    if (sim.editId) sim.flows = sim.flows.map(f => f.id === sim.editId ? rec : f); else sim.flows.push(rec);
    hideDrawer(); toast('Data flow saved. Not a live network path.'); render('design'); return;
  }
  if (name === 'add-diagram') { openDiagramDrawer(''); return; }
  if (name.startsWith('edit-diagram:')) { openDiagramDrawer(name.slice(13)); return; }
  if (name === 'save-diagram') {
    if (!canWrite()) return;
    const rec = { id: sim.editId || ('g' + Date.now()), name: val('f-name'), kind: val('f-kind'), status: val('f-status') };
    if (sim.editId) sim.diagrams = sim.diagrams.map(g => g.id === sim.editId ? rec : g); else sim.diagrams.push(rec);
    hideDrawer(); toast('Diagram record saved. Not AO acceptance of the picture.'); render('design'); return;
  }
  if (name === 'save-def') { toast('Draft saved. Not an approved SSP baseline and not an ATO.'); return; }
  if (name.startsWith('rmf-view:')) { sim.rmfView = name.slice(9); render('overview'); return; }
  if (name === 'confirm-rmf') {
    openDrawer({
      title: 'Confirm recorded RMF phase',
      desc: 'This records the phase through the audited lifecycle. It does not approve documents or issue authorization.',
      body: `<label class="field full">Phase to record<select id="f-phase">${rmfJourney.map(p => `<option ${p.phase === sim.rmfView ? 'selected' : ''}>${p.phase}</option>`).join('')}</select></label>
        <label class="field full">Basis for this recorded phase<textarea id="f-notes" rows="4" required>Residual implementation in progress; inherited AU/SC cite adopted r${current().adopted || 'n/a'}.</textarea></label>`,
      actions: goBtn('Cancel', '!drawer-close') + goBtn('Confirm phase', '!save-rmf', true),
    }); return;
  }
  if (name === 'save-rmf') {
    if (!canWrite()) return;
    sim.rmfConfirmed = val('f-phase') || sim.rmfView; sim.rmfView = sim.rmfConfirmed;
    hideDrawer(); toast('Phase recorded. Documents were not approved and no ATO was issued.'); render('overview'); return;
  }
  if (name === 'check-ready') {
    sim.readinessStatus = 'Current saved result';
    toast('Requirements checked. This does not approve documents, accept inheritance, submit eMASS or authorize the system.');
    render('overview'); return;
  }
  if (name === 'readiness') { toast('Residual readiness checked. This is not an AO decision or eMASS acceptance.'); return; }
  if (name === 'record') {
    if (!canWrite()) return;
    modal('Record external decision',
      '<p>Retain the issuing authority and outcome. This does not issue an ATO from the portal.</p><p>Source: eMASS · Decision: ATO · 12 Jan 2025</p>',
      [goBtn('Cancel', '!close'), goBtn('Record fact', '!recorded', true)]);
    return;
  }
  if (name === 'recorded') { closeDialog(); toast('External decision recorded. The portal did not issue it.'); render('package'); }
}

function bind() {
  document.body.addEventListener('click', e => {
    const go = e.target.closest('[data-go]');
    if (go) {
      e.preventDefault();
      if ($('dialog').open) closeDialog();
      hideDrawer();
      render(go.getAttribute('data-go'));
      return;
    }
    const act = e.target.closest('[data-action]');
    if (act && act.tagName === 'BUTTON') { e.preventDefault(); onAction(act.getAttribute('data-action')); }
  });
  document.body.addEventListener('input', e => {
    if (e.target.getAttribute('data-action') === 'create-name') sim.createName = e.target.value;
  });
  document.body.addEventListener('change', e => {
    const act = e.target.getAttribute('data-action');
    if (act === 'decl-il') sim.declare.il = e.target.value;
    if (act === 'decl-data') sim.declare.data = e.target.value;
    if (act === 'decl-path') sim.declare.interconnect = e.target.value;
    if (act === 'decl-env') sim.declare.env = e.target.value;
    if (act === 'decl-auth') sim.declare.authPath = e.target.value;
    if (act === 'decl-sub') sim.declare.existingSub = e.target.value === 'existing';
  });
  $('screen').addEventListener('change', () => render($('screen').value));
  $('state').addEventListener('change', () => { sim.state = $('state').value; render(sim.page || 'home'); });
  $('close').addEventListener('click', closeDialog);
  $('drawerClose').addEventListener('click', hideDrawer);
  $('drawerBackdrop').addEventListener('click', hideDrawer);
  document.querySelector('.brand').addEventListener('click', e => { e.preventDefault(); hideDrawer(); render('home'); });
  window.addEventListener('storage', e => {
    if (e.key === CORRECTION_KEY || e.key === ACCESS_KEY) render(sim.page || 'home');
  });
}

$('screen').innerHTML = Object.entries(pages).map(([id, p]) => `<option value="${id}">${p.title}</option>`).join('');
bind();
applyCorrectionQuery();
applyAccessQuery();
render((location.hash || '#home').slice(1));
