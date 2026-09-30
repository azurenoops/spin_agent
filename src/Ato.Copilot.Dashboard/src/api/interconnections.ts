import apiClient from './client';

export type InterconnectionType = 'Direct' | 'Vpn' | 'Api' | 'Federated' | 'Wireless' | 'RemoteAccess';
export type InterconnectionDirection = 'Inbound' | 'Outbound' | 'Bidirectional';
export type InterconnectionStatus = 'Proposed' | 'Active' | 'Suspended' | 'Terminated';

export interface InterconnectionEditableFields {
  targetSystemName: string;
  targetSystemOwner: string | null;
  targetSystemAcronym: string | null;
  interconnectionType: InterconnectionType;
  dataFlowDirection: InterconnectionDirection;
  dataClassification: string;
  dataDescription: string | null;
  protocolsUsed: string[];
  portsUsed: string[];
  securityMeasures: string[];
  authenticationMethod: string | null;
}

export interface InterconnectionAgreementSummary {
  id: string;
  agreementType: 'Isa' | 'Mou' | 'Sla';
  title: string;
  status: 'Draft' | 'PendingSignature' | 'Signed' | 'Expired' | 'Terminated';
  documentReference: string | null;
  effectiveDate: string | null;
  expirationDate: string | null;
  signedByLocal: string | null;
  signedByLocalDate: string | null;
  signedByRemote: string | null;
  signedByRemoteDate: string | null;
  reviewNotes: string | null;
  createdAt: string;
  modifiedAt: string | null;
}

export interface SystemInterconnectionDetail extends InterconnectionEditableFields {
  id: string;
  interconnectionId: string;
  systemId: string;
  status: InterconnectionStatus;
  statusReason: string | null;
  authorizationToConnect: boolean;
  hasAgreement: boolean;
  agreements: InterconnectionAgreementSummary[];
  createdBy: string;
  createdAt: string;
  modifiedAt: string | null;
  canManageInterconnections: boolean;
}

export interface SystemInterconnectionsResponse {
  items: SystemInterconnectionDetail[];
  total: number;
  page: number;
  pageSize: number;
  canManageInterconnections: boolean;
}

const root = (systemId: string) => `/systems/${encodeURIComponent(systemId)}/interconnections`;
const detailUrl = (systemId: string, id: string) => `${root(systemId)}/${encodeURIComponent(id)}`;
const nullableFields = ['targetSystemOwner', 'targetSystemAcronym', 'dataDescription', 'authenticationMethod'] as const;
const arrayFields = ['protocolsUsed', 'portsUsed', 'securityMeasures'] as const;
const textFields = ['targetSystemName', 'dataClassification', 'createdBy', 'createdAt'] as const;

function validateDetail(data: SystemInterconnectionDetail, systemId: string, id?: string): SystemInterconnectionDetail {
  if (!data || typeof data.id !== 'string' || !data.id || data.interconnectionId !== data.id
    || data.systemId !== systemId || (id !== undefined && data.id !== id)
    || textFields.some(key => typeof data[key] !== 'string')
    || nullableFields.some(key => data[key] !== null && typeof data[key] !== 'string')
    || arrayFields.some(key => !Array.isArray(data[key]) || data[key].some(value => typeof value !== 'string'))
    || !['Direct', 'Vpn', 'Api', 'Federated', 'Wireless', 'RemoteAccess'].includes(data.interconnectionType)
    || !['Inbound', 'Outbound', 'Bidirectional'].includes(data.dataFlowDirection)
    || !['Proposed', 'Active', 'Suspended', 'Terminated'].includes(data.status)
    || (data.statusReason !== null && typeof data.statusReason !== 'string')
    || (data.modifiedAt !== null && typeof data.modifiedAt !== 'string')
    || typeof data.canManageInterconnections !== 'boolean' || typeof data.authorizationToConnect !== 'boolean'
    || typeof data.hasAgreement !== 'boolean' || !Array.isArray(data.agreements)) {
    throw new Error('The server did not return complete canonical interconnection details. Reload before editing.');
  }
  return data;
}

export async function listSystemInterconnections(
  systemId: string, signal?: AbortSignal, page = 1, pageSize = 50,
): Promise<SystemInterconnectionsResponse> {
  const { data } = await apiClient.get<SystemInterconnectionsResponse>(root(systemId), {
    signal, params: { page, pageSize },
  });
  if (!data || !Array.isArray(data.items) || !Number.isInteger(data.total) || data.total < 0
    || data.page !== page || data.pageSize !== pageSize || typeof data.canManageInterconnections !== 'boolean') {
    throw new Error('The server did not return a valid interconnection list.');
  }
  data.items.forEach(item => validateDetail(item, systemId));
  return data;
}

export async function getSystemInterconnection(
  systemId: string, id: string, signal?: AbortSignal,
): Promise<SystemInterconnectionDetail> {
  const { data } = await apiClient.get<SystemInterconnectionDetail>(detailUrl(systemId, id), { signal });
  return validateDetail(data, systemId, id);
}

function editablePayload(body: InterconnectionEditableFields): InterconnectionEditableFields {
  // Explicit allowlist keeps lifecycle and agreement fields out even when callers pass a detail object.
  return {
    targetSystemName: body.targetSystemName,
    targetSystemOwner: body.targetSystemOwner ?? '',
    targetSystemAcronym: body.targetSystemAcronym ?? '',
    interconnectionType: body.interconnectionType,
    dataFlowDirection: body.dataFlowDirection,
    dataClassification: body.dataClassification,
    dataDescription: body.dataDescription ?? '',
    protocolsUsed: body.protocolsUsed,
    portsUsed: body.portsUsed,
    securityMeasures: body.securityMeasures,
    authenticationMethod: body.authenticationMethod ?? '',
  };
}

async function confirmPersisted(
  receipt: SystemInterconnectionDetail, systemId: string, body: InterconnectionEditableFields, id?: string,
): Promise<SystemInterconnectionDetail> {
  validateDetail(receipt, systemId, id);
  const persisted = await getSystemInterconnection(systemId, receipt.id);
  const actual = editablePayload(persisted);
  if ((Object.keys(body) as (keyof InterconnectionEditableFields)[])
    .some(key => JSON.stringify(actual[key]) !== JSON.stringify(body[key]))) {
    throw new Error('Saved interconnection details could not be confirmed. Reload the record before retrying.');
  }
  return persisted;
}

export async function createSystemInterconnection(
  systemId: string, body: InterconnectionEditableFields,
): Promise<SystemInterconnectionDetail> {
  const payload = editablePayload(body);
  const { data } = await apiClient.post<SystemInterconnectionDetail>(root(systemId), payload);
  return confirmPersisted(data, systemId, payload);
}

export async function updateSystemInterconnection(
  systemId: string, id: string, body: InterconnectionEditableFields,
): Promise<SystemInterconnectionDetail> {
  const payload = editablePayload(body);
  const { data } = await apiClient.put<SystemInterconnectionDetail>(detailUrl(systemId, id), payload);
  return confirmPersisted(data, systemId, payload, id);
}
