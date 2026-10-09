import { useEffect, useRef } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import { authorizationHref } from './api';

export const offeringSections = [
  ['', 'Overview'], ['inherited-coverage?task=capabilities', 'Capabilities'], ['boundary', 'Scope & duties'],
  ['packages', 'Sources & findings'], ['release', 'Release & changes'], ['mission-use', 'Mission use'],
] as const;

export function OfferingSectionNavigation({ offeringId, activePath }: { offeringId: string; activePath: string }) {
  const nav = useRef<HTMLElement>(null);
  useEffect(() => {
    const bar = nav.current;
    const active = bar?.querySelector<HTMLElement>('[aria-current="page"]');
    if (bar && active && bar.scrollWidth > bar.clientWidth)
      bar.scrollLeft = Math.max(0, active.offsetLeft - bar.offsetLeft - 12);
  }, [activePath]);
  return <nav ref={nav} aria-label="Offering sections" className="provider-tabs" onKeyDown={event => {
    if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
    const links = Array.from(event.currentTarget.querySelectorAll<HTMLAnchorElement>('a[href]'));
    const current = links.indexOf(event.target as HTMLAnchorElement);
    if (current < 0) return;
    event.preventDefault();
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? links.length - 1
      : (current + (event.key === 'ArrowRight' ? 1 : -1) + links.length) % links.length;
    links[next]?.focus();
  }}>
    {offeringSections.map(([path, label]) => <Link key={path} to={authorizationHref(offeringId, path)}
      aria-current={path === activePath ? 'page' : undefined}>{label}</Link>)}
  </nav>;
}
