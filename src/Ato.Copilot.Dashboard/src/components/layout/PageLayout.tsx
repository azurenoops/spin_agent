import { useState, useRef, useEffect, type ReactNode } from 'react';
import { Link, NavLink, useLocation } from '../../features/workspaces/workspaceNavigation';
import { Library } from 'lucide-react';
import WorkspaceSwitchDialog from '../../features/workspaces/WorkspaceSwitchDialog';
import { useMsal } from '@azure/msal-react';
import HelpPanel from '../help/HelpPanel';
import ChatToggle from '../chat/ChatToggle';
import { useChatPanel } from '../chat/ChatPanelContext';
import SettingsPanel from '../settings/SettingsPanel';
import NotificationPanel from '../notifications/NotificationPanel';
// Feature 051 cleanup: legacy RoleSwitcher (pre-login DEV persona
// override) removed. Real identity now comes from /api/auth/me via
// AccountMenu, so the header no longer needs a persona override.
import TenantPicker from '../../features/tenancy/TenantPicker';
// Feature 051 T135 [US8] — the impersonation banner is now mounted at
// the App shell top (App.tsx > AuthenticatedSessionGuardsActive) so it
// renders globally, sticky, and is driven by server-side /me state.
// The previous Feature 048 PageLayout-mounted banner
// (features/tenancy/ImpersonationBanner) has been superseded; the file
// is retained intact for reference but is no longer mounted here.
import AccountMenu from '../../features/auth/AccountMenu';
import { useNotifications } from '../../hooks/useNotifications';
import { useCspBranding } from './useCspBranding';
import spinLogo from '../../assets/2026-04-22_15-58-30.png';
import { useWorkspaceSession } from '../../features/workspaces/WorkspaceBoundary';
import ProviderNavigation from '../../features/provider-workspace/ProviderNavigation';
import OrganizationNavigation from '../../features/workspaces/OrganizationNavigation';
import { displayWorkspaceRoles } from '../../features/workspaces/workspaceRoles';

const legacyNavItems = [
  { to: '/', label: 'Portfolio' },
  { to: '/systems', label: 'Systems' },
  { to: '/components', label: 'Components' },
  { to: '/capabilities', label: 'Capabilities' },
  { to: '/controls', label: 'Controls' },
  // Wave 6 GAP-016
  { to: '/audit', label: 'Audit Log' },
  // Epic #134 NIST Knowledge Base
  { to: '/admin/knowledge-base', label: '📚 Knowledge Base' },
];

interface PageLayoutProps {
  title: string;
  children: ReactNode;
  sidePanel?: ReactNode;
  leftPanel?: ReactNode;
  defaultSidePanelOpen?: boolean;
}

