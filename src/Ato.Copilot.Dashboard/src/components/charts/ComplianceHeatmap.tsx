import { useState } from 'react';
import type { HeatmapFamily } from '../../types/dashboard';
import ControlDrillDown from './ControlDrillDown';

interface ComplianceHeatmapProps {
  families: HeatmapFamily[];
  systemId: string;
}

const severityColors: Record<string, string> = {
  green: 'bg-green-100 border-green-400 text-green-800',
  yellow: 'bg-yellow-100 border-yellow-400 text-yellow-800',
  red: 'bg-red-100 border-red-400 text-red-800',
  gray: 'bg-gray-100 border-gray-300 text-gray-500',
};

export default function ComplianceHeatmap({ families, systemId }: ComplianceHeatmapProps) {
  const [selectedFamily, setSelectedFamily] = useState<string | null>(null);

  return (
    <>
      <div className="grid grid-cols-4 gap-2 md:grid-cols-5 lg:grid-cols-6">
        {families.map((fam) => (
          <button
            key={fam.familyCode}
            type="button"
            onClick={() => setSelectedFamily(fam.familyCode)}
            className={`rounded-lg border-2 p-3 text-center transition-all hover:shadow-md ${severityColors[fam.severity]}`}
          >
            <div className="text-sm font-bold">{fam.familyCode}</div>
            <div className="text-lg font-semibold">{fam.compliancePercent.toFixed(0)}%</div>
            <div className="text-[10px]">
              {fam.satisfiedControls}/{fam.totalControls}
            </div>
          </button>
        ))}
      </div>

      {selectedFamily && (
        <ControlDrillDown
          systemId={systemId}
          familyCode={selectedFamily}
          onClose={() => setSelectedFamily(null)}
        />
      )}
    </>
  );
}
