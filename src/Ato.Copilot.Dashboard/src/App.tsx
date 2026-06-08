import { useEffect } from 'react';
import { Routes, Route } from 'react-router-dom';
import { useIsAuthenticated, useMsal } from '@azure/msal-react';
import PortfolioRoute from './pages/PortfolioRoute';
import SystemsRoute from './pages/SystemsRoute';
import ComponentsRoute from './pages/ComponentsRoute';
import CapabilitiesRoute from './pages/CapabilitiesRoute';
import ControlsRoute from './pages/ControlsRoute';
import OverrideReviewPage from './pages/OverrideReviewPage';
import SystemDetail from './pages/SystemDetail';
import AuthorizationPage from './pages/AuthorizationPage';
import RolesManagementPage from './pages/RolesManagementPage';
import Roadmap from './pages/Roadmap';
import BoundaryManagement from './pages/BoundaryManagement';
import Documents from './pages/Documents';
import ConMon from './pages/ConMon';
import Assessments from './pages/Assessments';
import Remediation from './pages/Remediation';
import Narratives from './pages/Narratives';
import DeviationsPage from './pages/DeviationsPage';
import CapabilityCoverage from './pages/CapabilityCoverage';
import EvidenceRepository from './pages/EvidenceRepository';
import LegalRegulatory from './pages/LegalRegulatory';
import ComponentInventory from './pages/ComponentInventory';
import PoamManagement from './pages/PoamManagement';
import ControlInheritance from './pages/ControlInheritance';
import BaselineManagement from './pages/BaselineManagement';
import SystemProfile from './pages/SystemProfile';
import SystemLayout from './components/layout/SystemLayout';
import ChatPanel from './components/chat/ChatPanel';
import { ChatPanelProvider, useChatPanel } from './components/chat/ChatPanelContext';
import { SettingsContext, useSettingsProvider } from './hooks/useSettings';
import OrgSettingsPage from './pages/settings/OrgSettingsPage';
import { OrganizationContextProvider } from './hooks/useOrganizationContext';
import SystemDataProvider from './components/SystemRoute';
import OnboardingShell from './features/onboarding/OnboardingShell';
import OnboardingGate from './features/onboarding/OnboardingGate';
import TenantWizard from './features/onboarding/TenantWizard';
import TenantOnboardingGuard from './features/onboarding/TenantWizard/TenantOnboardingGuard';
import CspWizard from './features/csp-onboarding/CspWizard';
import CspOnboardingGuard from './features/csp-onboarding/CspOnboardingGuard';
import CspInheritedComponentsPage from './features/csp-inherited-components/CspInheritedComponentsPage';
import ImportedDocumentsView from './features/admin/imported-documents/ImportedDocumentsView';
import AzureSettingsPage from './pages/AzureSettingsPage';
import TemplatesAdminPage from './pages/TemplatesAdminPage';
import AzureSettingsPage from './pages/AzureSettingsPage';
import LoginPage from './features/auth/LoginPage';
import LoginCallbackPage from './features/auth/LoginCallbackPage';
import TenantPickerPage from './features/auth/TenantPickerPage';
import LoginErrorPage from './features/auth/LoginErrorPage';
import RequireAuth from './features/auth/RequireAuth';
import IdleWarningModal from './features/auth/IdleWarningModal';
import RestoreUnsavedChangesPrompt from './features/auth/RestoreUnsavedChangesPrompt';
import ImpersonationBanner from './features/auth/ImpersonationBanner';
import { useIdleTimer } from './features/auth/useIdleTimer';
import { useLoginConfig } from './features/auth/LoginConfigContext';

