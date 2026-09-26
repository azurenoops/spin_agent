import { useEffect, useRef, useState } from 'react';
import { Cloud, Search, UserRound, ArrowRight } from 'lucide-react';
import * as api from './api';
import { buttonClass, errorClass, inputClass, message, secondaryButtonClass, Status, useRemote } from './workspaceUi';

export function EntraUserPicker({ onSelect }: { onSelect: (user: api.DirectoryUser) => void }) {
  const connections = useRemote(signal => api.getDirectoryConnections(signal), []);
  const [chosen, setChosen] = useState('');
  const [query, setQuery] = useState('');
  const [result, setResult] = useState<{ users: api.DirectoryUser[]; hasMore: boolean } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const controller = useRef<AbortController | null>(null);
  const directory = connections.data?.find(item => item.id === chosen) ?? (chosen ? undefined : connections.data?.[0]);
  useEffect(() => () => controller.current?.abort(), []);
  const reset = () => { controller.current?.abort(); setResult(null); setError(null); setBusy(false); };
  const search = async () => {
    if (!directory?.configured || query.trim().length < 2 || busy) return;
    controller.current?.abort();
    const pending = new AbortController(); controller.current = pending;
    setBusy(true); setError(null); setResult(null);
    try { const value = await api.searchDirectoryUsers(directory.id, query.trim(), pending.signal); if (!pending.signal.aborted) setResult(value); }
    catch (reason) { if (!pending.signal.aborted) setError(message(reason)); }
    finally { if (!pending.signal.aborted) setBusy(false); }
  };
  return <section aria-label="Entra directory search" className="space-y-5">
    <div className="flex items-start gap-3"><span className="rounded-xl bg-sky-50 p-3 text-sky-700 dark:bg-sky-950 dark:text-sky-200"><Cloud size={22} /></span>
      <div><h3 className="font-semibold">Find your administrator in Microsoft Entra</h3><p className="mt-1 text-sm text-slate-500 dark:text-slate-400">Search by the beginning of a name or email, then select the right person.</p></div></div>
    <Status loading={connections.loading} error={connections.error} retry={connections.retry} />
    {connections.data?.length === 0 && <p role="status" className="rounded-xl bg-slate-50 p-4 text-sm dark:bg-slate-800">No Entra directory is connected for your provider. Ask your deployment administrator to configure a connection. You can enter identity details manually or enroll later.</p>}
    {!!connections.data?.length && <>
      <div className="grid gap-1.5"><label htmlFor="entra-directory" className="text-sm font-medium">Directory</label>
        <select id="entra-directory" className={`${inputClass} w-full`} value={directory?.id ?? ''} onChange={event => { reset(); setChosen(event.target.value); }}>
          {connections.data.map(item => <option key={item.id} value={item.id}>{item.name} · {item.cloud}</option>)}
        </select>
        <p className="break-all text-xs text-slate-500">{directory?.directoryTenantId}</p>
      </div>
      {!directory?.configured && <p role="status" className="rounded-lg bg-amber-50 p-3 text-sm text-amber-900 dark:bg-amber-950 dark:text-amber-100">Connection setup required. Ask your administrator to configure credentials and directory-read consent.</p>}
      <div className="space-y-2"><label htmlFor="entra-person-search" className="text-sm font-medium">Find a person</label>
        <div className="flex flex-col gap-2 sm:flex-row"><input id="entra-person-search" type="search" autoComplete="off" maxLength={100} placeholder="Name or email address" className={`${inputClass} min-w-0 flex-1`}
          value={query} disabled={!directory?.configured} onChange={event => { reset(); setQuery(event.target.value); }} onKeyDown={event => { if (event.key === 'Enter') { event.preventDefault(); void search(); } }} />
          <button type="button" className={`${buttonClass} inline-flex items-center justify-center gap-2`} disabled={!directory?.configured || query.trim().length < 2 || busy} onClick={() => void search()}><Search size={16} />{busy ? 'Searching…' : 'Search Entra'}</button>
        </div><p className="text-xs text-slate-500">Enter at least 2 characters. Only the selected directory is searched.</p>
      </div>
    </>}
    {busy && <p role="status" className="text-sm">Searching Microsoft Entra…</p>}
    {error && <p role="alert" className={errorClass}>{error}</p>}
    {result && <div aria-live="polite" className="space-y-2">
      {!result.users.length && <p className="rounded-lg bg-slate-50 p-4 text-sm dark:bg-slate-800">No people found. Try a different name or email in this directory.</p>}
      {result.users.map(user => <button type="button" key={user.objectId} aria-label={`Select ${user.displayName} (${user.email})`} onClick={() => onSelect(user)}
        className={`${secondaryButtonClass} flex w-full items-center gap-3 rounded-xl p-4 text-left`}>
        <UserRound size={20} className="shrink-0 text-indigo-600" /><span className="min-w-0 flex-1"><span className="block break-words font-semibold">{user.displayName}</span><span className="block break-all text-xs text-slate-500">{user.email}</span><span className="block break-all text-xs text-slate-400">{user.objectId}</span></span><ArrowRight size={16} className="shrink-0" />
      </button>)}
      {result.hasMore && <p className="text-xs text-slate-500">Showing the first 20 matches. Refine your search to find a specific person.</p>}
    </div>}
  </section>;
}
