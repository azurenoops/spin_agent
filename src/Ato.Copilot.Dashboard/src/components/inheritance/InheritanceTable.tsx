import { useState } from 'react';
import type { InheritanceDesignation, InheritanceListQuery } from '../../types/inheritance';
import { KNOWN_PROVIDERS } from './constants';

// ─── Badges ─────────────────────────────────────────────────────────────────

function TypeBadge({ type }: { type: string }) {
  const map: Record<string, string> = {
    Inherited: 'bg-green-100 text-green-700',
    Shared: 'bg-indigo-100 text-indigo-700',
    Customer: 'bg-amber-100 text-amber-700',
    Undesignated: 'bg-gray-100 text-gray-500',
  };
  return <span className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${map[type] ?? 'bg-gray-100 text-gray-700'}`}>{type}</span>;
}

function SourceBadge({ source }: { source: string | null }) {
  if (!source || source === 'Manual') return null;
  const map: Record<string, string> = {
    OrgDerived: 'bg-teal-100 text-teal-700',
    OrgPropagation: 'bg-teal-100 text-teal-700',
    ProfileApply: 'bg-purple-100 text-purple-700',
    CrmImport: 'bg-sky-100 text-sky-700',
    BulkUpdate: 'bg-gray-100 text-gray-600',
  };
  const labels: Record<string, string> = {
    OrgDerived: 'Org Default',
    OrgPropagation: 'Org Default',
    ProfileApply: 'CSP Profile',
    CrmImport: 'CRM Import',
    BulkUpdate: 'Bulk',
  };
  return (
    <span className={`ml-1 inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${map[source] ?? 'bg-gray-100 text-gray-600'}`}>
      {labels[source] ?? source}
    </span>
  );
}

/**
 * Feature 048 (T137, FR-083): renders the canonical source-location label
 * `Source: Global Baseline` or `Source: <Tenant.DisplayName>` for an
 * inheritance row. Falls back to nothing when the wire payload lacks both
 * provenance fields (older backends), preserving backward compatibility.
 *
 * Pattern (a) — `Source: <CspProfile.DisplayName> (Inherited from CSP)` —
 * is owned by Phase 16 (T229) and intentionally not handled here.
 */
function SourceLocationLabel({ item }: { item: InheritanceDesignation }) {
  if (item.isGlobalBaseline) {
    return (
      <span
        className="ml-1 inline-flex rounded-full bg-blue-50 px-2 py-0.5 text-xs font-medium text-blue-700 ring-1 ring-inset ring-blue-200"
        title="This control's inheritance default was published as a global baseline by a CSP-Admin."
      >
        Source: Global Baseline
      </span>
    );
  }
  if (item.orgDisplayName) {
    return (
      <span
        className="ml-1 inline-flex rounded-full bg-slate-50 px-2 py-0.5 text-xs font-medium text-slate-600 ring-1 ring-inset ring-slate-200"
        title="This control's inheritance is owned by your tenant."
      >
        Source: {item.orgDisplayName}
      </span>
    );
  }
  return null;
}

function OrgDefaultTooltip({ item }: { item: InheritanceDesignation }) {
  const ds = item.designationSource;
  const org = item.orgDefault;
  if (!ds && !org) return null;

  const isOverride = ds === 'Manual' && org;
  const isOrgDerived = ds === 'OrgDerived';

  if (isOverride && org) {
    return (
      <span className="ml-1 inline-flex cursor-help items-center" title={`Override: Org default is ${org.inheritanceType} (from ${org.sourceCapabilities ?? 'unknown'})`}>
        <svg className="h-3.5 w-3.5 text-amber-500" fill="currentColor" viewBox="0 0 20 20">
          <path d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" />
        </svg>
      </span>
    );
  }

  if (isOrgDerived && org) {
    return (
      <span className="ml-1 inline-flex cursor-help items-center" title={`Org default from ${org.sourceCapabilities ?? 'org-level derivation'} (${org.mappingRole ?? ''})`}>
        <svg className="h-3.5 w-3.5 text-teal-500" fill="currentColor" viewBox="0 0 20 20">
          <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd" />
        </svg>
      </span>
    );
  }

  return null;
}

// ─── Inline Editing ─────────────────────────────────────────────────────────

interface InlineEditState {
  controlId: string;
  inheritanceType: string;
  provider: string;
  customerResponsibility: string;
}

// ─── Component ──────────────────────────────────────────────────────────────

interface InheritanceTableProps {
  items: InheritanceDesignation[];
  totalItems: number;
  query: InheritanceListQuery;
  loading: boolean;
  selectedIds: Set<string>;
  onQueryChange: (updater: (prev: InheritanceListQuery) => InheritanceListQuery) => void;
  onRowClick: (item: InheritanceDesignation) => void;
  onToggleSelect: (controlId: string) => void;
  onToggleSelectAll: () => void;
  onSave: (edit: { controlId: string; inheritanceType: string; provider?: string; customerResponsibility?: string }) => void;
}

export default function InheritanceTable({
  items, totalItems, query, loading, selectedIds,
  onQueryChange, onRowClick, onToggleSelect, onToggleSelectAll, onSave,
}: InheritanceTableProps) {
  const [editing, setEditing] = useState<InlineEditState | null>(null);

  const startEdit = (item: InheritanceDesignation) => {
    setEditing({
      controlId: item.controlId,
      inheritanceType: item.inheritanceType,
      provider: item.provider ?? '',
      customerResponsibility: item.customerResponsibility ?? '',
    });
  };

  const cancelEdit = () => setEditing(null);

  const saveEdit = () => {
    if (!editing) return;
    onSave({
      controlId: editing.controlId,
      inheritanceType: editing.inheritanceType,
      provider: editing.provider || undefined,
      customerResponsibility: editing.customerResponsibility || undefined,
    });
    setEditing(null);
  };

  const allSelected = items.length > 0 && items.every(i => selectedIds.has(i.controlId));

  return (
    <div className="overflow-hidden rounded-xl border border-gray-200 bg-white shadow-sm">
      {/* Filter bar */}
      <div className="flex flex-wrap items-center gap-3 border-b border-gray-200 px-4 py-3">
        <input
          type="text"
          placeholder="Search control ID or provider..."
          className="w-64 rounded-lg border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          value={query.search ?? ''}
          onChange={e => onQueryChange(q => ({ ...q, search: e.target.value || undefined, page: 1 }))}
        />
        <select
          className="rounded-lg border border-gray-300 px-3 py-1.5 text-sm"
          value={query.family ?? ''}
          onChange={e => onQueryChange(q => ({ ...q, family: e.target.value || undefined, page: 1 }))}
        >
          <option value="">All Families</option>
          {['AC','AT','AU','CA','CM','CP','IA','IR','MA','MP','PE','PL','PM','PS','PT','RA','SA','SC','SI','SR'].map(f => (
            <option key={f} value={f}>{f}</option>
          ))}
        </select>
        <select
          className="rounded-lg border border-gray-300 px-3 py-1.5 text-sm"
          value={query.inheritanceType ?? ''}
          onChange={e => onQueryChange(q => ({ ...q, inheritanceType: e.target.value || undefined, page: 1 }))}
        >
          <option value="">All Types</option>
          <option value="Inherited">Inherited</option>
          <option value="Shared">Shared</option>
          <option value="Customer">Customer</option>
          <option value="Undesignated">Undesignated</option>
        </select>
        <select
          className="rounded-lg border border-gray-300 px-3 py-1.5 text-sm"
          value={query.source ?? ''}
          onChange={e => onQueryChange(q => ({ ...q, source: (e.target.value || undefined) as InheritanceListQuery['source'], page: 1 }))}
        >
          <option value="">All Sources</option>
          <option value="org">Org Defaults Only</option>
          <option value="override">System Overrides Only</option>
          <option value="undesignated">Undesignated</option>
        </select>
      </div>

      {/* Table */}
      {loading && items.length === 0 ? (
        <div className="flex items-center justify-center py-20 text-gray-400">Loading inheritance data...</div>
      ) : items.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-20 text-gray-400">
          <p className="text-sm font-medium">No controls found</p>
          <p className="mt-1 text-xs">Adjust filters or select a baseline first</p>
        </div>
      ) : (
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="w-10 px-4 py-3">
                  <input type="checkbox" className="rounded border-gray-300" checked={allSelected} onChange={onToggleSelectAll} />
                </th>
                {['Control ID', 'Family', 'Inheritance Type', 'Provider', 'Customer Responsibility', 'Set By', 'Set At', ''].map(h => (
                  <th key={h} className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200 bg-white">
              {items.map(item => {
                const isEditing = editing?.controlId === item.controlId;
                return (
                  <tr key={item.controlId} className="hover:bg-gray-50">
                    <td className="px-4 py-3">
                      <input
                        type="checkbox"
                        className="rounded border-gray-300"
                        checked={selectedIds.has(item.controlId)}
                        onChange={() => onToggleSelect(item.controlId)}
                      />
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-sm font-medium text-gray-900">
                      <button className="text-indigo-600 hover:underline" onClick={() => onRowClick(item)}>
                        {item.controlId}
                      </button>
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-sm text-gray-600">{item.family}</td>

                    {isEditing ? (
                      <>
                        <td className="px-4 py-2">
                          <select
                            className="w-full rounded border border-gray-300 px-2 py-1 text-sm"
                            value={editing.inheritanceType}
                            onChange={e => setEditing({ ...editing, inheritanceType: e.target.value })}
                          >
                            <option value="Inherited">Inherited</option>
                            <option value="Shared">Shared</option>
                            <option value="Customer">Customer</option>
                          </select>
                        </td>
                        <td className="px-4 py-2">
                          <input
                            list="known-providers"
                            className="w-full rounded border border-gray-300 px-2 py-1 text-sm"
                            value={editing.provider}
                            onChange={e => setEditing({ ...editing, provider: e.target.value })}
                            placeholder="Provider name"
                          />
                          <datalist id="known-providers">
                            {KNOWN_PROVIDERS.map(p => <option key={p} value={p} />)}
                          </datalist>
                        </td>
                        <td className="px-4 py-2">
                          <input
                            className="w-full rounded border border-gray-300 px-2 py-1 text-sm"
                            value={editing.customerResponsibility}
                            onChange={e => setEditing({ ...editing, customerResponsibility: e.target.value })}
                            placeholder="Customer responsibility"
                          />
                        </td>
                        <td />
                        <td />
                        <td className="whitespace-nowrap px-4 py-2 text-sm">
                          <button onClick={saveEdit} className="mr-2 rounded bg-indigo-600 px-2 py-1 text-xs text-white hover:bg-indigo-700">Save</button>
                          <button onClick={cancelEdit} className="rounded border border-gray-300 px-2 py-1 text-xs text-gray-600 hover:bg-gray-100">Cancel</button>
                        </td>
                      </>
                    ) : (
                      <>
                        <td className="whitespace-nowrap px-4 py-3 text-sm">
                          <TypeBadge type={item.inheritanceType} />
                          <SourceBadge source={item.designationSource} />
                          <OrgDefaultTooltip item={item} />
                          <SourceLocationLabel item={item} />
                        </td>
                        <td className="max-w-xs truncate px-4 py-3 text-sm text-gray-600">{item.provider ?? '—'}</td>
                        <td className="max-w-xs truncate px-4 py-3 text-sm text-gray-600">{item.customerResponsibility ?? '—'}</td>
                        <td className="whitespace-nowrap px-4 py-3 text-sm text-gray-500">{item.setBy ?? '—'}</td>
                        <td className="whitespace-nowrap px-4 py-3 text-sm text-gray-500">
                          {item.setAt ? new Date(item.setAt).toLocaleDateString() : '—'}
                        </td>
                        <td className="whitespace-nowrap px-4 py-3 text-sm">
                          <button onClick={() => startEdit(item)} className="text-indigo-600 hover:underline text-xs">Edit</button>
                        </td>
                      </>
                    )}
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {/* Pagination */}
      {totalItems > 0 && (
        <div className="flex items-center justify-between border-t border-gray-200 px-4 py-3">
          <div className="text-sm text-gray-500">
            Showing {((query.page ?? 1) - 1) * (query.pageSize ?? 50) + 1}–{Math.min((query.page ?? 1) * (query.pageSize ?? 50), totalItems)} of {totalItems}
          </div>
          <div className="flex items-center gap-2">
            <select
              className="rounded border border-gray-300 px-2 py-1 text-sm"
              value={query.pageSize ?? 50}
              onChange={e => onQueryChange(q => ({ ...q, pageSize: Number(e.target.value), page: 1 }))}
            >
              <option value={25}>25</option>
              <option value={50}>50</option>
              <option value={100}>100</option>
              <option value={200}>200</option>
            </select>
            <button
              disabled={(query.page ?? 1) === 1}
              onClick={() => onQueryChange(q => ({ ...q, page: (q.page ?? 1) - 1 }))}
              className="rounded border border-gray-300 px-3 py-1 text-sm disabled:opacity-50"
            >
              Prev
            </button>
            <button
              disabled={(query.page ?? 1) * (query.pageSize ?? 50) >= totalItems}
              onClick={() => onQueryChange(q => ({ ...q, page: (q.page ?? 1) + 1 }))}
              className="rounded border border-gray-300 px-3 py-1 text-sm disabled:opacity-50"
            >
              Next
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
