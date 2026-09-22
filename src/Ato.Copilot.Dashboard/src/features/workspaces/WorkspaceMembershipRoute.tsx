import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import axios from 'axios';
import { useWorkspaceSession, WorkspaceStatus } from './WorkspaceBoundary';
import { workspaceErrorMessage } from './api';
import MembershipAdministrationPage from './MembershipAdministrationPage';

export default function WorkspaceMembershipRoute() {
  const session = useWorkspaceSession();
  const { organizationId } = useParams();
  if (!session || !session.workspace.permissions.canManageMemberships) {
    return <WorkspaceStatus message="Membership administration is not authorized in this workspace." />;
  }
  if (session.target.kind === 'organization') {
    if (organizationId) return <WorkspaceStatus message="Choose membership settings for the active organization." />;
    return <MembershipAdministrationPage tenantId={session.target.tenantId}
      tenantName={session.workspace.displayName} canManageMemberships onRevoked={membership => {
        const identity = session.identity;
        if (session.workspace.mode === 'ordinary'
          && membership.tenantId.toLowerCase() === session.workspace.tenantId?.toLowerCase()
          && membership.objectId.toLowerCase() === identity.oid.toLowerCase()
          && (identity.directoryTenantId
            ? membership.directoryTenantId.toLowerCase() === identity.directoryTenantId.toLowerCase()
            : membership.personId.toLowerCase() === session.workspace.personId?.toLowerCase())) {
          session.refresh();
        }
      }} />;
  }
  if (!organizationId || !session.workspace.permissions.canAccessCsp) {
    return <WorkspaceStatus message="Select an organization in the provider portfolio to manage its memberships." />;
  }
  return <ProviderMemberships key={organizationId} tenantId={organizationId} />;
}

function ProviderMemberships({ tenantId }: { tenantId: string }) {
  const [name, setName] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    axios.get(`/api/tenants/${encodeURIComponent(tenantId)}`, { signal: controller.signal })
      .then(response => {
        if (controller.signal.aborted) return;
        const tenant = response.data?.data;
        if (response.data?.status !== 'success' || tenant?.id?.toLowerCase() !== tenantId.toLowerCase()
          || typeof tenant.displayName !== 'string') throw new Error('The requested organization could not be verified.');
        setName(tenant.displayName);
      }).catch(reason => { if (!controller.signal.aborted) setError(workspaceErrorMessage(reason)); });
    return () => controller.abort();
  }, [tenantId]);
  if (error) return <WorkspaceStatus message={error} />;
  if (!name) return <WorkspaceStatus loading message="Verifying organization administration..." />;
  return <MembershipAdministrationPage tenantId={tenantId} tenantName={name} canManageMemberships />;
}
