import { Link } from 'react-router-dom';
import { useEffectiveAccess } from './access';

const cards = [
  {
    action: 'organization.profile.view',
    title: 'Organization profile',
    description: 'Review identity, classification posture, and organization contact information.',
    to: '/administration/organization/profile',
  },
  {
    action: 'organization.memberships.manage',
    title: 'People and access',
    description: 'Manage verified memberships and administrator assignments separately from system roles.',
    to: '/administration/organization/setup',
  },
  {
    action: 'organization.subscriptions.manage',
    title: 'Azure subscriptions',
    description: 'Register subscriptions independently from system attachment and Azure access.',
    to: '/administration/organization/subscriptions',
  },
  {
    action: 'organization.audit.view',
    title: 'Administrative audit',
    description: 'Review administrative events within the selected organization scope.',
    to: '/administration/organization/audit',
  },
  {
    action: 'organization.imports.manage',
    title: 'Document imports',
    description: 'Review organization onboarding imports and dependency status.',
    to: '/administration/organization/imports',
  },
  {
    action: 'organization.templates.manage',
    title: 'Document templates',
    description: 'Manage organization document templates and narrative seed files.',
    to: '/administration/organization/templates',
  },
  {
    action: 'provider.profile.view',
    title: 'Provider profile and setup',
    description: 'Manage provider identity, support information, and onboarding status.',
    to: '/administration/provider/setup',
  },
  {
    action: 'provider.organizations.manage',
    title: 'Customer organizations',
    description: 'Create and enroll organizations without implicitly opening customer system content.',
    to: '/administration/provider/organizations',
    unavailable: true,
  },
  {
    action: 'provider.offerings.manage',
    title: 'Provider offerings',
    description: 'Manage provider-owned capabilities and released scopes.',
    to: '/administration/provider/offerings',
    unavailable: true,
  },
  {
    action: 'platform.migration.preview',
    title: 'Restricted deployment migration',
    description: 'Preview and confirm the irreversible SingleTenant-to-MultiTenant deployment conversion.',
    to: '/administration/platform/migration',
  },
];

export default function AdminOverviewPage() {
  const { selectedDestination } = useEffectiveAccess();
  const visibleCards = cards.filter((card) => selectedDestination?.actions.includes(card.action));

  return (
    <div className="space-y-7">
      <section className="rounded-xl bg-gradient-to-r from-indigo-700 to-indigo-600 p-7 text-white shadow-sm">
        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-indigo-100">
          Administrative overview
        </p>
        <h2 className="mt-2 text-2xl font-semibold">{selectedDestination?.displayName}</h2>
        <p className="mt-2 max-w-2xl text-sm leading-6 text-indigo-100">
          Complete setup, registration, and access-administration work for this scope.
          System authorization metrics are intentionally excluded.
        </p>
      </section>

      {selectedDestination?.availability === 'SetupRequired' && (
        <section className="rounded-lg border border-amber-200 bg-amber-50 p-4" role="status">
          <h3 className="font-semibold text-amber-900">Setup needs attention</h3>
          <p className="mt-1 text-sm text-amber-800">
            This administrative scope has not completed onboarding. Resume setup to make all authorized functions available.
          </p>
        </section>
      )}

      <section>
        <h2 className="text-lg font-semibold text-slate-900">Administrative work</h2>
        <div className="mt-4 grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {visibleCards.map((card) => (
            <article key={card.title} className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
              <h3 className="font-semibold text-slate-900">{card.title}</h3>
              <p className="mt-2 min-h-12 text-sm leading-5 text-slate-600">{card.description}</p>
              {card.unavailable ? (
                <div className="mt-5">
                  <span className="rounded-full bg-slate-100 px-2.5 py-1 text-xs font-medium text-slate-600">
                    Not yet available
                  </span>
                  <p className="mt-2 text-xs text-slate-500">No operation is exposed until its API is implemented and authorized.</p>
                </div>
              ) : (
                <Link to={card.to} className="mt-5 inline-flex text-sm font-semibold text-indigo-700 hover:text-indigo-900">
                  Open <span aria-hidden="true" className="ml-1">→</span>
                </Link>
              )}
            </article>
          ))}
        </div>
      </section>

      {selectedDestination?.scopeKind === 'Provider' && (
        <section className="rounded-xl border border-slate-200 bg-white p-5 shadow-sm">
          <h2 className="font-semibold text-slate-900">FAST allocation intake</h2>
          <p className="mt-2 text-sm text-slate-600">
            FAST integration has not been implemented or validated in this deployment.
          </p>
          <span className="mt-3 inline-flex rounded-full bg-slate-100 px-2.5 py-1 text-xs font-medium text-slate-600">
            Integration unavailable
          </span>
        </section>
      )}
    </div>
  );
}
