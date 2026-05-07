import { useState, useCallback, useEffect } from 'react';
import { useParams } from 'react-router-dom';
import { usePolling } from '../hooks/usePolling';
import {
  getCapabilityCoverage,
  bulkRegenerateNarratives,
  type CapabilityCoverageResponse,
  type CapabilityCoverageDto,
  type BulkRegenerateResult,
} from '../api/capabilities';
import AddCapabilityDialog from '../components/AddCapabilityDialog';
import EvidenceUploadDialog from '../components/EvidenceUploadDialog';
import { useSettings } from '../hooks/useSettings';

const ROLE_COLORS: Record<string, string> = {
  Primary: 'bg-indigo-100 text-indigo-700',
  Supporting: 'bg-yellow-100 text-yellow-700',
  Shared: 'bg-gray-100 text-gray-600',
};

const TYPE_COLORS: Record<string, string> = {
  Person: 'bg-indigo-50 text-indigo-700',
  Place: 'bg-green-50 text-green-700',
  Thing: 'bg-purple-50 text-purple-700',
};

export default function CapabilityCoverage() {
  const { id: systemId } = useParams<{ id: string }>();
  const { settings } = useSettings();
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [showAddDialog, setShowAddDialog] = useState(false);

  const configuredSourceUrls = (() => {
    if (!settings?.sharePointSiteUrl || !settings?.sourceDocuments) {
      return [];
    }
    const base = settings.sharePointSiteUrl.trim().replace(/\/+$/, '');
    const lines = settings.sourceDocuments
      .split(/\r?\n|,/) 
      .map(x => x.trim())
      .filter(Boolean);

    return lines
      .map((line) => {
        if (/^https?:\/\//i.test(line)) return line;
        if (!base) return null;
        return `${base}/${line.replace(/^\/+/, '')}`;
      })
      .filter((x): x is string => Boolean(x));
  })();

  const fetcher = useCallback(
    () => (systemId ? getCapabilityCoverage(systemId) : Promise.reject('No systemId')),
    [systemId],
  );
  const { data, refresh } = usePolling<CapabilityCoverageResponse>(fetcher, 30000);

  if (!data) {
    return (
      <div className="p-6 text-center text-gray-500">Loading system capabilities...</div>
    );
  }

  const { capabilities, summary } = data;

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold text-gray-900">System Capabilities</h2>
          <p className="mt-1 text-sm text-gray-500">
            View and manage the org-wide capabilities mapped to this system, along with their narrative coverage and linked components.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setShowAddDialog(true)}
          className="inline-flex items-center gap-1.5 rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-indigo-700"
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
        <SummaryCard label="Custom" value={summary.totalNarrativesCustom} color="text-indigo-600" />
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
              systemId={systemId ?? ''}
              sourceUrls={configuredSourceUrls}
              isExpanded={expandedId === cap.capabilityId}
              onToggle={() => setExpandedId(expandedId === cap.capabilityId ? null : cap.capabilityId)}
              onRefresh={refresh}
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
            refresh();
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
  systemId,
  sourceUrls,
  isExpanded,
  onToggle,
  onRefresh,
}: {
  capability: CapabilityCoverageDto;
  systemId: string;
  sourceUrls: string[];
  isExpanded: boolean;
  onToggle: () => void;
  onRefresh: () => void;
}) {
  const [regenerating, setRegenerating] = useState(false);
  const [regenResult, setRegenResult] = useState<BulkRegenerateResult | null>(null);
  const ns = cap.narrativeStatus;
  const total = ns.populated + ns.custom + ns.empty;
  const filled = ns.populated + ns.custom;

  const handleBulkRegenerate = async () => {
    if (!systemId || regenerating) return;
    setRegenerating(true);
    setRegenResult(null);
    try {
      const result = await bulkRegenerateNarratives(
        systemId,
        cap.capabilityId,
        sourceUrls.length > 0 ? { sourceUrls } : undefined,
      );
      setRegenResult(result);
      onRefresh();
    } catch {
      setRegenResult({ totalControls: 0, regenerated: 0, skippedCustom: 0, failed: 1, regeneratedControlIds: [] });
    } finally {
      setRegenerating(false);
    }
  };

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
            <span className="text-indigo-600">Custom: {ns.custom}</span>
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

          {/* Bulk Regenerate Narratives */}
          {cap.mappedControlCount > 0 && (
            <div className="border-t border-gray-100 pt-3">
              <div className="flex items-center gap-3">
                <button
                  type="button"
                  onClick={handleBulkRegenerate}
                  disabled={regenerating}
                  className="inline-flex items-center gap-1.5 rounded-md bg-purple-600 px-3 py-1.5 text-xs font-medium text-white shadow-sm hover:bg-purple-700 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  {regenerating ? (
                    <svg className="h-3.5 w-3.5 animate-spin" fill="none" viewBox="0 0 24 24">
                      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                    </svg>
                  ) : (
                    <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0 3.181 3.183a8.25 8.25 0 0 0 13.803-3.7M4.031 9.865a8.25 8.25 0 0 1 13.803-3.7l3.181 3.182" />
                    </svg>
                  )}
                  {regenerating ? 'Regenerating...' : 'Regenerate Narratives'}
                </button>
                {sourceUrls.length > 0 && (
                  <span className="text-xs text-indigo-600">Using configured SharePoint/document sources</span>
                )}
                {regenResult && (
                  <span className="text-xs text-gray-600">
                    {regenResult.failed > 0 ? (
                      <span className="text-red-600">Regeneration failed</span>
                    ) : (
                      <>
                        <span className="text-green-600 font-medium">{regenResult.regenerated} regenerated</span>
                        {regenResult.skippedCustom > 0 && (
                          <span className="text-amber-600 ml-2">{regenResult.skippedCustom} custom skipped</span>
                        )}
                      </>
                    )}
                  </span>
                )}
              </div>
            </div>
          )}

          {/* Evidence Section (Feature 038) */}
          {systemId && (
            <CapabilityEvidenceSection systemId={systemId} capabilityId={cap.capabilityId} />
          )}
        </div>
      )}
    </div>
  );
}

