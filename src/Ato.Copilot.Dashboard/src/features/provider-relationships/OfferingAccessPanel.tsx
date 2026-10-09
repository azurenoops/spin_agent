import { useState } from 'react';
import { buttonClass, secondaryButtonClass, useRemote } from '../workspace-operations/workspaceUi';
import * as api from './api';

export default function OfferingAccessPanel({ systemId }: { systemId: string }) {
  const requests = useRemote(signal => api.listOfferingAccess(systemId, signal), [systemId]);
  const catalog = useRemote(signal => api.listRequestableOfferings(systemId, signal), [systemId]);
  const [authPath, setAuthPath] = useState('IATT');
  const [env, setEnv] = useState('Dev');
  const [il, setIl] = useState('IL4');
  const [data, setData] = useState('CUI as published');
  const [path, setPath] = useState('None');
  const [offeringId, setOfferingId] = useState('');
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState('');
  const latest = requests.data?.items[0];
  const send = async () => {
    const id = offeringId || catalog.data?.[0]?.offeringId;
    if (!id) { setNotice('No active offering is available to request.'); return; }
    setBusy(true); setNotice('');
    try {
      await api.requestOfferingAccess(systemId, {
        offeringId: id, authPath, environmentLane: env, impactLevel: il, dataTypes: data, interconnect: path,
      });
      setNotice('Sent to the CSP. Access, allocation, and inheritance did not change.');
      requests.retry();
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Unable to send the access request.');
    } finally { setBusy(false); }
  };
  return <section className="space-y-4 rounded border border-slate-300 p-4" aria-labelledby="offering-access">
    <h2 id="offering-access" className="text-xl font-semibold">Offering access</h2>
    <p>Request offering access and declare use. This does not allocate Azure, associate hosting, adopt a release, or issue IATT/ATO.</p>
    {requests.error && <p role="alert">{requests.error}</p>}
    {latest && <p role="status">{latest.offeringName || 'Offering'} · {latest.status} · {latest.authPath} · {latest.environmentLane}
      {latest.status === 'Requested' ? ' · Waiting on CSP what-if' : ''}
      {latest.status === 'Approved' ? ' · Access approved. CSP must still assign hosting scope, then you associate. Not inherited.' : ''}
    </p>}
    <div className="grid gap-3 sm:grid-cols-2">
      <label className="text-sm">Offering
        <select className="mt-1 block w-full rounded border p-2" value={offeringId} onChange={e => setOfferingId(e.target.value)}>
          <option value="">Select an active offering</option>
          {(catalog.data ?? []).map(item => <option key={item.offeringId} value={item.offeringId}>{item.name}</option>)}
        </select>
      </label>
      <label className="text-sm">Authorization path
        <select className="mt-1 block w-full rounded border p-2" value={authPath} onChange={e => setAuthPath(e.target.value)}>
          <option>IATT</option><option>ATO</option>
        </select>
      </label>
      <label className="text-sm">Declared env
        <select className="mt-1 block w-full rounded border p-2" value={env} onChange={e => setEnv(e.target.value)}>
          <option>Dev</option><option>Test</option><option>Prod</option>
        </select>
      </label>
      <label className="text-sm">Impact level
        <select className="mt-1 block w-full rounded border p-2" value={il} onChange={e => setIl(e.target.value)}>
          <option>IL4</option><option>IL5</option>
        </select>
      </label>
      <label className="text-sm">Data types
        <input className="mt-1 block w-full rounded border p-2" value={data} onChange={e => setData(e.target.value)} />
      </label>
      <label className="text-sm">Extra interconnection
        <select className="mt-1 block w-full rounded border p-2" value={path} onChange={e => setPath(e.target.value)}>
          <option>None</option><option>Sister IL5 system</option>
        </select>
      </label>
    </div>
    {notice && <p role="status">{notice}</p>}
    <button type="button" className={buttonClass} disabled={busy} onClick={() => void send()}>Send to CSP</button>
    {latest?.status === 'Approved' && <p>
      <button type="button" className={secondaryButtonClass} onClick={() => window.location.assign(`/systems/${encodeURIComponent(systemId)}/provider-relationships/setup`)}>
        Continue to associate existing hosting
      </button>
    </p>}
  </section>;
}
