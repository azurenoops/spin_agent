import { useId, useState, type ReactNode } from 'react';
import { Link } from '../../features/workspaces/workspaceNavigation';
import { useSettings, type DashboardSettings } from '../../hooks/useSettings';
import { useWorkspaceSession } from '../../features/workspaces/WorkspaceBoundary';
import { useOptionalMe } from '../../features/auth/useMe';
import SetupDialog from '../../features/workspace-operations/SetupDialog';
import NotificationSettingsPanel from '../../features/notifications/NotificationSettingsPanel';

interface SettingsPanelProps { onClose: () => void }

function SelectField<T extends string | number>({ label, value, options, onChange }: {
  label: string; value: T; options: { label: string; value: T }[]; onChange: (value: T) => void;
}) {
  return <label className="block space-y-1 text-sm">
    <span>{label}</span>
    <select value={value} onChange={event => {
      const option = options.find(item => String(item.value) === event.target.value);
      if (option) onChange(option.value);
    }} className="block w-full rounded border border-slate-300 bg-white px-3 py-2 dark:border-slate-600 dark:bg-slate-900">
      {options.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
    </select>
  </label>;
}

function Expandable({ title, initiallyOpen = false, busy = false, children }: {
  title: string; initiallyOpen?: boolean; busy?: boolean; children: ReactNode;
}) {
  const id = useId();
  const [open, setOpen] = useState(initiallyOpen);
  const [visited, setVisited] = useState(initiallyOpen);
  return <section className="border-b border-slate-200 py-2 dark:border-slate-700">
    <h3><button type="button" aria-expanded={open} aria-controls={id} disabled={busy}
      className="flex w-full items-center justify-between rounded py-3 text-left font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-indigo-500"
      onClick={() => { setOpen(value => !value); setVisited(true); }}>
      {title}<span aria-hidden="true">{open ? '−' : '+'}</span>
    </button></h3>
    <div id={id} hidden={!open} className="space-y-4 pb-4">{visited && children}</div>
  </section>;
}

function Preferences({ settings, update }: {
  settings: DashboardSettings; update: (value: Partial<DashboardSettings>) => void;
}) {
  const [timezone, setTimezone] = useState(settings.timezone);
  const [error, setError] = useState<string | null>(null);
  const applyTimezone = () => {
    const value = timezone.trim();
    try {
      if (!value) throw new RangeError('Timezone is required.');
      new Intl.DateTimeFormat('en', { timeZone: value }).format();
      update({ timezone: value }); setError(null);
    } catch (reason) {
      if (!(reason instanceof RangeError)) throw reason;
      setError('Enter a valid timezone, such as UTC or America/New_York.');
    }
  };
  return <>
    <p className="text-xs text-slate-500 dark:text-slate-400">Browser-local display preferences. These are not account-synced and do not change organization policy or document contents.</p>
    <SelectField label="Theme" value={settings.theme} onChange={theme => update({ theme })} options={[
      { label: 'Light', value: 'light' }, { label: 'Dark', value: 'dark' }, { label: 'System', value: 'system' },
    ]} />
    <SelectField label="Table density" value={settings.tableDensity} onChange={tableDensity => update({ tableDensity })} options={[
      { label: 'Compact', value: 'compact' }, { label: 'Comfortable', value: 'comfortable' },
    ]} />
    <SelectField label="Date display" value={settings.dateFormat} onChange={dateFormat => update({ dateFormat })} options={[
      { label: 'MM/DD/YYYY', value: 'US' }, { label: 'YYYY-MM-DD', value: 'ISO' }, { label: 'DD/MM/YYYY', value: 'EU' },
    ]} />
    <p className="text-xs text-slate-500 dark:text-slate-400">Applies to POA&amp;M due dates and milestones, ticket-sync times, and deviation requested dates. Calendar deadlines keep their recorded day. Audit history and exports are unchanged.</p>
    <form className="space-y-2" onSubmit={event => { event.preventDefault(); applyTimezone(); }}>
      <label className="block text-sm">Timezone
        <input value={timezone} onChange={event => setTimezone(event.target.value)} placeholder="America/New_York"
          className="mt-1 block w-full rounded border border-slate-300 bg-white px-3 py-2 dark:border-slate-600 dark:bg-slate-900" />
      </label>
      {error && <p role="alert" className="text-sm text-red-700 dark:text-red-300">{error}</p>}
      <button type="submit" className="rounded border px-3 py-2 text-sm">Apply timezone</button>
    </form>
  </>;
}

export default function SettingsPanel({ onClose }: SettingsPanelProps) {
  const { settings, updateSettings, resetSettings } = useSettings();
  const session = useWorkspaceSession();
  const identity = useOptionalMe();
  const [saving, setSaving] = useState(false);
  const [confirmReset, setConfirmReset] = useState(false);
  const [resetRevision, setResetRevision] = useState(0);
  const organizationAdmin = session?.workspace.kind === 'organization'
    && session.workspace.mode === 'ordinary' && session.workspace.permissions.canManageOrganization;
  const providerAdmin = session?.workspace.kind === 'csp'
    && session.identity.isCspAdmin && session.workspace.permissions.canAccessCsp;
  return <SetupDialog title="Settings" description="Personal preferences and workspace administration."
    placement="right" busy={saving} onClose={onClose}>
    <div className="space-y-2">
      <header className="rounded-lg bg-slate-50 p-3 text-sm dark:bg-slate-800">
        <p className="font-semibold">{session?.identity.displayName ?? identity?.data?.displayName ?? 'Signed-in identity unavailable'}</p>
        <p className="text-slate-600 dark:text-slate-300">{session?.workspace.displayName ?? 'No active workspace'}</p>
        {session && <p className="mt-1 text-xs text-slate-500 dark:text-slate-400">Effective roles: {session.roles.join(', ') || 'No assigned role'}</p>}
      </header>
      <Expandable title="Preferences" initiallyOpen>
        <Preferences key={resetRevision} settings={settings} update={updateSettings} />
      </Expandable>
      <Expandable title="Notifications" busy={saving}>
        <NotificationSettingsPanel onSavingChange={setSaving} />
      </Expandable>
      <Expandable title="Assistant">
        <p className="text-xs text-slate-500 dark:text-slate-400">Browser-local chat presentation. These controls do not change model access or permissions.</p>
        <label className="flex items-center gap-3 py-2 text-sm">
          <input type="checkbox" checked={settings.showQuickActions} onChange={event => updateSettings({ showQuickActions: event.target.checked })} />
          Show quick actions
        </label>
        <SelectField label="Chat panel width" value={settings.chatPanelWidth} onChange={chatPanelWidth => updateSettings({ chatPanelWidth })} options={[
          ...([360, 420, 520, 600].includes(settings.chatPanelWidth) ? [] : [{ label: `Current (${settings.chatPanelWidth}px)`, value: settings.chatPanelWidth }]),
          { label: 'Narrow (360px)', value: 360 }, { label: 'Default (420px)', value: 420 },
          { label: 'Wide (520px)', value: 520 }, { label: 'Extra wide (600px)', value: 600 },
        ]} />
      </Expandable>
      {(organizationAdmin || providerAdmin) && <section className="space-y-2 border-b border-slate-200 py-4 dark:border-slate-700">
        <h3 className="font-semibold">Administration</h3>
        <p className="text-xs text-slate-500 dark:text-slate-400">Organization setup, subscription registration and other operational tasks belong to their owning workspace.</p>
        <Link to={organizationAdmin ? '/settings/org' : '/provider-administration'} onClick={onClose}
          className="inline-block text-sm text-indigo-700 underline dark:text-indigo-300">
          {organizationAdmin ? 'Open organization administration' : 'Open provider administration'}
        </Link>
      </section>}
      <footer className="space-y-3 pt-4 text-sm">
        <p className="text-xs text-slate-500 dark:text-slate-400">Reset changes only browser-local appearance, date/time and chat presentation. It does not change account notification preferences, identity, organization policy, system records or exports.</p>
        {confirmReset ? <div className="space-y-3 rounded border border-slate-300 p-3">
          <p>Reset personal preferences in this browser?</p>
          <div className="flex flex-wrap gap-3">
            <button type="button" className="rounded border px-3 py-2" onClick={() => {
              resetSettings(); setResetRevision(value => value + 1); setConfirmReset(false);
            }}>Confirm reset</button>
            <button type="button" className="rounded px-3 py-2 underline" onClick={() => setConfirmReset(false)}>Cancel reset</button>
          </div>
        </div> : <button type="button" className="rounded border px-3 py-2" onClick={() => setConfirmReset(true)}>Reset personal preferences</button>}
      </footer>
    </div>
  </SetupDialog>;
}
