import { useEffect, useState, type ReactElement } from 'react';
import { useNavigate } from '../workspaces/workspaceNavigation';
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
import PortfolioWorkspaceLinks from '../../components/layout/PortfolioWorkspaceLinks';

/** Provider oversight landing; management and support live in dedicated workspaces. */
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
  const [revision, setRevision] = useState(0);
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
  }, [revision]);

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
        <UnavailableSurface state={state.state} onHome={() => navigate('/')} onRetry={() => setRevision(v => v + 1)} />
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
          <button type="button" onClick={() => setRevision(v => v + 1)} className="mt-3 underline">Retry portfolio</button>
        </div>
      </PageLayout>
    );
  }

  const summary = state.summary;

  return (
    <PageLayout title={portfolioTitle}>
      <div data-testid="csp-dashboard-page">
        <PageHero
          eyebrow="Provider overview"
          title={portfolioTitle}
          // CSP portfolio spans every org — there is no "active org" to
          // chip next to the title. Suppress the auto-rendered org name
          // (which would otherwise echo the caller's home-tenant org).
          showOrgName={false}
          description="A view across your hosted organizations, system authorizations and open risk. Provider publication and system ATO decisions remain separate."
          actions={
            <><button type="button" onClick={() => setRevision(v => v + 1)} className="rounded-lg border border-white/30 bg-white/15 px-4 py-2 text-sm font-semibold hover:bg-white/25">Refresh portfolio</button><span
              className="inline-flex items-center rounded-full bg-white/15 px-3 py-1 text-xs font-medium text-white ring-1 ring-white/30 backdrop-blur"
              data-testid="csp-dashboard-generated-at"
            >
              Generated {new Date(summary.generatedAt).toLocaleString()}
            </span></>
          }
        />

        <PortfolioWorkspaceLinks provider />
        <SummaryCards summary={summary} />

        <div className="mt-6 grid grid-cols-1 gap-4 lg:grid-cols-2">
          <AtoStatusChart counts={summary.atoStatusCounts} />
          <FindingsBySeverityChart
            counts={summary.openFindingsBySeverity}
            openPoamCount={summary.openPoamCount}
            openDeviationCount={summary.openDeviationCount}
          />
        </div>

        <section className="mt-6 rounded-xl border border-indigo-200 bg-indigo-50/50 p-5 dark:border-indigo-900 dark:bg-indigo-950/30" aria-labelledby="provider-next-steps">
          <h2 id="provider-next-steps" className="font-semibold text-gray-900 dark:text-gray-100">Portfolio follow-up</h2>
          <div className="mt-4 grid gap-4 sm:grid-cols-3">
            <div><p className="text-2xl font-semibold text-indigo-700 dark:text-indigo-300">{summary.tenantCounts.suspended}</p><p className="text-sm text-gray-600 dark:text-gray-300">Suspended organizations</p></div>
            <div><p className="text-2xl font-semibold text-indigo-700 dark:text-indigo-300">{summary.openFindingsBySeverity.critical + summary.openFindingsBySeverity.high}</p><p className="text-sm text-gray-600 dark:text-gray-300">Critical and high findings</p></div>
            <div><p className="text-2xl font-semibold text-indigo-700 dark:text-indigo-300">{summary.openDeviationCount}</p><p className="text-sm text-gray-600 dark:text-gray-300">Open deviations</p></div>
          </div>
          <p className="mt-4 text-xs text-gray-500 dark:text-gray-400">Use Organizations for onboarding, subscriptions and explicit support access. Review system findings in Systems.</p>
        </section>
      </div>
    </PageLayout>
  );
}

interface UnavailableSurfaceProps {
  state: UnavailableState;
  onHome: () => void;
  onRetry: () => void;
}

function UnavailableSurface({
  state,
  onHome,
  onRetry,
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
      <button type="button" onClick={onRetry} className="mt-3 mr-4 underline">Retry portfolio</button>
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