export default function PageLayout({ title, children, sidePanel, leftPanel, defaultSidePanelOpen = true }: PageLayoutProps) {
  const workspace = useWorkspaceSession();
  const location = useLocation();
  const inSystem = /^\/systems\/[^/]+(?:\/|$)/.test(location.pathname) && location.pathname !== '/systems/new';
  const providerWorkspace = workspace?.target.kind === 'csp' && !inSystem;
  const organizationWorkspace = workspace?.target.kind === 'organization';
  const mockWorkspace = providerWorkspace || inSystem;
  const navItems = workspace?.target.kind === 'csp'
    ? [
        { to: '/', label: 'Overview' },
        { to: '/organizations', label: 'Organizations' },
        { to: '/authorizations', label: 'Authorizations' },
        { to: '/security-capabilities', label: 'Security Capabilities' },
        { to: '/controls', label: 'Controls' },
        { to: '/audit', label: 'Audit Log' },
        { to: '/admin/knowledge-base', label: '📚 Knowledge Base' },
      ]
    : workspace?.target.kind === 'organization'
      ? [
          { to: '/', label: 'Portfolio' },
          { to: '/systems', label: 'Systems' },
          { to: '/security-capabilities', label: 'Security Capabilities' },
          { to: '/controls', label: 'Controls' },
          { to: '/audit', label: 'Audit Log' },
          { to: '/admin/knowledge-base', label: '📚 Knowledge Base' },
        ]
      : legacyNavItems;
  const [sidePanelOpen, setSidePanelOpen] = useState(defaultSidePanelOpen);
  const [helpPanelOpen, setHelpPanelOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [switchWorkspaceOpen, setSwitchWorkspaceOpen] = useState(false);
  const [notificationsOpen, setNotificationsOpen] = useState(false);
  const { panelState, togglePanel } = useChatPanel();
  const { unreadCount } = useNotifications();
  // Feature 051 T062 / T062d — pluck the Entra oid from MSAL so the
  // AccountMenu can call purgeUnsavedChanges(oid) on explicit sign-out
  // (FR-008). `localAccountId` is MSAL's projection of the `oid` claim.
  const { accounts } = useMsal();
  const oid = accounts[0]?.localAccountId;
  const displayName = accounts[0]?.name ?? accounts[0]?.username;
  // Feature 048 / US7 / T170: per-deployment CSP branding (logo +
  // display name). Falls back to the default SPIN logo + "Security Posture Intelligence Navigator"
  // wordmark in SingleTenant mode or while onboarding is incomplete.
  const cspBranding = useCspBranding();
  const notifRef = useRef<HTMLDivElement>(null);

  // Close notification panel when clicking outside
  useEffect(() => {
    if (!notificationsOpen) return;
    const handleClick = (e: MouseEvent) => {
      if (notifRef.current && !notifRef.current.contains(e.target as Node)) {
        setNotificationsOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, [notificationsOpen]);

  return (
    <div className={`flex ${workspace ? 'h-full' : 'h-screen'} flex-col overflow-hidden${organizationWorkspace ? ' organization-shell' : ''}`}>
      {/* Top header */}
      <header className={`relative flex ${providerWorkspace ? 'min-h-20 flex-wrap gap-y-2 py-3' : 'h-14'} flex-shrink-0 items-center justify-between border-b border-gray-200 bg-white px-3 sm:px-6 dark:border-gray-700 dark:bg-gray-900 dark:text-gray-100${organizationWorkspace ? ' organization-topbar' : ''}`}>
        <div className="flex min-w-0 items-center gap-2 lg:gap-6">
          <NavLink
            to="/"
            className="flex items-center gap-3"
            aria-label={
              cspBranding.displayName
                ? `${cspBranding.displayName} home`
                : 'Security Posture Intelligence Navigator home'
            }
          >
            {cspBranding.logoUrl ? (
              <img
                src={cspBranding.logoUrl}
                alt={`${cspBranding.displayName ?? 'CSP'} logo`}
                className="block h-10 w-auto object-contain"
                onError={(e) => {
                  (e.currentTarget as HTMLImageElement).style.display = 'none';
                }}
              />
            ) : (
              <img
                src={spinLogo}
                alt="Security Posture Intelligence Navigator"
                className="block h-10 w-auto object-contain sm:h-12"
              />
            )}
            {cspBranding.displayName && !providerWorkspace && !organizationWorkspace && (
              <span className="hidden text-base font-semibold text-gray-800 sm:inline dark:text-gray-100">
                {cspBranding.displayName}
              </span>
            )}
          </NavLink>
          {providerWorkspace && <>
            <div className="min-w-0 border-l border-slate-200 pl-4 dark:border-gray-700 sm:pl-6">
              <p className="max-w-[180px] truncate text-sm font-semibold sm:max-w-sm">{workspace.workspace.displayName}</p>
              <p className="text-[11px] text-slate-500 dark:text-slate-400">Provider workspace · {displayWorkspaceRoles(workspace.roles).join(', ')}</p>
            </div>
            <ProviderNavigation mobile />
          </>}
          {organizationWorkspace && <OrganizationNavigation />}
          {!providerWorkspace && !organizationWorkspace && <nav className="hidden items-center gap-1 xl:flex">
            {navItems.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.to === '/'}
                className={({ isActive }) =>
                  `rounded-md px-3 py-1.5 text-sm font-medium transition-colors ${
                    isActive
                      ? 'bg-indigo-50 text-indigo-700 dark:bg-indigo-950 dark:text-indigo-200'
                      : 'text-gray-600 hover:bg-gray-100 hover:text-gray-900 dark:text-gray-300 dark:hover:bg-gray-800 dark:hover:text-white'
                  }`
                }
              >
                {item.label}
              </NavLink>
            ))}
            {/* Feature 048 (T076 follow-up): the cross-tenant CSP dashboard
                resolves at `/` via PortfolioRoute for CSP-Admins, and the
                CSP-inherited components surface is folded into `/components`
                via ComponentsRoute (CSP-Admin not impersonating ⇒
                CspInheritedComponentsPage). The standalone `/csp-dashboard`
                and `/csp/inherited-components` top-nav links have been
                retired in favor of the scope-aware resolvers. */}
          </nav>}
          {!providerWorkspace && !organizationWorkspace && <details className="relative xl:hidden">
            <summary className="cursor-pointer rounded-md border px-3 py-2 text-sm font-medium text-gray-700 dark:border-gray-600 dark:text-gray-200">
              Navigation
            </summary>
            <nav aria-label="Mobile navigation"
              className="absolute left-0 top-full z-50 mt-2 grid min-w-56 gap-1 rounded-md border bg-white p-2 shadow-lg dark:border-gray-700 dark:bg-gray-900">
              {navItems.map(item => (
                <NavLink key={item.to} to={item.to} end={item.to === '/'}
                  className={({ isActive }) => `rounded px-3 py-2 text-sm ${isActive ? 'bg-indigo-50 font-semibold text-indigo-700 dark:bg-indigo-950 dark:text-indigo-200' : 'text-gray-700 dark:text-gray-200'}`}>
                  {item.label}
                </NavLink>
              ))}
            </nav>
          </details>}
          {!mockWorkspace && <><span className="hidden text-sm text-gray-400 lg:block">|</span>
          <h1 className="hidden text-sm font-medium text-gray-700 lg:block">{title}</h1></>}
        </div>
          <div className="flex items-center gap-1">
            {/* Feature 048 (T076): tenant picker. Self-hides in SingleTenant
                mode and for non-CSP.Admin callers per FR-041. */}
            {!workspace && <TenantPicker />}
            {/* Feature 051 cleanup: legacy <RoleSwitcher /> (the orange
                "DEV ISSM" pre-login persona override) removed — real
                identity now flows from /api/auth/me via AccountMenu. */}
             <div ref={notifRef} className="md:relative">
              <button type="button" onClick={() => setNotificationsOpen(!notificationsOpen)} className={`relative rounded-lg p-2 hover:bg-gray-100 hover:text-gray-700 dark:hover:bg-gray-800 dark:hover:text-gray-100 ${notificationsOpen ? 'bg-indigo-50 text-indigo-600 dark:bg-indigo-950 dark:text-indigo-200' : 'text-gray-500 dark:text-gray-300'}`} aria-label="Notifications" title="Notifications" aria-expanded={notificationsOpen}>
                <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M14.857 17.082a23.848 23.848 0 0 0 5.454-1.31A8.967 8.967 0 0 1 18 9.75V9A6 6 0 0 0 6 9v.75a8.967 8.967 0 0 1-2.312 6.022c1.733.64 3.56 1.085 5.455 1.31m5.714 0a24.255 24.255 0 0 1-5.714 0m5.714 0a3 3 0 1 1-5.714 0" />
                </svg>
                {unreadCount > 0 && (
                  <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-red-500 px-1 text-[10px] font-bold text-white">
                    {unreadCount > 99 ? '99+' : unreadCount}
                  </span>
                )}
              </button>
              {notificationsOpen && <NotificationPanel onClose={() => setNotificationsOpen(false)} />}
            </div>
            <button type="button" onClick={() => setHelpPanelOpen(!helpPanelOpen)} className={`rounded-lg p-2 hover:bg-gray-100 hover:text-gray-700 dark:hover:bg-gray-800 dark:hover:text-gray-100 ${helpPanelOpen ? 'bg-indigo-50 text-indigo-600 dark:bg-indigo-950 dark:text-indigo-200' : 'text-gray-500 dark:text-gray-300'}`} aria-label="Help" title="Help" aria-expanded={helpPanelOpen}>
              <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M9.879 7.519c1.171-1.025 3.071-1.025 4.242 0 1.172 1.025 1.172 2.687 0 3.712-.203.179-.43.326-.67.442-.745.361-1.45.999-1.45 1.827v.75M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9 5.25h.008v.008H12v-.008Z" />
              </svg>
            </button>
            <ChatToggle isOpen={panelState.isOpen} onClick={togglePanel} />
            {workspace && <Link to="/narrative-library"
              aria-label={`${workspace.target.kind === 'csp' ? 'Provider' : 'Organization'} Narrative Library`}
              title={`${workspace.target.kind === 'csp' ? 'Provider' : 'Organization'} Narrative Library`}
              className="rounded-lg p-2 text-gray-500 hover:bg-gray-100 hover:text-gray-700 dark:text-gray-300 dark:hover:bg-gray-800 dark:hover:text-gray-100">
              <Library className="h-5 w-5" aria-hidden="true" />
            </Link>}
            <button type="button" onClick={() => setSettingsOpen(!settingsOpen)} className={`rounded-lg p-2 hover:bg-gray-100 hover:text-gray-700 dark:hover:bg-gray-800 dark:hover:text-gray-100 ${settingsOpen ? 'bg-indigo-50 text-indigo-600 dark:bg-indigo-950 dark:text-indigo-200' : 'text-gray-500 dark:text-gray-300'}`} aria-label="Settings" title="Settings" aria-expanded={settingsOpen}>
              <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M9.594 3.94c.09-.542.56-.94 1.11-.94h2.593c.55 0 1.02.398 1.11.94l.213 1.281c.063.374.313.686.645.87.074.04.147.083.22.127.325.196.72.257 1.075.124l1.217-.456a1.125 1.125 0 0 1 1.37.49l1.296 2.247a1.125 1.125 0 0 1-.26 1.431l-1.003.827c-.293.241-.438.613-.43.992a7.723 7.723 0 0 1 0 .255c-.008.378.137.75.43.991l1.004.827c.424.35.534.955.26 1.43l-1.298 2.247a1.125 1.125 0 0 1-1.369.491l-1.217-.456c-.355-.133-.75-.072-1.076.124a6.47 6.47 0 0 1-.22.128c-.331.183-.581.495-.644.869l-.213 1.281c-.09.543-.56.94-1.11.94h-2.594c-.55 0-1.019-.398-1.11-.94l-.213-1.281c-.062-.374-.312-.686-.644-.87a6.52 6.52 0 0 1-.22-.127c-.325-.196-.72-.257-1.076-.124l-1.217.456a1.125 1.125 0 0 1-1.369-.49l-1.297-2.247a1.125 1.125 0 0 1 .26-1.431l1.004-.827c.292-.24.437-.613.43-.991a6.932 6.932 0 0 1 0-.255c.007-.38-.138-.751-.43-.992l-1.004-.827a1.125 1.125 0 0 1-.26-1.43l1.297-2.247a1.125 1.125 0 0 1 1.37-.491l1.216.456c.356.133.751.072 1.076-.124.072-.044.146-.086.22-.128.332-.183.582-.495.644-.869l.214-1.28Z" />
                <path strokeLinecap="round" strokeLinejoin="round" d="M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z" />
              </svg>
            </button>
            {/* Feature 051 T140 [US9]: full AccountMenu (display name,
                persona, home tenant, active PIM role, sign-out). The
                legacy hardcoded "JS" avatar that previously sat above
                this menu has been removed — real identity now flows
                from /api/auth/me. */}
            <AccountMenu oid={oid} displayName={displayName} onSwitchWorkspace={workspace ? () => setSwitchWorkspaceOpen(true) : undefined} />
          </div>
        </header>

      {/* Content area */}
      <div className="flex flex-1 overflow-hidden">
        {providerWorkspace && <ProviderNavigation />}
        {leftPanel}
        <main className={`flex-1 min-w-0 overflow-y-auto ${mockWorkspace ? 'bg-[#f6f7fb] p-4 sm:p-6 lg:px-8' : 'bg-white p-6'} text-gray-900 dark:bg-gray-950 dark:text-gray-100`}>{children}</main>
        {(sidePanel || helpPanelOpen) && (
          <div className="hidden xl:flex flex-shrink-0">
            {/* Toggle tab on the edge */}
            {!helpPanelOpen && (
              <button
                type="button"
                onClick={() => setSidePanelOpen(!sidePanelOpen)}
                className="flex h-8 w-5 items-center justify-center self-start mt-4 -mr-px rounded-l border border-r-0 border-gray-200 bg-gray-50 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors dark:border-gray-700 dark:bg-gray-900 dark:text-gray-300 dark:hover:bg-gray-800 dark:hover:text-gray-100"
                title={sidePanelOpen ? 'Collapse panel' : 'Expand panel'}
                aria-label={sidePanelOpen ? 'Collapse panel' : 'Expand panel'}
                aria-expanded={sidePanelOpen}
              >
                <svg className={`h-3 w-3 transition-transform ${sidePanelOpen ? '' : 'rotate-180'}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M9 5l7 7-7 7" />
                </svg>
              </button>
            )}
            {helpPanelOpen ? (
              <aside className="w-80 overflow-y-auto border-l border-gray-200 bg-white dark:border-gray-700 dark:bg-gray-900 dark:text-gray-100">
                <HelpPanel onClose={() => setHelpPanelOpen(false)} />
              </aside>
            ) : (
              sidePanelOpen && sidePanel && (
                <aside className="w-80 flex flex-col border-l border-gray-200 bg-white dark:border-gray-700 dark:bg-gray-900 dark:text-gray-100">
                  {sidePanel}
                </aside>
              )
            )}
          </div>
        )}
      </div>

      {/* Settings panel */}
      {settingsOpen && <SettingsPanel onClose={() => setSettingsOpen(false)} />}
      {switchWorkspaceOpen && workspace && <WorkspaceSwitchDialog currentWorkspace={workspace.target}
        currentName={workspace.workspace.displayName} onClose={() => setSwitchWorkspaceOpen(false)} />}
    </div>
  );
}
