import { useState, useEffect, useRef } from 'react';
import { useSettings, type DashboardSettings } from '../../hooks/useSettings';
import apiClient from '../../api/client';

interface SettingsPanelProps {
  onClose: () => void;
}

// ─── Section definitions ──────────────────────────────────────────────────────

type SectionId = 'profile' | 'notifications' | 'dashboard' | 'chat' | 'export' | 'compliance' | 'integrations' | 'admin';

const sections: { id: SectionId; label: string; icon: string }[] = [
  { id: 'profile', label: 'Profile & Identity', icon: 'M15.75 6a3.75 3.75 0 1 1-7.5 0 3.75 3.75 0 0 1 7.5 0ZM4.501 20.118a7.5 7.5 0 0 1 14.998 0A17.933 17.933 0 0 1 12 21.75c-2.676 0-5.216-.584-7.499-1.632Z' },
  { id: 'notifications', label: 'Notifications', icon: 'M14.857 17.082a23.848 23.848 0 0 0 5.454-1.31A8.967 8.967 0 0 1 18 9.75V9A6 6 0 0 0 6 9v.75a8.967 8.967 0 0 1-2.312 6.022c1.733.64 3.56 1.085 5.455 1.31m5.714 0a24.255 24.255 0 0 1-5.714 0m5.714 0a3 3 0 1 1-5.714 0' },
  { id: 'dashboard', label: 'Dashboard', icon: 'M3.75 6A2.25 2.25 0 0 1 6 3.75h2.25A2.25 2.25 0 0 1 10.5 6v2.25a2.25 2.25 0 0 1-2.25 2.25H6a2.25 2.25 0 0 1-2.25-2.25V6ZM3.75 15.75A2.25 2.25 0 0 1 6 13.5h2.25a2.25 2.25 0 0 1 2.25 2.25V18a2.25 2.25 0 0 1-2.25 2.25H6A2.25 2.25 0 0 1 3.75 18v-2.25ZM13.5 6a2.25 2.25 0 0 1 2.25-2.25H18A2.25 2.25 0 0 1 20.25 6v2.25A2.25 2.25 0 0 1 18 10.5h-2.25a2.25 2.25 0 0 1-2.25-2.25V6ZM13.5 15.75a2.25 2.25 0 0 1 2.25-2.25H18a2.25 2.25 0 0 1 2.25 2.25V18A2.25 2.25 0 0 1 18 20.25h-2.25A2.25 2.25 0 0 1 13.5 18v-2.25Z' },
  { id: 'chat', label: 'Chat & AI', icon: 'M7.5 8.25h9m-9 3H12m-9.75 1.51c0 1.6 1.123 2.994 2.707 3.227 1.087.16 2.185.283 3.293.369V21l4.076-4.076a1.526 1.526 0 0 1 1.037-.443 48.282 48.282 0 0 0 5.68-.494c1.584-.233 2.707-1.626 2.707-3.228V6.741c0-1.602-1.123-2.995-2.707-3.228A48.394 48.394 0 0 0 12 3c-2.392 0-4.744.175-7.043.513C3.373 3.746 2.25 5.14 2.25 6.741v6.018Z' },
  { id: 'export', label: 'Data & Export', icon: 'M3 16.5v2.25A2.25 2.25 0 0 0 5.25 21h13.5A2.25 2.25 0 0 0 21 18.75V16.5M16.5 12 12 16.5m0 0L7.5 12m4.5 4.5V3' },
  { id: 'compliance', label: 'Compliance', icon: 'M9 12.75 11.25 15 15 9.75m-3-7.036A11.959 11.959 0 0 1 3.598 6 11.99 11.99 0 0 0 3 9.749c0 5.592 3.824 10.29 9 11.623 5.176-1.332 9-6.03 9-11.622 0-1.31-.21-2.571-.598-3.751h-.152c-3.196 0-6.1-1.248-8.25-3.285Z' },
  { id: 'integrations', label: 'Integrations', icon: 'M13.19 8.688a4.5 4.5 0 0 1 1.242 7.244l-4.5 4.5a4.5 4.5 0 0 1-6.364-6.364l1.757-1.757m13.35-.622 1.757-1.757a4.5 4.5 0 0 0-6.364-6.364l-4.5 4.5a4.5 4.5 0 0 0 1.242 7.244' },
  { id: 'admin', label: 'Administration', icon: 'M10.343 3.94c.09-.542.56-.94 1.11-.94h1.093c.55 0 1.02.398 1.11.94l.149.894c.07.424.384.764.78.93.398.164.855.142 1.205-.108l.737-.527a1.125 1.125 0 0 1 1.45.12l.773.774c.39.389.44 1.002.12 1.45l-.527.737c-.25.35-.272.806-.107 1.204.165.397.505.71.93.78l.893.15c.543.09.94.56.94 1.109v1.094c0 .55-.397 1.02-.94 1.11l-.893.149c-.425.07-.765.383-.93.78-.165.398-.143.854.107 1.204l.527.738c.32.447.269 1.06-.12 1.45l-.774.773a1.125 1.125 0 0 1-1.449.12l-.738-.527c-.35-.25-.806-.272-1.203-.107-.397.165-.71.505-.781.929l-.149.894c-.09.542-.56.94-1.11.94h-1.094c-.55 0-1.019-.398-1.11-.94l-.148-.894c-.071-.424-.384-.764-.781-.93-.398-.164-.854-.142-1.204.108l-.738.527c-.447.32-1.06.269-1.45-.12l-.773-.774a1.125 1.125 0 0 1-.12-1.45l.527-.737c.25-.35.273-.806.108-1.204-.165-.397-.505-.71-.93-.78l-.894-.15c-.542-.09-.94-.56-.94-1.109v-1.094c0-.55.398-1.02.94-1.11l.894-.149c.424-.07.765-.383.93-.78.165-.398.143-.854-.107-1.204l-.527-.738a1.125 1.125 0 0 1 .12-1.45l.773-.773a1.125 1.125 0 0 1 1.45-.12l.737.527c.35.25.807.272 1.204.107.397-.165.71-.505.78-.929l.15-.894Z M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z' },
];

