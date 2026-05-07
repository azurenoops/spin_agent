import type { BoundaryDefinitionDto } from '../../types/dashboard';

const typeColors: Record<string, string> = {
  Physical: 'bg-orange-100 text-orange-800',
  Logical: 'bg-indigo-100 text-indigo-800',
  Hybrid: 'bg-purple-100 text-purple-800',
};

interface BoundarySummaryCardProps {
  boundary: BoundaryDefinitionDto;
  onEdit?: () => void;
  onDelete?: () => void;
  onExpand?: () => void;
  expanded?: boolean;
}

export function BoundarySummaryCard({ boundary, onEdit, onDelete, onExpand, expanded }: BoundarySummaryCardProps) {
  return (
    <div className={`rounded-lg border bg-white p-4 shadow-sm hover:shadow-md transition-shadow cursor-pointer ${expanded ? 'border-indigo-400 ring-1 ring-indigo-200' : 'border-gray-200'}`}
      onClick={onExpand}
    >
      <div className="flex items-center justify-between mb-2">
        <div className="flex items-center gap-2">
          <h3 className="font-semibold text-gray-900 truncate">{boundary.name}</h3>
          {boundary.isPrimary && (
            <span className="text-xs bg-green-100 text-green-800 px-2 py-0.5 rounded font-medium">
              Primary
            </span>
          )}
        </div>
        <span className={`text-xs px-2 py-0.5 rounded font-medium ${typeColors[boundary.boundaryType] ?? 'bg-gray-100 text-gray-700'}`}>
          {boundary.boundaryType}
        </span>
      </div>

      {boundary.description && (
        <p className="text-sm text-gray-500 mb-3 line-clamp-2">{boundary.description}</p>
      )}

      <div className="grid grid-cols-3 gap-2 text-center mb-3">
        <div>
          <p className="text-lg font-semibold text-gray-900">{boundary.resourceCount}</p>
          <p className="text-xs text-gray-500">Resources</p>
        </div>
        <div>
          <p className="text-lg font-semibold text-gray-900">{boundary.componentCount}</p>
          <p className="text-xs text-gray-500">Components</p>
        </div>
        <div>
          <p className="text-lg font-semibold text-gray-900">{boundary.coveragePercent.toFixed(1)}%</p>
          <p className="text-xs text-gray-500">Coverage</p>
        </div>
      </div>

      {(onEdit || (onDelete && !boundary.isPrimary)) && (
        <div className="flex gap-2 pt-1 border-t border-gray-100">
          {onEdit && (
            <button
              onClick={(e) => { e.stopPropagation(); onEdit(); }}
              className="px-3 py-1.5 text-xs bg-indigo-600 text-white rounded hover:bg-indigo-700"
            >
              Edit
            </button>
          )}
          {onDelete && !boundary.isPrimary && (
            <button
              onClick={(e) => { e.stopPropagation(); onDelete(); }}
              className="px-3 py-1.5 text-xs bg-red-50 text-red-600 rounded hover:bg-red-100"
            >
              Delete
            </button>
          )}
        </div>
      )}
    </div>
  );
}
