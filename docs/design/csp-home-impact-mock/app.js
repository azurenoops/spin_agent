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

const offerings = [
  { id: 'enclave', name: 'Secure Enclave IL4', version: 'v2.1', publication: 'Published', ato: 'Effective · exp 11 Mar 2027', worst: 'Invalidating' },
  { id: 'saas', name: 'Gov SaaS IL2', version: 'v4', publication: 'Published', ato: 'Conditional · CRM gap AC-2', worst: 'Exception' },
  { id: 'connect', name: 'Boundary Connect', version: 'v1', publication: 'Published', ato: 'Effective', worst: 'Fits' },
  { id: 'pilot', name: 'IL6 Pilot', version: 'draft', publication: 'Draft', ato: 'Not recorded', worst: '—' },
];

const missions = [
  { id: 'csw', org: '14th CSW', system: 'Mission Planning', offering: 'enclave', release: 'r2', severity: 'Invalidating', access: 'approved',
    authorized: 'IL4 CUI · enclave compute/storage · peering to CSP hub only',
    declared: 'IL5 mission data · same enclave · additional path to a sister IL5 system',
    why: [['Impact level', 'Declared IL5 exceeds offering IL4', 'OfferingVersion.impactLevel'],
      ['Data types', 'Mission data class not in authorized set', 'OfferingVersion.dataTypes'],
      ['Interconnections', 'Sister-system path not in published boundary', 'OfferingVersion.boundary'],
      ['CRM', 'Still inheriting AU/SC as if in-scope', 'InheritanceRelease r2']] },
  { id: 'aetc', org: 'AETC', system: 'Training Cloud', offering: 'enclave', release: 'r2', severity: 'Exception', access: 'approved',
    authorized: 'IL4 CUI · enclave · hub peering only',
    declared: 'IL4 CUI · extra interconnection to sister training system',
    why: [['Interconnections', 'Extra mission-to-mission path', 'OfferingVersion.boundary']] },
  { id: 'afrl', org: 'AFRL', system: 'Lab Enclave', offering: 'enclave', release: 'r1', severity: 'Exception', access: 'approved',
    authorized: 'Use fits r1 CRM',
    declared: 'Use still matches r1; current publication is r2',
    why: [['Inheritance', 'Adopted r1 · current r2', 'InheritanceRelease']] },
  { id: 'ngb', org: 'NGB', system: 'Portal', offering: 'enclave', release: 'r2', severity: 'Fits', access: 'approved',
    authorized: 'IL4 CUI · published boundary',
    declared: 'IL4 CUI · published boundary',
    why: [['All checks', 'Declared use inside authorized use', 'OfferingVersion v2.1']] },
  { id: 'wing', org: '97th Wing', system: 'Ops Desk', offering: 'saas', release: 'r3', severity: 'Exception', access: 'approved',
    authorized: 'IL2 · SaaS CRM r4 (current)',
    declared: 'IL2 · still on inherited r3',
    why: [['Drift', 'Adopted r3; current publication r4', 'InheritanceRelease']] },
];

const controls = [
  { id: 'AC-2', family: 'AC', name: 'Account management', enclave: 'shared', saas: 'unassigned' },
  { id: 'IA-2', family: 'IA', name: 'Identification and authentication', enclave: 'customer', saas: 'customer' },
  { id: 'AU-2', family: 'AU', name: 'Event logging', enclave: 'inherited', saas: 'inherited' },
  { id: 'AU-3', family: 'AU', name: 'Content of audit records', enclave: 'inherited', saas: 'inherited' },
  { id: 'SC-7', family: 'SC', name: 'Boundary protection', enclave: 'inherited', saas: 'shared' },
  { id: 'CM-6', family: 'CM', name: 'Configuration settings', enclave: 'shared', saas: 'customer' },
  { id: 'SA-9', family: 'SA', name: 'External system services', enclave: 'unassigned', saas: 'inherited' },
];

const nav = [
  ['home', 'Overview'], ['offerings', 'Offerings'], ['impact', 'Impact'],
  ['subscribers', 'Mission systems'], ['conmon', 'Changes'], ['access', 'Administration'],
];

const pages = {
  home: { title: 'Your provider workspace', eyebrow: 'Operate', desc: 'Keep the recorded offering authorization defensible. Prove attached missions’ declared use is still inside that scope — without reading their packages.' },
  offerings: { title: 'Offerings', eyebrow: 'Offerings', desc: 'Versioned products MOs can attach to. Worst scope fit is the signal, not subscriber count.' },
  offering: { title: 'Offering overview', eyebrow: 'Offerings', desc: 'One offering as a product: boundary, versions, CRM, inheritance, subscribers.' },
  crm: { title: 'Shared responsibility', eyebrow: 'Offerings', desc: 'Every in-scope control is inherited, shared, or customer — pinned to a CRM version.' },
  inherit: { title: 'Inheritance packages', eyebrow: 'Authorization', desc: 'Pinned releases MOs adopt. Current publication never silently replaces what they adopted.' },
  impact: { title: 'Tenant impact', eyebrow: 'Consumers', desc: 'Which attached missions are still inside the recorded authorization.' },
  'mo-impact': { title: 'Per-MO impact', eyebrow: 'Consumers', desc: 'One impact record. Request correction goes to the Mission Owner’s Provider hosting tab — not Administration and not their SSP.' },
  whatif: { title: 'What-if attach', eyebrow: 'Consumers', desc: 'Request → declare use → estimate scope fit against the recorded offering authorization → refuse or approve.' },
  subscribers: { title: 'MO subscribers', eyebrow: 'Consumers', desc: 'Access requests, approved subscriptions, adopted release, drift.' },
  conmon: { title: 'Continuous monitoring', eyebrow: 'Sustain', desc: 'Offering ConMon: CSP-owned evidence freshness. Not MO-sub telemetry.' },
  access: { title: 'Tenant access', eyebrow: 'Admin', desc: 'Who may publish offerings and see MO impact.' },
};

