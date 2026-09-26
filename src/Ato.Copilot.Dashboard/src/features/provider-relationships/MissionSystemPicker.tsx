import { useEffect, useState } from 'react';
import { listSetupSystems, getSetupSystem } from '../workspace-operations/api';
import { inputClass, buttonClass, secondaryButtonClass, useRemote } from '../workspace-operations/workspaceUi';
import { Link, useNavigate } from '../workspaces/workspaceNavigation';
import { ReadStatus } from './MissionTaskPresentation';

export default function MissionSystemPicker({ systemId, onContinue, autoContinue = false, locked = false }: {
  systemId?: string; onContinue: (name: string) => void; autoContinue?: boolean; locked?: boolean;
}) {
  if (systemId) return <SelectedSystem systemId={systemId} onContinue={onContinue} autoContinue={autoContinue} locked={locked} />;
  return <SystemChoices />;
}

function SelectedSystem({ systemId, onContinue, autoContinue, locked }: {
  systemId: string; onContinue: (name: string) => void; autoContinue: boolean; locked: boolean;
}) {
  const state = useRemote(async signal => {
    const system = await getSetupSystem(systemId, signal);
    if (!system.name || system.systemId.toLowerCase() !== systemId.toLowerCase()) {
      throw new Error('The server returned incomplete system information.');
    }
    return system;
  }, [systemId]);
  useEffect(() => { if (autoContinue && state.data) onContinue(state.data.name); }, [autoContinue, state.data, onContinue]);
  return <section className="space-y-4" aria-labelledby="system-step">
    <h2 id="system-step" className="text-xl font-semibold">Select system</h2>
    <ReadStatus state={state} name="system" />
    {state.data && <><p className="font-semibold">{state.data.name}</p>
      <details><summary>Details</summary><p className="break-all text-sm">System ID: {systemId}</p></details></>}
    <div className="flex flex-wrap items-center gap-4">
      {!locked && <Link to="/provider-relationships/setup" className="underline">Choose a different system</Link>}
      <button type="button" className={buttonClass} disabled={!state.data || !!state.error || state.loading}
        onClick={() => { if (state.data) onContinue(state.data.name); }}>Choose hosting scope</button>
    </div>
  </section>;
}

function SystemChoices() {
  const navigate = useNavigate();
  const [cursor, setCursor] = useState<string>();
  const [selected, setSelected] = useState('');
  const state = useRemote(async signal => {
    const page = await listSetupSystems({ pageSize: 50, cursor }, signal);
    if (!Array.isArray(page.items) || page.items.some(item => !item.systemId || !item.name)) {
      throw new Error('The authorized system list is incomplete. Retry before choosing a system.');
    }
    return page;
  }, [cursor]);
  return <section className="space-y-4" aria-labelledby="system-step">
    <h2 id="system-step" className="text-xl font-semibold">Select system</h2>
    <p>Choose an existing system you are authorized to read. Association permissions are checked separately for that system.</p>
    <ReadStatus state={state} name="systems" />
    <label className="grid gap-2">System
      <select className={inputClass} value={selected} disabled={state.loading || !!state.error}
        onChange={event => setSelected(event.target.value)}>
        <option value="">Select a system</option>
        {state.data?.items.map(item => <option key={item.systemId} value={item.systemId}>
          {item.name}{item.acronym ? ` · ${item.acronym}` : ''}
        </option>)}
      </select>
    </label>
    {state.data?.items.length === 0 && <p>No authorized systems are available. Ask your system administrator to verify your existing assignment.</p>}
    <div className="flex flex-wrap gap-3">
      {cursor && <button type="button" className={secondaryButtonClass} onClick={() => { setCursor(undefined); setSelected(''); }}>First systems</button>}
      {state.data?.nextCursor && <button type="button" className={secondaryButtonClass}
        onClick={() => { setCursor(state.data?.nextCursor ?? undefined); setSelected(''); }}>More systems</button>}
      <button type="button" className={buttonClass}
        disabled={state.loading || !!state.error || !state.data?.items.some(item => item.systemId === selected)}
        onClick={() => navigate(`/systems/${encodeURIComponent(selected)}/provider-relationships/setup`, { state: { chooseHosting: true } })}>
        Choose hosting scope
      </button>
    </div>
  </section>;
}
