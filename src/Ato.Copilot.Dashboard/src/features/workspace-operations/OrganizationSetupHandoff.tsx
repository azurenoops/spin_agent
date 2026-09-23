import { Link } from '../workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../workspaces/WorkspaceBoundary';
import type { OrganizationDetail, ProvisioningResult } from './types';
import { buttonClass, secondaryButtonClass } from './workspaceUi';
import { enrollmentComplete, organizationSetupLabel, setupCard } from './OrganizationSetupPresentation';

export default function OrganizationSetupHandoff({ detail, provisioning }: {
  detail: OrganizationDetail; provisioning: ProvisioningResult | null;
}) {
  const canManageMembers = useWorkspaceSession()?.workspace.permissions.canManageMemberships === true;
  const complete = provisioning ? enrollmentComplete(provisioning) : detail.setupState === 'Completed';
  return <section aria-label="Organization setup and next steps" className="space-y-4">
    <div className="flex flex-wrap items-center gap-3"><h2 className="text-lg font-semibold">Organization setup</h2>
      <span className={`rounded border px-2 py-1 text-xs ${complete ? 'border-emerald-200 bg-emerald-50 text-emerald-800' : 'border-amber-200 bg-amber-50 text-amber-900'}`}>
        {organizationSetupLabel(complete ? 'Completed' : provisioning?.lastError ? 'Failed' : provisioning ? 'Pending' : detail.setupState)}
      </span>
    </div>
    <div className="grid gap-3 sm:grid-cols-3">
      {[['Members', detail.memberCount ?? 'Unavailable'], ['Systems', detail.systems.length],
        ['Subscriptions', detail.subscriptions.filter(item => item.isActive).length]].map(([label, count]) =>
        <div className={setupCard} key={label}><p className="text-xs text-slate-500">{label}</p><p className="mt-1 text-xl font-semibold">{count}</p></div>)}
    </div>
    <div className="grid gap-4 md:grid-cols-3">
      <section className={setupCard}><h3 className="font-semibold">Manage members</h3>
        <p className="mb-4 mt-2 text-sm">Manage explicit organization access. System and RMF assignments remain separate.</p>
        {canManageMembers ? <Link className={`${buttonClass} inline-flex`} to={`/organizations/${detail.id}/memberships`}>Manage members</Link>
          : <p className="text-sm">Membership administration permission is required.</p>}
      </section>
      <section className={setupCard}><h3 className="font-semibold">Coordinate system onboarding</h3>
        <p className="mt-2 text-sm">Authorized organization users register systems and assign RMF roles in their organization workspace. This provider view grants no customer-system access.</p>
      </section>
      <section className={setupCard}><h3 className="font-semibold">Review available provider capabilities</h3>
        <p className="mb-4 mt-2 text-sm">Review provider offerings. Organization adoption and system subscriptions require separate, explicit selection.</p>
        <Link className={`${secondaryButtonClass} inline-flex`} to="/security-capabilities">View provider catalog</Link>
      </section>
    </div>
  </section>;
}
