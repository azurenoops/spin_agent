/**
 * Wave 6 — GAP-017: Admin Migration page.
 *
 * Guided UI for CSP-Admins to migrate a SingleTenant deployment to MultiTenant.
 * Accessible at /admin/migration.
 * Endpoints:
 *   GET  /api/admin/migrate-to-multitenant/preview
 *   POST /api/admin/migrate-to-multitenant
 */
import { useState, useEffect, useCallback } from 'react';
import PageLayout from '../components/layout/PageLayout';
import PageHero from '../components/layout/PageHero';
import apiClient from '../api/client';

interface MigrationPreview {
  currentMode: 'SingleTenant' | 'MultiTenant';
  defaultTenantId: string | null;
  defaultTenantName: string | null;
  warnings: string[];
  canMigrate: boolean;
  reason: string | null;
}

interface MigrationResult {
  success: boolean;
  newMode: string;
  message: string;
  migratedAt: string | null;
}

export default function AdminMigrationPage() {
  const [preview, setPreview] = useState<MigrationPreview | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [confirming, setConfirming] = useState(false);
  const [executing, setExecuting] = useState(false);
  const [result, setResult] = useState<MigrationResult | null>(null);
  const [execError, setExecError] = useState<string | null>(null);

  const loadPreview = useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      const { data } = await apiClient.get<MigrationPreview>(
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

  const handleExecute = async () => {
    setExecuting(true);
    setExecError(null);
    try {
      const { data } = await apiClient.post<MigrationResult>(
        '/admin/migrate-to-multitenant',
        { acknowledgedWarnings: true },
      );
      setResult(data);
      setConfirming(false);
    } catch (e: unknown) {
      setExecError((e as Error).message ?? 'Migration failed.');
    } finally {
      setExecuting(false);
    }
  };

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
          <div className="rounded-lg border border-gray-200 bg-white p-6 shadow-sm">
            <h2 className="mb-4 text-base font-semibold text-gray-900">Current Deployment State</h2>
            <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
              <div>
                <p className="text-xs font-medium uppercase tracking-wide text-gray-500">Mode</p>
                <p className="mt-1 text-lg font-bold text-gray-900">{preview.currentMode}</p>
              </div>
              {preview.defaultTenantName && (
                <div>
                  <p className="text-xs font-medium uppercase tracking-wide text-gray-500">Default Tenant</p>
                  <p className="mt-1 text-sm font-medium text-gray-900">{preview.defaultTenantName}</p>
                  <p className="text-xs text-gray-400 font-mono">{preview.defaultTenantId}</p>
                </div>
              )}
              <div>
                <p className="text-xs font-medium uppercase tracking-wide text-gray-500">Can Migrate</p>
                <p className={`mt-1 text-sm font-bold ${preview.canMigrate ? 'text-green-700' : 'text-red-700'}`}>
                  {preview.canMigrate ? '✓ Yes' : '✗ No'}
                </p>
                {preview.reason && <p className="text-xs text-gray-500 mt-0.5">{preview.reason}</p>}
              </div>
            </div>
          </div>

          {preview.currentMode === 'MultiTenant' && (
            <div className="rounded border border-green-200 bg-green-50 p-4 text-sm text-green-800">
              ✓ This deployment is already running in MultiTenant mode. No migration needed.
            </div>
          )}

          {result && (
            <div className={`rounded border p-4 text-sm ${result.success ? 'border-green-300 bg-green-50 text-green-800' : 'border-red-200 bg-red-50 text-red-800'}`}>
              {result.success
                ? `✓ Migration successful. Deployment is now ${result.newMode}. ${result.message}`
                : `Migration failed: ${result.message}`}
            </div>
          )}

          {preview.warnings.length > 0 && (
            <div className="rounded border border-amber-200 bg-amber-50 p-4">
              <p className="mb-2 text-sm font-semibold text-amber-800">Warnings</p>
              <ul className="list-disc list-inside space-y-1 text-sm text-amber-700">
                {preview.warnings.map((w, i) => <li key={i}>{w}</li>)}
              </ul>
            </div>
          )}

          {execError && (
            <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">{execError}</div>
          )}

          {preview.canMigrate && preview.currentMode !== 'MultiTenant' && !result?.success && (
            <>
              {!confirming ? (
                <button
                  onClick={() => setConfirming(true)}
                  className="rounded-md bg-red-600 px-4 py-2 text-sm font-medium text-white hover:bg-red-700"
                >
                  Migrate to MultiTenant…
                </button>
              ) : (
                <div className="rounded-lg border-2 border-red-300 bg-red-50 p-5 space-y-4">
                  <p className="text-sm font-semibold text-red-900">
                    ⚠️ This will permanently convert the deployment to MultiTenant mode.
                    This action <strong>cannot be reversed</strong>.
                  </p>
                  <p className="text-sm text-red-700">
                    All existing data will be scoped to tenant{' '}
                    <strong>{preview.defaultTenantName ?? preview.defaultTenantId}</strong>.
                    Confirm you have reviewed all warnings above.
                  </p>
                  <div className="flex gap-3">
                    <button
                      onClick={() => void handleExecute()}
                      disabled={executing}
                      className="inline-flex items-center gap-2 rounded-md bg-red-700 px-4 py-2 text-sm font-medium text-white hover:bg-red-800 disabled:opacity-50"
                    >
                      {executing ? (
                        <><span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />Migrating…</>
                      ) : 'Confirm: Migrate Now'}
                    </button>
                    <button
                      onClick={() => setConfirming(false)}
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
