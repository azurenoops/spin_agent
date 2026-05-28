/**
 * Feature 051 T129b [US8] — pre-impersonation URL capture for FR-029
 * (analysis C6).
 *
 * When a CSP-Admin enters impersonation we MUST remember the URL they
 * were on, so the "Exit impersonation" affordance can return them to
 * exactly that page (NOT to the CSP root). The URL is stored in
 * sessionStorage so it survives the impersonation cookie round-trip
 * AND is automatically discarded when the tab closes — preventing
 * stale URLs from leaking across browser sessions or other tabs.
 *
 * This helper is intentionally a thin wrapper around sessionStorage so
 * the tab-scope contract is explicit at every call site:
 *
 *   ```ts
 *   import {
 *     setPreImpersonationUrl,
 *     getPreImpersonationUrl,
 *     clearPreImpersonationUrl,
 *   } from '../features/auth/preImpersonationUrl';
 *   ```
 *
 * - `setPreImpersonationUrl(url)` — call BEFORE issuing the Feature 048
 *   impersonate request, with `window.location.pathname + search + hash`.
 * - `getPreImpersonationUrl()` — call from the Exit handler to discover
 *   the return URL (null if absent / cleared / stale).
 * - `clearPreImpersonationUrl()` — call AFTER reading the URL on Exit,
 *   AND on auto-expiry, to prevent stale entries from being reused.
 */

/**
 * The sessionStorage key. Exported so tests can pin it and downstream
 * tooling (e.g. browser-extension diagnostics) has a single source of
 * truth.
 */
export const PRE_IMPERSONATION_URL_KEY = 'ato.preImpersonationUrl';

/**
 * Persist `url` so a later call to `getPreImpersonationUrl()` returns
 * it. Silently no-ops when sessionStorage is unavailable
 * (Safari private mode, headless environments). The URL is opaque to
 * this module — pass any string the caller wants to navigate back to.
 */
export function setPreImpersonationUrl(url: string): void {
  try {
    sessionStorage.setItem(PRE_IMPERSONATION_URL_KEY, url);
  } catch {
    // sessionStorage may throw in Safari private mode or in restricted
    // iframes; ignore — Exit will fall back to the persona-default
    // landing page when the key is absent.
  }
}

/**
 * Returns the URL previously stored via `setPreImpersonationUrl`, or
 * `null` if no URL is stored. Never throws.
 */
export function getPreImpersonationUrl(): string | null {
  try {
    return sessionStorage.getItem(PRE_IMPERSONATION_URL_KEY);
  } catch {
    return null;
  }
}

/**
 * Removes the stored URL. Safe to call when no URL is stored. Should
 * be called by the Exit handler after reading the URL, AND by the
 * auto-expiry handler so a stale URL is not used on the next manual
 * impersonation.
 */
export function clearPreImpersonationUrl(): void {
  try {
    sessionStorage.removeItem(PRE_IMPERSONATION_URL_KEY);
  } catch {
    // ignore — same rationale as setPreImpersonationUrl.
  }
}
