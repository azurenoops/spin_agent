import { useEffect } from 'react';
import { Routes, Route } from 'react-router-dom';
import PortfolioRiskProfile from './pages/PortfolioRiskProfile';
import PortfolioDashboard from './pages/PortfolioDashboard';
import SystemDetail from './pages/SystemDetail';
import CapabilityLibrary from './pages/CapabilityLibrary';
import ComponentLibrary from './pages/ComponentLibrary';
import GapAnalysis from './pages/GapAnalysis';
import Roadmap from './pages/Roadmap';
import BoundaryManagement from './pages/BoundaryManagement';
import Documents from './pages/Documents';
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
import ControlCatalog from './pages/ControlCatalog';
import SystemProfile from './pages/SystemProfile';
import SystemLayout from './components/layout/SystemLayout';
import ChatPanel from './components/chat/ChatPanel';
import { ChatPanelProvider, useChatPanel } from './components/chat/ChatPanelContext';
import { SettingsContext, useSettingsProvider } from './hooks/useSettings';
import SystemDataProvider from './components/SystemRoute';

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
      <Routes>
        <Route path="/" element={<PortfolioRiskProfile />} />
        <Route path="/systems" element={<PortfolioDashboard />} />
        <Route path="/systems/:id" element={<SystemLayout />}>
          <Route index element={<SystemDetail />} />
          <Route path="roadmap" element={<Roadmap />} />
          <Route path="boundaries" element={<BoundaryManagement />} />
          <Route path="legal" element={<LegalRegulatory />} />
          <Route path="gaps" element={<GapAnalysis />} />
          <Route path="documents" element={<Documents />} />
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
        <Route path="/capabilities" element={<CapabilityLibrary />} />
        <Route path="/components" element={<ComponentLibrary />} />
        <Route path="/controls" element={<ControlCatalog />} />
      </Routes>
      <ChatPanel
        isOpen={panelState.isOpen}
        onClose={closePanel}
        width={panelState.width}
        onWidthChange={setWidth}
      />
    </SystemDataProvider>
  );
}

export default function App() {
  const settingsCtx = useSettingsProvider();

  return (
    <SettingsContext.Provider value={settingsCtx}>
      <ChatPanelProvider>
        <AppContent />
      </ChatPanelProvider>
    </SettingsContext.Provider>
  );
}