// ─── Capability Evidence (inline, Feature 038 US3) ──────────────────────────

function CapabilityEvidenceSection({ systemId, capabilityId }: { systemId: string; capabilityId: string }) {
  const [showUpload, setShowUpload] = useState(false);
  const [artifacts, setArtifacts] = useState<{ id: string; fileName: string; artifactCategory: string; uploadedBy: string; uploadedAt: string }[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [refreshKey, setRefreshKey] = useState(0);

  useEffect(() => {
    let active = true;
    setLoaded(false);

    import('../api/evidence')
      .then(({ listEvidence }) => listEvidence({ systemId, pageSize: 100 }))
      .then((res) => {
        if (!active) return;

        const capEvidence = (res.items ?? []).filter(
          (e: { securityCapabilityId?: string | null }) => e.securityCapabilityId === capabilityId,
        );

        setArtifacts(
          capEvidence.map(
            (e: {
              id: string;
              fileName?: string | null;
              artifactCategory?: string | null;
              uploadedBy?: string | null;
              uploadedAt?: string | null;
            }) => ({
              id: e.id,
              fileName: e.fileName ?? 'Evidence',
              artifactCategory: e.artifactCategory ?? 'Other',
              uploadedBy: e.uploadedBy ?? 'Unknown',
              uploadedAt: e.uploadedAt ?? '',
            }),
          ),
        );
      })
      .catch(() => {
        if (!active) return;
        setArtifacts([]);
      })
      .finally(() => {
        if (active) setLoaded(true);
      });

    return () => {
      active = false;
    };
  }, [systemId, capabilityId, refreshKey]);

  return (
    <div className="border-t border-gray-100 pt-3">
      <div className="flex items-center justify-between">
        <p className="text-xs font-medium text-gray-500">
          Evidence {artifacts.length > 0 && <span className="ml-1 text-indigo-600">({artifacts.length})</span>}
        </p>
        <button
          onClick={() => setShowUpload(true)}
          className="inline-flex items-center gap-1 rounded bg-indigo-600 px-2 py-1 text-xs font-medium text-white hover:bg-indigo-700"
        >
          <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
          </svg>
          Attach
        </button>
      </div>
      {loaded && artifacts.length === 0 && (
        <p className="mt-2 text-xs text-gray-400 italic">No evidence attached to this capability.</p>
      )}
      {artifacts.length > 0 && (
        <div className="mt-2 space-y-1">
          {artifacts.map((a) => (
            <div key={a.id} className="flex items-center gap-2 text-xs text-gray-600">
              <span className="font-medium">{a.fileName}</span>
              <span className="text-gray-400">&middot;</span>
              <span>{(a.artifactCategory || 'Other').replace(/([A-Z])/g, ' $1').trim()}</span>
              <span className="text-gray-400">&middot;</span>
              <span>{a.uploadedBy}</span>
            </div>
          ))}
        </div>
      )}
      {showUpload && (
        <EvidenceUploadDialog
          systemId={systemId}
          securityCapabilityId={capabilityId}
          onClose={() => setShowUpload(false)}
          onUploaded={() => {
            setShowUpload(false);
            setRefreshKey((k) => k + 1);
          }}
        />
      )}
    </div>
  );
}