// ─── Shared sub-components ──────────────────────────────────────────────────

function Toggle({ checked, onChange, label }: { checked: boolean; onChange: (v: boolean) => void; label: string }) {
  return (
    <label className="flex items-center justify-between py-2">
      <span className="text-sm text-gray-700">{label}</span>
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        onClick={() => onChange(!checked)}
        className={`relative inline-flex h-5 w-9 flex-shrink-0 cursor-pointer rounded-full transition-colors ${checked ? 'bg-blue-600' : 'bg-gray-200'}`}
      >
        <span className={`inline-block h-4 w-4 transform rounded-full bg-white shadow transition-transform mt-0.5 ${checked ? 'translate-x-4 ml-0.5' : 'translate-x-0.5'}`} />
      </button>
    </label>
  );
}

function SelectField<T extends string | number>({ label, value, options, onChange }: { label: string; value: T; options: { label: string; value: T }[]; onChange: (v: T) => void }) {
  return (
    <label className="flex items-center justify-between py-2">
      <span className="text-sm text-gray-700">{label}</span>
      <select
        value={String(value)}
        onChange={(e) => {
          const raw = e.target.value;
          // Coerce back to number if needed
          const parsed = typeof value === 'number' ? (Number(raw) as unknown as T) : (raw as unknown as T);
          onChange(parsed);
        }}
        className="rounded-md border border-gray-300 bg-white px-2 py-1 text-sm text-gray-700 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
      >
        {options.map((o) => (
          <option key={String(o.value)} value={String(o.value)}>{o.label}</option>
        ))}
      </select>
    </label>
  );
}

function TextField({ label, value, onChange, placeholder }: { label: string; value: string; onChange: (v: string) => void; placeholder?: string }) {
  return (
    <label className="flex flex-col gap-1 py-2">
      <span className="text-sm text-gray-700">{label}</span>
      <input
        type="text"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        className="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm text-gray-700 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
      />
    </label>
  );
}

function NumberField({ label, value, onChange, min, max, suffix }: { label: string; value: number; onChange: (v: number) => void; min?: number; max?: number; suffix?: string }) {
  return (
    <label className="flex items-center justify-between py-2">
      <span className="text-sm text-gray-700">{label}</span>
      <div className="flex items-center gap-1.5">
        <input
          type="number"
          value={value}
          onChange={(e) => onChange(Number(e.target.value))}
          min={min}
          max={max}
          className="w-20 rounded-md border border-gray-300 px-2 py-1 text-sm text-gray-700 text-right focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
        />
        {suffix && <span className="text-xs text-gray-500">{suffix}</span>}
      </div>
    </label>
  );
}

function SectionDivider({ title }: { title: string }) {
  return <div className="pt-3 pb-1 text-xs font-semibold uppercase tracking-wider text-gray-400">{title}</div>;
}

// ─── Section Renderers ──────────────────────────────────────────────────────

