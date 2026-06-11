/**
 * Wave 8 — UF-019: Admin Multi-Tenant Migration Guard.
 *
 * Guided UI for CSP-Admins to migrate a SingleTenant deployment to MultiTenant.
 * Accessible at /admin/migration.
 *
 * UF-019 requirement: the migration MUST NOT be triggerable without an explicit
 * two-factor confirmation step:
 *   1. Admin types the deployment name exactly as shown.
 *   2. Admin checks an irreversibility acknowledgement checkbox.
 * Only when BOTH are satisfied does the Confirm button become enabled.
 *
 * The POST call includes the `X-Admin-Confirm-Migration` header containing the
 * typed deployment name so the backend guard can validate it independently.
 *
 * Endpoints:
 *   GET  /api/admin/migrate-to-multitenant/preview
 *   POST /api/admin/migrate-to-multitenant
 *     Required header: X-Admin-Confirm-Migration: {deploymentName}
 */
import { useState, useEffect, useCallback, useRef } from 'react';
import PageLayout from '../components/layout/PageLayout';
import PageHero from '../components/layout/PageHero';
import apiClient from '../api/client';
import { useLoginConfig } from '../features/auth/LoginConfigContext';

const CONFIRMATION_HEADER = 'X-Admin-Confirm-Migration';

interface TablePreview {
  tableName: string;
  totalRows: number;
  rowsMissingTenant: number;
  rowsAssignedByOverride: number;
  rowsAssignedToDefault: number;
}

interface MigrationPreview {
  tables: TablePreview[];
}

interface MigrationReport {
  startedAt: string;
  completedAt: string;
  defaultTenantId: string;
  tables: Array<{
    tableName: string;
    totalRows: number;
    rowsAssignedByOverride: number;
    rowsAssignedToDefault: number;
    rowsAlreadyAssigned: number;
  }>;
  rlsInstalled: boolean;
  error: string | null;
}

