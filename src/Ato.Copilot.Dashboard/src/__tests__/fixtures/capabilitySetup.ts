import type { CapabilitySetupOperation } from '../../features/workspace-operations/types';

export function preparedProviderSetup(overrides: Partial<CapabilitySetupOperation> = {}): CapabilitySetupOperation {
  return {
    operationId: 'setup-provider', idempotencyKey: 'provider-key', tenantId: 'org-1',
    systemId: 'system-1', source: 'provider', recordId: 'provider-1', componentIds: [],
    inlineLocalCapability: null, subscribeRequested: true,
    recordState: 'Pending', componentLinksState: 'Pending', subscriptionState: 'Pending',
    outcomes: [], lastError: null, createdAt: '2026-09-23T00:00:00Z', updatedAt: '2026-09-23T00:00:00Z',
    ...overrides,
  };
}
