import { beforeEach, describe, expect, it, vi } from 'vitest';
const transport = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), put: vi.fn() }));
vi.mock('axios', () => ({ default: { create: () => transport } }));
vi.mock('../../features/auth/interceptors', () => ({ attachAuthInterceptor: vi.fn() }));
vi.mock('../../features/auth/msalInstance', () => ({ getMsalInstance: vi.fn(), DEFAULT_API_SCOPES: [] }));
import { tenantWizard } from '../../features/onboarding/TenantWizard/api';

beforeEach(() => {
  vi.resetAllMocks();
  for (const method of [transport.get, transport.post, transport.put])
    method.mockResolvedValue({ data: { status: 'success', data: { tenantId: 'tenant-a' } } });
});
describe('tenant activation canonical transport', () => {
  it('forwards an optional abort signal to state hydration', async () => {
    // Arrange
    const controller = new AbortController();
    // Act
    await tenantWizard.getState(controller.signal);
    // Assert
    expect(transport.get).toHaveBeenCalledWith('/state', { signal: controller.signal });
  });
  it('retains six legacy commands and explicitly confirmed activation', async () => {
    // Arrange / Act
    await tenantWizard.submitLegalEntity({ legalEntityName: 'Legal', expectedRevision: 1 });
    await tenantWizard.submitHqAddress({ hqAddressLine1: 'Address', hqCity: 'City', hqStateOrProvince: 'State', hqPostalCode: '00000', hqCountry: 'US' });
    await tenantWizard.submitClassification({ defaultClassificationLevel: 'CUI' });
    await tenantWizard.submitAo({ authorizingOfficialName: 'AO', authorizingOfficialEmail: 'ao@example.invalid' });
    await tenantWizard.submitPrimaryPoc({ primaryPocName: 'POC', primaryPocEmail: 'poc@example.invalid' });
    await tenantWizard.submitOrgProfile({ name: 'Profile' });
    await tenantWizard.submitFinal(6);
    await tenantWizard.submitFinal();
    await tenantWizard.discardDraft(7);
    // Assert
    expect(transport.post.mock.calls.map(call => call[0])).toEqual([
      '/legal-entity', '/hq-address', '/classification', '/ao', '/primary-poc', '/org-profile', '/submit', '/submit', '/draft/discard',
    ]);
    expect(transport.post).toHaveBeenNthCalledWith(7, '/submit', { expectedRevision: 6, confirmed: true });
    expect(transport.post).toHaveBeenNthCalledWith(8, '/submit', {});
    expect(transport.post).toHaveBeenNthCalledWith(9, '/draft/discard', { expectedRevision: 7 });
  });
  it('rejects server error envelopes and missing data instead of reporting save success', async () => {
    // Arrange
    transport.get.mockResolvedValueOnce({ data: { status: 'error', error: { errorCode: 'DENIED', message: 'Not authorized' } } })
      .mockResolvedValueOnce({ data: { status: 'success' } });
    // Act / Assert
    await expect(tenantWizard.getState()).rejects.toMatchObject({ message: 'Not authorized', errorCode: 'DENIED' });
    await expect(tenantWizard.getState()).rejects.toThrow('Tenant wizard request failed.');
  });
  it('writes a full private tenant draft without submitting its fields', async () => {
    // Arrange
    const request = { schemaVersion: 1 as const, expectedRevision: 2, currentStep: 'Tenant.LegalEntity' as const,
      values: {
        legalEntity: { legalEntityName: 'Draft' }, hqAddress: { hqAddressLine1: '', hqCity: '', hqStateOrProvince: '', hqPostalCode: '', hqCountry: '' },
        classification: { defaultClassificationLevel: 'CUI' as const }, ao: { authorizingOfficialName: '', authorizingOfficialEmail: '' },
        primaryPoc: { primaryPocName: '', primaryPocEmail: '' }, orgProfile: { name: '' },
      } };
    // Act
    await tenantWizard.saveDraft(request);
    // Assert
    expect(transport.put).toHaveBeenCalledWith('/draft', request);
    expect(transport.post).not.toHaveBeenCalled();
  });
});