export default function AdminMigrationPage() {
  const login = useLoginConfig();
  const deploymentName = login.branding.deploymentName || 'ATO Copilot';

  const [preview, setPreview] = useState<MigrationPreview | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [confirming, setConfirming] = useState(false);
  const [executing, setExecuting] = useState(false);
  const [report, setReport] = useState<MigrationReport | null>(null);
  const [execError, setExecError] = useState<string | null>(null);

  // UF-019 confirmation state
  const [typedName, setTypedName] = useState('');
  const [acknowledged, setAcknowledged] = useState(false);

  // Determine if the confirmation step is fully satisfied
  const confirmationSatisfied =
    typedName.trim().toLowerCase() === deploymentName.trim().toLowerCase() &&
    acknowledged;

  const confirmInputRef = useRef<HTMLInputElement>(null);

  const loadPreview = useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      const { data } = await apiClient.get<{ tables: TablePreview[] }>(
        '/admin/migrate-to-multitenant/preview',
      );
      setPreview(data);
    } catch (e: unknown) {
      setLoadError((e as Error).message ?? 'Failed to load migration preview.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void loadPreview(); }, [loadPreview]);

  // Auto-focus the typed-name input when the confirmation panel opens.
  useEffect(() => {
    if (confirming) {
      confirmInputRef.current?.focus();
    }
  }, [confirming]);

  // Reset confirmation state when the panel is closed.
  const cancelConfirm = () => {
    setConfirming(false);
    setTypedName('');
    setAcknowledged(false);
  };

  const handleExecute = async () => {
    if (!confirmationSatisfied) return;
    setExecuting(true);
    setExecError(null);
    try {
      const { data } = await apiClient.post<MigrationReport>(
        '/admin/migrate-to-multitenant',
        { installRls: true },
        {
          headers: {
            [CONFIRMATION_HEADER]: typedName.trim(),
          },
        },
      );
      setReport(data);
      setConfirming(false);
    } catch (e: unknown) {
      setExecError((e as Error).message ?? 'Migration failed.');
    } finally {
      setExecuting(false);
    }
  };

  const totalRowsMissingTenant = (preview?.tables ?? []).reduce(
    (sum, t) => sum + t.rowsMissingTenant,
    0,
  );
  const isAlreadyMigrated = totalRowsMissingTenant === 0 && preview !== null && !loading;

  return (
    <PageLayout title="Admin Migration">
      <PageHero
        eyebrow="Administration"
        title="Tenant Migration"
        description="Migrate this deployment from SingleTenant to MultiTenant mode. This operation is irreversible."
      />

      {loading && (
        <div className="flex items-center gap-2 text-sm text-gray-500 py-8">
          <span className="h-4 w-4 animate-spin rounded-full border-2 border-indigo-500 border-t-transparent" />
          Loading migration preview…
        </div>
      )}

      {loadError && (
        <div className="rounded border border-red-200 bg-red-50 p-4 text-sm text-red-800">{loadError}</div>
      )}

      {preview && !loading && (
        <div className="space-y-6">

          {/* Pre-migration table preview */}
          <div className="rounded-lg border border-gray-200 bg-white p-6 shadow-sm">
            <h2 className="mb-4 text-base font-semibold text-gray-900">Migration Preview</h2>
            <p className="mb-3 text-sm text-gray-600">
              Rows with a <code className="text-xs bg-gray-100 px-1 rounded">NULL TenantId</code> will
              be scoped to the default tenant after migration.
            </p>
            <table className="w-full text-sm">
              <thead>
                <tr className="text-left text-xs font-medium text-gray-500 uppercase tracking-wide border-b border-gray-100">
                  <th className="pb-2 pr-4">Table</th>
                  <th className="pb-2 pr-4 text-right">Total Rows</th>
                  <th className="pb-2 text-right">Missing Tenant</th>
                </tr>
              </thead>
              <tbody>
                {preview.tables.map((t) => (
                  <tr key={t.tableName} className="border-b border-gray-50">
                    <td className="py-1 pr-4 font-mono text-xs text-gray-700">{t.tableName}</td>
                    <td className="py-1 pr-4 text-right tabular-nums">{t.totalRows.toLocaleString()}</td>
                    <td className={`py-1 text-right tabular-nums ${t.rowsMissingTenant > 0 ? 'text-amber-700 font-medium' : 'text-gray-400'}`}>
                      {t.rowsMissingTenant.toLocaleString()}
                    </td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="border-t border-gray-200 font-semibold">
                  <td className="pt-2 pr-4 text-xs text-gray-700">Total</td>
                  <td className="pt-2 pr-4 text-right tabular-nums text-sm">
                    {preview.tables.reduce((s, t) => s + t.totalRows, 0).toLocaleString()}
                  </td>
                  <td className={`pt-2 text-right tabular-nums text-sm ${totalRowsMissingTenant > 0 ? 'text-amber-700' : 'text-green-700'}`}>
                    {totalRowsMissingTenant.toLocaleString()}
                  </td>
                </tr>
              </tfoot>
            </table>
          </div>

          {/* Already migrated */}
          {isAlreadyMigrated && !report && (
            <div className="rounded border border-green-200 bg-green-50 p-4 text-sm text-green-800">
              ✓ All rows already have a <code className="text-xs">TenantId</code>. No migration needed.
            </div>
          )}

          {/* Success */}
          {report && !report.error && (
            <div className="rounded border border-green-300 bg-green-50 p-4 text-sm text-green-800 space-y-1">
              <p className="font-semibold">✓ Migration successful</p>
              <p>Completed at {new Date(report.completedAt).toLocaleString()}</p>
              {report.rlsInstalled && <p>Row-Level Security policies installed.</p>}
            </div>
          )}

          {execError && (
            <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">{execError}</div>
          )}

          {/* Migration action — only show if there's actual work to do and migration hasn't succeeded */}
          {!isAlreadyMigrated && !report && (
            <>
              {!confirming ? (
                <button
                  onClick={() => setConfirming(true)}
                  className="rounded-md bg-red-600 px-4 py-2 text-sm font-medium text-white hover:bg-red-700"
                >
                  Migrate to MultiTenant…
                </button>
              ) : (
                /* ── UF-019 typed-confirmation guard ── */
                <div
                  className="rounded-lg border-2 border-red-300 bg-red-50 p-6 space-y-5"
                  role="dialog"
                  aria-labelledby="migration-confirm-title"
                  aria-describedby="migration-confirm-desc"
                >
                  <div>
                    <h3
                      id="migration-confirm-title"
                      className="text-base font-semibold text-red-900"
                    >
                      ⚠️ Confirm irreversible migration
                    </h3>
                    <p id="migration-confirm-desc" className="mt-1 text-sm text-red-700">
                      This will permanently convert the deployment to MultiTenant mode and scope
                      all existing data. <strong>This cannot be reversed.</strong>
                    </p>
                  </div>

                  {/* Step 1 — type the deployment name */}
                  <div>
                    <label
                      htmlFor="migration-confirm-name"
                      className="block text-sm font-medium text-red-800 mb-1"
                    >
                      Type the deployment name to confirm:{' '}
                      <code className="bg-red-100 px-1 rounded">{deploymentName}</code>
                    </label>
                    <input
                      id="migration-confirm-name"
                      ref={confirmInputRef}
                      type="text"
                      value={typedName}
                      onChange={(e) => setTypedName(e.target.value)}
                      placeholder={deploymentName}
                      autoComplete="off"
                      spellCheck={false}
                      disabled={executing}
                      className="w-full max-w-sm rounded border border-red-300 bg-white px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-red-400 disabled:opacity-50"
                      aria-invalid={typedName.length > 0 && !confirmationSatisfied && !acknowledged ? 'true' : 'false'}
                    />
                    {typedName.length > 0 &&
                      typedName.trim().toLowerCase() !== deploymentName.trim().toLowerCase() && (
                        <p className="mt-1 text-xs text-red-600">
                          Name does not match. Type exactly: <strong>{deploymentName}</strong>
                        </p>
                      )}
                  </div>

                  {/* Step 2 — acknowledge irreversibility */}
                  <div className="flex items-start gap-3">
                    <input
                      id="migration-ack"
                      type="checkbox"
                      checked={acknowledged}
                      onChange={(e) => setAcknowledged(e.target.checked)}
                      disabled={executing}
                      className="mt-0.5 h-4 w-4 accent-red-600 cursor-pointer disabled:opacity-50"
                    />
                    <label htmlFor="migration-ack" className="text-sm text-red-800 cursor-pointer select-none">
                      I understand this migration is <strong>irreversible</strong>. I have reviewed
                      the preview above and confirmed the row counts are as expected.
                    </label>
                  </div>

                  {/* Actions */}
                  <div className="flex gap-3">
                    <button
                      onClick={() => void handleExecute()}
                      disabled={!confirmationSatisfied || executing}
                      aria-disabled={!confirmationSatisfied || executing}
                      className="inline-flex items-center gap-2 rounded-md bg-red-700 px-4 py-2 text-sm font-medium text-white hover:bg-red-800 disabled:opacity-40 disabled:cursor-not-allowed"
                    >
                      {executing ? (
                        <>
                          <span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
                          Migrating…
                        </>
                      ) : (
                        'Confirm Migration'
                      )}
                    </button>
                    <button
                      onClick={cancelConfirm}
                      disabled={executing}
                      className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                    >
                      Cancel
                    </button>
                  </div>
                </div>
              )}
            </>
          )}
        </div>
      )}
    </PageLayout>
  );
}
