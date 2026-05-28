import { useMemo } from 'react';
import { useMsal } from '@azure/msal-react';
import { useSearchParams, useNavigate } from 'react-router-dom';
import { useLoginConfig } from './LoginConfigContext';
import { DEFAULT_API_SCOPES } from './msalInstance';
import { useLoginRaceListener } from './useLoginRaceListener';
import type { AuthMethodId } from './types';

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
        {login.branding.logoUrl && (
          <img
            src={login.branding.logoUrl}
            alt={`${login.branding.deploymentName} logo`}
            className="h-12 mx-auto mb-6"
          />
        )}
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

        {login.simulation && (
          <div
            data-testid="simulation-panel"
            className="mt-8 border-t border-gray-200 pt-6"
          >
            <h2 className="text-sm font-semibold text-gray-700 mb-2">
              Developer simulation
            </h2>
            <p className="text-xs text-gray-500">
              Simulated identities will be selectable here once Phase 10
              ships. ({login.simulation.identities.length} configured.)
            </p>
          </div>
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
