import { useEffect, useRef, useState } from 'react';
import { createTaskTicket, getTaskTicket, linkTaskTicket, refreshTaskTicket, unlinkTaskTicket } from '../../api/taskTickets';
import type { TaskTicketResponse } from '../../api/taskTickets';

export interface TaskTicketPanelProps { systemId: string; taskId: string }

function message(error: unknown): string {
  if (error && typeof error === 'object' && 'error' in error && typeof error.error === 'string') return error.error;
  return error instanceof Error ? error.message : 'Ticket operation failed. Reload the task before continuing.';
}

export default function TaskTicketPanel({ systemId, taskId }: TaskTicketPanelProps) {
  const [data, setData] = useState<TaskTicketResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [reference, setReference] = useState('');
  const [confirmCreate, setConfirmCreate] = useState(false);
  const [mustReload, setMustReload] = useState(false);
  const generation = useRef(0);
  const readController = useRef<AbortController | null>(null);

  useEffect(() => {
    const current = ++generation.current;
    const controller = new AbortController();
    readController.current = controller;
    setData(null); setError(null); setReference(''); setConfirmCreate(false); setMustReload(false); setBusy(false);
    getTaskTicket(systemId, taskId, controller.signal).then(value => {
      if (generation.current === current) setData(value);
    }).catch(reason => {
      if (generation.current === current) setError(message(reason));
    });
    return () => { generation.current++; readController.current?.abort(); };
  }, [systemId, taskId]);

  function reload() {
    readController.current?.abort();
    const controller = new AbortController();
    readController.current = controller;
    return act(() => getTaskTicket(systemId, taskId, controller.signal), true);
  }

  async function act(operation: () => Promise<TaskTicketResponse>, reload = false) {
    const current = generation.current;
    setBusy(true); setError(null); setConfirmCreate(false);
    try {
      const result = await operation();
      if (current === generation.current) {
        setData(result); setReference('');
        if (reload) setMustReload(false);
      }
    } catch (reason) {
      if (current === generation.current) { setError(message(reason)); setMustReload(true); }
    } finally {
      if (current === generation.current) setBusy(false);
    }
  }

  const link = data?.link;
  const canAct = data?.canManage && data.configured && !mustReload;
  const linked = link?.state === 'Linked';
  const recovery = link?.state === 'Pending' || link?.state === 'Uncertain';
  const button = 'rounded border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-800 hover:bg-slate-100 disabled:opacity-50 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-100 dark:hover:bg-slate-700';
  const muted = 'text-slate-500 dark:text-slate-400';
  return <section className="space-y-3 rounded-lg border border-slate-200 bg-slate-50 p-4 text-slate-800 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-100" aria-label="Task external ticket">
    <div>
      <h4 className="font-semibold text-slate-900 dark:text-white">External execution ticket</h4>
      <p className="text-sm text-slate-600 dark:text-slate-300">Manual read-only snapshots. External closure does not close this task, its finding, or its POA&amp;M.</p>
      <p className={`mt-1 text-xs ${muted}`}>Jira and ServiceNow. Incoming webhooks and bidirectional synchronization are not supported.</p>
    </div>
    {error && <p role="alert" className="text-sm text-red-700 dark:text-red-300">{error}</p>}
    {!data && !error && <p role="status">Loading ticket…</p>}
    {data && !data.configured && <p className="text-sm text-amber-800 dark:text-amber-300">An administrator must configure and enable the system ticket connector before linking or creating tickets.</p>}
    {data && !data.canManage && <p className="text-sm text-slate-600 dark:text-slate-300">Read-only access. Ticket changes require remediation management permission.</p>}
    {link && <dl className="grid grid-cols-2 gap-2 text-sm">
      <dt className={muted}>Provider</dt><dd>{link.provider}</dd>
      <dt className={muted}>Link state</dt><dd>{link.state}</dd>
      {linked && <>
        <dt className={muted}>Ticket</dt><dd>{link.externalUrl?.startsWith('https://')
          ? <a href={link.externalUrl} target="_blank" rel="noopener noreferrer" className="text-blue-700 underline dark:text-blue-300">{link.externalRef}</a>
          : link.externalRef}</dd>
        <dt className={muted}>External status</dt><dd>{link.externalStatus || 'Not refreshed yet'}</dd>
        <dt className={muted}>External assignee</dt><dd>{link.externalAssignee || 'Unassigned / unavailable'}</dd>
        <dt className={muted}>Last successful refresh</dt><dd>{link.lastSuccessfulSyncAt ? new Date(link.lastSuccessfulSyncAt).toLocaleString() : 'Never'}</dd>
      </>}
      <dt className={muted}>Recovery correlation</dt><dd className="break-all font-mono text-xs">{link.correlationKey}</dd>
    </dl>}
    {link?.lastError && <p role="alert" className="text-sm text-amber-800 dark:text-amber-300">{link.lastError}</p>}
    {recovery && <p className="text-sm text-amber-800 dark:text-amber-300">Creation may have reached the provider. Search there using the correlation key and link the existing ticket. Blind create retries are blocked.</p>}
    {canAct && !linked && <div className="space-y-2">
      <label className="block text-sm font-medium" htmlFor={`ticket-ref-${taskId}`}>Existing ticket identifier</label>
      <input id={`ticket-ref-${taskId}`} value={reference} onChange={event => setReference(event.target.value)}
        placeholder="Jira key (ABC-123) or ServiceNow sys_id" className="w-full rounded border border-slate-300 bg-white p-2 text-sm placeholder:text-slate-500 dark:border-slate-600 dark:bg-slate-950 dark:text-slate-100 dark:placeholder:text-slate-400" />
      <button type="button" className={button} disabled={busy || !reference.trim()}
        onClick={() => void act(() => linkTaskTicket(systemId, taskId, reference.trim(), link?.rowVersion))}>Link existing ticket</button>
      {!link && <div>
        {!confirmCreate ? <button type="button" className={button} disabled={busy} onClick={() => setConfirmCreate(true)}>Create external ticket</button>
          : <div className="space-y-2 rounded border border-amber-200 bg-amber-50 p-3 dark:border-amber-800 dark:bg-amber-950">
            <p className="text-sm">This sends the task title and description to the configured provider and creates a real ticket. Creation cannot be blindly retried.</p>
            <button type="button" className={button} disabled={busy} onClick={() => void act(() => createTaskTicket(systemId, taskId))}>Confirm create</button>
            <button type="button" className={`${button} ml-2`} onClick={() => setConfirmCreate(false)}>Cancel</button>
          </div>}
      </div>}
    </div>}
    {linked && data?.canManage && !mustReload && <div className="flex flex-wrap gap-2">
      <button type="button" className={button} disabled={busy || !data.configured} onClick={() => void act(() => refreshTaskTicket(systemId, taskId, link.rowVersion))}>Refresh snapshot</button>
      <button type="button" className={button} disabled={busy} onClick={() => void act(() => unlinkTaskTicket(systemId, taskId, link.rowVersion))}>Unlink locally</button>
      <p className={`w-full text-xs ${muted}`}>Unlink keeps audit history and never deletes the external ticket.</p>
    </div>}
    {(mustReload || (!data && error)) && <button type="button" className={button} disabled={busy} onClick={() => void reload()}>Reload ticket</button>}
  </section>;
}
