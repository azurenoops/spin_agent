import apiClient from './client';

export interface InventoryRecord {
  id: string; systemId: string; itemName: string; type: 'Hardware' | 'Software'; status: 'Active' | 'Decommissioned';
  hardwareFunction: string | null; softwareFunction: string | null;
  manufacturer: string | null; model: string | null; serialNumber: string | null; ipAddress: string | null; macAddress: string | null;
  location: string | null; vendor: string | null; version: string | null; patchLevel: string | null; licenseType: string | null;
  parentHardwareId: string | null; boundaryResourceId: string | null; createdAt: string; modifiedAt: string | null;
}
export interface InventoryInput {
  itemName: string; type: 0 | 1; hardwareFunction?: number | null; softwareFunction?: number | null;
  manufacturer?: string; model?: string; serialNumber?: string; ipAddress?: string; macAddress?: string;
  location?: string; vendor?: string; version?: string; patchLevel?: string; licenseType?: string; parentHardwareId?: string;
}
export interface InventoryPage { systemId: string; items: InventoryRecord[]; totalCount: number; page: number; pageSize: number; canManage: boolean }
export const inventoryHardwareFunctions = ['Server', 'Workstation', 'NetworkDevice', 'Storage', 'Other'];
export const inventorySoftwareFunctions = ['OperatingSystem', 'Database', 'Middleware', 'Application', 'SecurityTool', 'Other'];
const root = (systemId: string) => `/systems/${encodeURIComponent(systemId)}/inventory-items`;
function assertRecord(value: InventoryRecord, systemId: string, id?: string) {
  if (!value || value.systemId !== systemId || !value.id || id && value.id !== id
    || !['Hardware', 'Software'].includes(value.type) || !['Active', 'Decommissioned'].includes(value.status))
    throw new Error('The inventory response does not match this system and record. Refresh before continuing.');
}
export async function listInventoryRecords(systemId: string, page = 1, signal?: AbortSignal): Promise<InventoryPage> {
  const { data } = await apiClient.get<InventoryPage>(root(systemId), { params: { page, pageSize: 50 }, signal });
  if (data?.systemId !== systemId || !Array.isArray(data.items) || data.page !== page || data.pageSize !== 50
    || !Number.isSafeInteger(data.totalCount) || data.totalCount < 0 || typeof data.canManage !== 'boolean')
    throw new Error('The inventory register could not be confirmed for this system.');
  data.items.forEach(item => assertRecord(item, systemId));
  return data;
}
export async function saveInventoryRecord(systemId: string, id: string | undefined, body: InventoryInput): Promise<InventoryRecord> {
  const response = id ? await apiClient.put<InventoryRecord>(`${root(systemId)}/${encodeURIComponent(id)}`, body)
    : await apiClient.post<InventoryRecord>(root(systemId), body);
  assertRecord(response.data, systemId, id);
  const { data } = await apiClient.get<InventoryRecord>(`${root(systemId)}/${encodeURIComponent(response.data.id)}`);
  assertRecord(data, systemId, response.data.id);
  const fields = ['itemName', 'manufacturer', 'model', 'serialNumber', 'ipAddress', 'macAddress', 'location',
    'vendor', 'version', 'patchLevel', 'licenseType', 'parentHardwareId'] as const;
  if (data.type !== (body.type === 0 ? 'Hardware' : 'Software')
    || typeof body.hardwareFunction === 'number' && data.hardwareFunction !== inventoryHardwareFunctions[body.hardwareFunction]
    || typeof body.softwareFunction === 'number' && data.softwareFunction !== inventorySoftwareFunctions[body.softwareFunction]
    || fields.some(key => body[key] !== undefined && (data[key] ?? '') !== body[key]))
    throw new Error('The server did not confirm the saved inventory fields. Your entry is retained for review.');
  return data;
}
export async function retireInventoryRecord(systemId: string, id: string, rationale: string): Promise<void> {
  const { data } = await apiClient.post<InventoryRecord>(`${root(systemId)}/${encodeURIComponent(id)}/decommission`, { rationale });
  assertRecord(data, systemId, id);
  if (data.status !== 'Decommissioned') throw new Error('The inventory retirement was not confirmed.');
}
export const inventoryWorkbookUrl = (systemId: string) => `/api/dashboard${root(systemId)}/export`;
