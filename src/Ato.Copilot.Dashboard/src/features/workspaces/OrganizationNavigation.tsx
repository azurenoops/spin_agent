import { useRef } from 'react';
import { Link, useLocation } from './workspaceNavigation';
import '../systems/systemNavigation.css';

const destinations = [
  { to: '/', label: 'Portfolio' },
  { to: '/systems', label: 'Systems' },
  { to: '/security-capabilities', label: 'Security Capabilities' },
  { to: '/admin/knowledge-base', label: 'Knowledge Base' },
];

export default function OrganizationNavigation() {
  const { pathname } = useLocation();
  const menu = useRef<HTMLDetailsElement>(null);
  const selected = pathname.startsWith('/systems') ? '/systems'
    : /^\/(?:security-capabilities|capabilities|components)(?:\/|$)/.test(pathname) ? '/security-capabilities'
      : pathname.startsWith('/admin/knowledge-base') ? '/admin/knowledge-base' : pathname === '/' || pathname === '/portfolio' ? '/' : null;
  const links = destinations.map(item => <Link key={item.to} to={item.to} aria-current={selected === item.to ? 'page' : undefined}
    onClick={() => { if (menu.current) menu.current.open = false; }}>{item.label}</Link>);
  return <>
    <nav aria-label="Organization navigation" className="organization-top-links">{links}</nav>
    <details ref={menu} className="organization-mobile-nav">
      <summary aria-label="Open organization navigation">{destinations.find(item => item.to === selected)?.label ?? 'Navigation'}</summary>
      <nav aria-label="Mobile navigation">{links}</nav>
    </details>
  </>;
}
