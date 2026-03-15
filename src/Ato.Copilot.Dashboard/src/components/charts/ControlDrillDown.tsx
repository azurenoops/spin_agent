import { useEffect, useState } from 'react';
import { getHeatmapControls } from '../../api/systemDetail';
import type { HeatmapControl } from '../../types/dashboard';

interface ControlDrillDownProps {
  systemId: string;
  familyCode: string;
  onClose: () => void;
}

const statusColors: Record<string, string> = {
  Satisfied: 'bg-green-100 text-green-800',
  OtherThanSatisfied: 'bg-red-100 text-red-800',
  NotAssessed: 'bg-gray-100 text-gray-500',
};

export default function ControlDrillDown({
  systemId,
  familyCode,
  onClose,
}: ControlDrillDownProps) {
  const [controls, setControls] = useState<HeatmapControl[]>([]);
  const [familyName, setFamilyName] = useState('');
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    getHeatmapControls(systemId, familyCode)
      .then((result) => {
        if (!cancelled) {
          setControls(result.controls);
          setFamilyName(result.familyName);
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [systemId, familyCode]);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30">
      <div className="max-h-[80vh] w-full max-w-2xl overflow-y-auto rounded-lg bg-white p-6 shadow-xl">
        <div className="mb-4 flex items-center justify-between">
          <h3 className="text-lg font-semibold">
            {familyCode} — {familyName}
          </h3>
          <button
            type="button"
            onClick={onClose}
            className="rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600"
          >
            ✕
          </button>
        </div>

        {loading ? (
          <p className="text-gray-500">Loading controls...</p>
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b text-left text-xs font-medium uppercase text-gray-500">
                <th className="py-2">Control</th>
                <th className="py-2">Title</th>
                <th className="py-2">Status</th>
                <th className="py-2">Narrative</th>
                <th className="py-2">Capability</th>
              </tr>
            </thead>
            <tbody>
              {controls.map((ctrl) => (
                <tr key={ctrl.controlId} className="border-b border-gray-100">
                  <td className="py-2 font-mono text-xs">{ctrl.controlId}</td>
                  <td className="py-2">{ctrl.controlTitle}</td>
                  <td className="py-2">
                    <span
                      className={`rounded-full px-2 py-0.5 text-xs font-medium ${statusColors[ctrl.complianceStatus] ?? ''}`}
                    >
                      {ctrl.complianceStatus}
                    </span>
                  </td>
                  <td className="py-2">
                    {ctrl.hasNarrative ? (
                      <span className="text-green-600">
                        ✓{ctrl.isManuallyCustomized ? ' (Customized)' : ''}
                      </span>
                    ) : (
                      <span className="text-gray-400">—</span>
                    )}
                  </td>
                  <td className="py-2 text-xs text-gray-500">
                    {ctrl.securityCapabilityName ?? '—'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
