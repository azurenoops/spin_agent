import { lazy, Suspense, useMemo } from 'react';
import { useMsal } from '@azure/msal-react';
import { useSearchParams, useNavigate } from 'react-router-dom';
import { useLoginConfig } from './LoginConfigContext';
import { DEFAULT_API_SCOPES } from './msalInstance';
import { useLoginRaceListener } from './useLoginRaceListener';
import type { AuthMethodId } from './types';
import spinLogo from '../../assets/2026-04-22_15-58-30.png';

// Epic #207 / Task #235 — Build-time simulation mode exclusion.
//
// import.meta.env.DEV is statically `false` in production builds. Vite's
// dead-code eliminator removes the lazy() call AND the entire SimulationPanel
// module from the production bundle — the code is ABSENT, not merely
// unreachable via a flag. This satisfies the FedRAMP constraint from Epic #207:
// "a feature flag that is merely 'off' is insufficient; the code path must be
// excluded at build time."
//
// lazy() + Suspense is used rather than a top-level await import so this file
// remains a standard synchronous module (compatible with SSR and test runners).
const SimulationPanel = import.meta.env.DEV
  ? lazy(() => import('./SimulationPanel'))
  : null;

/**
 * Feature 051 T047 [US1] — branded `/login` page. Renders the deployment
 * name and one button per `enabledMethods` entry. Clicking a button
 * triggers `loginRedirect({ scopes, state })` where `state` carries the
 * deep-link (`?return=/foo`) so post-callback navigation can resume.
 *
 * The simulation panel is only present in development builds (see above).
 */
export default function LoginPage() {
  const login = useLoginConfig();
  const { instance } = useMsal();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();

  /** Resolve the `return` deep-link query param, defaulting to `/`. */
  const returnPath = useMemo(() => {
    const ret = searchParams.get('return');
    if (ret && ret.startsWith('/')) {
      return ret;
    }
    return '/';
  }, [searchParams]);

  // T053c [US1]: when a sibling tab completes sign-in, advance THIS tab to
  // the deep link without forcing a second user click.
  useLoginRaceListener({
    onLoginCompletedInAnotherTab: () => navigate(returnPath, { replace: true }),
  });

  const handleSignIn = (_method: AuthMethodId) => {
    void instance.loginRedirect({
      scopes: DEFAULT_API_SCOPES,
      // `state` round-trips through Entra and is echoed back to
      // /login/callback; we use it to preserve the deep link per FR-016.
      state: returnPath,
    });
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50 px-4">
      <div className="max-w-md w-full bg-white shadow-lg rounded-lg p-8">
        <img
          src={login.branding.logoUrl || spinLogo}
          alt={`${login.branding.deploymentName} logo`}
          className="h-16 mx-auto mb-6"
        />
        <h1 className="text-2xl font-semibold text-gray-900 text-center mb-2">
          {login.branding.deploymentName}
        </h1>
        <p className="text-sm text-gray-600 text-center mb-8">
          Sign in to continue
        </p>

        <div className="space-y-3">
          {login.enabledMethods.map((m) => {
            const isPrimary = m.id === login.defaultMethod;
            const classes = isPrimary
              ? 'w-full px-4 py-3 rounded-md bg-blue-600 text-white font-medium hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2'
              : 'w-full px-4 py-3 rounded-md border border-gray-300 bg-white text-gray-700 font-medium hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-gray-300 focus:ring-offset-2';
            return (
              <button
                key={m.id}
                type="button"
                data-primary={isPrimary ? 'true' : 'false'}
                onClick={() => handleSignIn(m.id)}
                className={classes}
              >
                {m.displayName}
              </button>
            );
          })}
        </div>

        {/* Simulation panel — only present in DEV builds; null in production */}
        {SimulationPanel !== null && login.simulation && (
          <Suspense fallback={null}>
            <SimulationPanel />
          </Suspense>
        )}

        {login.branding.supportEmail && (
          <p className="mt-6 text-xs text-gray-500 text-center">
            Need help?{' '}
            <a
              href={`mailto:${login.branding.supportEmail}`}
              className="text-blue-600 hover:underline"
            >
              {login.branding.supportEmail}
            </a>
          </p>
        )}
      </div>
    </div>
  );
}
