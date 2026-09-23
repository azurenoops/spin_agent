import { useCallback, useSyncExternalStore } from 'react';
import { useMsal } from '@azure/msal-react';
import { EventType, type AccountInfo } from '@azure/msal-browser';
import { Route, Routes, useLocation } from 'react-router-dom';
import { SettingsContext, useSettingsProvider } from './hooks/useSettings';
import { MeProvider } from './features/auth/MeProvider';
import RequireAuth from './features/auth/RequireAuth';
import LoginPage from './features/auth/LoginPage';
import LoginCallbackPage from './features/auth/LoginCallbackPage';
import LoginErrorPage from './features/auth/LoginErrorPage';
import TenantPickerPage from './features/auth/TenantPickerPage';
import WorkspaceBoundary, { WorkspaceStatus } from './features/workspaces/WorkspaceBoundary';
import WorkspaceEntry from './features/workspaces/WorkspaceEntry';
import { buildWorkspaceUrl, parseWorkspaceUrl, systemIdFromRoute } from './features/workspaces/workspaceRoutes';
import ApplicationFrame from './ApplicationFrame';
import ApplicationRoutes from './ApplicationRoutes';

export default function App() {
  const settings = useSettingsProvider();
  const location = useLocation();
  const accountKey = useAuthenticationAccountKey();
  const routePath = location.pathname.replace(/\/+$/, '').toLowerCase();
  const publicRoute = ['/login', '/login/callback', '/login/error'].includes(routePath);
  let parsed;
  let invalid = false;
  try {
    parsed = parseWorkspaceUrl(location.pathname);
  } catch {
    invalid = true;
  }
  const workspaceKey = publicRoute ? 'public'
    : parsed ? buildWorkspaceUrl(parsed.workspace)
    : routePath === '/login/select-tenant' ? 'picker' : 'legacy';
  const contextKey = JSON.stringify([workspaceKey, accountKey]);
  const frameKey = `${contextKey}:${systemIdFromRoute(parsed?.route ?? (invalid ? '/' : location.pathname)) ?? ''}`;
  const application = <ApplicationFrame key={frameKey}><ApplicationRoutes /></ApplicationFrame>;
  const canonical = parsed && (
    <RequireAuth scoped>
      <WorkspaceBoundary target={parsed.workspace}>{application}</WorkspaceBoundary>
    </RequireAuth>
  );
  return (
    <SettingsContext.Provider value={settings}>
      <MeProvider contextKey={contextKey} enabled={!publicRoute && !invalid}>
        {invalid ? <WorkspaceStatus message="The workspace URL is invalid. Choose an authorized workspace." /> : (
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/login/callback" element={<LoginCallbackPage />} />
            <Route path="/login/error" element={<LoginErrorPage />} />
            <Route path="/login/select-tenant" element={<RequireAuth scoped><TenantPickerPage /></RequireAuth>} />
            <Route path="/workspaces/csp/*" element={canonical} />
            <Route path="/workspaces/organizations/:tenantId/*" element={canonical} />
            <Route path="/workspaces/support/organizations/:tenantId/*" element={canonical} />
            <Route path="/*" element={<RequireAuth><WorkspaceEntry>{application}</WorkspaceEntry></RequireAuth>} />
          </Routes>
        )}
      </MeProvider>
    </SettingsContext.Provider>
  );
}

function accountIdentity(account: AccountInfo | null | undefined) {
  return account ? [account.environment, account.homeAccountId, account.tenantId, account.localAccountId] : null;
}

function useAuthenticationAccountKey() {
  const { instance } = useMsal();
  const snapshot = useCallback(() => JSON.stringify([
    accountIdentity(instance.getAllAccounts()[0]),
    accountIdentity(instance.getActiveAccount()),
  ]), [instance]);
  const subscribe = useCallback((onChange: () => void) => {
    const id = instance.addEventCallback(event => {
      if (event.eventType === EventType.ACTIVE_ACCOUNT_CHANGED
        || event.eventType === EventType.ACCOUNT_ADDED || event.eventType === EventType.ACCOUNT_REMOVED
        || event.eventType === EventType.LOGIN_SUCCESS || event.eventType === EventType.LOGOUT_SUCCESS) {
        onChange();
      }
    });
    return () => { if (id !== null) instance.removeEventCallback(id); };
  }, [instance]);
  // Include both identities until the shared auth transport selects the active
  // account consistently. Neither account choice grants workspace authority.
  return useSyncExternalStore(subscribe, snapshot);
}
