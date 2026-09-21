import type { IPublicClientApplication } from '@azure/msal-browser';
import { selectMsalAccount } from './accountSelection';

/**
 * Feature 051 — module-singleton accessor for the
 * <see cref="IPublicClientApplication"/> instantiated in `main.tsx`.
 *
 * Why a module-level reference?
 *   Many feature folders (e.g. `features/tenancy/api.ts`, `api/client.ts`)
 *   create their OWN `axios.create({...})` clients. Those instances do
 *   NOT inherit interceptors from the global `axios` default — so we
 *   need to attach the MSAL bearer-injection interceptor (defined in
 *   `interceptors.ts`) to each of them. They cannot import `msalInstance`
 *   from `main.tsx` directly because `main.tsx` only finishes constructing
 *   it asynchronously after fetching `GET /api/auth/login-config`.
 *
 *   So `main.tsx` calls {@link setMsalInstance} once after
 *   `PublicClientApplication.initialize()` succeeds, and every feature
 *   `api.ts` lazily resolves the instance via {@link getMsalInstance}
 *   at first use.
 *
 * The accessor throws if read before set — that's an ordering bug that
 * should fail loudly in development, never silently produce unauthenticated
 * requests.
 */

let _instance: IPublicClientApplication | null = null;

export function setMsalInstance(instance: IPublicClientApplication): void {
  _instance = instance;
}

export function getMsalInstance(): IPublicClientApplication {
  if (_instance === null) {
    throw new Error(
      'MSAL instance has not been initialised. ' +
        'Call setMsalInstance() in main.tsx after PublicClientApplication.initialize() completes.',
    );
  }
  return _instance;
}

/** The default API scopes used by every feature-level axios client. */
export const DEFAULT_API_SCOPES = ['api://ato-copilot/access_as_user'];

/**
 * Best-effort bearer-token acquisition for non-axios callers
 * (SignalR `accessTokenFactory`, `fetch` headers, etc.). Returns the
 * access token string when an active MSAL account is present, or an
 * empty string when no account exists. Never throws — callers in the
 * SignalR critical path benefit from a soft failure.
 */
export async function acquireBearer(
  scopes: string[] = DEFAULT_API_SCOPES,
): Promise<string> {
  try {
    const msal = getMsalInstance();
    const account = selectMsalAccount(msal);
    if (!account) return '';
    const result = await msal.acquireTokenSilent({ scopes, account });
    return result.accessToken ?? '';
  } catch {
    return '';
  }
}
