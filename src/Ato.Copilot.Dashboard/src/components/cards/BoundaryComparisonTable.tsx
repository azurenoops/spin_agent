import type { BoundaryComparisonItem } from '../../types/dashboard';

interface BoundaryComparisonTableProps {
  items: BoundaryComparisonItem[];
}

export function BoundaryComparisonTable({ items }: BoundaryComparisonTableProps) {
  if (items.length === 0) return null;

  const hasWaivedControls = items.some((item) => item.waivedControls > 0);

  return (
    <div className="rounded-lg border border-gray-200 bg-white shadow-sm overflow-hidden">
      <table className="w-full text-sm">
        <thead className="bg-gray-50 border-b border-gray-200">
          <tr>
            <th className="text-left px-4 py-2 font-medium text-gray-700">Boundary</th>
            <th className="text-left px-4 py-2 font-medium text-gray-700">Type</th>
            <th className="text-right px-4 py-2 font-medium text-gray-700">Controls</th>
            <th className="text-right px-4 py-2 font-medium text-gray-700">Covered</th>
            {hasWaivedControls && <th className="text-right px-4 py-2 font-medium text-gray-700">Waived</th>}
            <th className="text-right px-4 py-2 font-medium text-gray-700">Gaps</th>
            <th className="text-right px-4 py-2 font-medium text-gray-700">Coverage</th>
          </tr>
        </thead>
        <tbody>
          {items.map((item) => (
            <tr key={item.boundaryId} className="border-b border-gray-100 last:border-0 hover:bg-gray-50">
              <td className="px-4 py-2">
                <div className="flex items-center gap-2">
                  <span className="font-medium text-gray-900">{item.boundaryName}</span>
                  {item.isPrimary && (
                    <span className="text-xs bg-green-100 text-green-800 px-1.5 py-0.5 rounded">
                      Primary
                    </span>
                  )}
                </div>
              </td>
              <td className="px-4 py-2 text-gray-600">{item.boundaryType}</td>
              <td className="px-4 py-2 text-right text-gray-900">{item.totalControls}</td>
              <td className="px-4 py-2 text-right text-green-700">{item.coveredControls}</td>
              {hasWaivedControls && <td className="px-4 py-2 text-right text-purple-600">{item.waivedControls}</td>}
              <td className="px-4 py-2 text-right text-red-600">{item.gapCount}</td>
              <td className="px-4 py-2 text-right">
                <span
                  className={`font-medium ${
                    item.coveragePercent >= 80
                      ? 'text-green-700'
                      : item.coveragePercent >= 50
                        ? 'text-yellow-700'
                        : 'text-red-600'
                  }`}
                >
                  {item.coveragePercent.toFixed(1)}%
                </span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
