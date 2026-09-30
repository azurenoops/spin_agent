import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { FileFirstImport } from '../../features/provider-authorizations/FileFirstImport';
import { packageRequest } from '../../features/package-imports/request';
import { packageStatus } from '../package-imports/fixtures';
import { setupState } from './testData';
import type { UploadIntent, UploadIntentInput } from '../../features/csp-onboarding/providerSetupApi';
import '../package-imports/crypto';

vi.mock('../../features/package-imports/request', async original => ({
  ...await original<typeof import('../../features/package-imports/request')>(), packageRequest: vi.fn(),
}));
let saved: UploadIntent | null;
const receipt = packageStatus({ processingState: 'Received' });
function Location() { return <output aria-label="Location">{useLocation().pathname}{useLocation().search}</output>; }
beforeEach(() => {
  vi.clearAllMocks(); saved = null;
  vi.mocked(packageRequest).mockImplementation(async request => {
    if (request.url === '/api/csp/onboarding/handling-policy') return setupState().handling as never;
    if (request.url === '/api/csp/package-imports/upload-intents' && request.method === 'POST') {
      const input: UploadIntentInput = request.data.intent;
      saved = { input, intentId: input.intentId, intentHash: 'A'.repeat(64), revision: 1, savedAt: '2026-09-30T12:00:00Z',
        receipt: null, reconciliation: { outcome: 'NotObserved', nextAction: 'ReselectSameFiles', observedAt: '2026-09-30T12:00:00Z' } };
      return saved as never;
    }
    if (request.url === '/api/csp/package-imports/upload-intents') return {
      items: saved ? [saved] : [], page: 1, pageSize: 25, total: saved ? 1 : 0,
    } as never;
    if (request.url?.includes('/upload-intents/')) return saved as never;
    if (request.url === '/api/csp/inherited-components/import') {
      if (saved) saved.receipt = receipt;
      return receipt as never;
    }
    throw new Error(`Unconfigured provider portal request ${request.url}`);
  });
});
describe('policy-enforced active provider portal intake', () => {
  it('registers ActivePortal intent before source bytes without requiring or modifying a setup draft', async () => {
    // Arrange
    render(<MemoryRouter><Location /><FileFirstImport /></MemoryRouter>);
    // Act
    fireEvent.change(await screen.findByLabelText('Select source files'), {
      target: { files: [new File(['synthetic'], 'source.txt', { type: 'text/plain' })] },
    });
    const classification = screen.queryByLabelText('Declared source classification');
    if (classification) fireEvent.change(classification, { target: { value: 'Unclassified' } });
    const synthetic = screen.queryByLabelText('These files contain only synthetic data.');
    if (synthetic) fireEvent.click(synthetic);
    fireEvent.click(screen.getByRole('button', { name: 'Upload package' }));
    // Assert
    await waitFor(() => expect(packageRequest).toHaveBeenCalledWith(expect.objectContaining({
      method: 'POST', url: '/api/csp/package-imports/upload-intents',
      data: expect.objectContaining({ expectedSetupRevision: 0, intent: expect.objectContaining({
        entryPoint: 'ActivePortal', associationMode: 'Unassociated', context: null,
      }) }),
    })));
    const calls = vi.mocked(packageRequest).mock.calls.map(([value]) => value);
    expect(calls.some(value => value.url === '/api/csp/onboarding/setup/draft')).toBe(false);
    expect(calls.findIndex(value => value.url === '/api/csp/package-imports/upload-intents' && value.method === 'POST'))
      .toBeLessThan(calls.findIndex(value => value.url === '/api/csp/inherited-components/import'));
    await waitFor(() => expect(screen.getByLabelText('Location')).toHaveTextContent(`packageId=${receipt.packageId}`));
  });
});