const sim = {
  offering: 'enclave', severity: 'all', selected: 'csw', family: 'all',
  correction: {}, state: 'ready', crm: {},
  crmPublished: { enclave: 2, saas: 4, connect: 1, pilot: 0 },
  inheritCurrent: { enclave: 2, saas: 4, connect: 1, pilot: 0 },
  pending: [],
  extras: [],
  accessId: null,
  lastAccessId: null,
  whatIf: { step: 0, org: '551st ISRW', system: 'ISR Ground Station', il: 'IL4', data: 'CUI as published', interconnect: 'None', env: 'Dev', authPath: 'IATT' },
};

function offering() { return offerings.find(o => o.id === sim.offering); }
function selectedMo() { return missions.concat(sim.extras).find(m => m.id === sim.selected) || missions[0]; }

const CORRECTION_KEY = 'spin.mock.declaredUseCorrections';
const MO_SYSTEM = { csw: 'planning', aetc: 'training' };

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
  all[id] = { ...(all[id] || {}), ...patch, id };
  localStorage.setItem(ACCESS_KEY, JSON.stringify(all));
  return all[id];
}
function syncAccessInbox() {
  sim.pending = Object.values(loadAccess()).filter(r => r.status === 'requested');
}
function moAttachLink(id) {
  if (!id) return '';
  const status = (loadAccess()[id] && loadAccess()[id].status) || 'requested';
  return `<p class="caption"><a href="../mo-org-portal-mock/index.html?access=${id}&accessStatus=${status}#attach:${id}">Open Mission Owner · Provider hosting</a> · They still allocate Azure and adopt. Approve is not inheritance.</p>`;
}
function moHostingLink(corr) {
  if (!corr || !corr.moSystemId) return '';
  return `<p class="caption"><a href="../mo-org-portal-mock/index.html?correction=${corr.cspMissionId}#attach:${corr.moSystemId}">Open Mission Owner · Provider hosting</a> · Declared use only. Not their SSP.</p>`;
}
function applyCorrectionQuery() {
  const q = new URLSearchParams(location.search);
  const resent = q.get('resubmitted');
  if (!resent) return;
  const cur = loadCorrections()[resent] || {};
  const m = missions.find(x => x.id === resent);
  putCorrection(resent, {
    moSystemId: MO_SYSTEM[resent] || cur.moSystemId || null,
    org: cur.org || (m && m.org) || '',
    system: cur.system || (m && m.system) || '',
    offering: cur.offering || 'Secure Enclave IL4',
    status: 'resubmitted',
    authorized: cur.authorized || (m && m.authorized),
    declared: cur.declared || (m && m.declared),
    message: cur.message || 'Declared use is outside the published boundary.',
  });
}
function allMissions() { return missions.concat(sim.extras); }
function visibleMissions() {
  return allMissions().filter(m => m.offering === sim.offering && m.access === 'approved')
    .filter(m => sim.severity === 'all' || m.severity.toLowerCase() === sim.severity);
}
function counts(list) {
  return {
    total: list.length,
    fit: list.filter(m => m.severity === 'Fits').length,
    exception: list.filter(m => m.severity === 'Exception').length,
    invalidating: list.filter(m => m.severity === 'Invalidating').length,
  };
}
function tone(severity) {
  return severity === 'Invalidating' ? 'red' : severity === 'Exception' ? 'amber' : severity === 'Fits' ? 'green' : '';
}
function fitLabel(severity) {
  return severity === 'Invalidating' ? 'Out of scope' : severity === 'Fits' ? 'In scope' : severity === 'Exception' ? 'Exception' : severity;
}
const chipName = { all: 'All', invalidating: 'Out of scope', exception: 'Exception', fits: 'In scope' };
function allocOf(control) {
  const draft = sim.crm[sim.offering] || {};
  return draft[control.id] || control[sim.offering] || 'unassigned';
}
function crmGaps() { return controls.filter(c => allocOf(c) === 'unassigned'); }
function crmLabel() { return `CRM v${sim.crmPublished[sim.offering] || 0}`; }
function releaseLabel() {
  const n = sim.inheritCurrent[sim.offering];
  return n ? `r${n}` : 'None';
}
function drifted(m) { return m.release !== `r${sim.inheritCurrent[m.offering]}`; }

function toast(msg) {
  const el = $('toast');
  el.textContent = msg;
  el.style.display = 'block';
  clearTimeout(toast._t);
  toast._t = setTimeout(() => { el.style.display = 'none'; }, 4000);
}
function closeDialog() { $('dialog').close(); }
function modal(title, body, actions) {
  $('dialogTitle').textContent = title;
  $('dialogBody').innerHTML = body;
  $('dialogActions').innerHTML = actions.join('');
  $('dialog').showModal();
}
function offeringTabs(active) {
  const items = [['offering', 'Overview'], ['crm', 'CRM'], ['inherit', 'Inheritance'], ['subscribers', 'Subscribers']];
  return `<nav id="tabs" aria-label="Offering sections">${items.map(([id, label]) =>
    `<button data-go="${id}" class="${id === active ? 'active' : ''}">${label}</button>`).join('')}</nav>`;
}
function emptyHome() {
  return `<div class="banner"><div><b>No Mission Owners attached</b><p>Publish an inheritance release, then wait for access requests.</p></div>${goBtn('Open offerings', 'offerings', true)}</div>`;
}
function errorView(title) {
  return `<div class="banner amber"><div><b>${title} unavailable</b><p>This does not mean there is no risk.</p></div>${goBtn('Retry (simulated)', '!retry')}</div>`;
}
function restrictedView() {
  return `<div class="banner"><div><b>Assessor preview</b><p>Published CRM and inheritance are visible. Out-of-scope consumer detail is limited to ISSM / AO.</p></div></div>`;
}

