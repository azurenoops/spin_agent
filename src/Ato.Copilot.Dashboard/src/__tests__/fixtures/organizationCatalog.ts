import type { OrganizationCapability } from '../../features/workspace-operations/types';

export const organizationCatalogRecords: OrganizationCapability[] = [
  { source: 'local', recordType: 'capability', recordId: 'local-cap', name: 'Enterprise monitoring',
    description: 'Organization monitoring service', category: 'AU', availability: 'Planned',
    isSubscribed: false, systemCount: 0, mutationAuthority: 'organization' },
  { source: 'provider', recordType: 'capability', recordId: 'provider-cap', name: 'Backup and recovery',
    description: 'Published backup service', category: 'CP', availability: 'Published',
    isSubscribed: false, systemCount: 0, mutationAuthority: 'provider' },
  { source: 'local', recordType: 'component', recordId: 'local-component', name: 'Enterprise operations team',
    description: 'Organization-wide operators', category: 'Person', availability: 'Planned',
    isSubscribed: false, systemCount: 0, mutationAuthority: 'organization' },
  { source: 'provider', recordType: 'component', recordId: 'provider-component', name: 'Provider backup platform',
    description: 'Published platform', category: 'Service', availability: 'Published',
    isSubscribed: false, systemCount: 0, mutationAuthority: 'provider' },
];
