import type {
  CapabilityResponsibilityItem, CapabilityResponsibilityResponse, ConfirmCapabilityResponsibilitiesRequest,
} from '../../../api/capabilityResponsibilities';
import { WorkspaceOperationError, workspaceRequest } from '../workspaceRequest';

export type SystemCapabilityReviewEvidence = {
  providerCoverageVerified?: boolean | null;
  customerDutiesReviewed?: boolean | null;
  reviewNotes?: string | null;
};
type ReviewedItem = CapabilityResponsibilityItem & SystemCapabilityReviewEvidence;
type ReviewedResponse = CapabilityResponsibilityResponse & { items: ReviewedItem[] };
type Confirmation = ConfirmCapabilityResponsibilitiesRequest & {
  providerCoverageVerified: true;
  customerDutiesReviewed: true;
  reviewNotes: string;
};

export async function confirmSystemCapabilityResponsibilities(
  tenantId: string, systemId: string, capabilityId: string, body: Confirmation, signal?: AbortSignal,
): Promise<ReviewedResponse> {
  const response = await workspaceRequest<ReviewedResponse>({
    method: 'POST',
    url: `/api/workspaces/organizations/${encodeURIComponent(tenantId)}/systems/${encodeURIComponent(systemId)}/security-capabilities/provider/capability/${encodeURIComponent(capabilityId)}/responsibilities/confirm`,
    data: body, signal,
  });
  if (!response || response.systemId !== systemId || response.baselineId !== body.baselineId
    || !Array.isArray(response.items) || !Array.isArray(response.pendingImpacts) || typeof response.canConfirm !== 'boolean'
    || body.allocations.some(allocation => !response.items.some((item: ReviewedItem) =>
      item.capabilityId === capabilityId && item.controlId === allocation.controlId
      && item.providerCoverageVerified === true && item.customerDutiesReviewed === true && item.reviewNotes === body.reviewNotes)))
    throw new WorkspaceOperationError('The confirmation response did not verify the saved notes and review checks. Refresh responsibilities before another write.',
      502, 'INVALID_RESPONSIBILITY_CONFIRMATION');
  return response;
}