function homeReady() {
  const c = counts(allMissions().filter(m => m.access === 'approved'));
  return `<div class="metrics">
      ${metric('Offerings', '4', '3 published · 1 draft')}
      ${metric('Recorded authorization', 'Effective', 'Earliest expiry 11 Mar 2027 · 1 conditional · not issued here')}
      ${metric('MOs associated', String(c.total), `Associated — not inherited · ${c.fit} in scope · ${c.exception} exception · ${c.invalidating} out of scope`)}
      ${metric('Needs a human', String(1 + sim.pending.length), `${c.invalidating} out of scope · ${sim.pending.length} access request`)}
    </div>
    <p class="caption">Job: prove tenant use still matches the authorized offering. Not “how each MO affects your ATO.” Not the Mission Owner package.</p>
    <h2>Three facts — never one green light</h2>
    <div class="facts">
      <div class="fact">${pill('Recorded authorization', 'green')} <strong>3 effective · 1 conditional</strong><p>Decision as recorded. Not an issued ATO.</p></div>
      <div class="fact">${pill('Package readiness', 'amber')} <strong>2 ready · 2 incomplete</strong><p>Provider package completeness. Not authorization.</p></div>
      <div class="fact">${pill('Monitoring health', 'red')} <strong>1 stale · AU 41 days</strong><p>CSP-owned offering evidence. Not MO-sub telemetry.</p></div>
    </div>
    <p class="caption">Offering inheritance package: recorded authorization cites · CRM and pinned releases · provider evidence summaries · declared-use scope fit. Change-impact fan-out is a different screen.</p>
    ${panelish('Needs a human', table(['Item', 'Why', 'Scope fit', ''], [
      ['14th CSW / Mission Planning', 'IL5 data on IL4 offering', pill('Out of scope', 'red'), goBtn('Open record', 'mo-impact:csw', true)],
      ...sim.pending.map(p => [`${p.org} / ${p.system}`, 'Access requested — no impact record yet', pill('Requested', 'purple'), goBtn('Run what-if', 'whatif')]),
      ['AFRL / Lab Enclave', `Adopted r1; current ${releaseLabel()}`, pill('Drifted', 'amber'), goBtn('Inheritance', 'inherit')],
    ]))}
    ${panelish('Offerings', table(['Offering', 'CRM / inheritance', 'Worst scope fit', ''],
      offerings.map(o => [o.name + `<small>${o.version}</small>`, `${sim.crmPublished[o.id] ? 'CRM v' + sim.crmPublished[o.id] : 'No CRM'} · ${sim.inheritCurrent[o.id] ? 'r' + sim.inheritCurrent[o.id] : 'No release'}`,
        o.worst === '—' ? '—' : pill(fitLabel(o.worst), tone(o.worst)), goBtn('Open', 'offering:' + o.id)])))}`;
}

function offeringsReady() {
  return panelish('Your offerings', table(['Offering', 'Publication', 'Recorded authorization', 'Worst scope fit', ''],
    offerings.map(o => [o.name + `<small>${o.version}</small>`, o.publication, o.ato,
      o.worst === '—' ? '—' : pill(fitLabel(o.worst), tone(o.worst)), goBtn('Manage', 'offering:' + o.id, o.id === 'enclave')])));
}

function offeringReady() {
  const o = offering();
  const attached = allMissions().filter(m => m.offering === o.id && m.access === 'approved');
  const gaps = crmGaps();
  return offeringTabs('offering') + `<div class="metrics">
      ${metric('Publication', o.publication, o.version)}
      ${metric('CRM', sim.crmPublished[o.id] ? 'v' + sim.crmPublished[o.id] : 'None', gaps.length ? gaps.length + ' unassigned' : 'No draft gaps')}
      ${metric('Inheritance', sim.inheritCurrent[o.id] ? 'r' + sim.inheritCurrent[o.id] : 'None', attached.filter(drifted).length + ' drifted')}
      ${metric('Subscribers', String(attached.length), sim.pending.filter(p => p.offering === o.id).length + ' requested')}
    </div>
    <div class="compare">
      <section class="panel"><h3>Authorized use</h3><p>IL4 CUI · enclave compute/storage · peering to CSP hub only.</p>${goBtn('Edit CRM', 'crm')}</section>
      <section class="panel"><h3>Parent ATO</h3><p>Effective through 11 Mar 2027. Package ready. Monitoring stale (AU 41 days).</p><p class="caption">Recording a decision is not issuing one.</p></section>
    </div>`;
}

function crmReady() {
  const families = ['all', ...new Set(controls.map(c => c.family))];
  const rows = controls.filter(c => sim.family === 'all' || c.family === sim.family);
  const gaps = crmGaps();
  const allocBtns = c => ['inherited', 'shared', 'customer', 'unassigned'].map(a =>
    `<button data-action="alloc:${c.id}:${a}" class="${allocOf(c) === a ? 'primary' : ''}">${a}</button>`).join(' ');
  return offeringTabs('crm') + `<div class="metrics">
      ${metric('Published', crmLabel(), 'Pinned; missions do not auto-move')}
      ${metric('Inherited', String(controls.filter(c => allocOf(c) === 'inherited').length), 'CSP implements')}
      ${metric('Shared', String(controls.filter(c => allocOf(c) === 'shared').length), 'Both have duties')}
      ${metric('Unassigned', String(gaps.length), gaps.length ? 'Blocks CRM publish' : 'Ready to publish')}
    </div>
    <div class="filterchips" role="group" aria-label="Control family">${families.map(f =>
      `<button data-action="family:${f}" class="${sim.family === f ? 'active' : ''}">${f === 'all' ? 'All families' : f}</button>`).join('')}</div>
    ${gaps.length ? `<div class="banner amber"><div><b>Cannot publish CRM with unassigned controls.</b><p>${gaps.map(g => g.id).join(', ')} have no allocation.</p></div></div>` : ''}
    ${panelish('Control allocation', table(['Control', 'Duty', 'Allocation', ''],
      rows.map(c => [c.id + `<small>${c.name}</small>`, allocOf(c) === 'inherited' ? 'CSP' : allocOf(c) === 'customer' ? 'MO' : allocOf(c) === 'shared' ? 'Both' : 'Unknown',
        pill(allocOf(c), allocOf(c) === 'unassigned' ? 'amber' : allocOf(c) === 'inherited' ? 'green' : ''), allocBtns(c)])))}
    <p class="caption">Publishing a CRM version is not publishing an inheritance release and is not an ATO.</p>`;
}

