import { createContext, useContext, useCallback, type ReactNode } from 'react';
import { useLocalStorage } from './useLocalStorage';

// ─── Settings Types ─────────────────────────────────────────────────────────

export interface DashboardSettings {
  // Profile & Identity
  displayName: string;
  role: 'AO' | 'ISSM' | 'ISSO' | 'SCA' | 'Engineer' | '';
  organization: string;

  // Notifications & Alerts
  poamOverdueAlerts: boolean;
  atoExpirationAlerts: boolean;
  complianceDriftAlerts: boolean;
  alertDaysBefore: number; // days before expiration to alert

  // Dashboard Preferences
  defaultLandingPage: '/' | '/assessments' | '/remediation' | '/capabilities';
  defaultRemediationView: 'table' | 'kanban';
  tableDensity: 'compact' | 'comfortable';
  autoRefreshInterval: 0 | 15000 | 30000 | 60000; // 0 = off
  showSummaryCards: boolean;

  // Chat & AI
  chatVerbosity: 'concise' | 'detailed' | 'executive';
  showQuickActions: boolean;
  chatPanelWidth: number;

  // Data & Export
  defaultExportFormat: 'csv' | 'json' | 'xlsx';
  dateFormat: 'US' | 'ISO' | 'EU'; // MM/DD/YYYY | YYYY-MM-DD | DD/MM/YYYY
  timezone: string;

  // Compliance Framework
  activeFramework: 'NIST 800-53' | 'NIST CSF' | 'CMMC' | 'FedRAMP';
  baselineOverride: 'Low' | 'Moderate' | 'High' | '';
  poamOverdueThreshold: number; // days

  // Integrations
  emassEnabled: boolean;
  acasEnabled: boolean;
  prismaCloudEnabled: boolean;
  stigViewerEnabled: boolean;

  // Administration
  sessionTimeout: 15 | 30 | 60 | 120; // minutes
  enableAnalytics: boolean;
  debugMode: boolean;
}

export const DEFAULT_SETTINGS: DashboardSettings = {
  // Profile & Identity
  displayName: '',
  role: '',
  organization: '',

  // Notifications & Alerts
  poamOverdueAlerts: true,
  atoExpirationAlerts: true,
  complianceDriftAlerts: true,
  alertDaysBefore: 30,

  // Dashboard Preferences
  defaultLandingPage: '/',
  defaultRemediationView: 'table',
  tableDensity: 'comfortable',
  autoRefreshInterval: 30000,
  showSummaryCards: true,

  // Chat & AI
  chatVerbosity: 'detailed',
  showQuickActions: true,
  chatPanelWidth: 420,

  // Data & Export
  defaultExportFormat: 'csv',
  dateFormat: 'US',
  timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,

  // Compliance Framework
  activeFramework: 'NIST 800-53',
  baselineOverride: '',
  poamOverdueThreshold: 30,

  // Integrations
  emassEnabled: false,
  acasEnabled: false,
  prismaCloudEnabled: false,
  stigViewerEnabled: false,

  // Administration
  sessionTimeout: 30,
  enableAnalytics: true,
  debugMode: false,
};

// ─── Context ────────────────────────────────────────────────────────────────

export interface SettingsContextValue {
  settings: DashboardSettings;
  updateSettings: (partial: Partial<DashboardSettings>) => void;
  resetSettings: () => void;
}

const SettingsContext = createContext<SettingsContextValue | null>(null);

export { SettingsContext };
export type { ReactNode };

export function useSettingsProvider(): SettingsContextValue {
  const [settings, setSettings] = useLocalStorage<DashboardSettings>('ato-dashboard-settings', DEFAULT_SETTINGS);

  const updateSettings = useCallback(
    (partial: Partial<DashboardSettings>) => {
      setSettings((prev) => ({ ...prev, ...partial }));
    },
    [setSettings],
  );

  const resetSettings = useCallback(() => {
    setSettings(DEFAULT_SETTINGS);
  }, [setSettings]);

  return { settings, updateSettings, resetSettings };
}

export function useSettings(): SettingsContextValue {
  const ctx = useContext(SettingsContext);
  if (!ctx) throw new Error('useSettings must be used within SettingsProvider');
  return ctx;
}
