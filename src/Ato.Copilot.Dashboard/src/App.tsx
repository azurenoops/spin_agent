import { Routes, Route } from 'react-router-dom';
import PortfolioDashboard from './pages/PortfolioDashboard';
import SystemDetail from './pages/SystemDetail';
import CapabilityLibrary from './pages/CapabilityLibrary';
import ComponentInventory from './pages/ComponentInventory';
import GapAnalysis from './pages/GapAnalysis';
import Roadmap from './pages/Roadmap';

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<PortfolioDashboard />} />
      <Route path="/systems/:id" element={<SystemDetail />} />
      <Route path="/systems/:id/roadmap" element={<Roadmap />} />
      <Route path="/capabilities" element={<CapabilityLibrary />} />
      <Route path="/systems/:id/components" element={<ComponentInventory />} />
      <Route path="/systems/:id/gaps" element={<GapAnalysis />} />
    </Routes>
  );
}
