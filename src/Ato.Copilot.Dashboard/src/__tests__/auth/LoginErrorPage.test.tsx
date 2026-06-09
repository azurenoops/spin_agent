import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { LoginConfig, ErrorClass } from '../../features/auth/types';
import LoginErrorPage from '../../features/auth/LoginErrorPage';
import { errorCopy } from '../../features/auth/errorCopy';

// ─── Mocks ──────────────────────────────────────────────────────────────

// loginRedirect captured so we can assert the "Try again" CTA wires it.
const loginRedirect = vi.fn();
vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({
    instance: {
      loginRedirect,
    },
  }),
}));

let currentConfig: LoginConfig;
vi.mock('../../features/auth/LoginConfigContext', () => ({
  useLoginConfig: () => currentConfig,
}));

function makeConfig(overrides: Partial<LoginConfig> = {}): LoginConfig {
  return {
    branding: {
      deploymentName: 'Coastal Watch — ATO Copilot',
      logoUrl: null,
      supportEmail: 'support@coastal-watch.gov',
    },
    defaultMethod: 'Cac',
    enabledMethods: [{ id: 'Cac', displayName: 'Sign in with CAC/PIV' }],
    cloud: 'AzureUSGovernment',
    idleTimeoutMinutes: 30,
    rememberTenantCookieDays: 30,
    simulation: null,
    msal: {
      clientId: 'cid',
      authority: 'https://login.microsoftonline.us/tid',
      redirectUri: 'http://localhost/login/callback',
      postLogoutRedirectUri: 'http://localhost/login',
    },
    ...overrides,
  };
}

function renderPage(query: string) {
  return render(
    <MemoryRouter initialEntries={[`/login/error${query}`]}>
      <LoginErrorPage />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  loginRedirect.mockReset();
  currentConfig = makeConfig();
});

// ─── Tests ──────────────────────────────────────────────────────────────

describe('LoginErrorPage', () => {
  const allClasses: ErrorClass[] = [
    'NoCardInserted',
    'CertExpired',
    'CertNotYetValid',
    'CertRevoked',
    'ClockSkew',
    'NoTenantAssignment',
    'AccountDisabled',
    'MfaFailure',
    'ConditionalAccessBlock',
    'NetworkFailure',
  ];

  for (const cls of allClasses) {
    it(`renders the title + body for ErrorClass=${cls}`, () => {
      renderPage(`?errorClass=${cls}&correlationId=corr-${cls}`);

      const copy = errorCopy[cls];
      expect(screen.getByRole('heading', { name: copy.title })).toBeInTheDocument();
      // The body sentence MUST appear verbatim (we tolerate trailing
      // whitespace via getByText's default normalisation).
      expect(screen.getByText(copy.body)).toBeInTheDocument();
    });
  }

  it('renders the correlation id from the query string', () => {
    renderPage('?errorClass=NoTenantAssignment&correlationId=corr-12345');

    expect(screen.getByText(/corr-12345/)).toBeInTheDocument();
  });

  it('renders a support email mailto link from useLoginConfig().branding.supportEmail', () => {
    currentConfig = makeConfig({
      branding: {
        deploymentName: 'X',
        logoUrl: null,
        supportEmail: 'helpdesk@example.gov',
      },
    });

    renderPage('?errorClass=NoTenantAssignment&correlationId=c');

    const link = screen.getByRole('link', { name: /helpdesk@example\.gov/i });
    expect(link).toHaveAttribute('href', expect.stringMatching(/^mailto:helpdesk@example\.gov/));
  });

  it('falls back to a generic contact pointer when branding.supportEmail is null', () => {
    currentConfig = makeConfig({
      branding: {
        deploymentName: 'X',
        logoUrl: null,
        supportEmail: null,
      },
    });

    renderPage('?errorClass=NoTenantAssignment&correlationId=c');

    // No mailto link rendered when no email is configured — but the
    // page must still tell the user what to do (the "Need help?" footer
    // is unique to the support-contact section, separate from the body
    // copy's recovery suggestion).
    expect(screen.queryByRole('link', { name: /mailto/i })).not.toBeInTheDocument();
    expect(screen.getByText(/need help\?/i)).toBeInTheDocument();
  });

  it('renders a Conditional Access remediation link when ?remediationUrl= is present', () => {
    const url = 'https://login.microsoftonline.us/common/resolve?challenge=abc';
    renderPage(
      `?errorClass=ConditionalAccessBlock&correlationId=c&remediationUrl=${encodeURIComponent(url)}`,
    );

    const link = screen.getByRole('link', { name: /resolve|open|remediat/i });
    expect(link).toHaveAttribute('href', url);
  });

  it('renders a generic fallback when errorClass is missing', () => {
    renderPage('?correlationId=c-only');

    expect(screen.getByRole('heading', { name: /sign-in failed/i })).toBeInTheDocument();
    expect(screen.getByText(/c-only/)).toBeInTheDocument();
  });

  it('renders a generic fallback when errorClass is unknown', () => {
    renderPage('?errorClass=NotARealClass&correlationId=c');

    expect(screen.getByRole('heading', { name: /sign-in failed/i })).toBeInTheDocument();
  });

  it('"Try again" button calls instance.loginRedirect', () => {
    renderPage('?errorClass=NetworkFailure&correlationId=c');

    fireEvent.click(screen.getByRole('button', { name: /try again/i }));

    expect(loginRedirect).toHaveBeenCalledTimes(1);
  });

  // ─── Security regressions for #363 ─────────────────────────────────────
  it("security #363: does NOT render a link for javascript: remediationUrl", () => {
    renderPage("?errorClass=ConditionalAccessBlock&correlationId=c&remediationUrl=javascript%3Aalert(document.cookie)");
    expect(screen.queryByRole("link", { name: /remediat|open/i })).not.toBeInTheDocument();
  });
  it("security #363: does NOT render a link for data: remediationUrl", () => {
    renderPage("?errorClass=ConditionalAccessBlock&correlationId=c&remediationUrl=data%3Atext%2Fhtml%2C%3Cscript%3Ealert(1)%3C%2Fscript%3E");
    expect(screen.queryByRole("link", { name: /remediat|open/i })).not.toBeInTheDocument();
  });
  it("security #363: does NOT render a link for http: remediationUrl", () => {
    renderPage("?errorClass=ConditionalAccessBlock&correlationId=c&remediationUrl=http%3A%2F%2Fevil.example.com%2Fsteal");
    expect(screen.queryByRole("link", { name: /remediat|open/i })).not.toBeInTheDocument();
  });
  it("security #363: accepts a well-formed https:// remediationUrl", () => {
    renderPage("?errorClass=ConditionalAccessBlock&correlationId=c&remediationUrl=https%3A%2F%2Faka.ms%2Fconditional-access");
    expect(screen.getByRole("link", { name: /remediat|open/i })).toHaveAttribute("href", "https://aka.ms/conditional-access");
  });
});