function inheritReady() {
  const current = sim.inheritCurrent[sim.offering];
  const attached = allMissions().filter(m => m.offering === sim.offering && m.access === 'approved');
  const releases = [];
  for (let n = current; n >= 1; n--) {
    releases.push({
      id: 'r' + n,
      crm: 'CRM v' + Math.max(1, n === current ? sim.crmPublished[sim.offering] : n),
      current: n === current,
      adopters: attached.filter(m => m.release === 'r' + n),
    });
  }
  return offeringTabs('inherit') + `<div class="metrics">
      ${metric('Current release', current ? 'r' + current : 'None', crmLabel() + ' + ATO cites')}
      ${metric('Adopters on current', String(attached.filter(m => m.release === 'r' + current).length), 'Pinned, not latest')}
      ${metric('Drifted', String(attached.filter(drifted).length), 'Still on an older release')}
      ${metric('CRM draft gaps', String(crmGaps().length), 'Inheritance publish needs a published CRM')}
    </div>
    ${panelish('Releases', table(['Release', 'Contains', 'Adopters', 'State', ''],
      releases.map(r => [r.id, r.crm + ' · parent ATO cites', String(r.adopters.length),
        r.current ? pill('Current', 'green') : pill('Superseded', ''),
        r.adopters.length ? goBtn('See adopters', 'subscribers') : '—'])))}
    ${panelish('Drifted missions', attached.filter(drifted).length
      ? table(['MO / system', 'Adopted', 'Current', ''], attached.filter(drifted).map(m =>
        [`${m.org} / ${m.system}`, m.release, 'r' + current, goBtn('Impact', 'mo-impact:' + m.id)]))
      : '<p>No drifted adopters on this offering.</p>')}
    <p class="caption">Publishing a new release leaves existing MOs on their adopted release. They become drifted. They are not silently upgraded.</p>`;
}

function impactReady() {
  const o = offering();
  const list = visibleMissions();
  const all = allMissions().filter(m => m.offering === sim.offering && m.access === 'approved');
  const c = counts(all);
  const chips = ['all', 'invalidating', 'exception', 'fits'].map(id =>
    `<button data-action="severity:${id}" class="${sim.severity === id ? 'active' : ''}">${chipName[id]}</button>`).join('');
  return `<div class="toolbar"><label class="field">Offering
      <select data-action="offering">${offerings.map(x => `<option value="${x.id}" ${x.id === sim.offering ? 'selected' : ''}>${x.name} · ${x.version}</option>`).join('')}</select>
    </label>${goBtn('What-if attach', 'whatif', true)}</div>
    <p>${pill(o.name)} ${pill(o.version)} ${pill('Inheritance ' + releaseLabel())}</p>
    <div class="metrics">
      ${metric('Attached', String(c.total), 'Approved subscriptions')}
      ${metric('In scope', String(c.fit), 'Declared use inside CRM and IL')}
      ${metric('Exception', String(c.exception), 'CRM or extra path; still at authorized IL')}
      ${metric('Out of scope', String(c.invalidating), 'Declared use is not covered by the recorded authorization')}
    </div>
    <div class="filterchips" role="group" aria-label="Filter by scope fit">${chips}</div>
    ${list.length ? panelish('Declared use vs recorded authorization', table(['MO / system', 'Release', 'Declared vs authorized', 'Scope fit', ''],
      list.map(m => [`${m.org} / ${m.system}`, m.release + (drifted(m) ? ` ${pill('Drifted', 'amber')}` : ''),
      m.severity === 'Fits' ? 'Inside CRM and IL' : m.why[0][1], pill(fitLabel(m.severity), tone(m.severity)), goBtn('Open', `mo-impact:${m.id}`)]),
      list.map(m => `severity-${m.severity.toLowerCase()}`))) : '<p>No missions match this filter.</p>'}
    <div class="banner amber"><div><b>The recorded authorization is not automatically withdrawn.</b><p>Out-of-scope use is queued for ISSM action. This screen does not load MO SSP, SAR, or the MO AO decision.</p></div></div>`;
}

function moReady() {
  const m = selectedMo();
  const corr = loadCorrections()[m.id];
  const asked = corr && corr.status === 'requested';
  const resent = corr && corr.status === 'resubmitted';
  const declared = (corr && corr.declared) || m.declared;
  sim.correction[m.id] = Boolean(corr);
  const banner = asked
    ? `<div class="banner amber"><div><b>Correction requested</b><p>Sent to ${m.org} on <b>Provider hosting</b>. Subscription stays ${m.access}. Scope fit stays ${fitLabel(m.severity)} until they resubmit declared use and you re-score.</p></div></div>${moHostingLink(corr)}`
    : resent
      ? `<div class="banner amber"><div><b>Mission Owner resubmitted declared use</b><p>Re-score scope fit. Access stays ${m.access}. Resubmit is not an SSP share and does not flip in-scope by itself.</p></div></div>${moHostingLink(corr)}`
      : '';
  return `${banner}
  <p>${pill(fitLabel(m.severity), tone(m.severity))} ${pill(offering().name)} ${pill('Adopted ' + m.release)} ${pill('Access: ' + m.access)}</p>
    <h2>Declared use vs authorized use</h2>
    <div class="compare">
      <section class="panel"><h3>Published authorized use</h3><p>${m.authorized}</p><p class="caption">Pinned to offering version and ${m.release}.</p></section>
      <section class="panel"><h3>This MO’s declared use</h3><p>${declared}</p><p class="caption">From the subscription declaration / what-if. Not inferred from the MO SSP.</p></section>
    </div>
    ${panelish('Why this scope fit', table(['Check', 'Result', 'Cite'], m.why))}
    <p class="caption">${goBtn('Open adopted CRM release', 'inherit')} ${goBtn('Open current CRM draft', 'crm')}</p>`;
}

