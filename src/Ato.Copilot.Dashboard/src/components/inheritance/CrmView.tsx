import { useState } from 'react';
import type { CrmResult, CrmFamilyGroup, CrmExportFormat, CrmExportLayout } from '../../types/inheritance';

function TypeBadge({ type }: { type: string }) {
  const map: Record<string, string> = {
    Inherited: 'bg-green-100 text-green-700',
    Shared: 'bg-indigo-100 text-indigo-700',
    Customer: 'bg-amber-100 text-amber-700',
    Undesignated: 'bg-gray-100 text-gray-500',
  };
  return <span className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${map[type] ?? 'bg-gray-100 text-gray-700'}`}>{type}</span>;
}

interface CrmViewProps {
  crm: CrmResult | null;
  loading: boolean;
  onExport: (format: CrmExportFormat, layout: CrmExportLayout) => void;
  onClose: () => void;
}

export default function CrmView({ crm, loading, onExport, onClose }: CrmViewProps) {
  const [exportFormat, setExportFormat] = useState<CrmExportFormat>('csv');
  const [exportLayout, setExportLayout] = useState<CrmExportLayout>('custom');

  if (loading) {
    return (
      <div className="rounded-xl border border-gray-200 bg-white p-6 shadow-sm">
        <div className="flex items-center justify-center py-12 text-gray-400">Generating CRM...</div>
      </div>
    );
  }

  if (!crm) return null;

  return (
    <div className="rounded-xl border border-gray-200 bg-white shadow-sm">
      <div className="flex items-center justify-between border-b border-gray-200 px-4 py-3">
        <div>
          <h3 className="text-sm font-semibold text-gray-900">Customer Responsibility Matrix</h3>
          <p className="text-xs text-gray-500">
            {crm.systemName} — {crm.baselineLevel} Baseline — {crm.totalControls} controls
          </p>
        </div>
        <div className="flex items-center gap-2">
          <select
            className="rounded border border-gray-300 px-2 py-1 text-xs"
            value={exportFormat}
            onChange={e => setExportFormat(e.target.value as CrmExportFormat)}
          >
            <option value="csv">CSV</option>
            <option value="excel">Excel</option>
          </select>
          <select
            className="rounded border border-gray-300 px-2 py-1 text-xs"
            value={exportLayout}
            onChange={e => setExportLayout(e.target.value as CrmExportLayout)}
          >
            <option value="custom">Custom</option>
            <option value="fedramp">FedRAMP</option>
            <option value="emass">eMASS</option>
          </select>
          <button
            onClick={() => onExport(exportFormat, exportLayout)}
            className="rounded bg-indigo-600 px-3 py-1 text-xs font-medium text-white hover:bg-indigo-700"
          >
            Export
          </button>
          <button
            onClick={onClose}
            className="rounded border border-gray-300 px-2 py-1 text-xs text-gray-600 hover:bg-gray-100"
          >
            Close
          </button>
        </div>
      </div>

      {/* Summary stats */}
      <div className="grid grid-cols-5 gap-3 border-b border-gray-200 px-4 py-3">
        <div className="text-center">
          <p className="text-lg font-bold text-green-600">{crm.inheritedControls}</p>
          <p className="text-xs text-gray-500">Inherited</p>
        </div>
        <div className="text-center">
          <p className="text-lg font-bold text-indigo-600">{crm.sharedControls}</p>
          <p className="text-xs text-gray-500">Shared</p>
        </div>
        <div className="text-center">
          <p className="text-lg font-bold text-amber-600">{crm.customerControls}</p>
          <p className="text-xs text-gray-500">Customer</p>
        </div>
        <div className="text-center">
          <p className="text-lg font-bold text-gray-500">{crm.undesignatedControls}</p>
          <p className="text-xs text-gray-500">Undesignated</p>
        </div>
        <div className="text-center">
          <p className="text-lg font-bold text-indigo-600">{crm.inheritancePercentage}%</p>
          <p className="text-xs text-gray-500">Designated</p>
        </div>
      </div>

      {/* Family-grouped table */}
      <div className="max-h-[600px] overflow-y-auto">
        {crm.familyGroups.map((group: CrmFamilyGroup) => (
          <div key={group.family} className="border-b border-gray-100 last:border-0">
            <div className="bg-gray-50 px-4 py-2">
              <span className="text-xs font-semibold text-gray-700">{group.family} — {group.familyName}</span>
              <span className="ml-2 text-xs text-gray-400">({group.controls.length} controls)</span>
            </div>
            <table className="min-w-full">
              <tbody className="divide-y divide-gray-50">
                {group.controls.map(c => (
                  <tr key={c.controlId} className="hover:bg-gray-50">
                    <td className="w-24 whitespace-nowrap px-4 py-2 text-xs font-medium text-gray-900">{c.controlId}</td>
                    <td className="w-28 whitespace-nowrap px-4 py-2 text-xs"><TypeBadge type={c.inheritanceType} /></td>
                    <td className="px-4 py-2 text-xs text-gray-600">{c.provider ?? '—'}</td>
                    <td className="px-4 py-2 text-xs text-gray-600">{c.customerResponsibility ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ))}
      </div>
    </div>
  );
}
