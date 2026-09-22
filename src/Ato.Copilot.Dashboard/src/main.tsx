import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import axios from 'axios';
import { PublicClientApplication } from '@azure/msal-browser';
import { MsalProvider } from '@azure/msal-react';
import App from './App';
import './index.css';
import { LoginConfigProvider } from './features/auth/LoginConfigContext';
import { buildMsalConfig } from './features/auth/msalConfig';
import { attachAuthInterceptor } from './features/auth/interceptors';
import { setMsalInstance, DEFAULT_API_SCOPES } from './features/auth/msalInstance';
import type { LoginConfig } from './features/auth/types';

/**
 * Bootstrap fetch for `GET /api/auth/login-config`. The endpoint is public
 * (no bearer required) so we use a dedicated axios instance with no
 * interceptors — the MSAL interceptor only attaches after this resolves.
 *
 * The server returns the standard Security Posture Intelligence Navigator envelope
 * `{ status, data, metadata }` per http-api.md § 1.4 — unwrap to the
 * `LoginConfig` payload before handing it to the MSAL bootstrap.
 */
interface LoginConfigEnvelope {
  status: 'success' | 'error';
  data: LoginConfig;
}

async function fetchLoginConfig(): Promise<LoginConfig> {
  const bootstrap = axios.create({ baseURL: '/' });
  const response = await bootstrap.get<LoginConfigEnvelope>('/api/auth/login-config');
  if (response.data?.status !== 'success' || !response.data.data) {
    throw new Error(
      'Server returned a malformed login-config envelope. ' +
      'Expected `{ status: "success", data: { ... } }`, got: ' +
      JSON.stringify(response.data).slice(0, 300));
  }
  return response.data.data;
}

/** Default API scopes for `acquireTokenSilent` come from the shared
 * `msalInstance` module so `main.tsx` and feature `api.ts` agree on
 * the value. */

function renderError(message: string): void {
  const root = document.getElementById('root');
  if (!root) return;
  root.innerHTML = `
    <div style="font-family: ui-sans-serif, system-ui; padding: 2rem; max-width: 720px; margin: 0 auto;">
      <h1 style="color: #b91c1c;">Security Posture Intelligence Navigator — Bootstrap failure</h1>
      <p>The dashboard could not load its login configuration.</p>
      <pre style="background: #f3f4f6; padding: 1rem; border-radius: 0.5rem; white-space: pre-wrap;">${message}</pre>
      <p>Please refresh the page. If the problem persists, contact your administrator.</p>
    </div>
  `;
}

async function bootstrap(): Promise<void> {
  const root = createRoot(document.getElementById('root')!);

  try {
    const loginConfig = await fetchLoginConfig();

    const msalInstance = new PublicClientApplication(buildMsalConfig(loginConfig));
    await msalInstance.initialize();

    // Publish the instance so feature `api.ts` files can attach the MSAL
    // bearer interceptor to their own `axios.create({...})` clients.
    setMsalInstance(msalInstance);

    // Wire the global axios default — every direct `axios.*` call across
    // the dashboard now inherits the bearer-injection + silent-renewal
    // behaviour. Feature folders that build their own `axios.create({...})`
    // pull the instance from `msalInstance.ts` and attach the same
    // interceptor (per T053).
    attachAuthInterceptor(axios, msalInstance, DEFAULT_API_SCOPES);

    root.render(
      <StrictMode>
        <MsalProvider instance={msalInstance}>
          <LoginConfigProvider value={loginConfig}>
            <BrowserRouter>
              <App />
            </BrowserRouter>
          </LoginConfigProvider>
        </MsalProvider>
      </StrictMode>,
    );
  } catch (err) {
    // Covers fetch failure, malformed envelope, MSAL config build, and
    // MSAL.initialize() rejection. Without this widened catch the page
    // silently blanks (the original Phase 2 implementation only caught
    // the fetch step, so a malformed envelope or MSAL config error
    // produced an unhandled promise rejection).
    console.error('[ato-copilot] Bootstrap failed:', err);
    const message = err instanceof Error ? err.message : String(err);
    renderError(message);
  }
}

void bootstrap();
