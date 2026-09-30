import { useEffect, useRef } from 'react';
import { Link, useLocation, useParams } from '../workspaces/workspaceNavigation';
import { isSystemScreenActive, SYSTEM_SCREEN_GROUPS } from './systemScreenRoutes';

interface TaskLink { label: string; path: string; unavailable?: string }

export default function SystemTaskNavigation({ definitionOnly = false }: { definitionOnly?: boolean }) {
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  const navigation = useRef<HTMLElement>(null);
  useEffect(() => {
    const bar = navigation.current;
    const active = bar?.querySelector<HTMLElement>('[aria-current="page"]');
    if (bar && active && bar.scrollWidth > bar.clientWidth)
      bar.scrollLeft = Math.max(0, active.offsetLeft - bar.offsetLeft - 12);
  }, [location.pathname, location.search]);
  if (!id) return null;
  const base = `/systems/${encodeURIComponent(id)}`;
  const path = location.pathname.slice(base.length + 1);
  let tabs: TaskLink[] = [];
  let contribution = '';
  if (path.startsWith('profile/EnvironmentAndDeployment') || path.startsWith('provider-relationships')) {
    contribution = 'SSP · Environment and hosting scope';
    tabs = [
      { label: 'Environment', path: 'profile/EnvironmentAndDeployment' },
      { label: 'Provider hosting', path: 'profile/EnvironmentAndDeployment/hosting' },
    ];
  } else if (path === 'security-capabilities/inventory' || path === 'boundaries') {
    contribution = 'SSP · Boundary description and inventory';
    tabs = [{ label: 'System boundary', path: 'boundaries' }, { label: 'Component inventory', path: 'security-capabilities/inventory' }];
  } else if (path.startsWith('security-capabilities') || path.startsWith('inheritance')) {
    contribution = 'SSP · Control implementation / CRM';
    tabs = [
      { label: 'Applied capabilities', path: 'security-capabilities' },
      { label: 'Responsibilities', path: 'inheritance/subscriptions' },
      { label: 'Control inheritance summary', path: 'inheritance' },
    ];
  } else if (path.startsWith('documents')) {
    contribution = 'SSP / SAP / SAR / POA&M · Package handoff';
    tabs = [
      { label: 'Readiness', path: 'documents' },
      { label: 'Document previews', path: 'documents/preview' },
      { label: 'Export packages', path: 'documents?tab=exports' },
    ];
  } else if (path.startsWith('assessments')) {
    contribution = 'SAP / SAR · Assessment scope and results';
    tabs = [
      { label: 'Assessment plan', path: 'assessments?tab=plan' },
      { label: 'Assessments & results', path: 'assessments' },
      { label: 'Assessment environment', path: 'assessments/environment' },
    ];
  } else if (path.startsWith('narratives')) {
    contribution = 'SSP · Implementation statements';
    tabs = [{ label: 'Narratives', path: 'narratives' }, { label: 'Narrative library', path: 'narratives/library' }];
  } else if (path.startsWith('conmon')) {
    contribution = 'ConMon · Monitoring evidence and reports';
    tabs = [
      { label: 'Coverage & health', path: 'conmon' }, { label: 'Rules', path: 'conmon/rules' },
      { label: 'Detected changes', path: 'conmon/changes' }, { label: 'Impact reviews', path: 'conmon/impacts' },
      { label: 'Reports', path: 'conmon/reports' }, { label: 'Written monitoring plan', path: 'conmon/plan' },
    ];
  }
  const group = SYSTEM_SCREEN_GROUPS.find(candidate => candidate.items.some(item =>
    isSystemScreenActive(item.path, location.pathname, location.search, base, true)));
  if (group) {
    const additionalViews = tabs.filter(tab => !group.items.some(item => item.path === tab.path));
    tabs = [...group.items, ...additionalViews];
    contribution ||= group.label === 'System definition' ? 'SSP · Reviewed system definition'
      : group.label === 'Assessment & risk' ? 'SAR / POA&M · Assessment and risk records'
        : group.label === 'Controls & evidence' ? 'SSP · Control implementation and supporting sources'
          : group.label === 'Team & permissions' ? 'SSP · Responsible personnel'
            : group.label === 'Activity & history' ? 'Retained activity and decision references'
              : 'Reviewed system records';
  }
  if (definitionOnly) tabs = SYSTEM_SCREEN_GROUPS.find(candidate => candidate.label === 'System definition')!.items;
  if (!tabs.length) return null;

  const providerHandoff = path.startsWith('profile/EnvironmentAndDeployment')
    || path.startsWith('provider-relationships') || path.startsWith('security-capabilities')
    || path.startsWith('inheritance');
  const handoff: TaskLink[] = providerHandoff
    ? [{ label: 'Applied capabilities', path: 'security-capabilities' },
      { label: 'Responsibilities', path: 'inheritance/subscriptions' },
      { label: 'Evidence', path: 'evidence' }, { label: 'Documents', path: 'documents' }]
      .filter(item => !tabs.some(tab => tab.path === item.path))
    : [];

  return <section className={definitionOnly ? 'my-6 min-w-0' : 'mb-5 min-w-0 space-y-3'} aria-label="System task navigation">
    {!definitionOnly && group?.label !== 'ATO Readiness' && <div className="flex flex-wrap items-center justify-between gap-2">
      <p className="rounded-full bg-indigo-50 px-3 py-1 text-xs font-medium text-indigo-800 dark:bg-indigo-950 dark:text-indigo-200">
        Contributes to {contribution}
      </p>
      {handoff.length > 0 && <nav aria-label="Related system tasks" className="flex flex-wrap gap-x-4 gap-y-2 text-sm">
        {handoff.map(item => <Link key={item.path} className="text-indigo-700 underline underline-offset-4 dark:text-indigo-300"
          to={`${base}/${item.path}`}>{item.label}</Link>)}
      </nav>}
    </div>}
    <nav ref={navigation} aria-label="System task views" className="system-section-tabs">
      {tabs.map(item => {
        const active = isSystemScreenActive(item.path, location.pathname, location.search, base)
          && !tabs.some(other => other !== item && other.path.startsWith(`${item.path}/`)
            && isSystemScreenActive(other.path, location.pathname, location.search, base));
        if (item.unavailable) return <span key={item.path} aria-disabled="true" title={item.unavailable}
          className="px-1 py-3 text-sm text-slate-400">{item.label} (not available)</span>;
        let destination = `${base}${item.path ? `/${item.path}` : ''}`;
        if (group?.label === 'ATO Readiness') {
          const [pathname, query] = destination.split('?');
          const target = new URLSearchParams(query);
          const current = new URLSearchParams(location.search);
          for (const key of ['purpose', 'context', 'run']) {
            const value = current.get(key);
            if (value !== null) target.set(key, value);
          }
          destination = `${pathname}${target.size ? `?${target}` : ''}`;
        }
        return <Link key={item.path} to={destination} aria-current={active ? 'page' : undefined}
              className={active ? 'font-semibold' : undefined}>
              {item.label}
            </Link>;
      })}
    </nav>
  </section>;
}