function estimateWhatIf() {
  const w = sim.whatIf;
  if (w.il === 'IL5' || w.data.includes('IL5')) {
    return { severity: 'Invalidating', reason: 'Declared IL5 exceeds offering IL4.', change: true,
      crm: 'AU/SC inheritance would be claimed outside authorized scope.', poam: 'New shared-control exceptions.', conmon: 'Extra classification watch.' };
  }
  if ((w.authPath || 'IATT') === 'IATT' && w.env === 'Prod') {
    return { severity: 'Invalidating', reason: 'IATT declared in Prod. Testing in production is out of authorized scope. This is not an ATO.', change: false,
      crm: 'Do not adopt the operational inheritance release for an IATT.', poam: 'No new CSP POA&M from this portal.', conmon: 'Offering ConMon only.' };
  }
  if ((w.authPath || 'IATT') === 'IATT' && w.env === 'Test') {
    return { severity: 'Exception', reason: 'IATT declared in Test. Recorded test scope is Dev. Moving IATT to Test because Dev is locked is hosting drift, not “closer to ATO.”', change: false,
      crm: 'Test CRM/release if recorded — not the prod inheritance release.', poam: 'No new CSP POA&M.', conmon: 'Watch the Test lane; not MO-sub telemetry.' };
  }
  if ((w.authPath || 'IATT') === 'ATO' && w.env !== 'Prod') {
    return { severity: 'Exception', reason: `ATO declared in ${w.env}. Operational authorization is recorded for Prod unless that lane is the operational system.`, change: false,
      crm: 'Do not treat a Dev/Test sub as the operational inheritance.', poam: 'No new CSP POA&M.', conmon: 'Offering ConMon only.' };
  }
  if (w.interconnect !== 'None') {
    return { severity: 'Exception', reason: 'Extra interconnection is outside the published boundary.', change: false,
      crm: 'SC-7 stays inherited; MO must document the extra path as customer duty.', poam: 'No new CSP POA&M.', conmon: 'Watch the extra path.' };
  }
  return { severity: 'Fits', reason: `Declared ${(w.authPath || 'IATT')} in ${w.env || 'Dev'} is inside IL4 and the published CRM.`, change: false,
    crm: 'Inherited AU/SC remain valid for this path/lane.', poam: 'No new CSP POA&M.', conmon: 'No extra load.' };
}

function whatIfReady() {
  const w = sim.whatIf;
  const labels = ['Request', 'Declare use', 'Estimate', 'Decide'];
  const req = (sim.accessId && loadAccess()[sim.accessId]) || sim.pending[0];
  if (w.step === 0) {
    return steps(labels, 0) + (req && req.status === 'requested'
      ? panelish('Pending access request', `<div class="kv"><span>Organization</span><span>${req.org}</span></div>
          <div class="kv"><span>System</span><span>${req.system}</span></div>
          <div class="kv"><span>Offering</span><span>${offering().name} ${offering().version}</span></div>
          <div class="kv"><span>Declared use (from MO)</span><span>${req.authPath || w.authPath || 'IATT'} · ${req.env || w.env || 'Dev'} · ${req.il || w.il} · ${req.data || w.data} · ${req.interconnect || w.interconnect}</span></div>
          <p class="caption">This request originated on MO Provider hosting. Access is not approved. There is no inheritance and no Azure allocation yet.</p>${moAttachLink(req.id)}`)
      : `<div class="banner"><div><b>No pending request</b><p>Requests originate on the Mission Owner’s Provider hosting tab — not this catalog.</p></div>
          <p class="caption"><a href="../mo-org-portal-mock/index.html#attach:isr">Open 551st ISR · Provider hosting</a></p>
          ${goBtn('Create sample request', '!newrequest')}</div>`);
  }
  if (w.step === 1) {
    return steps(labels, 1) + `<div class="fields">
        <label class="field">Organization<input value="${w.org}" disabled></label>
        <label class="field">System<input value="${w.system}" disabled></label>
        <label class="field">Authorization path<select data-action="wif-auth"><option ${(w.authPath || 'IATT') === 'IATT' ? 'selected' : ''}>IATT</option><option ${w.authPath === 'ATO' ? 'selected' : ''}>ATO</option></select></label>
        <label class="field">Declared env<select data-action="wif-env"><option ${(w.env || 'Dev') === 'Dev' ? 'selected' : ''}>Dev</option><option ${w.env === 'Test' ? 'selected' : ''}>Test</option><option ${w.env === 'Prod' ? 'selected' : ''}>Prod</option></select></label>
        <label class="field">Declared impact level<select data-action="wif-il"><option ${w.il === 'IL4' ? 'selected' : ''}>IL4</option><option ${w.il === 'IL5' ? 'selected' : ''}>IL5</option></select></label>
        <label class="field">Declared data<select data-action="wif-data"><option ${w.data === 'CUI as published' ? 'selected' : ''}>CUI as published</option><option ${w.data.includes('IL5') ? 'selected' : ''}>IL5 mission data</option></select></label>
        <label class="field full">Extra interconnections<select data-action="wif-path"><option ${w.interconnect === 'None' ? 'selected' : ''}>None</option><option ${w.interconnect !== 'None' ? 'selected' : ''}>Sister IL5 system</option></select></label>
      </div>
      <p class="caption" style="margin-top:16px">Declaration is MO-stated use, not an SSP import. IATT ≠ ATO. Dev/Test/Prod are hosting. Estimate uses offering v2.1 + ${crmLabel()} + ${releaseLabel()}.</p>`;
  }
  const est = estimateWhatIf();
  if (w.step === 2) {
    return steps(labels, 2) + `<div class="banner ${est.severity === 'Fits' ? 'green' : 'amber'}"><div><b>Estimated scope fit: ${fitLabel(est.severity)}</b><p>${est.reason} This is not an authorization decision.</p></div></div>
      ${panelish('If approved, effects on the recorded authorization', `<div class="kv"><span>CRM</span><span>${est.crm}</span></div>
        <div class="kv"><span>CSP POA&amp;M</span><span>${est.poam}</span></div>
        <div class="kv"><span>ConMon load</span><span>${est.conmon}</span></div>
        <div class="kv"><span>Significant change required?</span><span>${est.change ? 'Yes — offering would have to expand' : 'No'}</span></div>`)}
      ${panelish('Controls that would stay inherited', table(['Control', 'Allocation', 'If this attach proceeds'],
        controls.filter(c => allocOf(c) === 'inherited').map(c => [c.id, 'inherited', est.severity === 'Invalidating' ? 'Claim would be out of scope' : 'Remains inherited'])))}`;
  }
  return steps(labels, 3) + `<div class="compare">
      <section class="panel"><h3>Refuse attach</h3><p>No subscription. No scope-fit record. Request stays denied.</p>${goBtn('Refuse', '!refuse', est.severity === 'Invalidating')}</section>
      <section class="panel"><h3>Approve with this scope fit</h3><p>Creates a subscription and a scope-fit record: <strong>${fitLabel(est.severity)}</strong>. Does not expand or issue the offering authorization.</p>${goBtn('Approve ' + fitLabel(est.severity), '!approve', est.severity === 'Fits')}</section>
    </div>
    ${est.change ? `<div class="banner amber"><div><b>Do not silently edit the CRM to make this fit.</b><p>IL5 use needs a significant change and a new inheritance release. Existing adopters would then drift.</p></div>${goBtn('Open CRM', 'crm')}</div>` : ''}`;
}

