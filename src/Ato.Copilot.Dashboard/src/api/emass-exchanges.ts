import apiClient from './client';

export type ExchangeOutcome = 'TransferRecorded' | 'ReceiptRecorded' | 'ImportAccepted' | 'ImportRejected' | 'PartialImport';
export interface ExchangeExport {
  packageId: string;
  packageHash: string;
  exportGeneratedAt: string;
  purpose: string;
}
export interface ExchangeRecord {
  id: string;
  version: number;
  packageId: string;
  packageHash: string;
  exportGeneratedAt: string;
  outcome: ExchangeOutcome;
  receivingWorkflow: string;
  externalReference: string;
  occurredAt: string;
  recordedAt: string;
  recordedBy: string;
  notes: string;
  supersedesId: string | null;
}
export interface ExchangeHistory { version: number; canRecord: boolean; items: ExchangeRecord[] }
export interface RecordExchangeRequest {
  packageId: string;
  packageHash: string;
  exportGeneratedAt: string;
  outcome: ExchangeOutcome;
  receivingWorkflow: string;
  externalReference: string;
  occurredAt: string;
  notes: string;
  expectedVersion: number;
  idempotencyKey: string;
  supersedesId: string | null;
}
interface Envelope<T> { data: T }

export async function getExchangeHistory(systemId: string): Promise<ExchangeHistory> {
  return (await apiClient.get<Envelope<ExchangeHistory>>(`/systems/${systemId}/emass/exchanges`)).data.data;
}
export async function getExchangeExports(systemId: string): Promise<ExchangeExport[]> {
  return (await apiClient.get<Envelope<ExchangeExport[]>>(`/systems/${systemId}/emass/exchange-exports`)).data.data;
}
export async function recordExchange(systemId: string, request: RecordExchangeRequest): Promise<ExchangeRecord> {
  return (await apiClient.post<Envelope<ExchangeRecord>>(`/systems/${systemId}/emass/exchanges`, request)).data.data;
}
