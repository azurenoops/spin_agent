import { useEffect, useState } from 'react';
import { useLocation, useNavigate } from '../features/workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../features/workspaces/WorkspaceBoundary';
import PageLayout from '../components/layout/PageLayout';
import ReferenceLibraryManager, { type ReferenceScopeChoice } from '../components/narratives/ReferenceLibraryManager';
import { narrativeErrorMessage } from '../components/narratives/referenceLibraryUtils';
import { getOrganizationLibraryAccess, getProviderLibraryAccess, getScopedReferences, importScopedReference,
  updateScopedReference, publishScopedReference, type NarrativeReference, type ReferenceLibraryTarget } from '../api/narrativeLibrary';
import './NarrativeWorkspace.css';

interface LibraryContext {
  label: string; canAuthor: boolean; scopes: ReferenceScopeChoice[]; capabilities: { id: string; name: string }[];
  references: NarrativeReference[];
}
export default function StandaloneNarrativeLibrary() {
  const session = useWorkspaceSession();
  if (!session) return <p role="alert">Choose an authorized workspace before opening its narrative library.</p>;
  const tenantId = session.target.kind === 'organization' ? session.target.tenantId : null;
  return <StandaloneLibrary key={`${session.target.kind}:${tenantId ?? ''}:${session.target.kind === 'organization' ? session.target.mode ?? 'ordinary' : ''}`}
    target={session.target.kind === 'csp' ? { kind: 'provider' } : { kind: 'organization' }}
    tenantId={tenantId} organizationName={session.workspace.displayName} />;
}
function StandaloneLibrary({ target, tenantId, organizationName }: {
  target: Exclude<ReferenceLibraryTarget, { kind: 'system' }>; tenantId: string | null; organizationName: string;
}) {
  const location = useLocation();
  const navigate = useNavigate();
  const [data, setData] = useState<LibraryContext | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setError(''); setLoading(true);
    void (async () => {
      try {
        let next: Omit<LibraryContext, 'references'>;
        if (target.kind === 'organization') {
          const access = await getOrganizationLibraryAccess(controller.signal);
          if (typeof access.tenantId !== 'string' || access.tenantId.toLowerCase() !== tenantId?.toLowerCase())
            throw new Error('The organization library does not match this workspace.');
          next = { label: organizationName, canAuthor: access.canPublishShared === true, capabilities: access.capabilities,
            scopes: [{ scope: 'Organization', id: access.tenantId, available: access.canPublishShared === true },
              { scope: 'Capability', capability: true, available: access.canPublishShared === true }] };
        } else {
          const access = await getProviderLibraryAccess(controller.signal);
          next = { label: access.displayName, canAuthor: access.canPublish === true, capabilities: access.capabilities,
            scopes: [{ scope: 'Provider', id: access.cspProfileId, available: access.canPublish === true },
              { scope: 'ProviderCapability', capability: true, available: access.canPublish === true }] };
        }
        if (controller.signal.aborted) return;
        const references = await getScopedReferences(target, controller.signal);
        if (!controller.signal.aborted) setData({ ...next, references });
      } catch (reason) {
        if (!controller.signal.aborted) {
          setData(null);
          setError(narrativeErrorMessage(reason));
        }
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    })();
    return () => controller.abort();
  }, [target.kind, tenantId, organizationName, revision]);
  const view = location.pathname.replace(/\/$/, '').endsWith('/import') ? 'import' : 'library';
  return <PageLayout title={target.kind === 'provider' ? 'Provider Narrative Library' : 'Organization Narrative Library'}>
    <div className="narrative-workspace">
      {loading && <p role="status">Loading reference library...</p>}
      {error && <div role="alert" className="nw-alert">{error}<button onClick={() => setRevision(value => value + 1)}>Retry loading</button></div>}
      {data && <ReferenceLibraryManager view={view} contextLabel={data.label} scopes={data.scopes} capabilities={data.capabilities}
        references={data.references} canAuthor={data.canAuthor} locked={loading} loadError={Boolean(error)}
        onNavigate={next => navigate(next === 'import' ? '/narrative-library/import' : '/narrative-library')}
        onChanged={() => setRevision(value => value + 1)}
        api={{ import: form => importScopedReference(target, form), update: (id, body) => updateScopedReference(target, id, body),
          publish: (id, expectedRevision, passages) => publishScopedReference(target, id, expectedRevision, passages) }} />}
    </div>
  </PageLayout>;
}
