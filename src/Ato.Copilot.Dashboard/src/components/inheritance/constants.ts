export const KNOWN_PROVIDERS = [
  'Azure Government (FedRAMP High)',
  'AWS GovCloud (FedRAMP High)',
  'Google Cloud Platform (FedRAMP High)',
  'Oracle Cloud Infrastructure (FedRAMP High)',
  'Microsoft 365 GCC High (FedRAMP High)',
  'ServiceNow (FedRAMP High)',
  'Salesforce Government Cloud (FedRAMP High)',
] as const;

export type KnownProvider = (typeof KNOWN_PROVIDERS)[number];
