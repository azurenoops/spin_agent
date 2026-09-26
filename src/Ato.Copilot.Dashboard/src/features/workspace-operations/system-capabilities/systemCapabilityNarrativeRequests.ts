import type { NarrativeProposal } from '../../../api/narrativeLibrary';
import { WorkspaceOperationError, workspaceRequest } from '../workspaceRequest';
import type { SystemCapabilitySource } from './systemCapabilityTypes';

export async function generateScopedSystemCapabilityProposal(
  tenantId: string, systemId: string, source: SystemCapabilitySource, recordId: string,
  body: { controlId: string; narrativeType: string; expectedVersion: number; sourceRevision: string },
): Promise<NarrativeProposal> {
  const result = await workspaceRequest<NarrativeProposal>({
    method: 'POST',
    url: `/api/workspaces/organizations/${encodeURIComponent(tenantId)}/systems/${encodeURIComponent(systemId)}/security-capabilities/${source}/capability/${encodeURIComponent(recordId)}/narrative-proposals`,
    data: body,
  });
  if (!result || typeof result.id !== 'string' || !result.id || !Number.isInteger(result.revision)
    || result.controlId !== body.controlId || result.narrativeType !== body.narrativeType)
    throw new WorkspaceOperationError('The generated proposal does not match the requested control and narrative type. Refresh saved proposals before retrying.',
      502, 'INVALID_SYSTEM_CAPABILITY_PROPOSAL');
  return result;
}
