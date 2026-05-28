import { describe, it, expect, beforeEach } from 'vitest';
import {
  PRE_IMPERSONATION_URL_KEY,
  clearPreImpersonationUrl,
  getPreImpersonationUrl,
  setPreImpersonationUrl,
} from '../../features/auth/preImpersonationUrl';

// Feature 051 T129a [US8] — verifies the thin sessionStorage helper that
// implements FR-029 (analysis C6): the "Exit impersonation" affordance
// MUST return the user to the URL they were on BEFORE entering
// impersonation. We persist that URL in sessionStorage (tab-scoped) so it
// survives the impersonation cookie round-trip, but does NOT leak across
// tabs or browser restarts.

describe('preImpersonationUrl', () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  it('writes the URL under the documented sessionStorage key', () => {
    // Arrange
    const url = '/systems/abc-123?tab=controls#findings';

    // Act
    setPreImpersonationUrl(url);

    // Assert
    expect(sessionStorage.getItem(PRE_IMPERSONATION_URL_KEY)).toBe(url);
    // The key is documented in FR-029 / analysis C6 — pin it so future
    // refactors don't accidentally rename it and orphan in-flight tabs.
    expect(PRE_IMPERSONATION_URL_KEY).toBe('ato.preImpersonationUrl');
  });

  it('returns the stored URL via getPreImpersonationUrl', () => {
    // Arrange
    const url = '/csp/dashboard';
    setPreImpersonationUrl(url);

    // Act
    const actual = getPreImpersonationUrl();

    // Assert
    expect(actual).toBe(url);
  });

  it('returns null when no URL is stored', () => {
    // Arrange
    // (sessionStorage cleared in beforeEach.)

    // Act
    const actual = getPreImpersonationUrl();

    // Assert
    expect(actual).toBeNull();
  });

  it('clearPreImpersonationUrl removes the key from sessionStorage', () => {
    // Arrange
    setPreImpersonationUrl('/poam');
    expect(sessionStorage.getItem(PRE_IMPERSONATION_URL_KEY)).not.toBeNull();

    // Act
    clearPreImpersonationUrl();

    // Assert
    expect(sessionStorage.getItem(PRE_IMPERSONATION_URL_KEY)).toBeNull();
    expect(getPreImpersonationUrl()).toBeNull();
  });

  it('clearing sessionStorage also removes the key (tab-scope guarantee)', () => {
    // Arrange — verify the key naming uses sessionStorage semantics, NOT
    // localStorage. sessionStorage.clear() must wipe the key.
    setPreImpersonationUrl('/components');
    expect(sessionStorage.getItem(PRE_IMPERSONATION_URL_KEY)).not.toBeNull();

    // Act
    sessionStorage.clear();

    // Assert
    expect(sessionStorage.getItem(PRE_IMPERSONATION_URL_KEY)).toBeNull();
    // And — defensively — assert localStorage was NOT used (the helper
    // is tab-scoped, not device-scoped).
    expect(localStorage.getItem(PRE_IMPERSONATION_URL_KEY)).toBeNull();
  });

  it('overwriting an existing URL replaces the previous value', () => {
    // Arrange
    setPreImpersonationUrl('/first');

    // Act
    setPreImpersonationUrl('/second');

    // Assert
    expect(getPreImpersonationUrl()).toBe('/second');
  });
});
