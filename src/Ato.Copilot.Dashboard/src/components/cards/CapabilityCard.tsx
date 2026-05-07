import type { SecurityCapabilityDto } from '../../types/dashboard';

const statusColors: Record<string, string> = {
  Implemented: 'bg-green-100 text-green-800',
  InProgress: 'bg-indigo-100 text-indigo-800',
  Planned: 'bg-gray-100 text-gray-700',
  Deprecated: 'bg-red-100 text-red-800',
};

interface CapabilityCardProps {
  capability: SecurityCapabilityDto;
  isExpanded: boolean;
  onToggle: () => void;
  onEdit: () => void;
  onDelete: () => void;
  onLinkComponents: () => void;
}

export function CapabilityCard({ capability, isExpanded, onToggle, onEdit, onDelete, onLinkComponents }: CapabilityCardProps) {
  return (
    <div className="border rounded-lg bg-white shadow-sm hover:shadow-md transition-shadow">
      <button
        onClick={onToggle}
        className="w-full text-left p-4 focus:outline-none focus:ring-2 focus:ring-indigo-300 rounded-lg"
      >
        <div className="flex items-center justify-between">
          <div className="flex-1 min-w-0">
            <h3 className="font-semibold text-gray-900 truncate">{capability.name}</h3>
            <p className="text-sm text-gray-500">{capability.provider}</p>
          </div>
          <div className="flex items-center gap-3 ml-4">
            <span className="text-xs bg-indigo-50 text-indigo-700 px-2 py-0.5 rounded font-medium">
              {capability.categoryName}
            </span>
            <span className={`text-xs px-2 py-0.5 rounded font-medium ${statusColors[capability.implementationStatus] ?? 'bg-gray-100 text-gray-700'}`}>
              {capability.implementationStatus}
            </span>
            <span className="text-xs text-gray-400">
              {capability.mappedControlCount} controls
            </span>
            <svg
              className={`w-4 h-4 text-gray-400 transition-transform ${isExpanded ? 'rotate-180' : ''}`}
              fill="none" viewBox="0 0 24 24" stroke="currentColor"
            >
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
            </svg>
          </div>
        </div>
      </button>

      {isExpanded && (
        <div className="border-t px-4 pb-4 pt-3 space-y-3">
          <p className="text-sm text-gray-600 line-clamp-3">{capability.description}</p>
          <div className="flex items-center justify-between text-xs text-gray-500">
            <span>Owner: {capability.owner}</span>
            <span>{capability.systemsUsingCount} system(s) using</span>
          </div>
          {capability.linkedComponents && capability.linkedComponents.length > 0 && (
            <div className="flex flex-wrap gap-1">
              {capability.linkedComponents.map(c => (
                <span key={c.id} className="text-xs bg-gray-100 text-gray-600 px-2 py-0.5 rounded">
                  {c.name}
                </span>
              ))}
            </div>
          )}
          <div className="flex gap-2 pt-1">
            <button
              onClick={onEdit}
              className="px-3 py-1.5 text-xs bg-indigo-600 text-white rounded hover:bg-indigo-700"
            >
              Edit
            </button>
            <button
              onClick={onLinkComponents}
              className="px-3 py-1.5 text-xs border border-indigo-600 text-indigo-600 rounded hover:bg-indigo-50"
            >
              Link Components
            </button>
            <button
              onClick={onDelete}
              className="px-3 py-1.5 text-xs bg-red-50 text-red-600 rounded hover:bg-red-100"
            >
              Delete
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
