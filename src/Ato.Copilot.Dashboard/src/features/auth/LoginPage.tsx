import { useMemo } from 'react';
import { useMsal } from '@azure/msal-react';
import { useSearchParams, useNavigate } from 'react-router-dom';
import { useLoginConfig } from './LoginConfigContext';
import { DEFAULT_API_SCOPES } from './msalInstance';
import { useLoginRaceListener } from './useLoginRaceListener';
import SimulationPanel from './SimulationPanel';
import type { AuthMethodId } from './types';
// Default deployment logo, used when AuthBrandingOptions.LogoUrl is empty.
// Matches the spin logo PageLayout uses in the dashboard chrome.
import spinLogo from '../../assets/2026-04-22_15-58-30.png';

/**
 * Feature 051 T047 [US1] — branded `/login` page. Renders the deployment
 * name and one button per `enabledMethods` entry. Clicking a button
 * triggers `loginRedirect({ scopes, state })` where `state` carries the
 * deep-link (`?return=/foo`) so post-callback navigation can resume.
 *
 * The simulation panel is a placeholder (`data-testid="simulation-panel"`)
 * for now — Phase 10 (US7) ships the real interactive panel.
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

  // T053c [US1]: when a sibling tab completes sign-in (MSAL writes its
  // account keys to localStorage), advance THIS tab to the deep link
  // without forcing a second user click.
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

        {login.simulation && <SimulationPanel />}

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
