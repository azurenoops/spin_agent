import { beforeEach, expect, it, vi } from 'vitest';
import { readProviderCapabilitySources } from '../../features/workspace-operations/providerCapabilitySources';
import * as api from '../../features/provider-authorizations/api';
import * as packages from '../../features/package-imports/api';
import { candidate, packageStatus, page } from '../package-imports/fixtures';
import { offering } from '../provider-authorizations/testData';

vi.mock('../../features/provider-authorizations/api', () => ({ getAssociatedPackage: vi.fn(), getOffering: vi.fn() }));
vi.mock('../../features/package-imports/api', () => ({ getPackageCandidates: vi.fn() }));
const packageId = '11111111-1111-1111-1111-111111111111';
const artifact = { componentId: 'parent', componentName: 'Source group', sourceFormat: 'Package', sourceFileName: 'source.json',
  sourceReference: `package:${packageId}/artifact:22222222-2222-2222-2222-222222222222` };
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getAssociatedPackage).mockResolvedValue({ ...packageStatus({ packageId }),
    association: { offeringId: offering.offeringId, packageVersionId: 'v', boundaryRevisionId: 'b' } });
  vi.mocked(api.getOffering).mockResolvedValue(offering);
  vi.mocked(packages.getPackageCandidates).mockResolvedValue(page([
    candidate({ candidateId: 'mine', type: 'Responsibility', contributorIds: ['capability-a'], description: 'Customer enables audit sources.' }),
    candidate({ candidateId: 'other', type: 'Responsibility', contributorIds: ['capability-b'], description: 'Other capability duty.' }),
  ]));
});
it.each(['capability-a', 'CAPABILITY-A'])('uses exact package provenance and normalized contributor identity for %s', async capabilityId => {
  // Arrange / Act
  const result = await readProviderCapabilitySources(capabilityId, [artifact, artifact], new AbortController().signal);
  // Assert
  expect(result.offering).toEqual(offering);
  expect(result.sources[0]?.duties.map(item => item.candidateId)).toEqual(['mine']);
  expect(api.getAssociatedPackage).toHaveBeenCalledTimes(1);
  expect(packages.getPackageCandidates).toHaveBeenCalledWith(packageId, { page: 1, pageSize: 100, type: 'Responsibility' }, expect.any(AbortSignal));
});
it('keeps a legacy free-text source as provenance without inventing a package identity', async () => {
  // Arrange / Act
  const result = await readProviderCapabilitySources('capability-a', [{ ...artifact, sourceReference: 'Legacy source note' }], new AbortController().signal);
  // Assert
  expect(result).toEqual({ offering: null, sources: [] });
  expect(api.getAssociatedPackage).not.toHaveBeenCalled();
});
it('surfaces unavailable source reads instead of presenting them as no duties', async () => {
  // Arrange
  vi.mocked(api.getAssociatedPackage).mockRejectedValue(new Error('Source access unavailable'));
  // Act / Assert
  await expect(readProviderCapabilitySources('capability-a', [artifact], new AbortController().signal)).rejects.toThrow('Source access unavailable');
});
