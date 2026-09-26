import { ArrowUpRight, Building2, Layers3, Server } from 'lucide-react';
import { Link } from '../../features/workspaces/workspaceNavigation';

/** Read-only navigation; destination screens resolve their own scoped permissions. */
export default function PortfolioWorkspaceLinks({ provider = false }: { provider?: boolean }) {
  const links = [
    ...(provider ? [{ to: '/organizations', title: 'Organizations', description: 'Review adoption, onboarding and organization details.', icon: Building2 }] : []),
    { to: '/systems', title: 'Systems', description: provider ? 'Explore systems across your hosted organizations.' : 'Open a system to continue its RMF work.', icon: Server },
    { to: '/security-capabilities', title: 'Security capabilities', description: provider ? 'Manage your catalog, working revisions and releases.' : 'Connect components, coverage and system responsibilities.', icon: Layers3 },
  ];
  return <nav aria-label="Portfolio workspaces" className={`mb-6 grid gap-3 ${provider ? 'md:grid-cols-3' : 'md:grid-cols-2'}`}>
    {links.map(({ to, title, description, icon: Icon }) => <Link key={to} to={to}
      className="group flex items-center gap-4 rounded-xl border border-gray-200 bg-white p-5 transition-colors hover:border-indigo-400 hover:bg-indigo-50/40 focus-visible:outline focus-visible:outline-2 focus-visible:outline-indigo-500 dark:border-gray-700 dark:bg-gray-900 dark:hover:bg-gray-800">
      <span className="rounded-lg bg-indigo-50 p-3 text-indigo-700 dark:bg-indigo-950 dark:text-indigo-300"><Icon size={20} aria-hidden="true" /></span>
      <span className="min-w-0 flex-1"><span className="block text-sm font-semibold text-gray-900 dark:text-gray-100">{title}</span><span className="mt-1 block text-xs leading-5 text-gray-500 dark:text-gray-400">{description}</span></span>
      <ArrowUpRight size={18} className="shrink-0 text-gray-400 group-hover:text-indigo-600" aria-hidden="true" />
    </Link>)}
  </nav>;
}
