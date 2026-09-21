import { useCallback, useEffect, useState } from 'react';
import { deleteValidationLink, getControlValidationLinks, type ControlValidationLink } from '../api/complianceApi';
import { buildAzurePortalUrl } from '../utils/azurePortalUrl';
import AddValidationLinkModal from './AddValidationLinkModal';
import { useWorkspaceHref } from '../../workspaces/workspaceNavigation';

interface Props {
  systemId: string;
  controlId: string;
  canManage: boolean;
}

const TYPE_LABELS: Record<ControlValidationLink['linkType'], string> = {
  AzureResource: 'Azure Resource',
  ScanFinding: 'Scan Finding',
  EvidenceArtifact: 'Evidence Artifact',
  ExternalUrl: 'External URL',
};

function targetUrl(systemId: string, link: ControlValidationLink): string {
  if (link.linkType === 'AzureResource') return buildAzurePortalUrl(link.linkTarget);
  if (link.linkType === 'ExternalUrl') {
    try {
      const url = new URL(link.linkTarget);
      return url.protocol === 'http:' || url.protocol === 'https:' ? url.href : '#';
    } catch {
      return '#';
    }
  }
  if (link.linkType === 'EvidenceArtifact') return `/systems/${encodeURIComponent(systemId)}/evidence`;
  return `/systems/${encodeURIComponent(systemId)}/assessments`;
}

export default function ValidationEvidencePanel({ systemId, controlId, canManage }: Props) {
  const workspaceHref = useWorkspaceHref();
  const [links, setLinks] = useState<ControlValidationLink[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showAdd, setShowAdd] = useState(false);

  const refresh = useCallback(async () => {
    try {
      const response = await getControlValidationLinks(systemId, controlId);
      setLinks(response.links);
      setError('');
    } catch {
      setError('Unable to load validation links.');
    } finally {
      setLoading(false);
    }
  }, [systemId, controlId]);

  useEffect(() => { void refresh(); }, [refresh]);

  const handleDelete = async (link: ControlValidationLink) => {
    if (!confirm(`Delete validation link "${link.description ?? link.linkTarget}"?`)) return;
    try {
      await deleteValidationLink(systemId, controlId, link.id);
      await refresh();
    } catch {
      setError('Unable to delete the validation link.');
    }
  };

  return (
    <section className="mt-4 border-t border-gray-200 pt-4" aria-label="Validation evidence">
      <div className="flex items-center justify-between">
        <h4 className="text-sm font-semibold text-gray-700">Validation Evidence</h4>
        {canManage && (
          <button type="button" onClick={() => setShowAdd(true)} className="rounded-md bg-indigo-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-indigo-700" aria-label="Add validation link">
            Add link
          </button>
        )}
      </div>

      {loading ? (
        <p className="mt-3 text-xs text-gray-500">Loading validation links...</p>
      ) : error ? (
        <p className="mt-3 text-sm text-red-600" role="alert">{error}</p>
      ) : links.length === 0 ? (
        <div className="mt-3 border-l-4 border-amber-400 bg-amber-50 px-3 py-2">
          <p className="text-sm font-medium text-gray-700">No validation links attached to this control.</p>
          <p className="mt-1 text-xs text-amber-800">No validation evidence linked. Adding evidence strengthens your ATO package.</p>
        </div>
      ) : (
        <div className="mt-3 space-y-2">
          {links.map((link) => (
            <div key={link.id} className="flex items-start gap-3 rounded-md border border-gray-200 bg-white px-3 py-2">
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="rounded bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-700">{TYPE_LABELS[link.linkType]}</span>
                  <span className={`rounded px-2 py-0.5 text-xs font-medium ${link.isAutomated ? 'bg-teal-100 text-teal-700' : 'bg-blue-100 text-blue-700'}`}>
                    {link.isAutomated ? 'Auto' : 'Manual'}
                  </span>
                </div>
                {link.description && <p className="mt-1 text-sm font-medium text-gray-900">{link.description}</p>}
                <a href={workspaceHref(targetUrl(systemId, link))} target={link.linkType === 'ExternalUrl' || link.linkType === 'AzureResource' ? '_blank' : undefined} rel="noreferrer" className="mt-1 block truncate text-xs text-indigo-600 hover:underline" aria-label="Open validation target">
                  {link.linkTarget}
                </a>
                <p className="mt-1 text-xs text-gray-500">Added by {link.addedBy} on {new Date(link.addedAt).toLocaleDateString()}</p>
              </div>
              {canManage && (
                <button type="button" onClick={() => void handleDelete(link)} className="rounded p-1 text-gray-400 hover:bg-red-50 hover:text-red-600" aria-label="Delete validation link" title="Delete validation link">
                  <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" aria-hidden="true">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                  </svg>
                </button>
              )}
            </div>
          ))}
        </div>
      )}

      {showAdd && (
        <AddValidationLinkModal
          systemId={systemId}
          controlId={controlId}
          onClose={() => setShowAdd(false)}
          onAdded={() => { setShowAdd(false); void refresh(); }}
        />
      )}
    </section>
  );
}