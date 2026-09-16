import { describe, expect, it } from 'vitest';
import { buildAzurePortalUrl } from '../azurePortalUrl';

describe('buildAzurePortalUrl', () => {
  it.each([
    '/subscriptions/sub-1/resourceGroups/rg-1/providers/Microsoft.Compute/virtualMachines/vm-1',
    '/subscriptions/sub-2/resourceGroups/rg-two/providers/Microsoft.KeyVault/vaults/vault-1',
  ])('builds a portal resource URL for %s', (resourceId) => {
    // Act
    const result = buildAzurePortalUrl(resourceId);

    // Assert
    expect(result).toBe(`https://portal.azure.com/#resource/${resourceId}`);
  });
});