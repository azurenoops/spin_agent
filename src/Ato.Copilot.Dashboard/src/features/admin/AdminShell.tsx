import { NavLink, Outlet, useLocation, useNavigate } from '../workspaces/workspaceNavigation';
import { useMsal } from '@azure/msal-react';
import AccountMenu from '../auth/AccountMenu';
import spinLogo from '../../assets/2026-04-22_15-58-30.png';
import { useEffectiveAccess } from './access';
import { buildWorkspaceUrl } from '../workspaces/workspaceRoutes';

const navigation = [
  { label: 'Overview', to: '/administration', action: 'organization.overview.view', providerAction: 'provider.profile.view' },
  { label: 'Organization profile', to: '/administration/organization/profile', action: 'organization.profile.view' },
  { label: 'People & setup', to: '/administration/organization/setup', action: 'organization.memberships.manage' },
  { label: 'Subscriptions', to: '/administration/organization/subscriptions', action: 'organization.subscriptions.manage' },
  { label: 'Imports', to: '/administration/organization/imports', action: 'organization.imports.manage' },
  { label: 'Templates', to: '/administration/organization/templates', action: 'organization.templates.manage' },
  { label: 'Audit history', to: '/administration/organization/audit', action: 'organization.audit.view' },
  { label: 'Provider setup', to: '/administration/provider/setup', action: 'provider.profile.view' },
  { label: 'Deployment migration', to: '/administration/platform/migration', action: 'platform.migration.preview' },
];

export default function AdminShell() {
  const { access, selectedDestination, selectDestination } = useEffectiveAccess();
  const { accounts } = useMsal();
  const navigate = useNavigate();
  const location = useLocation();
  const administrationDestinations = access?.destinations.filter((destination) =>
    destination.workspace === 'Administration') ?? [];

  const switchWorkspace = async (destinationId: string) => {
    const destination = access?.destinations.find((item) => item.id === destinationId);
    if (!destination || !await selectDestination(destinationId)) return;
    if (destination.scopeKind === 'Organization') {
      navigate(buildWorkspaceUrl(
        { kind: 'organization', tenantId: destination.scopeId },
        '/administration',
      ));
      return;
    }
    if (destination.scopeKind === 'Provider') {
      navigate(buildWorkspaceUrl({ kind: 'csp' }, '/administration'));
      return;
    }
    navigate(destination.workspace === 'Administration'
      ? '/administration'
      : `/systems/${destination.scopeId}`);
  };

  const segments = location.pathname.split('/').filter(Boolean).slice(1);

  return (
    <div className="min-h-screen bg-slate-50 text-slate-900">
      <header className="sticky top-0 z-30 border-b border-slate-200 bg-white">
        <div className="flex h-16 items-center justify-between px-4 sm:px-6">
          <div className="flex min-w-0 items-center gap-4">
            <NavLink to="/administration" aria-label="SPIN administration home" className="shrink-0">
              <img src={spinLogo} alt="Security Posture Intelligence Navigator" className="h-12 w-auto" />
            </NavLink>
            <div className="hidden h-7 w-px bg-slate-200 sm:block" />
            <div className="min-w-0">
              <p className="truncate text-base font-semibold text-slate-950">Administration</p>
              <p className="truncate text-xs text-slate-500">{selectedDestination?.displayName}</p>
            </div>
          </div>
          <div className="flex items-center gap-3">
            {(access?.destinations.length ?? 0) > 1 && (
              <label className="hidden items-center gap-2 text-sm text-slate-600 md:flex">
                <span>Switch workspace</span>
                <select
                  value={selectedDestination?.id ?? ''}
                  onChange={(event) => { void switchWorkspace(event.target.value); }}
                  className="rounded-md border border-slate-300 bg-white px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-200"
                  aria-label="Switch workspace"
                >
                  <option value="" disabled>Choose workspace</option>
                  {access?.destinations.map((destination) => (
                    <option key={destination.id} value={destination.id}>
                      {destination.workspace}: {destination.displayName}
                    </option>
                  ))}
                </select>
              </label>
            )}
            <AccountMenu
              oid={accounts[0]?.localAccountId}
              displayName={accounts[0]?.name ?? accounts[0]?.username}
            />
          </div>
        </div>
      </header>

      <div className="mx-auto grid max-w-[1600px] lg:grid-cols-[250px_1fr]">
        <aside className="border-b border-slate-200 bg-white p-4 lg:min-h-[calc(100vh-4rem)] lg:border-b-0 lg:border-r">
          <div className="mb-5 rounded-lg bg-slate-50 p-3">
            <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">
              {selectedDestination?.scopeKind} scope
            </p>
            <p className="mt-1 text-sm font-semibold text-slate-900">{selectedDestination?.displayName}</p>
            <div className="mt-2 flex flex-wrap gap-1.5">
              {selectedDestination?.badges.map((badge) => (
                <span key={`${badge.source}:${badge.label}`} title={`Verified by ${badge.source}`} className="rounded-full bg-indigo-50 px-2 py-1 text-[11px] font-medium text-indigo-700">
                  {badge.label}
                </span>
              ))}
            </div>
          </div>
          <nav aria-label="Administration" className="grid gap-1 sm:grid-cols-2 lg:grid-cols-1">
            {navigation
              .filter((item) => selectedDestination?.actions.includes(item.action)
                || (item.providerAction && selectedDestination?.actions.includes(item.providerAction)))
              .map((item) => (
                <NavLink
                  key={item.to}
                  to={item.to}
                  end={item.to === '/administration'}
                  className={({ isActive }) =>
                    `rounded-md px-3 py-2 text-sm font-medium ${
                      isActive
                        ? 'bg-indigo-50 text-indigo-700'
                        : 'text-slate-600 hover:bg-slate-100 hover:text-slate-950'
                    }`
                  }
                >
                  {item.label}
                </NavLink>
              ))}
          </nav>
          {administrationDestinations.length > 1 && (
            <NavLink to="/workspace" className="mt-5 block text-sm font-semibold text-indigo-700 hover:text-indigo-900">
              View all administrative scopes
            </NavLink>
          )}
        </aside>

        <main className="min-w-0 p-5 sm:p-7 lg:p-9">
          <nav aria-label="Breadcrumb" className="mb-5 text-sm text-slate-500">
            <ol className="flex flex-wrap items-center gap-2">
              <li><NavLink to="/administration" className="hover:text-indigo-700">Administration</NavLink></li>
              {segments.map((segment) => (
                <li key={segment} className="flex items-center gap-2">
                  <span aria-hidden="true">/</span>
                  <span className="capitalize text-slate-700">{segment.replaceAll('-', ' ')}</span>
                </li>
              ))}
            </ol>
          </nav>
          <Outlet />
        </main>
      </div>
    </div>
  );
}
