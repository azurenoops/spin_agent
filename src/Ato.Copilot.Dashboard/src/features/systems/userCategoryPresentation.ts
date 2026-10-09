export interface UserDocumentationRow {
  id?: string; _tempId?: string; categoryName?: string; identityType?: string | null; privilegeLevel?: string | null;
  affiliation?: string | null; accessMethod?: string | null; authenticationMethod?: string | null;
  responsibleOwner?: string | null; userLocations?: string | null; permittedEnvironments?: string | null;
  authorizedDataTypes?: string | null; pendingDeletion?: boolean;
}
const fields = ['categoryName', 'identityType', 'privilegeLevel', 'affiliation', 'accessMethod', 'authenticationMethod',
  'responsibleOwner', 'userLocations', 'permittedEnvironments', 'authorizedDataTypes'] as const;
export function userDocumentation(rows: UserDocumentationRow[]) {
  const active = rows.filter(r => !r.pendingDeletion);
  const recorded = active.reduce((count, row) => count + fields.filter(field => !!row[field]?.trim()).length, 0);
  const total = active.length * fields.length;
  const incomplete = active.find(row => fields.some(field => !row[field]?.trim()));
  const workload = active.find(row => row.identityType === 'WorkloadIdentity'
    && (!row.responsibleOwner?.trim() || !row.permittedEnvironments?.trim()));
  return { recorded, total, missing: total - recorded, categories: active.length,
    firstIncompleteId: (workload ?? incomplete)?.id ?? (workload ?? incomplete)?._tempId, workloadName: workload?.categoryName };
}
export type UserDocumentation = ReturnType<typeof userDocumentation>;
export function userClassificationLabel(value: string): string {
  const names: Record<string, string> = { WorkloadIdentity: 'Workload identity', NonPrivileged: 'Non-privileged' };
  return names[value] ?? value;
}
export function identitySummary(row: UserDocumentationRow): string {
  return [row.identityType, row.privilegeLevel, row.affiliation].map(value => value ? userClassificationLabel(value) : 'Not recorded').join(' · ');
}
