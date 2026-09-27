import { packageRequest } from '../package-imports/request';

export interface OrganizationAdministratorAssignment {
  id: string; tenantId: string; personId: string; role: 'Administrator';
}
export const enrollOrganizationAdministrator = (tenantId: string, personId: string) =>
  packageRequest<OrganizationAdministratorAssignment>({
    method: 'POST', url: `/api/tenants/${encodeURIComponent(tenantId)}/administrator-assignments`, data: { personId },
  });
