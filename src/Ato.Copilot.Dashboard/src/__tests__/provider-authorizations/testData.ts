import type { BoundaryRevision, Offering, PackageReceipt } from '../../features/provider-authorizations/types';
import { packageStatus } from '../package-imports/fixtures';

export const offering: Offering = {
  offeringId: 'offering-1', providerId: 'provider-1', name: 'Synthetic service', description: 'Test-only offering',
  environments: ['AzureUSGovernment'], revision: 4, lifecycle: 'Draft',
  currentBoundaryRevisionId: 'boundary-1', currentHostingScopeRevisionId: null,
};
export const boundary: BoundaryRevision = {
  offeringId: offering.offeringId, offeringRevision: 4, boundaryRevisionId: 'boundary-1', version: 1,
  name: 'Test service boundary', scopeStatement: 'Synthetic service only; workloads not covered.',
  componentSnapshotIds: [], services: [], includedScopes: [], exclusions: [], providerResponsibilities: [],
  customerResponsibilities: [], citations: [], predecessorRevisionId: null,
  snapshotHash: 'synthetic-boundary-hash', createdAt: '2026-09-24T00:00:00Z',
};
export const receipt: PackageReceipt = {
  package: { ...packageStatus({ processingState: 'Processing' }), association: {
    offeringId: offering.offeringId, packageVersionId: 'version-1', boundaryRevisionId: boundary.boundaryRevisionId,
  } },
  packageVersion: { packageVersionId: 'version-1', offeringId: offering.offeringId, seriesId: 'series-1', version: 1,
    packageId: packageStatus().packageId, boundaryRevisionId: boundary.boundaryRevisionId, previousVersionId: null,
    manifestHash: 'synthetic-manifest', createdAt: '2026-09-24T00:00:00Z' },
};