function ProfileSection({ settings, update }: { settings: DashboardSettings; update: (p: Partial<DashboardSettings>) => void }) {
  return (
    <div className="space-y-1">
      <SectionDivider title="Identity" />
      <TextField label="Display Name" value={settings.displayName} onChange={(v) => update({ displayName: v })} placeholder="e.g. John Spinella" />
      <SelectField label="Role" value={settings.role} onChange={(v) => update({ role: v })} options={[
        { label: 'Select role...', value: '' },
        { label: 'Authorizing Official (AO)', value: 'AO' },
        { label: 'ISSM', value: 'ISSM' },
        { label: 'ISSO', value: 'ISSO' },
        { label: 'Security Control Assessor', value: 'SCA' },
        { label: 'Engineer', value: 'Engineer' },
      ]} />
      <TextField label="Organization" value={settings.organization} onChange={(v) => update({ organization: v })} placeholder="e.g. DISA, USCYBERCOM" />
    </div>
  );
}

function NotificationsSection({ settings, update }: { settings: DashboardSettings; update: (p: Partial<DashboardSettings>) => void }) {
  const syncTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const loadedRef = useRef(false);

  // Load preferences from backend on first render
  useEffect(() => {
    if (loadedRef.current) return;
    loadedRef.current = true;
    apiClient.get('/notifications/preferences').then((res) => {
      const prefs = res.data;
      update({
        poamOverdueAlerts: prefs.poamOverdueAlerts,
        atoExpirationAlerts: prefs.atoExpirationAlerts,
        complianceDriftAlerts: prefs.complianceDriftAlerts,
        alertDaysBefore: prefs.alertDaysBefore,
      });
    }).catch(() => { /* use local defaults */ });
  }, [update]);

  // Debounced sync to backend when notification settings change
  const handleUpdate = (partial: Partial<DashboardSettings>) => {
    update(partial);
    const merged = { ...settings, ...partial };

    if (syncTimer.current) clearTimeout(syncTimer.current);
    syncTimer.current = setTimeout(() => {
      apiClient.put('/notifications/preferences', {
        poamOverdueAlerts: merged.poamOverdueAlerts,
        atoExpirationAlerts: merged.atoExpirationAlerts,
        complianceDriftAlerts: merged.complianceDriftAlerts,
        alertDaysBefore: merged.alertDaysBefore,
      }).catch(() => { /* best-effort */ });
    }, 500);
  };

  return (
    <div className="space-y-1">
      <SectionDivider title="Alert Triggers" />
      <Toggle label="POA&M Overdue Alerts" checked={settings.poamOverdueAlerts} onChange={(v) => handleUpdate({ poamOverdueAlerts: v })} />
      <Toggle label="ATO Expiration Alerts" checked={settings.atoExpirationAlerts} onChange={(v) => handleUpdate({ atoExpirationAlerts: v })} />
      <Toggle label="Compliance Drift Alerts" checked={settings.complianceDriftAlerts} onChange={(v) => handleUpdate({ complianceDriftAlerts: v })} />
      <SectionDivider title="Timing" />
      <NumberField label="Alert Days Before Expiry" value={settings.alertDaysBefore} onChange={(v) => handleUpdate({ alertDaysBefore: v })} min={1} max={365} suffix="days" />
      <p className="mt-2 text-xs text-gray-400">Notification preferences are synced to your account.</p>
    </div>
  );
}

function DashboardSection({ settings, update }: { settings: DashboardSettings; update: (p: Partial<DashboardSettings>) => void }) {
  return (
    <div className="space-y-1">
      <SectionDivider title="Layout" />
      <SelectField label="Landing Page" value={settings.defaultLandingPage} onChange={(v) => update({ defaultLandingPage: v })} options={[
        { label: 'Portfolio', value: '/' },
        { label: 'Capabilities', value: '/capabilities' },
      ]} />
      <SelectField label="Remediation Default View" value={settings.defaultRemediationView} onChange={(v) => update({ defaultRemediationView: v })} options={[
        { label: 'Table', value: 'table' },
        { label: 'Kanban Board', value: 'kanban' },
      ]} />
      <SelectField label="Table Density" value={settings.tableDensity} onChange={(v) => update({ tableDensity: v })} options={[
        { label: 'Compact', value: 'compact' },
        { label: 'Comfortable', value: 'comfortable' },
      ]} />
      <SectionDivider title="Refresh" />
      <SelectField label="Auto-Refresh Interval" value={settings.autoRefreshInterval} onChange={(v) => update({ autoRefreshInterval: v })} options={[
        { label: 'Off', value: 0 },
        { label: '15 seconds', value: 15000 },
        { label: '30 seconds', value: 30000 },
        { label: '60 seconds', value: 60000 },
      ]} />
      <Toggle label="Show Summary Cards" checked={settings.showSummaryCards} onChange={(v) => update({ showSummaryCards: v })} />
    </div>
  );
}

