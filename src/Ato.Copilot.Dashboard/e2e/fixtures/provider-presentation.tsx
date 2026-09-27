import { createRoot } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import '../../src/index.css';
import { WorkspaceNavigationProvider } from '../../src/features/workspaces/workspaceNavigation';
import WorkspacePageHeader from '../../src/components/layout/WorkspacePageHeader';
import { OfferingOverview } from '../../src/features/provider-authorizations/OfferingOverview';
import { OfferingList } from '../../src/features/provider-authorizations/OfferingList';
import { HostingSetupPage } from '../../src/features/provider-authorizations/HostingSetupPage';
import { FindingsPage } from '../../src/features/provider-authorizations/FindingsPage';
import { PackagesSection } from '../../src/features/provider-authorizations/AuthorizationsPage';
import { fixtureOffering } from './provider-presentation-data';

const screen = new URLSearchParams(location.search).get('screen') ?? 'offering';
const titles: Record<string, string> = { offering: fixtureOffering.name, offerings: 'Service offerings',
  scope: 'Services & scope', capabilities: 'Capabilities & responsibilities', evidence: 'Evidence & findings', sources: 'Authorizations & sources' };
const entry = `/workspaces/csp/authorizations/offerings/${fixtureOffering.offeringId}${screen === 'capabilities' ? '/inherited-coverage?task=capabilities' : ''}`;
createRoot(document.getElementById('root')!).render(
  <MemoryRouter initialEntries={[entry]}><WorkspaceNavigationProvider workspace={{ kind: 'csp' }}>
    <div style={{ background: '#28263f', color: 'white', padding: '12px 24px', fontSize: 12 }}>ISOLATED COMPONENT FIXTURE · Synthetic API responses · No backend access</div>
    <main className="provider-workspace" style={{ maxWidth: 1240, margin: '0 auto', minHeight: '100vh', padding: 32 }}>
      <WorkspacePageHeader eyebrow="Provider offering fixture" title={titles[screen]}
        description="Manage the source-backed service baseline that customers can apply to their systems." />
      {screen !== 'offerings' && <nav className="provider-tabs" aria-label="Fixture offering sections">
        {Object.entries({ offering: 'Overview', sources: 'Authorizations & sources', scope: 'Services & scope', capabilities: 'Capabilities & responsibilities', evidence: 'Evidence & findings' }).map(([id, label]) =>
          <a key={id} href={`?screen=${id}`} aria-current={screen === id ? 'page' : undefined}>{label}</a>)}
      </nav>}
      {screen === 'offering' ? <OfferingOverview offering={fixtureOffering} />
        : screen === 'offerings' ? <OfferingList />
          : screen === 'sources' ? <PackagesSection offering={fixtureOffering} />
          : screen === 'evidence' ? <FindingsPage offering={fixtureOffering} onChanged={() => { throw new Error('Read-only fixture must not mutate'); }} />
            : <HostingSetupPage offering={fixtureOffering} onChanged={() => { throw new Error('Read-only fixture must not mutate'); }} />}
    </main>
  </WorkspaceNavigationProvider></MemoryRouter>,
);
