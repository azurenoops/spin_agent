import { useEffect, useState, type ReactElement } from 'react';
import { useNavigate } from 'react-router-dom';
import PageLayout from '../../components/layout/PageLayout';
import PageHero from '../../components/layout/PageHero';
import { useCspBranding } from '../../components/layout/useCspBranding';
import {
  getCspDashboardSummary,
  isUnavailable,
  type SummaryResponse,
  type UnavailableState,
} from './api';
import SummaryCards from './widgets/SummaryCards';
import AtoStatusChart from './widgets/AtoStatusChart';
import FindingsBySeverityChart from './widgets/FindingsBySeverityChart';
import OrgsTable from './OrgsTable';

/**
 * Feature 048 / US8 (Phase 3 re-scope) — CSP Portfolio page.
 *
 * Rendered at `/` by `PortfolioRoute` for CSP-Admins who are not
 * currently impersonating an org. The page presents an org-portfolio
 * view: KPIs roll up across every org (mission owner) in the CSP, and
 * the table lists each org for drill-through-via-impersonation.
 *
 * Architectural note: in this codebase a `Tenant` IS the unit of "org /
 * mission owner" — every compliance row carries `TenantId` only, never
 * `OrganizationId`. The legacy `Organization` entity is a sub-grouping
 * stub that no compliance row references. The page therefore reads
 * `tenantCounts.*` from `GET /api/csp/dashboard/summary` and surfaces
 * them as org counts, and `OrgsTable` reads `GET /api/csp/dashboard/tenants`
 * (whose row contract is already org-shaped) and labels rows as orgs.
 *
 * Self-hides defensively when the deployment is `SingleTenant`, the
 * caller is not `CSP.Admin`, or CSP onboarding (US7) is not yet `Active`.
 * The page-level App route + sidebar gating already prevent direct
 * navigation in those cases; this is defense-in-depth.
 */
type LoadState =
  | { kind: 'loading' }
  | { kind: 'ready'; summary: SummaryResponse }
  | { kind: 'unavailable'; state: UnavailableState }
  | { kind: 'error'; message: string };

export default function CspDashboardPage(): ReactElement {
  const navigate = useNavigate();
  // Feature 048 / US7 / T170: pull the CSP-onboarded display name so the
  // portfolio header reads e.g. "Flankspeed portfolio" rather than the
  // generic "CSP portfolio". Falls back to "CSP" until the wizard probe
  // resolves or in deployments where onboarding has not been finalized.
  const cspBranding = useCspBranding();
  const cspName = cspBranding.displayName ?? 'CSP';
  const portfolioTitle = `${cspName} portfolio`;
  const [state, setState] = useState<LoadState>({ kind: 'loading' });

  useEffect(() => {
    let cancelled = false;
    setState({ kind: 'loading' });
    getCspDashboardSummary()
      .then((result) => {
        if (cancelled) return;
        if (isUnavailable(result)) {
          setState({ kind: 'unavailable', state: result });
          return;
        }
        setState({ kind: 'ready', summary: result });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const message = err instanceof Error ? err.message : 'Failed to load CSP dashboard.';
        setState({ kind: 'error', message });
      });
    return () => {
      cancelled = true;
    };
  }, []);

  if (state.kind === 'loading') {
    return (
      <PageLayout title={portfolioTitle}>
        <div className="text-sm text-gray-500" data-testid="csp-dashboard-loading">
          Loading {cspName} portfolio…
        </div>
      </PageLayout>
    );
  }

  if (state.kind === 'unavailable') {
    return (
      <PageLayout title={portfolioTitle}>
        <UnavailableSurface state={state.state} onHome={() => navigate('/')} />
      </PageLayout>
    );
  }

  if (state.kind === 'error') {
    return (
      <PageLayout title={portfolioTitle}>
        <div
          className="rounded border border-red-200 bg-red-50 p-4 text-sm text-red-700"
          role="alert"
          data-testid="csp-dashboard-error"
        >
          <div className="font-semibold">{cspName} portfolio unavailable</div>
          <div className="mt-1">{state.message}</div>
        </div>
      </PageLayout>
    );
  }

  const summary = state.summary;

  return (
    <PageLayout title={portfolioTitle}>
      <div data-testid="csp-dashboard-page">
        <PageHero
          eyebrow="Portfolio"
          title={portfolioTitle}
          description={`All-up KPIs and an entry table for every org (mission owner) in the ${cspName} portfolio. Click an org to drop into its workspace.`}
          actions={
            <span
              className="inline-flex items-center rounded-full bg-white/15 px-3 py-1 text-xs font-medium text-white ring-1 ring-white/30 backdrop-blur"
              data-testid="csp-dashboard-generated-at"
            >
              Generated {new Date(summary.generatedAt).toLocaleString()}
            </span>
          }
        />

        <SummaryCards summary={summary} />

        <div className="mt-6 grid grid-cols-1 gap-4 lg:grid-cols-2">
          <AtoStatusChart counts={summary.atoStatusCounts} />
          <FindingsBySeverityChart
            counts={summary.openFindingsBySeverity}
            openPoamCount={summary.openPoamCount}
            openDeviationCount={summary.openDeviationCount}
          />
        </div>

        <div className="mt-6">
          <OrgsTable />
        </div>
      </div>
    </PageLayout>
  );
}

interface UnavailableSurfaceProps {
  state: UnavailableState;
  onHome: () => void;
}

function UnavailableSurface({
  state,
  onHome,
}: UnavailableSurfaceProps): ReactElement {
  const message =
    state.reason === 'SINGLE_TENANT_MODE'
      ? 'This deployment runs in SingleTenant mode; the cross-CSP portfolio view is not applicable.'
      : state.reason === 'NOT_CSP_ADMIN'
        ? 'You do not have CSP.Admin access. The CSP portfolio is restricted to CSP administrators.'
        : state.reason === 'CSP_ONBOARDING_INCOMPLETE'
          ? 'Complete the CSP onboarding wizard before opening the CSP portfolio.'
          : 'The CSP portfolio service is unreachable. Try again in a moment.';
  return (
    <div
      className="rounded border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800"
      role="alert"
      data-testid={`csp-dashboard-unavailable-${state.reason}`}
    >
      <div className="font-semibold">CSP portfolio unavailable</div>
      <div className="mt-1">{message}</div>
      <button
        type="button"
        onClick={onHome}
        className="mt-3 rounded border border-amber-300 px-3 py-1 text-xs font-medium text-amber-900 hover:bg-amber-100"
      >
        Return home
      </button>
    </div>
  );
}
