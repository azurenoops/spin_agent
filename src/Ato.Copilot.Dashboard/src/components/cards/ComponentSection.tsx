import { useState } from 'react';
import type { SystemComponentDto } from '../../types/dashboard';

const statusColors: Record<string, string> = {
  Active: 'bg-green-100 text-green-800',
  Planned: 'bg-blue-100 text-blue-800',
  Decommissioned: 'bg-gray-100 text-gray-600',
};

const typeEmoji: Record<string, string> = {
  Person: '👤',
  Place: '🏢',
  Thing: '🔧',
};

interface ComponentSectionProps {
  title: string;
  type: string;
  components: SystemComponentDto[];
  count: number;
  onEdit: (comp: SystemComponentDto) => void;
  onDelete: (id: string) => void;
  onRelink?: (comp: SystemComponentDto) => void;
  onCreateCapability?: (comp: SystemComponentDto) => void;
  riskMap?: Record<string, { openCount: number; overdueCount: number; highestSeverity: string | null }>;
}

const severityBadge: Record<string, string> = {
  I: 'bg-red-600 text-white',
  II: 'bg-orange-500 text-white',
  III: 'bg-yellow-400 text-gray-900',
};

export function ComponentSection({ title, type, components, count, onEdit, onDelete, onRelink, onCreateCapability, riskMap }: ComponentSectionProps) {
  const [expanded, setExpanded] = useState(true);

  return (
    <div className="border rounded-lg bg-white">
      <button
        onClick={() => setExpanded(!expanded)}
        className="w-full flex items-center justify-between p-4 text-left focus:outline-none"
      >
        <div className="flex items-center gap-2">
          <span>{typeEmoji[type] ?? '📦'}</span>
          <h3 className="font-semibold text-gray-800">{title}</h3>
          <span className="text-xs bg-gray-100 text-gray-600 px-2 py-0.5 rounded-full">{count}</span>
        </div>
        <svg
          className={`w-4 h-4 text-gray-400 transition-transform ${expanded ? 'rotate-180' : ''}`}
          fill="none" viewBox="0 0 24 24" stroke="currentColor"
        >
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
        </svg>
      </button>

      {expanded && (
        <div className="border-t">
          {components.length === 0 ? (
            <p className="px-4 py-3 text-sm text-gray-400 italic">No {title.toLowerCase()} yet</p>
          ) : (
            <div className="divide-y">
              {components.map((comp) => (
                <div key={comp.id} className="px-4 py-3 flex items-center justify-between hover:bg-gray-50">
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="font-medium text-gray-900 text-sm truncate">{comp.name}</span>
                      <span className={`text-xs px-1.5 py-0.5 rounded ${statusColors[comp.status] ?? 'bg-gray-100'}`}>
                        {comp.status}
                      </span>
                      {riskMap?.[comp.id] && riskMap[comp.id]!.openCount > 0 && (
                        <span className={`text-xs px-1.5 py-0.5 rounded font-medium ${severityBadge[riskMap[comp.id]!.highestSeverity ?? 'III'] ?? 'bg-gray-200'}`}
                          title={`${riskMap[comp.id]!.openCount} open POA&M(s), ${riskMap[comp.id]!.overdueCount} overdue`}
                        >
                          {riskMap[comp.id]!.openCount} POA&M{riskMap[comp.id]!.openCount > 1 ? 's' : ''}
                          {riskMap[comp.id]!.overdueCount > 0 && ` (${riskMap[comp.id]!.overdueCount} overdue)`}
                        </span>
                      )}
                    </div>
                    <div className="flex items-center gap-3 mt-1 text-xs text-gray-500">
                      {comp.subType && <span>{comp.subType}</span>}
                      {comp.owner && <span>Owner: {comp.owner}</span>}
                      {comp.personName && <span>{comp.personName}</span>}
                      {comp.email && <span className="text-blue-600">{comp.email}</span>}
                      {comp.rmfRole && <span className="text-purple-600 font-medium">{comp.rmfRole}</span>}
                    </div>
                    {comp.linkedCapabilities.length > 0 && (
                      <div className="flex flex-wrap gap-1 mt-1">
                        <span className="text-xs text-gray-500 mr-1">{comp.linkedCapabilities.length} cap{comp.linkedCapabilities.length !== 1 ? 's' : ''}</span>
                        {comp.linkedCapabilities.map((lc) => (
                          <span key={lc.capabilityId} className="text-xs bg-indigo-50 text-indigo-600 px-1.5 py-0.5 rounded">
                            {lc.capabilityName}
                          </span>
                        ))}
                      </div>
                    )}
                  </div>
                  <div className="flex gap-1 ml-3">
                    {onCreateCapability && comp.componentType === 'Thing' && comp.linkedCapabilities.length === 0 && (
                      <button onClick={() => onCreateCapability(comp)} className="text-xs text-green-600 hover:text-green-800 px-2 py-1">+ Capability</button>
                    )}
                    {onRelink && comp.azureResourceId && (
                      <button onClick={() => onRelink(comp)} className="text-xs text-indigo-600 hover:text-indigo-800 px-2 py-1">Re-link</button>
                    )}
                    <button onClick={() => onEdit(comp)} className="text-xs text-blue-600 hover:text-blue-800 px-2 py-1">Edit</button>
                    <button onClick={() => onDelete(comp.id)} className="text-xs text-red-500 hover:text-red-700 px-2 py-1">Delete</button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