function AppContent() {
  const { panelState, togglePanel, closePanel, setWidth } = useChatPanel();

  // T030: Global keyboard shortcut — Ctrl+Shift+C (Cmd+Shift+C on macOS)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.shiftKey && e.key === 'C') {
        e.preventDefault();
        togglePanel();
      }
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [togglePanel]);

  return (
    <SystemDataProvider>
      <AuthenticatedSessionGuards />
      <CspOnboardingGuard>
        <TenantOnboardingGuard>
          <Routes>
          {/* Feature 051 [US1]: public login routes — MUST NOT be wrapped in RequireAuth. */}
          <Route path="/login" element={<LoginPage />} />
          <Route path="/login/callback" element={<LoginCallbackPage />} />
          {/* Feature 051 T083 [US4]: error page is public — RequireAuth would
              loop a failed-auth user back through MSAL forever. */}
          <Route path="/login/error" element={<LoginErrorPage />} />
          {/* Feature 051 T073 [US3]: tenant picker is authenticated. */}
          <Route path="/login/select-tenant" element={<RequireAuth><TenantPickerPage /></RequireAuth>} />
          {/* All other routes require authentication; RequireAuth triggers
              loginRedirect with the deep-link as `state` when unauthenticated. */}
          <Route path="/" element={<RequireAuth><PortfolioRoute /></RequireAuth>} />
          <Route path="/systems" element={<RequireAuth><SystemsRoute /></RequireAuth>} />
          <Route path="/systems/:id" element={<RequireAuth><SystemLayout /></RequireAuth>}>
            <Route index element={<SystemDetail />} />
            <Route path="roadmap" element={<Roadmap />} />
            <Route path="boundaries" element={<BoundaryManagement />} />
            <Route path="legal" element={<LegalRegulatory />} />
            <Route path="documents" element={<Documents />} />
            <Route path="conmon" element={<ConMon />} />
            <Route path="narratives" element={<Narratives />} />
            <Route path="deviations" element={<DeviationsPage />} />
            <Route path="assessments" element={<Assessments />} />
            <Route path="remediation" element={<Remediation />} />
            <Route path="evidence" element={<EvidenceRepository />} />
            <Route path="components" element={<ComponentInventory />} />
            <Route path="poam" element={<PoamManagement />} />
            <Route path="capability-coverage" element={<CapabilityCoverage />} />
            <Route path="inheritance" element={<ControlInheritance />} />
            <Route path="baseline" element={<BaselineManagement />} />
            <Route path="profile/:sectionType" element={<SystemProfile />} />
            {/* Epic #121 / Task #146 — Authorization phase page */}
            <Route path="authorize" element={<AuthorizationPage />} />
            {/* Epic #121 / Task #147 — Roles management page */}
            <Route path="roles" element={<RolesManagementPage />} />
          </Route>
          <Route path="/capabilities" element={<RequireAuth><CapabilitiesRoute /></RequireAuth>} />
          <Route path="/components" element={<RequireAuth><ComponentsRoute /></RequireAuth>} />
          <Route path="/onboarding" element={<RequireAuth><OnboardingShell /></RequireAuth>} />
          <Route path="/onboarding/tenant" element={<RequireAuth><TenantWizard /></RequireAuth>} />
          <Route path="/onboarding/csp" element={<RequireAuth><CspWizard /></RequireAuth>} />
          {/* Feature 048 follow-up: the cross-tenant CSP dashboard now
              resolves at `/` via PortfolioRoute for CSP-Admins. The standalone
              `/csp-dashboard` route has been retired. */}
          <Route path="/csp/inherited-components" element={<RequireAuth><CspInheritedComponentsPage /></RequireAuth>} />
          <Route path="/admin/imported-documents" element={<RequireAuth><ImportedDocumentsView /></RequireAuth>} />
          {/* Epic #215 / T001 - Admin Azure settings */}
          <Route path="/admin/azure-settings" element={<RequireAuth><AzureSettingsPage /></RequireAuth>} />
          {/* Epic #222 / T001 - Admin templates & seeds */}
          <Route path="/admin/templates" element={<RequireAuth><TemplatesAdminPage /></RequireAuth>} />
          <Route path="/controls" element={<RequireAuth><ControlsRoute /></RequireAuth>} />
          {/* Epic #208 / Task #250 — Org settings page */}
          <Route path="/settings/org" element={<RequireAuth><OrgSettingsPage /></RequireAuth>} />
          {/* Epic #215 / Task #293 — Azure subscription settings page */}
          <Route path="/settings/azure-subscriptions" element={<RequireAuth><AzureSettingsPage /></RequireAuth>} />
          <Route path="/controls/overrides" element={<RequireAuth><OverrideReviewPage /></RequireAuth>} />
        </Routes>
        </TenantOnboardingGuard>
      </CspOnboardingGuard>
      <ChatPanel
        isOpen={panelState.isOpen}
        onClose={closePanel}
        width={panelState.width}
        onWidthChange={setWidth}
      />
      <OnboardingGate />
    </SystemDataProvider>
  );
}

export default function App() {
  const settingsCtx = useSettingsProvider();

  return (
    <SettingsContext.Provider value={settingsCtx}>
      <ChatPanelProvider>
        <OrganizationContextProvider>
          <AppContent />
        </OrganizationContextProvider>
      </ChatPanelProvider>
    </SettingsContext.Provider>
  );
}

/**
 * Feature 051 T061 [US2] — mounts the idle-sign-out timer + warning
 * modal inside the SPA shell. Only active when MSAL reports an
 * authenticated user, so the public /login and /login/callback routes
 * do NOT arm the timer. Renders nothing visible until the warning
 * fires (the modal self-hides). Reads idleTimeoutMinutes from the
 * bootstrap LoginConfig (FR-007).
 */
function AuthenticatedSessionGuards() {
  const isAuthenticated = useIsAuthenticated();
  if (!isAuthenticated) return null;
  return <AuthenticatedSessionGuardsActive />;
}

function AuthenticatedSessionGuardsActive() {
  const { idleTimeoutMinutes } = useLoginConfig();
  const { accounts } = useMsal();
  // MSAL's `localAccountId` is the Entra `oid` claim — the same value
  // the backend writes into LoginAuditEvent.Oid (see AuthEndpoints
  // GetMeAsync). Use it directly so FR-008's localStorage keys are
  // scoped consistently with the server-side audit identity.
  const oid = accounts[0]?.localAccountId ?? '';
  useIdleTimer(idleTimeoutMinutes);
  return (
    <>
      {/* Feature 051 T135 [US8] — sticky impersonation banner driven by
          the server-side /me state. Mounted at the authenticated app
          shell so every route inherits it without per-page wiring.
          Self-hides when me.isImpersonating === false. */}
      <ImpersonationBanner />
      <IdleWarningModal />
      {oid && <RestoreUnsavedChangesPrompt oid={oid} />}
    </>
  );
}