import { useEffect } from 'react';
import { Routes, Route } from 'react-router-dom';
import PortfolioRoute from './pages/PortfolioRoute';
import SystemsRoute from './pages/SystemsRoute';
import ComponentsRoute from './pages/ComponentsRoute';
import CapabilitiesRoute from './pages/CapabilitiesRoute';
import ControlsRoute from './pages/ControlsRoute';
import SystemDetail from './pages/SystemDetail';
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
      <CspOnboardingGuard>
        <TenantOnboardingGuard>
          <Routes>
          <Route path="/" element={<PortfolioRoute />} />
          <Route path="/systems" element={<SystemsRoute />} />
          <Route path="/systems/:id" element={<SystemLayout />}>
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
          </Route>
          <Route path="/capabilities" element={<CapabilitiesRoute />} />
          <Route path="/components" element={<ComponentsRoute />} />
          <Route path="/onboarding" element={<OnboardingShell />} />
          <Route path="/onboarding/tenant" element={<TenantWizard />} />
          <Route path="/onboarding/csp" element={<CspWizard />} />
          {/* Feature 048 follow-up: the cross-tenant CSP dashboard now
              resolves at `/` via PortfolioRoute for CSP-Admins. The standalone
              `/csp-dashboard` route has been retired. */}
          <Route path="/csp/inherited-components" element={<CspInheritedComponentsPage />} />
          <Route path="/admin/imported-documents" element={<ImportedDocumentsView />} />
          <Route path="/controls" element={<ControlsRoute />} />
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