function ChatSection({ settings, update }: { settings: DashboardSettings; update: (p: Partial<DashboardSettings>) => void }) {
  return (
    <div className="space-y-1">
      <SectionDivider title="Response Style" />
      <SelectField label="Chat Verbosity" value={settings.chatVerbosity} onChange={(v) => update({ chatVerbosity: v })} options={[
        { label: 'Concise', value: 'concise' },
        { label: 'Detailed', value: 'detailed' },
        { label: 'Executive Summary', value: 'executive' },
      ]} />
      <SectionDivider title="Panel" />
      <Toggle label="Show Quick Actions" checked={settings.showQuickActions} onChange={(v) => update({ showQuickActions: v })} />
      <SelectField label="Panel Width" value={settings.chatPanelWidth} onChange={(v) => update({ chatPanelWidth: v })} options={[
        { label: 'Narrow (360px)', value: 360 },
        { label: 'Default (420px)', value: 420 },
        { label: 'Wide (520px)', value: 520 },
        { label: 'Extra Wide (640px)', value: 640 },
      ]} />
    </div>
  );
}

function ExportSection({ settings, update }: { settings: DashboardSettings; update: (p: Partial<DashboardSettings>) => void }) {
  return (
    <div className="space-y-1">
      <SectionDivider title="Formats" />
      <SelectField label="Default Export Format" value={settings.defaultExportFormat} onChange={(v) => update({ defaultExportFormat: v })} options={[
        { label: 'CSV', value: 'csv' },
        { label: 'JSON', value: 'json' },
        { label: 'Excel (XLSX)', value: 'xlsx' },
      ]} />
      <SectionDivider title="Date & Time" />
      <SelectField label="Date Format" value={settings.dateFormat} onChange={(v) => update({ dateFormat: v })} options={[
        { label: 'MM/DD/YYYY (US)', value: 'US' },
        { label: 'YYYY-MM-DD (ISO)', value: 'ISO' },
        { label: 'DD/MM/YYYY (EU)', value: 'EU' },
      ]} />
      <TextField label="Timezone" value={settings.timezone} onChange={(v) => update({ timezone: v })} placeholder="America/New_York" />
    </div>
  );
}

function ComplianceSection({ settings, update }: { settings: DashboardSettings; update: (p: Partial<DashboardSettings>) => void }) {
  return (
    <div className="space-y-1">
      <SectionDivider title="Framework" />
      <SelectField label="Organization Framework" value={settings.activeFramework} onChange={(v) => update({ activeFramework: v })} options={[
        { label: 'NIST 800-53 Rev. 5', value: 'NIST 800-53 Rev. 5' },
        { label: 'NIST 800-53 Rev. 4', value: 'NIST 800-53 Rev. 4' },
        { label: 'FedRAMP Rev. 5', value: 'FedRAMP Rev. 5' },
        { label: 'CNSSI 1253', value: 'CNSSI 1253' },
      ]} />
      <SectionDivider title="POA&M Thresholds" />
      <NumberField label="Overdue Threshold" value={settings.poamOverdueThreshold} onChange={(v) => update({ poamOverdueThreshold: v })} min={1} max={365} suffix="days" />
    </div>
  );
}

function IntegrationsSection({ settings, update }: { settings: DashboardSettings; update: (p: Partial<DashboardSettings>) => void }) {
  return (
    <div className="space-y-1">
      <SectionDivider title="Connected Systems" />
      <Toggle label="eMASS Integration" checked={settings.emassEnabled} onChange={(v) => update({ emassEnabled: v })} />
      <Toggle label="ACAS / Nessus Import" checked={settings.acasEnabled} onChange={(v) => update({ acasEnabled: v })} />
      <Toggle label="Prisma Cloud" checked={settings.prismaCloudEnabled} onChange={(v) => update({ prismaCloudEnabled: v })} />
      <Toggle label="STIG Viewer" checked={settings.stigViewerEnabled} onChange={(v) => update({ stigViewerEnabled: v })} />
      <p className="mt-2 text-xs text-gray-400">Enable integrations to auto-import compliance data from external sources.</p>
    </div>
  );
}