function subscribersReady() {
  const attached = allMissions().filter(m => m.offering === sim.offering);
  const pending = Object.values(loadAccess()).filter(p => p.status === 'requested' && (!p.offering || p.offering === sim.offering));
  const last = sim.lastAccessId && loadAccess()[sim.lastAccessId];
  return offeringTabs('subscribers')
    + (last && last.status === 'approved' ? `<div class="banner"><div><b>Offering access approved for ${last.org} / ${last.system}</b><p>They still allocate Azure and adopt a pinned release. This did not issue an ATO.</p></div></div>${moAttachLink(last.id)}` : '')
    + panelish('Access requests', pending.length
      ? table(['Organization / system', 'Wanted release', 'State', ''], pending.map(p =>
        [`${p.org} / ${p.system}`, releaseLabel(), pill('Requested', 'purple'), goBtn('Run what-if', 'whatif:' + p.id, true)]))
      : '<p>No pending requests on this offering. Requests originate on MO Provider hosting.</p>')
    + panelish('Approved subscriptions', attached.filter(m => m.access === 'approved').length
      ? table(['MO / system', 'Access', 'Adopted', 'Impact', ''], attached.filter(m => m.access === 'approved').map(m =>
        [`${m.org} / ${m.system}`, m.access, m.release + (drifted(m) ? ` ${pill('Drifted', 'amber')}` : ''),
          pill(fitLabel(m.severity), tone(m.severity)), goBtn('Record', 'mo-impact:' + m.id)]))
      : '<p>No approved subscriptions.</p>')
    + panelish('Refused / revoked', attached.filter(m => m.access !== 'approved').length
      ? table(['MO / system', 'Access', ''], attached.filter(m => m.access !== 'approved').map(m =>
        [`${m.org} / ${m.system}`, pill(m.access, 'amber'), '—']))
      : '<p>None in this simulation.</p>');
}

function stub(title, body, action) {
  return `<div class="banner"><div><b>${title}</b><p>${body}</p></div>${action || ''}</div>`;
}

function render(id) {
  syncAccessInbox();
  if (id.startsWith('mo-impact')) {
    const [, mid] = id.split(':');
    if (mid) sim.selected = mid;
    id = 'mo-impact';
  }
  if (id.startsWith('whatif')) {
    const [, aid] = id.split(':');
    if (aid) {
      sim.accessId = aid;
      const hit = loadAccess()[aid];
      if (hit) {
        sim.offering = hit.offering || sim.offering;
        sim.whatIf = {
          step: sim.whatIf.step || 0,
          org: hit.org,
          system: hit.system,
          il: hit.il || 'IL4',
          data: hit.data || 'CUI as published',
          interconnect: hit.interconnect || 'None',
          env: hit.env || 'Dev',
          authPath: hit.authPath || 'IATT',
        };
      }
    }
    id = 'whatif';
  }
  if (id.startsWith('offering:')) {
    sim.offering = id.split(':')[1];
    id = 'offering';
  }
  sim.page = id;
  const meta = pages[id] || pages.home;
  if ([...$('screen').options].some(o => o.value === id)) $('screen').value = id;
  $('breadcrumb').textContent = `Provider / ${meta.eyebrow} / ${id === 'offering' ? offering().name : meta.title}`;
  $('eyebrow').textContent = meta.eyebrow;
  $('title').textContent = id === 'mo-impact' ? `${selectedMo().org} / ${selectedMo().system}`
    : id === 'offering' || id === 'crm' || id === 'inherit' ? `${offering().name} · ${meta.title}` : meta.title;
  $('description').textContent = meta.desc;
  const actions = {
    home: goBtn('Review tenant impact', 'impact', true) + goBtn('Pending request', 'whatif'),
    offerings: goBtn('Open Secure Enclave', 'offering:enclave', true),
    offering: goBtn('Edit CRM', 'crm', true) + goBtn('Inheritance', 'inherit'),
    crm: goBtn('Publish CRM version', '!pubcrm', true) + goBtn('Build inheritance release', 'inherit'),
    inherit: goBtn('Publish inheritance release', '!pubrel', true) + goBtn('See subscribers', 'subscribers'),
    impact: goBtn('What-if attach', 'whatif') + goBtn('Back to home', 'home'),
    'mo-impact': goBtn('Request correction', '!correct', true) + goBtn('Back to tenant impact', 'impact'),
    whatif: (sim.whatIf.step > 0 ? goBtn('Back', '!wif-back') : '') + (sim.whatIf.step < 3 ? goBtn(sim.whatIf.step === 0 ? 'Start declaration' : sim.whatIf.step === 1 ? 'Estimate impact' : 'Decide', '!wif-next', true) : ''),
    subscribers: goBtn('Run what-if', 'whatif', true) + goBtn('Tenant impact', 'impact'),
    conmon: goBtn('Back to home', 'home'),
    access: goBtn('Back to home', 'home'),
  };
  $('actions').innerHTML = actions[id] || '';
  const offerGroup = ['offerings', 'offering', 'crm', 'inherit'];
  $('sidebar').innerHTML = `<small>Provider workspace</small>${nav.map(([nid]) => {
    const on = nid === 'offerings' && offerGroup.includes(id)
      || nid === 'impact' && ['impact', 'mo-impact', 'whatif'].includes(id)
      || nid === 'subscribers' && id === 'subscribers'
      || nid === id;
    return `<button data-go="${nid}" class="${on ? 'active' : ''}">${nav.find(x => x[0] === nid)[1]}</button>`;
  }).join('')}
    <div class="navfoot"><b>This overlay</b><a href="../csp-portal-product-brief.html">Product brief</a><a href="../provider-workspace-mock/index.html">079 mock</a></div>`;
  let html;
  const detailed = ['home', 'impact', 'mo-impact', 'crm', 'inherit', 'whatif', 'subscribers', 'offering'];
  if (sim.state === 'error' && detailed.includes(id)) html = errorView(meta.title);
  else if (sim.state === 'restricted' && ['impact', 'mo-impact', 'whatif'].includes(id)) html = restrictedView();
  else if (sim.state === 'empty' && ['home', 'impact', 'subscribers'].includes(id)) html = emptyHome();
  else if (id === 'home') html = homeReady();
  else if (id === 'offerings') html = offeringsReady();
  else if (id === 'offering') html = offeringReady();
  else if (id === 'crm') html = crmReady();
  else if (id === 'inherit') html = inheritReady();
  else if (id === 'impact') html = impactReady();
  else if (id === 'mo-impact') html = moReady();
  else if (id === 'whatif') html = whatIfReady();
  else if (id === 'subscribers') html = subscribersReady();
  else if (id === 'conmon') html = stub('Parent ATO monitoring', 'AU evidence 41 days stale. Unavailable telemetry is not “no changes.”', goBtn('Back to queue', 'home', true));
  else html = stub('Administration', 'Roster and grants are not in this mock.', goBtn('Back to home', 'home'));
  $('content').innerHTML = html;
  history.replaceState(null, '', '#' + (id === 'mo-impact' ? `mo-impact:${sim.selected}` : id === 'offering' ? `offering:${sim.offering}` : id));
  $('main').focus({ preventScroll: true });
}

