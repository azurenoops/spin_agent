import { useState } from 'react';
import axios from 'axios';
import { useLoginConfig } from './LoginConfigContext';

/**
 * Feature 051 T127 [US7] — dev-only identity selector mounted on the
 * `/login` page below the Sign In buttons.
 *
 * **Layer 2 of the three-layer simulation-panel security invariant**
 * (research.md § R-Summary item 4): the component returns `null` whenever
 * `useLoginConfig().simulation` is `null`. The {@link SimulationPanelProps.force}
 * prop exists ONLY so unit tests can prove the guard cannot be bypassed —
 * production callers omit it.
 *
 * On click the panel POSTs to `/api/auth/simulate?identityId=<key>` (no
 * body, per contracts/http-api.md § 5.2). On 204 it reloads the page so
 * the SPA re-bootstraps as the new identity (re-runs the MSAL state hook,
 * re-fetches `/me`, re-renders the dashboard shell). Network failures
 * surface a user-readable error message and leave the page intact.
 */
export interface SimulationPanelProps {
  /**
   * Test-only — never set in production. Even when true the route guard
   * STILL refuses to mount the panel when
   * `useLoginConfig().simulation == null`. Documented so reviewers see
   * the defense-in-depth contract.
   */
  force?: boolean;
}

export default function SimulationPanel(_props: SimulationPanelProps = {}) {
  const login = useLoginConfig();
  const [pendingId, setPendingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Defense-in-depth: even with `force=true` we refuse to mount when the
  // server's login-config did not surface a simulation descriptor.
  if (login.simulation === null) {
    return null;
  }

  const identities = login.simulation.identities;

  const handleClick = async (id: string) => {
    setPendingId(id);
    setError(null);
    try {
      await axios.post(`/api/auth/simulate?identityId=${encodeURIComponent(id)}`);
      // On success the server set the ato-simulation + X-Simulated cookies
      // (both HttpOnly). Navigate to the dashboard root — RequireAuth on
      // that route probes /api/auth/me, sees the simulation cookies, and
      // promotes the user to authenticated without bouncing through Entra.
      // Using window.location.assign (not reload) so we LEAVE /login
      // instead of re-bootstrapping it and ending up back on the panel.
      window.location.assign('/');
    } catch {
      setPendingId(null);
      setError(
        'Could not start simulated session. Check the dev server logs and confirm the simulated identity is configured under CacAuth:SimulatedIdentities.',
      );
    }
  };

  return (
    <div
      data-testid="simulation-panel"
      className="mt-8 border-t border-gray-200 pt-6"
    >
      <h2 className="text-sm font-semibold text-gray-700 mb-2">
        Developer simulation
      </h2>
      <p className="text-xs text-gray-500 mb-3">
        Development environment only — pick a simulated identity to sign in
        without OAuth / CAC.
      </p>
      <div className="space-y-2">
        {identities.map((i) => {
          const isPending = pendingId === i.id;
          return (
            <button
              key={i.id}
              type="button"
              disabled={pendingId !== null}
              onClick={() => handleClick(i.id)}
              className={
                'w-full px-3 py-2 rounded-md border border-amber-300 bg-amber-50 ' +
                'text-amber-900 text-sm text-left hover:bg-amber-100 ' +
                'focus:outline-none focus:ring-2 focus:ring-amber-400 ' +
                'disabled:opacity-60 disabled:cursor-not-allowed'
              }
            >
              <span className="font-medium">{i.displayName}</span>
              <span className="ml-2 text-xs text-amber-700">({i.persona})</span>
              {isPending && (
                <span className="ml-2 text-xs italic">signing in…</span>
              )}
            </button>
          );
        })}
      </div>
      {error && (
        <div
          role="alert"
          className="mt-3 text-xs text-red-700 bg-red-50 border border-red-200 rounded p-2"
        >
          {error}
        </div>
      )}
    </div>
  );
}