function AdminSection({ settings, update }: { settings: DashboardSettings; update: (p: Partial<DashboardSettings>) => void }) {
  return (
    <div className="space-y-1">
      <SectionDivider title="Session" />
      <SelectField label="Session Timeout" value={settings.sessionTimeout} onChange={(v) => update({ sessionTimeout: v })} options={[
        { label: '15 minutes', value: 15 },
        { label: '30 minutes', value: 30 },
        { label: '1 hour', value: 60 },
        { label: '2 hours', value: 120 },
      ]} />
      <SectionDivider title="Advanced" />
      <Toggle label="Usage Analytics" checked={settings.enableAnalytics} onChange={(v) => update({ enableAnalytics: v })} />
      <Toggle label="Debug Mode" checked={settings.debugMode} onChange={(v) => update({ debugMode: v })} />
    </div>
  );
}

// ─── Main Component ─────────────────────────────────────────────────────────

export default function SettingsPanel({ onClose }: SettingsPanelProps) {
  const { settings, updateSettings, resetSettings } = useSettings();
  const [activeSection, setActiveSection] = useState<SectionId>('profile');
  const [showResetConfirm, setShowResetConfirm] = useState(false);

  const renderSection = () => {
    switch (activeSection) {
      case 'profile': return <ProfileSection settings={settings} update={updateSettings} />;
      case 'notifications': return <NotificationsSection settings={settings} update={updateSettings} />;
      case 'dashboard': return <DashboardSection settings={settings} update={updateSettings} />;
      case 'chat': return <ChatSection settings={settings} update={updateSettings} />;
      case 'export': return <ExportSection settings={settings} update={updateSettings} />;
      case 'compliance': return <ComplianceSection settings={settings} update={updateSettings} />;
      case 'integrations': return <IntegrationsSection settings={settings} update={updateSettings} />;
      case 'admin': return <AdminSection settings={settings} update={updateSettings} />;
    }
  };

  return (
    <>
      {/* Backdrop */}
      <div className="fixed inset-0 z-40 bg-black/20 transition-opacity" onClick={onClose} />

      {/* Panel */}
      <div className="fixed inset-y-0 right-0 z-50 flex w-full max-w-lg shadow-xl">
        <div className="flex w-full flex-col bg-white">
          {/* Header */}
          <div className="flex items-center justify-between border-b border-gray-200 px-5 py-4">
            <h2 className="text-lg font-semibold text-gray-900">Settings</h2>
            <div className="flex items-center gap-2">
              {showResetConfirm ? (
                <div className="flex items-center gap-1.5">
                  <span className="text-xs text-gray-500">Reset all?</span>
                  <button type="button" onClick={() => { resetSettings(); setShowResetConfirm(false); }} className="rounded px-2 py-1 text-xs font-medium text-red-600 hover:bg-red-50">Yes</button>
                  <button type="button" onClick={() => setShowResetConfirm(false)} className="rounded px-2 py-1 text-xs font-medium text-gray-500 hover:bg-gray-100">No</button>
                </div>
              ) : (
                <button type="button" onClick={() => setShowResetConfirm(true)} className="rounded px-2 py-1 text-xs font-medium text-gray-500 hover:bg-gray-100 hover:text-gray-700" title="Reset to defaults">
                  Reset
                </button>
              )}
              <button
                type="button"
                onClick={onClose}
                className="rounded-md p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors"
                aria-label="Close settings"
              >
                <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M6 18 18 6M6 6l12 12" />
                </svg>
              </button>
            </div>
          </div>

          {/* Body: sidebar + content */}
          <div className="flex flex-1 overflow-hidden">
            {/* Sidebar nav */}
            <nav className="w-44 flex-shrink-0 overflow-y-auto border-r border-gray-100 bg-gray-50 py-2">
              {sections.map((s) => (
                <button
                  key={s.id}
                  type="button"
                  onClick={() => setActiveSection(s.id)}
                  className={`flex w-full items-center gap-2 px-3 py-2 text-left text-sm transition-colors ${
                    activeSection === s.id
                      ? 'bg-blue-50 text-blue-700 font-medium'
                      : 'text-gray-600 hover:bg-gray-100 hover:text-gray-900'
                  }`}
                >
                  <svg className="h-4 w-4 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                    <path strokeLinecap="round" strokeLinejoin="round" d={s.icon} />
                  </svg>
                  {s.label}
                </button>
              ))}
            </nav>

            {/* Content */}
            <div className="flex-1 overflow-y-auto px-5 py-4">
              <h3 className="mb-3 text-sm font-semibold text-gray-900">
                {sections.find((s) => s.id === activeSection)?.label}
              </h3>
              {renderSection()}
            </div>
          </div>

          {/* Footer */}
          <div className="border-t border-gray-200 px-5 py-3">
            <p className="text-xs text-gray-400">Changes are saved automatically. Notification preferences sync to your account.</p>
          </div>
        </div>
      </div>
    </>
  );
}
