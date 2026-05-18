/**
 * Feature 048 (T074): Tenant picker dropdown for the dashboard header.
 *
 * Visibility (FR-041 / FR-042):
 *  - Hidden when deployment mode is `SingleTenant`.
 *  - Hidden when the caller is not in the `CSP.Admin` group (we detect
 *    this by attempting `GET /api/tenants` once: 200 → CSP-Admin, 401/403
 *    → silently hide).
 *
 * On selection the component starts impersonation, refreshes the impersonation
 * banner (via the `ato:impersonation-changed` event from `api.ts`), and reloads
 * the route so dependent caches re-fetch with the new scope.
 */

import { useEffect, useMemo, useRef, useState } from 'react';
import {
  type DeploymentMode,
  type Tenant,
  endImpersonation,
  getDeploymentMode,
  listTenants,
  onImpersonationChanged,
  readImpersonation,
  startImpersonation,
} from './api';
import { isVestigeTenant } from './vestigeTenants';

type LoadState = 'loading' | 'visible' | 'hidden';

export default function TenantPicker() {
  const [loadState, setLoadState] = useState<LoadState>('loading');
  const [mode, setMode] = useState<DeploymentMode>('SingleTenant');
  const [tenants, setTenants] = useState<Tenant[]>([]);
  const [open, setOpen] = useState(false);
  const [busyTenantId, setBusyTenantId] = useState<string | null>(null);
  const [activeTenantId, setActiveTenantId] = useState<string | null>(
    () => readImpersonation()?.tenantId ?? null,
  );
  const ref = useRef<HTMLDivElement>(null);

  // Re-render when impersonation state changes elsewhere (e.g. banner dismiss).
  useEffect(() => {
    return onImpersonationChanged(() => {
      setActiveTenantId(readImpersonation()?.tenantId ?? null);
    });
  }, []);

  // Probe deployment mode + CSP-Admin membership exactly once on mount.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const deployment = await getDeploymentMode();
        if (cancelled) return;
        if (deployment.mode !== 'MultiTenant') {
          setMode(deployment.mode);
          setLoadState('hidden');
          return;
        }
        setMode('MultiTenant');
        try {
          const result = await listTenants({ pageSize: 200 });
          if (cancelled) return;
          // Filter out the system tenant AND the FR-070 default tenant
          // — both are bootstrap rows, not real impersonation targets.
          // See `vestigeTenants.ts` for the rationale.
          setTenants(result.items.filter((t) => !isVestigeTenant(t.id)));
          setLoadState('visible');
        } catch {
          // 401/403 → not CSP-Admin → hide silently.
          if (!cancelled) setLoadState('hidden');
        }
      } catch {
        if (!cancelled) setLoadState('hidden');
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  // Close on outside click.
  useEffect(() => {
    if (!open) return;
    const handle = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener('mousedown', handle);
    return () => document.removeEventListener('mousedown', handle);
  }, [open]);

  const activeTenant = useMemo(
    () => tenants.find((t) => t.id === activeTenantId) ?? null,
    [tenants, activeTenantId],
  );

  if (loadState !== 'visible' || mode !== 'MultiTenant') {
    return null;
  }

  const handleSelect = async (tenant: Tenant) => {
    if (tenant.id === activeTenantId) {
      setOpen(false);
      return;
    }
    setBusyTenantId(tenant.id);
    try {
      await startImpersonation(tenant.id, tenant.displayName);
      setActiveTenantId(tenant.id);
      setOpen(false);
      // Reload so per-route data refetches under the new tenant scope.
      window.location.reload();
    } catch {
      // Keep dropdown open; user can retry. A toast would be nicer once
      // the dashboard wires a global notification surface.
    } finally {
      setBusyTenantId(null);
    }
  };

  const handleClear = async () => {
    setBusyTenantId('__clear__');
    try {
      await endImpersonation();
      setActiveTenantId(null);
      setOpen(false);
      window.location.reload();
    } finally {
      setBusyTenantId(null);
    }
  };

  const label = activeTenant
    ? activeTenant.displayName
    : tenants.length === 0
      ? 'No organizations'
      : 'All organizations';

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen(!open)}
        className={`flex items-center gap-1.5 rounded-md border px-2.5 py-1 text-xs font-medium transition-colors ${
          activeTenantId
            ? 'border-purple-300 bg-purple-50 text-purple-700 hover:bg-purple-100'
            : 'border-gray-300 bg-white text-gray-700 hover:bg-gray-50'
        }`}
        title={activeTenant ? `Impersonating ${activeTenant.displayName}` : 'Switch organization scope'}
        aria-expanded={open}
        aria-haspopup="listbox"
        data-testid="tenant-picker"
      >
        <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            d="M3.75 21h16.5M4.5 3h15M5.25 3v18m13.5-18v18M9 6.75h1.5m-1.5 3h1.5m-1.5 3h1.5m4.5-6h1.5m-1.5 3h1.5m-1.5 3h1.5"
          />
        </svg>
        <span className="max-w-[180px] truncate">{label}</span>
        <svg
          className={`h-3.5 w-3.5 text-gray-400 transition-transform ${open ? 'rotate-180' : ''}`}
          fill="none"
          viewBox="0 0 24 24"
          stroke="currentColor"
          strokeWidth={2}
        >
          <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 8.25l-7.5 7.5-7.5-7.5" />
        </svg>
      </button>

      {open && (
        <div
          className="absolute right-0 z-50 mt-1 max-h-96 w-72 overflow-y-auto rounded-lg border border-gray-200 bg-white py-1 shadow-lg"
          role="listbox"
        >
          <div className="border-b border-gray-100 px-3 py-2">
            <p className="text-[10px] font-semibold uppercase tracking-wide text-purple-600">
              CSP.Admin · Organization Scope
            </p>
            <p className="mt-0.5 text-xs text-gray-500">
              Impersonation is audit-logged with both your identity and the target organization.
            </p>
          </div>

          {tenants.length === 0 ? (
            <p className="px-3 py-3 text-xs text-gray-500">No organizations registered.</p>
          ) : (
            tenants.map((tenant) => (
              <button
                key={tenant.id}
                type="button"
                role="option"
                aria-selected={tenant.id === activeTenantId}
                disabled={busyTenantId !== null}
                onClick={() => handleSelect(tenant)}
                className={`flex w-full items-center gap-3 px-3 py-2 text-left text-sm transition-colors ${
                  tenant.id === activeTenantId
                    ? 'bg-purple-50 text-purple-700'
                    : 'text-gray-700 hover:bg-gray-50'
                } disabled:cursor-not-allowed disabled:opacity-60`}
              >
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-1.5 font-medium">
                    <span className="truncate">{tenant.displayName}</span>
                    {tenant.status !== 'Active' && (
                      <span className="rounded bg-amber-100 px-1.5 py-0.5 text-[10px] font-semibold uppercase text-amber-700">
                        {tenant.status}
                      </span>
                    )}
                  </div>
                  {tenant.doDComponent && (
                    <div className="text-xs text-gray-400">{tenant.doDComponent}</div>
                  )}
                </div>
                {tenant.id === activeTenantId && (
                  <svg
                    className="h-4 w-4 flex-shrink-0 text-purple-600"
                    fill="none"
                    viewBox="0 0 24 24"
                    stroke="currentColor"
                    strokeWidth={2.5}
                  >
                    <path strokeLinecap="round" strokeLinejoin="round" d="M4.5 12.75l6 6 9-13.5" />
                  </svg>
                )}
              </button>
            ))
          )}

          {activeTenantId && (
            <>
              <div className="mx-3 my-1 border-t border-gray-100" />
              <button
                type="button"
                onClick={handleClear}
                disabled={busyTenantId !== null}
                className="flex w-full items-center gap-3 px-3 py-2 text-left text-sm text-gray-500 hover:bg-gray-50 disabled:opacity-60"
              >
                Clear impersonation
              </button>
            </>
          )}
        </div>
      )}
    </div>
  );
}
