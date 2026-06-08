import { useState, useEffect } from 'react';
import { useParams, useSearchParams, Link } from 'react-router-dom';
import PageLayout from '../components/layout/PageLayout';
import PageHero from '../components/layout/PageHero';
import apiClient from '../api/client';

// ─── Types ────────────────────────────────────────────────────────────────────

interface CapabilityDetail {
  id: string;
  name: string;
  description: string;
  provider: string;
  componentName: string;
  mappedControls: string[];
  mappingConfidence: number | null;
  reviewerNote: string | null;
  reviewedBy: string | null;
  reviewedAt: string | null;
  isSubscribed: boolean;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

// Derive a human-readable inheritance type label from a control ID.
// In the current data model, CspInheritedCapability stores control IDs as a flat list.
// The inheritance type (Inherited / Shared / Customer Responsible) would come from
// CapabilityControlMapping.Role — for now we default to Inherited for all capability
// controls and note this is a future enrichment once per-control inheritance types
// are plumbed through the org-user API.
function inferInheritanceType(_controlId: string): 'Inherited' | 'Shared' | 'Customer Responsible' {
  return 'Inherited';
}

const INHERITANCE_BADGE: Record<string, string> = {
  Inherited: 'bg-green-100 text-green-800',
  Shared: 'bg-yellow-100 text-yellow-800',
  'Customer Responsible': 'bg-red-100 text-red-800',
};

// ─── Page ─────────────────────────────────────────────────────────────────────

/**
 * UF-CSP-02 — View Capability Control Coverage Detail (spec-070 T017–T018).
 *
 * Shows a single CSP capability's full control coverage table for an org user
 * (ISSO/SCA) to evaluate before subscribing.
 *
 * Route: /capability-library/:capabilityId?systemId=<id>
 */
export default function OrgCapabilityDetailPage() {
  const { capabilityId } = useParams<{ capabilityId: string }>();
  const [searchParams] = useSearchParams();
  const systemId = searchParams.get('systemId') ?? undefined;

  const [capability, setCapability] = useState<CapabilityDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [notFound, setNotFound] = useState(false);
  const [subscribing, setSubscribing] = useState(false);
  const [toast, setToast] = useState<{ message: string; type: 'success' | 'error' } | null>(null);

  useEffect(() => {
    if (!capabilityId) return;
    setLoading(true);

    const params: Record<string, string> = {};
    if (systemId) params.systemId = systemId;

    apiClient
      .get<CapabilityDetail>(`/capability-library/${capabilityId}`, { params })
      .then((res) => setCapability(res.data))
      .catch((err) => {
        if ((err as { response?: { status?: number } }).response?.status === 404)
          setNotFound(true);
      })
      .finally(() => setLoading(false));
  }, [capabilityId, systemId]);

  const handleSubscribeToggle = async () => {
    if (!systemId || !capability) return;
    setSubscribing(true);
    try {
      if (capability.isSubscribed) {
        await apiClient.delete(`/systems/${systemId}/capability-subscriptions/${capability.id}`);
        setCapability((prev) => prev ? { ...prev, isSubscribed: false } : prev);
        setToast({ message: `Unsubscribed from ${capability.name}`, type: 'success' });
      } else {
        await apiClient.post(`/systems/${systemId}/capability-subscriptions`, {
          capabilityId: capability.id,
        });
        setCapability((prev) => prev ? { ...prev, isSubscribed: true } : prev);
        setToast({ message: `Subscribed to ${capability.name}`, type: 'success' });
      }
    } catch {
      setToast({ message: 'Action failed — please try again.', type: 'error' });
    } finally {
      setSubscribing(false);
      setTimeout(() => setToast(null), 3000);
    }
  };

  if (loading)
    return (
      <PageLayout>
        <div className="py-16 text-center text-sm text-gray-500">Loading capability…</div>
      </PageLayout>
    );

  if (notFound || !capability)
    return (
      <PageLayout>
        <div className="py-16 text-center">
          <p className="text-sm text-gray-500">Capability not found.</p>
          <Link
            to={`/capability-library${systemId ? `?systemId=${systemId}` : ''}`}
            className="mt-3 inline-block text-sm text-indigo-600 hover:underline"
          >
            ← Back to Library
          </Link>
        </div>
      </PageLayout>
    );

  return (
    <PageLayout>
      {/* Breadcrumb */}
      <div className="mb-4">
        <Link
          to={`/capability-library${systemId ? `?systemId=${systemId}` : ''}`}
          className="text-sm text-indigo-600 hover:underline"
        >
          ← Back to Capability Library
        </Link>
      </div>

      <PageHero
        title={capability.name}
        description={capability.description}
      />

      {/* Toast */}
      {toast && (
        <div
          className={`mb-4 rounded-md px-4 py-3 text-sm font-medium ${
            toast.type === 'success' ? 'bg-green-50 text-green-800' : 'bg-red-50 text-red-800'
          }`}
        >
          {toast.message}
        </div>
      )}

      {/* Header metadata */}
      <div className="mb-6 flex flex-wrap items-center gap-4 rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-gray-500">Provider</p>
          <p className="mt-0.5 text-sm text-gray-900">{capability.provider}</p>
        </div>
        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-gray-500">Component</p>
          <p className="mt-0.5 text-sm text-gray-900">{capability.componentName}</p>
        </div>
        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-gray-500">Controls</p>
          <p className="mt-0.5 text-sm text-gray-900">{capability.mappedControls.length}</p>
        </div>
        {capability.mappingConfidence != null && (
          <div>
            <p className="text-xs font-medium uppercase tracking-wide text-gray-500">AI Confidence</p>
            <p className="mt-0.5 text-sm text-gray-900">
              {(capability.mappingConfidence * 100).toFixed(0)}%
            </p>
          </div>
        )}
        {capability.reviewedBy && (
          <div>
            <p className="text-xs font-medium uppercase tracking-wide text-gray-500">Reviewed By</p>
            <p className="mt-0.5 text-sm text-gray-900">{capability.reviewedBy}</p>
          </div>
        )}
        {/* Subscribe / Unsubscribe action */}
        {systemId && (
          <div className="ml-auto">
            <button
              type="button"
              onClick={() => void handleSubscribeToggle()}
              disabled={subscribing}
              className={`rounded px-4 py-2 text-sm font-medium transition-colors ${
                capability.isSubscribed
                  ? 'border border-gray-300 bg-white text-gray-700 hover:bg-gray-50'
                  : 'bg-indigo-600 text-white hover:bg-indigo-700'
              }`}
            >
              {subscribing
                ? '…'
                : capability.isSubscribed
                ? 'Unsubscribe from System'
                : 'Subscribe to System'}
            </button>
          </div>
        )}
      </div>

      {/* Reviewer note */}
      {capability.reviewerNote && (
        <div className="mb-5 rounded-md border border-blue-200 bg-blue-50 px-4 py-3 text-sm text-blue-800">
          <span className="font-medium">CSP Admin Note:</span> {capability.reviewerNote}
        </div>
      )}

      {/* Control Coverage Table */}
      <div className="rounded-lg border border-gray-200 bg-white shadow-sm">
        <div className="border-b border-gray-200 px-5 py-4">
          <h2 className="text-base font-semibold text-gray-900">Control Coverage</h2>
          <p className="mt-0.5 text-sm text-gray-500">
            NIST SP 800-53 controls addressed by this capability.
          </p>
        </div>
        {capability.mappedControls.length === 0 ? (
          <div className="px-5 py-10 text-center text-sm text-gray-500">
            No control mappings on record.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  {['Control ID', 'Inheritance Type'].map((h) => (
                    <th
                      key={h}
                      scope="col"
                      className="px-5 py-3 text-left text-xs font-semibold uppercase tracking-wide text-gray-500"
                    >
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 bg-white">
                {capability.mappedControls.map((controlId) => {
                  const inheritanceType = inferInheritanceType(controlId);
                  return (
                    <tr key={controlId} className="hover:bg-gray-50">
                      <td className="whitespace-nowrap px-5 py-3 text-sm font-medium text-gray-900">
                        {controlId}
                      </td>
                      <td className="px-5 py-3">
                        <span
                          className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${
                            INHERITANCE_BADGE[inheritanceType]
                          }`}
                        >
                          {inheritanceType}
                        </span>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </PageLayout>
  );
}
