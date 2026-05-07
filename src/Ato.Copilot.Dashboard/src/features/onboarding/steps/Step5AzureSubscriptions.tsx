import { useEffect, useState } from 'react';
import { onboarding } from '../api/onboardingApi';
import type {
  AzureSubscriptionInfoDto,
  AzureSubscriptionRegistrationDto,
} from '../api/onboardingApi';

/**
 * Step 5 — Azure Subscriptions (User Story 5 / FR-070..FR-077).
 *
 * Workflow: enumerate the user's visible subscriptions via delegated ARM token,
 * surface a "Connect Azure" CTA on `WIZARD_ARM_CONSENT_REQUIRED`, let the admin
 * pick a subset, and PUT the selection so subsequent Azure-touching features
 * auto-scope to those subscriptions.
 */
interface Props {
  onSaved?: () => void;
}

export default function Step5AzureSubscriptions({ onSaved }: Props) {
  const [available, setAvailable] = useState<AzureSubscriptionInfoDto[]>([]);
  const [registered, setRegistered] = useState<AzureSubscriptionRegistrationDto[]>([]);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [consentRequired, setConsentRequired] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void loadAll();
  }, []);

  async function loadAll() {
    setLoading(true);
    setError(null);
    try {
      const [subs, regs] = await Promise.all([
        onboarding.listAzureSubscriptions().catch((e: unknown) => {
          const err = e as { errorCode?: string };
          if (err.errorCode === 'WIZARD_ARM_CONSENT_REQUIRED') {
            setConsentRequired(true);
            return [] as AzureSubscriptionInfoDto[];
          }
          throw e;
        }),
        onboarding.listAzureRegistrations(),
      ]);
      setAvailable(subs);
      setRegistered(regs);
      setSelected(new Set(regs.filter((r) => r.status === 'Selected').map((r) => r.subscriptionId)));
    } catch (e: unknown) {
      const err = e as { errorCode?: string; message?: string };
      setError(`${err.errorCode ?? 'LOAD_FAILED'}: ${err.message ?? 'Failed to load subscriptions'}`);
    } finally {
      setLoading(false);
    }
  }

  function toggle(id: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  async function handleSave() {
    setBusy(true);
    setError(null);
    try {
      const rows = await onboarding.putAzureRegistrations(Array.from(selected));
      setRegistered(rows);
      onSaved?.();
    } catch (e: unknown) {
      const err = e as { errorCode?: string; message?: string };
      setError(`${err.errorCode ?? 'SAVE_FAILED'}: ${err.message ?? 'Save failed'}`);
    } finally {
      setBusy(false);
    }
  }

  if (loading) {
    return <p className="text-sm text-gray-600">Loading subscriptions…</p>;
  }

  if (consentRequired) {
    return (
      <section className="space-y-4">
        <h2 className="text-xl font-semibold">Step 3 — Azure subscriptions</h2>
        <div className="rounded border border-amber-300 bg-amber-50 p-4 text-sm text-amber-900">
          <p className="font-medium">Azure consent required</p>
          <p className="mt-1">
            Approve the Azure Resource Manager (ARM) <code>user_impersonation</code> scope
            so the wizard can list the subscriptions you can see.
          </p>
          {error && (
            <div role="alert" className="mt-3 rounded border border-red-300 bg-red-50 p-3 text-xs text-red-800">
              {error}
            </div>
          )}
          <button
            type="button"
            disabled={busy}
            className="mt-3 rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
            onClick={async () => {
              // Retry enumeration. The MCP server now uses the configured
              // service principal (or DefaultAzureCredential) instead of a
              // null stub, so a successful retry replaces the consent banner
              // with the real subscription picker. If the credential is still
              // unavailable, the underlying error is surfaced inline.
              setBusy(true);
              setError(null);
              try {
                setConsentRequired(false);
                await loadAll();
              } finally {
                setBusy(false);
              }
            }}
          >
            {busy ? 'Connecting…' : 'Connect Azure'}
          </button>
        </div>
      </section>
    );
  }

  return (
    <section className="space-y-4">
      <header>
        <h2 className="text-xl font-semibold">Step 3 — Azure subscriptions</h2>
        <p className="text-sm text-gray-600">
          Select the subscriptions to bring under the wizard's scope. Subsequent
          Azure features (Policy, Defender, JIT, inventory, assessments) auto-scope
          to your selection without re-prompting.
        </p>
      </header>

      {error && (
        <div role="alert" className="rounded border border-red-300 bg-red-50 p-3 text-sm text-red-800">
          {error}
        </div>
      )}

      {available.length === 0 ? (
        <p className="text-sm text-gray-600">
          No subscriptions are visible to your account. Skip this step or contact an
          Azure administrator to grant ARM read access.
        </p>
      ) : (
        <table className="min-w-full divide-y divide-gray-200 text-sm">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-3 py-2 text-left">Select</th>
              <th className="px-3 py-2 text-left">Display name</th>
              <th className="px-3 py-2 text-left">Subscription ID</th>
              <th className="px-3 py-2 text-left">Cloud</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {available.map((s) => (
              <tr key={s.subscriptionId}>
                <td className="px-3 py-2">
                  <input
                    type="checkbox"
                    checked={selected.has(s.subscriptionId)}
                    onChange={() => toggle(s.subscriptionId)}
                  />
                </td>
                <td className="px-3 py-2">{s.displayName}</td>
                <td className="px-3 py-2 font-mono text-xs">{s.subscriptionId}</td>
                <td className="px-3 py-2">{s.environment}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {registered.some((r) => r.status === 'Unavailable') && (
        <div className="rounded border border-amber-300 bg-amber-50 p-3 text-xs text-amber-900">
          The following previously-selected subscriptions are no longer visible to
          you and remain flagged Unavailable:
          <ul className="mt-1 list-disc pl-4">
            {registered
              .filter((r) => r.status === 'Unavailable')
              .map((r) => (
                <li key={r.id}>
                  {r.displayName} ({r.subscriptionId})
                </li>
              ))}
          </ul>
        </div>
      )}

      <div className="flex gap-2">
        <button
          type="button"
          onClick={handleSave}
          disabled={busy || selected.size === 0}
          className="rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
        >
          Save selection ({selected.size})
        </button>
      </div>
    </section>
  );
}
