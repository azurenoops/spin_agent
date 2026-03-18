import { useState, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { usePolling } from '../hooks/usePolling';
import {
  getCapabilityCoverage,
  type CapabilityCoverageResponse,
  type CapabilityCoverageDto,
} from '../api/capabilities';
import AddCapabilityDialog from '../components/AddCapabilityDialog';

const ROLE_COLORS: Record<string, string> = {
  Primary: 'bg-indigo-100 text-indigo-700',
  Supporting: 'bg-yellow-100 text-yellow-700',
  Shared: 'bg-gray-100 text-gray-600',
};

const TYPE_COLORS: Record<string, string> = {
  Person: 'bg-blue-50 text-blue-700',
  Place: 'bg-green-50 text-green-700',
  Thing: 'bg-purple-50 text-purple-700',
};

export default function CapabilityCoverage() {
  const { id: systemId } = useParams<{ id: string }>();
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [showAddDialog, setShowAddDialog] = useState(false);

  const fetcher = useCallback(
    () => (systemId ? getCapabilityCoverage(systemId) : Promise.reject('No systemId')),
    [systemId],
  );
  const { data } = usePolling<CapabilityCoverageResponse>(fetcher, 30000);

  if (!data) {
    return (
      <div className="p-6 text-center text-gray-500">Loading capability coverage...</div>
    );
  }

  const { capabilities, summary } = data;

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold text-gray-900">Capability Coverage</h2>
          <p className="mt-1 text-sm text-gray-500">
            Maps organizational capabilities to NIST SP 800-53 controls, showing which security requirements are addressed by each capability.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setShowAddDialog(true)}
          className="inline-flex items-center gap-1.5 rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-blue-700"
        >
          <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" />
          </svg>
          Add Capability
        </button>
      </div>

      {/* Summary bar */}
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
        <SummaryCard label="Capabilities" value={summary.totalCapabilities} />
        <SummaryCard label="Mapped Controls" value={summary.totalMappedControls} />
        <SummaryCard label="Populated" value={summary.totalNarrativesPopulated} color="text-green-600" />
        <SummaryCard label="Custom" value={summary.totalNarrativesCustom} color="text-blue-600" />
        <SummaryCard label="Empty" value={summary.totalNarrativesEmpty} color="text-red-600" />
        <SummaryCard label="Coverage" value={`${summary.coveragePercent}%`} color="text-indigo-600" />
      </div>

      {/* Capability list */}
      {capabilities.length === 0 ? (
        <div className="rounded-lg border-2 border-dashed border-gray-300 p-12 text-center">
          <p className="text-sm text-gray-500">No capabilities mapped to this system yet.</p>
        </div>
      ) : (
        <div className="space-y-3">
          {capabilities.map((cap) => (
            <CapabilityRow
              key={cap.capabilityId}
              capability={cap}
              isExpanded={expandedId === cap.capabilityId}
              onToggle={() => setExpandedId(expandedId === cap.capabilityId ? null : cap.capabilityId)}
            />
          ))}
        </div>
      )}

      {/* Add Capability Dialog */}
      {showAddDialog && systemId && (
        <AddCapabilityDialog
          systemId={systemId}
          existingCapabilityIds={capabilities.map((c) => c.capabilityId)}
          onClose={() => setShowAddDialog(false)}
          onAdded={() => {
            setShowAddDialog(false);
          }}
        />
      )}
    </div>
  );
}

function SummaryCard({ label, value, color }: { label: string; value: string | number; color?: string }) {
  return (
    <div className="rounded-lg border border-gray-200 bg-white p-4">
      <p className="text-xs font-medium text-gray-500">{label}</p>
      <p className={`mt-1 text-2xl font-bold ${color ?? 'text-gray-900'}`}>{value}</p>
    </div>
  );
}

function CapabilityRow({
  capability: cap,
  isExpanded,
  onToggle,
}: {
  capability: CapabilityCoverageDto;
  isExpanded: boolean;
  onToggle: () => void;
}) {
  const ns = cap.narrativeStatus;
  const total = ns.populated + ns.custom + ns.empty;
  const filled = ns.populated + ns.custom;

  return (
    <div className="rounded-lg border border-gray-200 bg-white shadow-sm">
      <button
        type="button"
        onClick={onToggle}
        className="flex w-full items-center justify-between px-4 py-3 text-left hover:bg-gray-50"
      >
        <div className="flex items-center gap-3 min-w-0">
          <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${ROLE_COLORS[cap.role] ?? 'bg-gray-100 text-gray-600'}`}>
            {cap.role}
          </span>
          <div className="min-w-0">
            <span className="font-medium text-gray-900 truncate">{cap.capabilityName}</span>
            <span className="ml-2 text-sm text-gray-500">{cap.provider}</span>
          </div>
        </div>
        <div className="flex items-center gap-4 text-sm text-gray-500 shrink-0">
          <span>{cap.mappedControlCount} controls</span>
          <span className={filled === total && total > 0 ? 'text-green-600 font-medium' : ''}>
            {filled}/{total} narratives
          </span>
          <svg
            className={`h-5 w-5 transform transition-transform ${isExpanded ? 'rotate-180' : ''}`}
            fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}
          >
            <path strokeLinecap="round" strokeLinejoin="round" d="m19.5 8.25-7.5 7.5-7.5-7.5" />
          </svg>
        </div>
      </button>

      {isExpanded && (
        <div className="border-t border-gray-100 px-4 py-3 space-y-3">
          {/* Narrative status breakdown */}
          <div className="flex flex-wrap gap-4 text-sm">
            <span className="text-green-600">Populated: {ns.populated}</span>
            <span className="text-blue-600">Custom: {ns.custom}</span>
            <span className="text-red-600">Empty: {ns.empty}</span>
            {ns.aiGenerated > 0 && <span className="text-purple-600">AI Generated: {ns.aiGenerated}</span>}
          </div>

          {/* Linked components */}
          {cap.components.length > 0 ? (
            <div>
              <p className="text-xs font-medium text-gray-500 mb-2">Linked Components</p>
              <div className="space-y-1">
                {cap.components.map((comp) => (
                  <div key={comp.componentId} className="flex items-center gap-2 text-sm">
                    <span className={`inline-flex items-center rounded px-1.5 py-0.5 text-xs font-medium ${TYPE_COLORS[comp.componentType] ?? 'bg-gray-50 text-gray-600'}`}>
                      {comp.componentType}
                    </span>
                    <span className="text-gray-900">{comp.name}</span>
                    {comp.owner && <span className="text-gray-400">({comp.owner})</span>}
                    {comp.boundaryName && (
                      <span className="text-xs text-gray-400 ml-auto">{comp.boundaryName}</span>
                    )}
                  </div>
                ))}
              </div>
            </div>
          ) : (
            <p className="text-xs text-gray-400 italic">No components linked to this capability for this system</p>
          )}

          {/* Capability metadata */}
          <div className="flex flex-wrap gap-4 text-xs text-gray-500 border-t border-gray-100 pt-2">
            <span>Category: {cap.category}</span>
            <span>Status: {cap.implementationStatus}</span>
            {cap.owner && <span>Owner: {cap.owner}</span>}
          </div>
        </div>
      )}
    </div>
  );
}
