import { useEffect } from 'react';
import { Routes, Route } from 'react-router-dom';
import PortfolioDashboard from './pages/PortfolioDashboard';
import SystemDetail from './pages/SystemDetail';
import CapabilityLibrary from './pages/CapabilityLibrary';
import ComponentInventory from './pages/ComponentInventory';
import GapAnalysis from './pages/GapAnalysis';
import Roadmap from './pages/Roadmap';
import BoundaryManagement from './pages/BoundaryManagement';
import Documents from './pages/Documents';
import Assessments from './pages/Assessments';
import Remediation from './pages/Remediation';
import Narratives from './pages/Narratives';
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
        <Route path="/" element={<PortfolioDashboard />} />
        <Route path="/systems/:id" element={<SystemDetail />} />
        <Route path="/systems/:id/roadmap" element={<Roadmap />} />
        <Route path="/systems/:id/boundaries" element={<BoundaryManagement />} />
        <Route path="/capabilities" element={<CapabilityLibrary />} />
        <Route path="/systems/:id/components" element={<ComponentInventory />} />
        <Route path="/systems/:id/gaps" element={<GapAnalysis />} />
        <Route path="/systems/:id/documents" element={<Documents />} />
        <Route path="/assessments" element={<Assessments />} />
        <Route path="/remediation" element={<Remediation />} />
        <Route path="/systems/:id/narratives" element={<Narratives />} />
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
