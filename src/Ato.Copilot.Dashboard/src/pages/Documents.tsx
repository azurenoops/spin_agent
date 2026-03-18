import { useState, useCallback, useEffect } from 'react';
import { useParams } from 'react-router-dom';
import { usePolling } from '../hooks/usePolling';
import { getSystemDocuments } from '../api/documents';
import { listExports, downloadExportUrl } from '../api/exports';
import type { ExportSummary } from '../api/exports';
import type {
  SystemDocumentsResponse,
  SspSectionInfo,
  InterconnectionDocInfo,
  ScanImportInfo,
} from '../api/documents';
import ExportSspDialog from '../components/ExportSspDialog';
import TemplateManagementDialog from '../components/TemplateManagementDialog';

// ─── Helpers ────────────────────────────────────────────────────────────────

function StatusBadge({ status, variant }: { status: string; variant?: 'green' | 'amber' | 'red' | 'blue' | 'gray' }) {
  const colors = {
    green: 'bg-green-100 text-green-700',
    amber: 'bg-amber-100 text-amber-700',
    red: 'bg-red-100 text-red-700',
    blue: 'bg-blue-100 text-blue-700',
    gray: 'bg-gray-100 text-gray-500',
  };
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${colors[variant ?? 'gray']}`}>
      {status}
    </span>
  );
}

function variantForStatus(status: string): 'green' | 'amber' | 'red' | 'blue' | 'gray' {
  const s = status.toLowerCase();
  if (s === 'approved' || s === 'finalized' || s === 'active' || s === 'completed') return 'green';
  if (s === 'underreview' || s === 'inreview' || s === 'draft' || s === 'pending') return 'blue';
  if (s === 'needsrevision' || s === 'expired' || s === 'revoked') return 'amber';
  if (s === 'denied' || s === 'dato' || s === 'failed') return 'red';
  return 'gray';
}

function formatDate(dt: string | null | undefined): string {
  if (!dt) return '—';
  return new Date(dt).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
}

function DocIcon() {
  return (
    <svg className="h-4 w-4 text-gray-400 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 14.25v-2.625a3.375 3.375 0 0 0-3.375-3.375h-1.5A1.125 1.125 0 0 1 13.5 7.125v-1.5a3.375 3.375 0 0 0-3.375-3.375H8.25m2.25 0H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 0 0-9-9z" />
    </svg>
  );
}

function SectionHeader({ icon, title, action }: { icon: string; title: string; action?: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between px-5 py-3 bg-gray-50 border-b border-gray-200">
      <div className="flex items-center gap-2">
        <span className="text-base">{icon}</span>
        <h3 className="text-sm font-semibold text-gray-900">{title}</h3>
      </div>
      {action}
    </div>
  );
}

function DocRow({
  label,
  subtitle,
  status,
  statusVariant,
  detail,
  missing,
}: {
  label: string;
  subtitle?: string;
  status?: string;
  statusVariant?: 'green' | 'amber' | 'red' | 'blue' | 'gray';
  detail?: string;
  missing?: boolean;
}) {
  return (
    <div className={`flex items-center gap-3 px-5 py-3 ${missing ? 'opacity-50' : ''}`}>
      <DocIcon />
      <div className="flex-1 min-w-0">
        <p className="text-sm font-medium text-gray-900">{label}</p>
        {subtitle && <p className="text-xs text-gray-500 mt-0.5">{subtitle}</p>}
      </div>
      <div className="flex items-center gap-2 flex-shrink-0">
        {detail && <span className="text-xs text-gray-500">{detail}</span>}
        {status && <StatusBadge status={status} variant={statusVariant ?? variantForStatus(status)} />}
        {missing && <StatusBadge status="Missing" variant="gray" />}
      </div>
    </div>
  );
}

// ─── Sub-section Components ─────────────────────────────────────────────────

function AuthPackageSection({ data }: { data: SystemDocumentsResponse }) {
  return (
    <div className="rounded-lg border border-gray-200 bg-white shadow-sm overflow-hidden">
      <SectionHeader icon="📦" title="Authorization Package" />
      <div className="divide-y divide-gray-100">
        <DocRow
          label="System Security Plan (SSP)"
          subtitle={`${data.ssp.completedNarratives}/${data.ssp.totalNarratives} narratives complete`}
          status={data.ssp.totalNarratives === 0 ? 'Not Started' : data.ssp.narrativeCompletionPct >= 100 ? 'Complete' : 'In Progress'}
          statusVariant={data.ssp.narrativeCompletionPct >= 100 ? 'green' : data.ssp.narrativeCompletionPct > 0 ? 'blue' : 'gray'}
          detail={`${data.ssp.narrativeCompletionPct}%`}
        />
        {data.sap ? (
          <DocRow
            label="Security Assessment Plan (SAP)"
            subtitle={data.sap.title}
            status={data.sap.status}
            detail={data.sap.contentHash ? `SHA: ${data.sap.contentHash.slice(0, 12)}…` : `${data.sap.totalControls} controls`}
          />
        ) : (
          <DocRow label="Security Assessment Plan (SAP)" missing />
        )}
        {data.authorization ? (
          <DocRow
            label={`Authorization Decision (${data.authorization.decisionType})`}
            subtitle={`Issued by ${data.authorization.issuedBy} — ${formatDate(data.authorization.decisionDate)}`}
            status={
              data.authorization.daysUntilExpiration !== null && data.authorization.daysUntilExpiration < 0
                ? 'Expired'
                : data.authorization.daysUntilExpiration !== null && data.authorization.daysUntilExpiration < 90
                  ? `${data.authorization.daysUntilExpiration}d left`
                  : data.authorization.decisionType
            }
            statusVariant={
              data.authorization.decisionType === 'DATO' ? 'red'
                : data.authorization.daysUntilExpiration !== null && data.authorization.daysUntilExpiration < 0 ? 'red'
                  : data.authorization.daysUntilExpiration !== null && data.authorization.daysUntilExpiration < 90 ? 'amber'
                    : 'green'
            }
            detail={`Risk: ${data.authorization.residualRisk}`}
          />
        ) : (
          <DocRow label="Authorization Decision" missing />
        )}
        <DocRow
          label="Plan of Action & Milestones (POA&M)"
          subtitle={data.poamOverdueCount > 0 ? `${data.poamOverdueCount} overdue` : undefined}
          status={data.poamCount > 0 ? `${data.poamCount} open` : 'None'}
          statusVariant={data.poamOverdueCount > 0 ? 'red' : data.poamCount > 0 ? 'amber' : 'gray'}
        />
        <DocRow
          label="Customer Responsibility Matrix (CRM)"
          status={data.hasBaseline ? 'Available' : 'No Baseline'}
          statusVariant={data.hasBaseline ? 'green' : 'gray'}
          detail={data.hasBaseline ? `${data.baselineControlCount} controls` : undefined}
          missing={!data.hasBaseline}
        />
      </div>
    </div>
  );
}

function PrivacySection({ data }: { data: SystemDocumentsResponse }) {
  return (
    <div className="rounded-lg border border-gray-200 bg-white shadow-sm overflow-hidden">
      <SectionHeader icon="🔒" title="Privacy" />
      <div className="divide-y divide-gray-100">
        {data.pta ? (
          <DocRow
            label="Privacy Threshold Analysis (PTA)"
            subtitle={`Analyzed by ${data.pta.analyzedBy} — ${formatDate(data.pta.analyzedAt)}`}
            status={data.pta.determination}
            statusVariant={data.pta.determination === 'PiaNotRequired' || data.pta.determination === 'Exempt' ? 'green' : 'blue'}
            detail={data.pta.collectsPii ? `PII: ${data.pta.piiCategories.join(', ')}` : 'No PII'}
          />
        ) : (
          <DocRow label="Privacy Threshold Analysis (PTA)" missing />
        )}
        {data.pia ? (
          <DocRow
            label="Privacy Impact Assessment (PIA)"
            subtitle={
              data.pia.approvedBy
                ? `Approved by ${data.pia.approvedBy} — ${formatDate(data.pia.approvedAt)}`
                : `Version ${data.pia.version}`
            }
            status={data.pia.status}
            statusVariant={variantForStatus(data.pia.status)}
            detail={
              data.pia.daysUntilExpiration !== null
                ? data.pia.daysUntilExpiration < 0
                  ? 'EXPIRED'
                  : data.pia.daysUntilExpiration < 90
                    ? `Expires in ${data.pia.daysUntilExpiration}d`
                    : `Expires ${formatDate(data.pia.expirationDate)}`
                : undefined
            }
          />
        ) : data.pta?.determination === 'PiaRequired' ? (
          <DocRow
            label="Privacy Impact Assessment (PIA)"
            status="Required"
            statusVariant="amber"
          />
        ) : (
          <DocRow label="Privacy Impact Assessment (PIA)" status={data.pta ? 'Not Required' : '—'} statusVariant="gray" />
        )}
      </div>
    </div>
  );
}

function SspSectionsSection({ sections, activeWaiverCount }: { sections: SspSectionInfo[]; activeWaiverCount: number }) {
  if (sections.length === 0) return null;

  const completed = sections.filter(s => s.status === 'Approved').length;

  return (
    <div className="rounded-lg border border-gray-200 bg-white shadow-sm overflow-hidden">
      <SectionHeader
        icon="📝"
        title="SSP Sections"
        action={
          <div className="flex items-center gap-3">
            {activeWaiverCount > 0 && (
              <span className="inline-flex items-center rounded-full border border-dashed border-purple-300 bg-purple-50 px-2 py-0.5 text-xs font-medium text-purple-700">
                {activeWaiverCount} waived control{activeWaiverCount !== 1 ? 's' : ''}
              </span>
            )}
            <span className="text-xs text-gray-500">{completed}/{sections.length} approved</span>
          </div>
        }
      />
      <div className="divide-y divide-gray-100">
        {sections.map((s) => (
          <div key={s.sectionNumber} className="flex items-center gap-3 px-5 py-2.5">
            <span className={`flex h-6 w-6 items-center justify-center rounded-full text-xs font-semibold ${
              s.status === 'Approved' ? 'bg-green-100 text-green-700'
                : s.status === 'UnderReview' ? 'bg-blue-100 text-blue-700'
                  : s.status === 'NeedsRevision' ? 'bg-amber-100 text-amber-700'
                    : s.status === 'Draft' ? 'bg-gray-100 text-gray-600'
                      : 'bg-gray-50 text-gray-400'
            }`}>
              {s.sectionNumber}
            </span>
            <div className="flex-1 min-w-0">
              <p className="text-sm text-gray-900 truncate">§{s.sectionNumber} — {s.title}</p>
              {s.authoredBy && (
                <p className="text-xs text-gray-500">{s.authoredBy} · v{s.version}</p>
              )}
            </div>
            <StatusBadge status={s.status} variant={variantForStatus(s.status)} />
          </div>
        ))}
      </div>
    </div>
  );
}

function NarrativeGovernanceSection({ data }: { data: SystemDocumentsResponse }) {
  const gov = data.narrativeGovernance;
  if (!gov) return null;

  const segments = [
    { label: 'Approved', count: gov.approved, color: 'bg-green-500' },
    { label: 'In Review', count: gov.inReview, color: 'bg-blue-500' },
    { label: 'Needs Revision', count: gov.needsRevision, color: 'bg-amber-500' },
    { label: 'Draft', count: gov.draft, color: 'bg-gray-300' },
  ];

  return (
    <div className="rounded-lg border border-gray-200 bg-white shadow-sm overflow-hidden">
      <SectionHeader
        icon="✅"
        title="Narrative Governance"
        action={<span className="text-xs text-gray-500">{gov.approvalPct}% approved</span>}
      />
      <div className="p-5">
        {/* Stacked bar */}
        <div className="flex h-3 rounded-full overflow-hidden mb-3">
          {segments.map((seg) =>
            seg.count > 0 ? (
              <div
                key={seg.label}
                className={`${seg.color} transition-all`}
                style={{ width: `${(seg.count / gov.totalNarratives) * 100}%` }}
                title={`${seg.label}: ${seg.count}`}
              />
            ) : null,
          )}
        </div>
        <div className="flex flex-wrap gap-4 text-xs">
          {segments.map((seg) => (
            <div key={seg.label} className="flex items-center gap-1.5">
              <span className={`h-2.5 w-2.5 rounded-full ${seg.color}`} />
              <span className="text-gray-600">{seg.label}:</span>
              <span className="font-medium text-gray-900">{seg.count}</span>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

function InterconnectionsSection({ interconnections }: { interconnections: InterconnectionDocInfo[] }) {
  if (interconnections.length === 0) return null;

  return (
    <div className="rounded-lg border border-gray-200 bg-white shadow-sm overflow-hidden">
      <SectionHeader
        icon="🔗"
        title="Interconnections & Agreements"
        action={<span className="text-xs text-gray-500">{interconnections.length} registered</span>}
      />
      <div className="divide-y divide-gray-100">
        {interconnections.map((ic) => (
          <DocRow
            key={ic.interconnectionId}
            label={`ISA — ${ic.targetSystem}`}
            subtitle={`${ic.direction} · ${ic.status}`}
            status={ic.hasAgreement ? `${ic.agreementType} (${ic.agreementStatus})` : 'No Agreement'}
            statusVariant={ic.hasAgreement ? variantForStatus(ic.agreementStatus ?? '') : 'amber'}
          />
        ))}
      </div>
    </div>
  );
}

function ConMonSection({ data }: { data: SystemDocumentsResponse }) {
  if (!data.conMon) return null;

  return (
    <div className="rounded-lg border border-gray-200 bg-white shadow-sm overflow-hidden">
      <SectionHeader icon="📊" title="Continuous Monitoring" />
      <div className="divide-y divide-gray-100">
        <DocRow
          label="ConMon Plan"
          subtitle={`Frequency: ${data.conMon.frequency}`}
          status="Active"
          statusVariant="green"
        />
        <DocRow
          label="ConMon Reports"
          subtitle={data.conMon.lastReportDate ? `Last: ${formatDate(data.conMon.lastReportDate)}` : 'No reports generated yet'}
          detail={`${data.conMon.reportCount} report${data.conMon.reportCount !== 1 ? 's' : ''}`}
        />
      </div>
    </div>
  );
}

function ImportTypeLabel(type: string): string {
  const map: Record<string, string> = {
    Ckl: 'CKL',
    Xccdf: 'XCCDF',
    PrismaCsv: 'Prisma CSV',
    PrismaApi: 'Prisma API',
    NessusXml: 'ACAS/Nessus',
  };
  return map[type] ?? type;
}

function ImportHistorySection({ imports }: { imports: ScanImportInfo[] }) {
  const [showAll, setShowAll] = useState(false);
  const visible = showAll ? imports : imports.slice(0, 5);

  if (imports.length === 0) return null;

  return (
    <div className="rounded-lg border border-gray-200 bg-white shadow-sm overflow-hidden">
      <SectionHeader
        icon="📥"
        title="Scan & Import History"
        action={<span className="text-xs text-gray-500">{imports.length} imports</span>}
      />
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-100 text-left text-xs text-gray-500">
              <th className="px-5 py-2 font-medium">Type</th>
              <th className="px-5 py-2 font-medium">File</th>
              <th className="px-5 py-2 font-medium">Date</th>
              <th className="px-5 py-2 font-medium text-right">Entries</th>
              <th className="px-5 py-2 font-medium text-right">Open</th>
              <th className="px-5 py-2 font-medium text-right">Pass</th>
              <th className="px-5 py-2 font-medium">Benchmark</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-50">
            {visible.map((imp) => (
              <tr key={imp.importId} className="hover:bg-gray-50">
                <td className="px-5 py-2.5">
                  <StatusBadge status={ImportTypeLabel(imp.importType)} variant="blue" />
                </td>
                <td className="px-5 py-2.5 text-gray-900 max-w-[200px] truncate" title={imp.fileName}>
                  {imp.fileName}
                </td>
                <td className="px-5 py-2.5 text-gray-500">{formatDate(imp.importedAt)}</td>
                <td className="px-5 py-2.5 text-right tabular-nums text-gray-900">{imp.totalEntries}</td>
                <td className="px-5 py-2.5 text-right tabular-nums">
                  <span className={imp.openCount > 0 ? 'text-red-600 font-medium' : 'text-gray-400'}>
                    {imp.openCount}
                  </span>
                </td>
                <td className="px-5 py-2.5 text-right tabular-nums text-green-600">{imp.passCount}</td>
                <td className="px-5 py-2.5 text-gray-500 max-w-[200px] truncate" title={imp.benchmarkTitle ?? ''}>
                  {imp.benchmarkTitle ?? '—'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {imports.length > 5 && (
        <div className="border-t border-gray-100 px-5 py-2 text-center">
          <button
            onClick={() => setShowAll(!showAll)}
            className="text-xs text-blue-600 hover:text-blue-800 font-medium"
          >
            {showAll ? 'Show Less' : `View All ${imports.length} Imports`}
          </button>
        </div>
      )}
    </div>
  );
}

function ExportsSection({ data, onExportClick, onManageTemplates }: { data: SystemDocumentsResponse; onExportClick: () => void; onManageTemplates: () => void }) {
  const [exportHistory, setExportHistory] = useState<ExportSummary[]>([]);
  const [showAll, setShowAll] = useState(false);

  useEffect(() => {
    listExports(data.systemId, { limit: 10 })
      .then(res => setExportHistory(res.items))
      .catch(() => setExportHistory([]));
  }, [data.systemId]);

  const exports = [
    { label: 'eMASS Controls Export', format: '.xlsx', available: data.hasBaseline },
    { label: 'eMASS POA&M Export', format: '.xlsx', available: data.poamCount > 0 },
    { label: 'OSCAL SSP (JSON)', format: '.json', available: data.ssp.totalNarratives > 0 },
    { label: 'HW/SW Inventory', format: '.xlsx', available: data.inventoryItemCount > 0 },
  ];

  const visible = showAll ? exportHistory : exportHistory.slice(0, 5);

  const formatIconMap: Record<string, string> = { docx: '📄', pdf: '📕', json: '🔗' };

  function formatBytes(bytes: number | null): string {
    if (!bytes) return '—';
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  return (
    <div className="rounded-lg border border-gray-200 bg-white shadow-sm overflow-hidden">
      <SectionHeader
        icon="📤"
        title="Exports"
        action={
          <div className="flex items-center gap-2">
            <button
              onClick={onManageTemplates}
              className="inline-flex items-center gap-1.5 rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 transition-colors"
            >
              Manage Templates
            </button>
            <button
              onClick={onExportClick}
              disabled={!data.hasBaseline}
              title={!data.hasBaseline ? 'Select a baseline first' : 'Export SSP document'}
              className="inline-flex items-center gap-1.5 rounded-md bg-blue-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M3 16.5v2.25A2.25 2.25 0 0 0 5.25 21h13.5A2.25 2.25 0 0 0 21 18.75V16.5m-13.5-9L12 3m0 0l4.5 4.5M12 3v13.5" />
              </svg>
              Export SSP
            </button>
          </div>
        }
      />
      <div className="divide-y divide-gray-100">
        {exports.map((exp) => (
          <div key={exp.label} className={`flex items-center gap-3 px-5 py-3 ${!exp.available ? 'opacity-40' : ''}`}>
            <DocIcon />
            <div className="flex-1">
              <p className="text-sm text-gray-900">{exp.label}</p>
              <p className="text-xs text-gray-500">{exp.format}</p>
            </div>
            <button
              disabled={!exp.available}
              className="inline-flex items-center gap-1 rounded-md border border-gray-300 bg-white px-2.5 py-1 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M3 16.5v2.25A2.25 2.25 0 0 0 5.25 21h13.5A2.25 2.25 0 0 0 21 18.75V16.5M16.5 12 12 16.5m0 0L7.5 12m4.5 4.5V3" />
              </svg>
              Download
            </button>
          </div>
        ))}
      </div>

      {/* Export History */}
      {exportHistory.length > 0 && (
        <>
          <div className="border-t border-gray-200 px-5 py-2 bg-gray-50">
            <h4 className="text-xs font-semibold text-gray-600 uppercase tracking-wide">Export History</h4>
          </div>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-100 text-left text-xs text-gray-500">
                  <th className="px-5 py-2 font-medium">Format</th>
                  <th className="px-5 py-2 font-medium">Generated By</th>
                  <th className="px-5 py-2 font-medium">Date</th>
                  <th className="px-5 py-2 font-medium text-right">Size</th>
                  <th className="px-5 py-2 font-medium">Status</th>
                  <th className="px-5 py-2 font-medium"></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-50">
                {visible.map((exp) => (
                  <tr key={exp.exportId} className="hover:bg-gray-50">
                    <td className="px-5 py-2.5">
                      <span className="inline-flex items-center gap-1">
                        <span>{formatIconMap[exp.format] ?? '📄'}</span>
                        <span className="text-xs font-medium uppercase text-gray-700">{exp.format}</span>
                      </span>
                    </td>
                    <td className="px-5 py-2.5 text-gray-700">{exp.generatedBy}</td>
                    <td className="px-5 py-2.5 text-gray-500">{formatDate(exp.generatedAt)}</td>
                    <td className="px-5 py-2.5 text-right tabular-nums text-gray-700">{formatBytes(exp.fileSize)}</td>
                    <td className="px-5 py-2.5">
                      <StatusBadge status={exp.status} variant={variantForStatus(exp.status)} />
                    </td>
                    <td className="px-5 py-2.5">
                      {exp.status === 'Completed' && (
                        <a
                          href={downloadExportUrl(data.systemId, exp.exportId)}
                          className="inline-flex items-center gap-1 rounded-md border border-gray-300 bg-white px-2 py-1 text-xs font-medium text-gray-700 hover:bg-gray-50 transition-colors"
                          download
                        >
                          <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                            <path strokeLinecap="round" strokeLinejoin="round" d="M3 16.5v2.25A2.25 2.25 0 005.25 21h13.5A2.25 2.25 0 0021 18.75V16.5M16.5 12L12 16.5m0 0L7.5 12m4.5 4.5V3" />
                          </svg>
                          Download
                        </a>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {exportHistory.length > 5 && (
            <div className="border-t border-gray-100 px-5 py-2 text-center">
              <button
                onClick={() => setShowAll(!showAll)}
                className="text-xs text-blue-600 hover:text-blue-800 font-medium"
              >
                {showAll ? 'Show Less' : `View All ${exportHistory.length} Exports`}
              </button>
            </div>
          )}
        </>
      )}
    </div>
  );
}

function InventoryRow({ count }: { count: number }) {
  return (
    <div className="rounded-lg border border-gray-200 bg-white shadow-sm overflow-hidden">
      <SectionHeader icon="🖥️" title="Hardware & Software Inventory" />
      <div className="px-5 py-3">
        <DocRow
          label="eMASS-Compatible Inventory"
          subtitle={count > 0 ? `${count} item${count !== 1 ? 's' : ''} tracked` : 'No items registered'}
          status={count > 0 ? `${count} items` : 'Empty'}
          statusVariant={count > 0 ? 'green' : 'gray'}
        />
      </div>
    </div>
  );
}

// ─── Main Page ──────────────────────────────────────────────────────────────

export default function Documents() {
  const { id } = useParams<{ id: string }>();
  const [showExportDialog, setShowExportDialog] = useState(false);
  const [showTemplateDialog, setShowTemplateDialog] = useState(false);

  const fetcher = useCallback(() => getSystemDocuments(id!), [id]);
  const { data, loading, error } = usePolling<SystemDocumentsResponse>(fetcher, 30000);

  if (loading) {
    return <p className="text-gray-500">Loading document catalog...</p>;
  }

  if (error || !data) {
    return (
      <div className="bg-yellow-50 border border-yellow-200 rounded-lg p-4">
        <p className="text-yellow-800 font-medium">Document catalog unavailable</p>
        <p className="text-yellow-600 text-sm mt-1">Unable to load documents for this system.</p>
      </div>
    );
  }

  return (
    <>
      {/* Header */}
      <div className="mb-6">
        <h2 className="text-2xl font-bold text-gray-900">Documents</h2>
        <p className="mt-1 text-sm text-gray-500">
          Authorization package artifacts, SSP sections, narrative governance, privacy documents, and export history.
        </p>
      </div>

      {/* Phase indicator */}
      <div className="mb-6 flex items-center gap-2">
        <span className="text-xs text-gray-500">Current Phase:</span>
        <StatusBadge status={data.currentPhase} variant="blue" />
      </div>

      <div className="space-y-6">
        {/* Authorization Package */}
        <AuthPackageSection data={data} />

        {/* SSP Sections */}
        <SspSectionsSection sections={data.sspSections} activeWaiverCount={data.activeWaiverCount} />

        {/* Narrative Governance */}
        <NarrativeGovernanceSection data={data} />

        {/* Privacy */}
        <PrivacySection data={data} />

        {/* Interconnections */}
        <InterconnectionsSection interconnections={data.interconnections} />

        {/* Continuous Monitoring */}
        <ConMonSection data={data} />

        {/* Scan & Import History */}
        <ImportHistorySection imports={data.importHistory} />

        {/* Exports */}
        <ExportsSection data={data} onExportClick={() => setShowExportDialog(true)} onManageTemplates={() => setShowTemplateDialog(true)} />

        {/* Inventory */}
        <InventoryRow count={data.inventoryItemCount} />
      </div>

      {/* Export SSP Dialog */}
      {showExportDialog && (
        <ExportSspDialog
          systemId={data.systemId}
          onClose={() => setShowExportDialog(false)}
        />
      )}

      {/* Template Management Dialog */}
      {showTemplateDialog && (
        <TemplateManagementDialog
          onClose={() => setShowTemplateDialog(false)}
        />
      )}
    </>
  );
}
