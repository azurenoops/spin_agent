import { Activity, Building2, House, Settings, Users } from 'lucide-react';
import { Link, NavLink, useLocation } from '../workspaces/workspaceNavigation';

const destinations = [
  { to: '/', label: 'Overview', icon: House },
  { to: '/authorizations', label: 'Offerings', icon: Building2 },
  { to: '/systems', label: 'Mission systems', icon: Users },
  { to: '/provider-changes', label: 'Changes', icon: Activity },
  { to: '/provider-administration', label: 'Administration', icon: Settings },
];

export default function ProviderNavigation({ mobile = false }: { mobile?: boolean }) {
  const { pathname, search } = useLocation();
  const relationship = /^\/authorizations\/offerings\/[^/]+\/missions\//.test(pathname);
  const capability = /^\/security-capabilities\/[^/]+(?:\/|$)/.test(pathname);
  const changes = pathname.startsWith('/authorizations/offerings/') && new URLSearchParams(search).get('returnTo') === 'changes';
  const contextDestination = changes ? '/provider-changes' : relationship ? '/systems' : capability ? '/authorizations' : null;
  const links = <nav aria-label={mobile ? 'Provider mobile navigation' : 'Provider workspace'} className="space-y-1">
    {destinations.map(({ to, label, icon: Icon }) => contextDestination && ['/systems', '/authorizations', '/provider-changes'].includes(to)
      ? <Link key={to} to={to} aria-current={contextDestination === to ? 'page' : undefined}
        className={`flex items-center gap-3 rounded-lg px-3 py-3 text-[13px] ${contextDestination === to ? 'bg-[#efedfc] font-semibold text-[#5143d7]' : 'text-slate-600 hover:bg-slate-50'}`}>
        <Icon size={18} aria-hidden="true" />{label}
      </Link>
      : <NavLink key={to} to={to} end={to === '/'}
      className={({ isActive }) => `flex items-center gap-3 rounded-lg px-3 py-3 text-[13px] transition-colors ${
        isActive ? 'bg-[#efedfc] font-semibold text-[#5143d7] dark:bg-indigo-950 dark:text-indigo-200'
          : 'text-slate-600 hover:bg-slate-50 dark:text-slate-300 dark:hover:bg-gray-800'}`}>
      <Icon size={18} aria-hidden="true" />{label}
    </NavLink>)}
  </nav>;
  if (mobile) return <details className="relative lg:hidden">
    <summary className="cursor-pointer rounded-md border border-slate-200 px-3 py-2 text-xs dark:border-gray-700">
      Provider navigation
    </summary>
    <div className="absolute left-0 top-full z-50 mt-2 w-56 rounded-lg border border-slate-200 bg-white p-2 shadow-lg dark:border-gray-700 dark:bg-gray-900">
      {links}
    </div>
  </details>;
  return <aside className="hidden w-[214px] flex-shrink-0 overflow-y-auto border-r border-slate-200 bg-white px-[13px] py-6 dark:border-gray-700 dark:bg-gray-900 lg:block">
    <p className="mb-4 px-3 text-[10px] uppercase tracking-[.12em] text-slate-500">Provider workspace</p>
    {links}
    <div className="mx-3 mt-9 space-y-3 border-t border-slate-200 pt-5 text-xs dark:border-gray-700">
      <NavLink to="/audit" className="block text-indigo-700 dark:text-indigo-300">Audit history</NavLink>
      <NavLink to="/admin/knowledge-base" className="block text-indigo-700 dark:text-indigo-300">Knowledge Base</NavLink>
    </div>
  </aside>;
}
