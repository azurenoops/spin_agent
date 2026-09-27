import type { OfferingEnvironment, ProviderScope } from './types';

export const offeringEnvironments: Record<OfferingEnvironment, string> = {
  AzureCloud: 'Azure Commercial', AzureUSGovernment: 'Azure Government',
  Microsoft365DoD: 'Microsoft 365 DoD (manual service)', ManualService: 'Other manually documented service',
};
const object = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const text = (value: unknown) => typeof value === 'string' && value.trim().length > 0;
export function isProviderScope(value: unknown): value is ProviderScope {
  if (!object(value)) return false;
  if (value.kind === 'Service') return text(value.serviceId) && text(value.serviceName)
    && typeof value.environment === 'string' && Object.prototype.hasOwnProperty.call(offeringEnvironments, value.environment)
    && (value.tenantReference === null || text(value.tenantReference))
    && !['cloud', 'directoryTenantId', 'subscriptionId', 'resourceId'].some(key => key in value);
  return (value.kind === undefined || value.kind === 'Azure') && (value.cloud === 'AzureCloud' || value.cloud === 'AzureUSGovernment')
    && text(value.directoryTenantId) && text(value.subscriptionId) && text(value.resourceId)
    && !['serviceId', 'serviceName', 'environment', 'tenantReference'].some(key => key in value);
}
export const scopeLabel = (scope: ProviderScope) => scope.kind === 'Service'
  ? `${scope.serviceName} · ${scope.serviceId} · ${offeringEnvironments[scope.environment]}${scope.tenantReference ? ` · ${scope.tenantReference}` : ''}`
  : scope.resourceId || scope.subscriptionId;
export const blankProviderScope = (kind: 'Azure' | 'Service' = 'Azure', environment: OfferingEnvironment = 'AzureUSGovernment'): ProviderScope =>
  kind === 'Service' ? { kind: 'Service', serviceId: '', serviceName: '', environment, tenantReference: null }
    : { cloud: environment === 'AzureCloud' ? 'AzureCloud' : 'AzureUSGovernment', directoryTenantId: '', subscriptionId: '', resourceId: '' };
