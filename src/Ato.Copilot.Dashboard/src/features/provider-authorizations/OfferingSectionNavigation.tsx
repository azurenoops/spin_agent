import { useEffect, useRef } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import { authorizationHref } from './api';

export const offeringSections = [
  ['', 'Overview'], ['packages', 'Authorizations & sources'], ['inherited-coverage', 'Services & scope'],
  ['inherited-coverage?task=capabilities', 'Capabilities & responsibilities'], ['findings', 'Evidence & findings'],
] as const;

export function OfferingSectionNavigation({ offeringId, activePath }: { offeringId: string; activePath: string }) {
  const nav = useRef<HTMLElement>(null);
  useEffect(() => {
    const bar = nav.current;
    const active = bar?.querySelector<HTMLElement>('[aria-current="page"]');
    if (bar && active && bar.scrollWidth > bar.clientWidth)
      bar.scrollLeft = Math.max(0, active.offsetLeft - bar.offsetLeft - 12);
  }, [activePath]);
  return <nav ref={nav} aria-label="Offering sections" className="provider-tabs">
    {offeringSections.map(([path, label]) => <Link key={path} to={authorizationHref(offeringId, path)}
      aria-current={path === activePath ? 'page' : undefined}>{label}</Link>)}
  </nav>;
}
