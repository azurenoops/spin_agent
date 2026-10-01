import { createContext, useContext } from 'react';
import type { TenantDraftValues } from './api';

export const TenantDraftContext = createContext<{
  values: TenantDraftValues;
  revision: number;
  change: (slice: keyof TenantDraftValues, field: string, value: string) => void;
} | null>(null);

export function useTenantField(slice: keyof TenantDraftValues, field: string): [string, (value: string) => void] {
  const context = useContext(TenantDraftContext);
  if (!context) throw new Error('Tenant step requires its domain draft context.');
  const value = (context.values[slice] as unknown as Record<string, unknown>)[field];
  return [typeof value === 'string' ? value : '', next => context.change(slice, field, next)];
}

export function useTenantDraftRevision() {
  return useContext(TenantDraftContext)?.revision;
}
