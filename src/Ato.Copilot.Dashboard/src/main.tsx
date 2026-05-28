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
import type { LoginConfig } from './features/auth/types';

/**
 * Bootstrap fetch for `GET /api/auth/login-config`. The endpoint is public
 * (no bearer required) so we use a dedicated axios instance with no
 * interceptors — the MSAL interceptor only attaches after this resolves.
 */
async function fetchLoginConfig(): Promise<LoginConfig> {
  const bootstrap = axios.create({ baseURL: '/' });
  const response = await bootstrap.get<LoginConfig>('/api/auth/login-config');
  return response.data;
}

/** Default API scopes for `acquireTokenSilent`. */
const DEFAULT_API_SCOPES = ['api://ato-copilot/.default'];

function renderError(message: string): void {
  const root = document.getElementById('root');
  if (!root) return;
  root.innerHTML = `
    <div style="font-family: ui-sans-serif, system-ui; padding: 2rem; max-width: 720px; margin: 0 auto;">
      <h1 style="color: #b91c1c;">ATO Copilot — Bootstrap failure</h1>
      <p>The dashboard could not load its login configuration.</p>
      <pre style="background: #f3f4f6; padding: 1rem; border-radius: 0.5rem; white-space: pre-wrap;">${message}</pre>
      <p>Please refresh the page. If the problem persists, contact your administrator.</p>
    </div>
  `;
}

async function bootstrap(): Promise<void> {
  const root = createRoot(document.getElementById('root')!);

  let loginConfig: LoginConfig;
  try {
    loginConfig = await fetchLoginConfig();
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    renderError(message);
    return;
  }

  const msalInstance = new PublicClientApplication(buildMsalConfig(loginConfig));
  await msalInstance.initialize();

  // Wire the global axios default — every existing `axios.create({...})`
  // and direct `axios.*` call across the dashboard now inherits the
  // bearer-injection + silent-renewal behaviour. Existing per-feature
  // `apiClient` instances retain their own request interceptors; the
  // `localStorage.getItem('auth_token')` snippet is stripped in T053.
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
}

void bootstrap();
