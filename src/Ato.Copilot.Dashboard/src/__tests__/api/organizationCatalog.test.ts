import { beforeEach, describe, expect, it, vi } from 'vitest';
import axios from 'axios';
import { addOrganizationCatalogRecord, getOrganizationCatalogAccess, WorkspaceOperationError } from '../../features/workspace-operations/api';
import type { OrganizationCatalogAddition } from '../../features/workspace-operations/types';

vi.mock('axios', async importOriginal => {
  const actual = await importOriginal<typeof import('axios')>();
  return { ...actual, default: { ...actual.default, request: vi.fn() } };
});

const addition: OrganizationCatalogAddition = {
  idempotencyKey: 'organization-addition-key', source: 'provider', recordType: 'capability',
  recordId: 'provider-capability', components: [], newComponents: [],
  organizationContribution: 'Operate the central organizational service', owner: 'Organization operations',
};

beforeEach(() => vi.resetAllMocks());

describe('organization catalog API transport', () => {
  it('loads catalog access from organization scope without a system parameter', async () => {
    // Arrange
    vi.mocked(axios.request).mockResolvedValue({ status: 200, data: { data: { canManageCatalog: true } } });
    const controller = new AbortController();
    // Act
    const access = await getOrganizationCatalogAccess('org-1', controller.signal);
    // Assert
    expect(access).toEqual({ canManageCatalog: true });
    expect(axios.request).toHaveBeenCalledWith({
      method: 'GET', url: '/api/workspaces/organizations/org-1/catalog-access', signal: controller.signal,
    });
  });

  it('sends the exact organization save payload and reads the created result', async () => {
    // Arrange
    const result = { source: 'provider', recordType: 'capability', recordId: 'provider-capability', name: 'Central service', existing: false };
    vi.mocked(axios.request).mockResolvedValue({ status: 201, data: { data: result } });
    // Act
    const saved = await addOrganizationCatalogRecord('org-1', addition);
    // Assert
    expect(saved).toEqual(result);
    expect(axios.request).toHaveBeenCalledWith({
      method: 'POST', url: '/api/workspaces/organizations/org-1/catalog-additions', data: addition,
    });
  });

  it('preserves server status and safe message for denied organization writes', async () => {
    // Arrange
    vi.mocked(axios.request).mockRejectedValue({
      isAxiosError: true, message: 'Request failed', response: {
        status: 403, data: { error: { errorCode: 'CATALOG_FORBIDDEN', message: 'Organization catalog permission is required.' } },
      },
    });
    // Act
    const result = addOrganizationCatalogRecord('org-1', addition);
    // Assert
    await expect(result).rejects.toMatchObject({
      status: 403, code: 'CATALOG_FORBIDDEN', message: 'Organization catalog permission is required.',
    });
  });

  it('rejects a response without the workspace data envelope instead of reporting saved', async () => {
    // Arrange
    vi.mocked(axios.request).mockResolvedValue({ status: 200, data: '<html>SPA fallback</html>' });
    // Act
    const result = addOrganizationCatalogRecord('org-1', addition);
    // Assert
    await expect(result).rejects.toBeInstanceOf(WorkspaceOperationError);
  });
});