function finishApprove(severity) {
  const w = sim.whatIf;
  const req = (sim.accessId && loadAccess()[sim.accessId]) || sim.pending[0];
  const est = estimateWhatIf();
  const id = (req && req.id) || 'new';
  if (req) putAccess(id, { ...req, status: 'approved', decidedAt: new Date().toISOString() });
  if (!sim.extras.some(e => e.id === id)) {
    sim.extras.push({
      id,
      org: w.org, system: w.system, offering: sim.offering,
      release: releaseLabel() === 'None' ? 'r2' : releaseLabel(), drifted: false, severity, access: 'approved',
      authorized: 'IL4 CUI · enclave · hub peering only',
      declared: `${w.authPath || 'IATT'} · ${w.env || 'Dev'} · ${w.il} · ${w.data} · ${w.interconnect}`,
      why: [['What-if estimate', est.reason, 'WhatIf ' + releaseLabel()]],
    });
  }
  sim.whatIf.step = 0;
  sim.selected = id;
  sim.lastAccessId = id;
  sim.accessId = null;
  toast(`Offering access approved as ${fitLabel(severity)}. Not Azure allocation. Not inheritance. Not an ATO.`);
  render('subscribers');
}

function onAction(name) {
  if (name === 'close') return closeDialog();
  if (name === 'retry') { toast('Retry is simulated. Still unavailable in this preview state.'); return; }
  if (name.startsWith('severity:')) { sim.severity = name.slice(9); render('impact'); return; }
  if (name.startsWith('family:')) { sim.family = name.slice(7); render('crm'); return; }
  if (name.startsWith('alloc:')) {
    const [, control, alloc] = name.split(':');
    sim.crm[sim.offering] = sim.crm[sim.offering] || {};
    sim.crm[sim.offering][control] = alloc;
    render('crm');
    return;
  }
  if (name === 'pubcrm') {
    const gaps = crmGaps();
    if (gaps.length) {
      modal('CRM not ready to publish', `<p>Unassigned: ${gaps.map(g => g.id + ' ' + g.name).join(', ')}. Assign inherited / shared / customer first.</p>`,
        [goBtn('Keep editing', '!close', true)]);
      return;
    }
    modal('Publish CRM version', `<p>Publish the next CRM version for ${offering().name}. Missions stay on their adopted inheritance release. They do not auto-receive this CRM.</p>`,
      [goBtn('Cancel', '!close'), goBtn('Publish CRM', '!crmdone', true)]);
    return;
  }
  if (name === 'crmdone') {
    sim.crmPublished[sim.offering] = (sim.crmPublished[sim.offering] || 0) + 1;
    closeDialog();
    toast(`${crmLabel()} published. Build an inheritance release before MOs can adopt it.`);
    render('crm');
    return;
  }
  if (name === 'pubrel') {
    if (!sim.crmPublished[sim.offering]) {
      modal('Need a published CRM', '<p>An inheritance release must cite a published CRM version and parent ATO facts.</p>',
        [goBtn('Open CRM', 'crm', true)]);
      return;
    }
    if (crmGaps().length) {
      modal('Draft CRM has gaps', '<p>Finish CRM allocation before shipping a new inheritance release.</p>',
        [goBtn('Open CRM', 'crm', true)]);
      return;
    }
    const next = (sim.inheritCurrent[sim.offering] || 0) + 1;
    modal('Publish inheritance r' + next, `<p>Contains ${crmLabel()}, evidence summary, and parent ATO cites. Existing adopters stay on their current release and become drifted.</p>`,
      [goBtn('Cancel', '!close'), goBtn('Publish r' + next, '!reldone', true)]);
    return;
  }
  if (name === 'reldone') {
    sim.inheritCurrent[sim.offering] = (sim.inheritCurrent[sim.offering] || 0) + 1;
    closeDialog();
    toast(`Inheritance ${releaseLabel()} published. Current adopters are now drifted.`);
    render('inherit');
    return;
  }
  if (name === 'correct') {
    const m = selectedMo();
    modal('Request Mission Owner correction',
      `<p>Sends a task to <b>${m.org} / ${m.system}</b> on <b>Provider hosting</b>. Declared use only — not their SSP, SAR, or AO package. Access stays ${m.access}. Scope fit stays ${fitLabel(m.severity)} until they resubmit and you re-score.</p>
       <label class="field">Message to Mission Owner<textarea id="correctionMessage">Declared use is outside the published boundary. Bring impact level, data types, and extra paths inside ${offering().name} ${offering().version}.</textarea></label>`,
      [goBtn('Cancel', '!close'), goBtn('Send to Mission Owner', '!sent', true)]);
    return;
  }
  if (name === 'sent') {
    const m = selectedMo();
    const message = (document.getElementById('correctionMessage') || {}).value
      || 'Declared use is outside the published boundary.';
    putCorrection(m.id, {
      moSystemId: MO_SYSTEM[m.id] || null,
      org: m.org,
      system: m.system,
      offering: offering().name,
      offeringVersion: offering().version,
      message,
      status: 'requested',
      requestedAt: new Date().toISOString(),
      authorized: m.authorized,
      declared: m.declared,
    });
    sim.correction[m.id] = true;
    closeDialog();
    toast(`Sent to ${m.org} on Provider hosting. Access and scope fit unchanged.`);
    render('mo-impact');
    return;
  }
  if (name === 'newrequest') {
    putAccess('isr', {
      org: '551st ISRW', system: 'ISR Ground Station', offering: sim.offering, offeringName: offering().name,
      status: 'requested', il: 'IL4', data: 'CUI as published', interconnect: 'None',
      env: 'Dev', authPath: 'IATT',
      allocated: false, associated: false, allocation: '',
    });
    sim.accessId = 'isr';
    sim.whatIf = { step: 0, org: '551st ISRW', system: 'ISR Ground Station', il: 'IL4', data: 'CUI as published', interconnect: 'None', env: 'Dev', authPath: 'IATT' };
    render('whatif');
    return;
  }
  if (name === 'wif-next') { sim.whatIf.step = Math.min(3, sim.whatIf.step + 1); render('whatif'); return; }
  if (name === 'wif-back') { sim.whatIf.step = Math.max(0, sim.whatIf.step - 1); render('whatif'); return; }
  if (name === 'refuse') {
    const req = (sim.accessId && loadAccess()[sim.accessId]) || sim.pending[0];
    if (req) {
      putAccess(req.id, { ...req, status: 'refused', decidedAt: new Date().toISOString() });
      if (!sim.extras.some(e => e.id === req.id)) {
        sim.extras.push({
          id: req.id, org: req.org, system: req.system, offering: sim.offering, release: releaseLabel(),
          severity: 'Fits', access: 'refused', authorized: 'n/a', declared: 'n/a',
          why: [['Access', 'Refused at what-if', 'Subscription']],
        });
      }
      sim.lastAccessId = req.id;
    }
    sim.whatIf.step = 0;
    sim.accessId = null;
    toast('Attach refused. No subscription, allocation, or inheritance.');
    render('subscribers');
    return;
  }
  if (name === 'approve') {
    const est = estimateWhatIf();
    if (est.severity === 'Invalidating') {
      modal('Approve would be out of scope',
        '<p>Creates a subscription and an out-of-scope record. It does not expand, issue, or withdraw the recorded offering authorization, and it does not rewrite the CRM.</p>',
        [goBtn('Cancel', '!close'), goBtn('Create out-of-scope subscription', '!forced', true)]);
      return;
    }
    finishApprove(est.severity);
    return;
  }
  if (name === 'forced') { closeDialog(); finishApprove('Invalidating'); }
}

