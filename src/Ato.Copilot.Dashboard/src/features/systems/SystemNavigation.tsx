import { Link, useLocation, useNavigate } from '../workspaces/workspaceNavigation';
import { SYSTEM_SCREEN_GROUPS, isSystemScreenActive } from './systemScreenRoutes';
import './systemNavigation.css';
import { Activity, ClipboardCheck, FileCheck2, FolderCog, History, House, ShieldCheck, Users, type LucideIcon } from 'lucide-react';

const sectionIcons = new Map<string, LucideIcon>([
  ['Overview', House], ['System definition', FolderCog], ['Controls & evidence', ShieldCheck],
  ['Assessment & risk', ClipboardCheck], ['ATO Readiness', FileCheck2],
  ['Continuous monitoring', Activity], ['Team & permissions', Users], ['Activity & history', History],
]);

function useSystemNavigation(systemId: string) {
  const location = useLocation();
  const base = `/systems/${encodeURIComponent(systemId)}`;
  const relatedPath = location.pathname.startsWith(`${base}/provider-relationships`)
    ? `${base}/profile/EnvironmentAndDeployment`
    : location.pathname === `${base}/conmon/plan` ? `${base}/conmon` : location.pathname;
  const current = SYSTEM_SCREEN_GROUPS.flatMap(group => group.items).find(item =>
    isSystemScreenActive(item.path, relatedPath, location.search, base, true));
  return { base, current };
}

export function SystemSidebar({ systemId, systemName }: { systemId: string; systemName: string }) {
  const { base, current } = useSystemNavigation(systemId);
  const currentGroup = SYSTEM_SCREEN_GROUPS.find(group => group.items.some(item => item.path === current?.path));
  return <aside className="system-sidebar">
    <p className="system-sidebar-title">{systemName}</p>
    <nav aria-label="System navigation">
      {SYSTEM_SCREEN_GROUPS.map(group => {
        const first = group.items.find(item => !item.unavailable);
        const Icon = sectionIcons.get(group.label) ?? FolderCog;
        return first ? <Link key={group.label} to={`${base}${first.path ? `/${first.path}` : ''}`}
          aria-current={currentGroup === group ? 'page' : undefined}><Icon size={18} strokeWidth={1.6} aria-hidden="true" /><span>{group.label}</span></Link>
          : <span key={group.label} aria-disabled="true" className="system-nav-unavailable"><Icon size={18} aria-hidden="true" /><span>{group.label}</span></span>;
      })}
    </nav>
  </aside>;
}

export function SystemPageSelector({ systemId }: { systemId: string }) {
  const { base, current } = useSystemNavigation(systemId);
  const navigate = useNavigate();
  return <select className="system-mobile-page-selector" aria-label="Navigate system pages"
    value={current?.path ?? '__current'} onChange={event => {
      const item = SYSTEM_SCREEN_GROUPS.flatMap(group => group.items).find(item => item.path === event.target.value && !item.unavailable);
      if (item) navigate(`${base}${item.path ? `/${item.path}` : ''}`);
    }}>
    {!current && <option value="__current" disabled>Choose a system page</option>}
    {SYSTEM_SCREEN_GROUPS.map(group => <optgroup key={group.label} label={group.label}>
      {group.items.map(item => <option key={item.path} value={item.path} disabled={!!item.unavailable}>{item.label}</option>)}
    </optgroup>)}
  </select>;
}