function bind() {
  document.body.addEventListener('click', e => {
    const go = e.target.closest('[data-go]');
    if (go) {
      e.preventDefault();
      if ($('dialog').open) closeDialog();
      render(go.getAttribute('data-go'));
      return;
    }
    const act = e.target.closest('[data-action]');
    if (act && act.tagName === 'BUTTON') { e.preventDefault(); onAction(act.getAttribute('data-action')); }
  });
  document.body.addEventListener('change', e => {
    const act = e.target.getAttribute('data-action');
    if (act === 'offering') {
      sim.offering = e.target.value;
      sim.severity = 'all';
      const first = allMissions().find(m => m.offering === sim.offering);
      if (first) sim.selected = first.id;
      render('impact');
    }
    if (act === 'wif-il') sim.whatIf.il = e.target.value;
    if (act === 'wif-data') sim.whatIf.data = e.target.value;
    if (act === 'wif-path') sim.whatIf.interconnect = e.target.value;
    if (act === 'wif-env') sim.whatIf.env = e.target.value;
    if (act === 'wif-auth') sim.whatIf.authPath = e.target.value;
  });
  $('screen').addEventListener('change', () => render($('screen').value));
  $('state').addEventListener('change', () => { sim.state = $('state').value; render(sim.page || 'home'); });
  $('close').addEventListener('click', closeDialog);
  document.querySelector('.brand').addEventListener('click', e => { e.preventDefault(); render('home'); });
  window.addEventListener('storage', e => {
    if (e.key === CORRECTION_KEY || e.key === ACCESS_KEY) render(sim.page || 'home');
  });
}

function applyAccessQuery() {
  const id = new URLSearchParams(location.search).get('access');
  if (!id) return;
  sim.accessId = id;
  let hit = loadAccess()[id];
  if (!hit) {
    hit = putAccess(id, {
      org: id === 'ops' ? '97th Wing' : '551st ISRW',
      system: id === 'ops' ? 'Wing Ops' : 'ISR Ground Station',
      offering: sim.offering,
      offeringName: offering().name,
      status: 'requested',
      il: 'IL4',
      data: 'CUI as published',
      interconnect: 'None',
      env: 'Dev',
      authPath: 'IATT',
    });
  }
  sim.offering = hit.offering || sim.offering;
  sim.whatIf = {
    step: 0,
    org: hit.org,
    system: hit.system,
    il: hit.il || 'IL4',
    data: hit.data || 'CUI as published',
    interconnect: hit.interconnect || 'None',
    env: hit.env || 'Dev',
    authPath: hit.authPath || 'IATT',
  };
}

$('screen').innerHTML = Object.entries(pages).map(([id, p]) => `<option value="${id}">${p.title}</option>`).join('');
bind();
applyCorrectionQuery();
applyAccessQuery();
render((location.hash || '#home').slice(1));
