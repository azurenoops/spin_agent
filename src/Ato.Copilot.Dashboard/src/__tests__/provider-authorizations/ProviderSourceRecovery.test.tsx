import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import CspWizard from '../../features/csp-onboarding/CspWizard';
import * as api from '../../features/csp-onboarding/providerSetupApi';
import type { SetupState, UploadIntent } from '../../features/csp-onboarding/providerSetupApi';
import { setupResult, setupState } from './testData';
import { packageStatus } from '../package-imports/fixtures';
import '../package-imports/crypto';

vi.mock('../../features/csp-onboarding/providerSetupApi', () => ({
  getSetup: vi.fn(), saveDraft: vi.fn(), prepareUpload: vi.fn(), uploadSource: vi.fn(), reconcileReceipt: vi.fn(),
}));
let server: SetupState;
beforeEach(() => {
  vi.clearAllMocks();
  server = setupState('p-sources');
  vi.mocked(api.getSetup).mockImplementation(async () => structuredClone(server));
  vi.mocked(api.saveDraft).mockImplementation(async (_revision, fields) => {
    server.draft!.fields = structuredClone(fields); server.draft!.revision++;
    return setupResult(structuredClone(server));
  });
  vi.mocked(api.prepareUpload).mockImplementation(async (_revision, input) => {
    const intent: UploadIntent = { intentId: input.intentId, input, intentHash: 'A'.repeat(64), revision: 1,
      savedAt: '2026-09-30T12:00:00Z', receipt: null,
      reconciliation: { outcome: 'NotObserved', observedAt: '2026-09-30T12:00:00Z', nextAction: 'ReselectSameFiles' } };
    server.uploadIntents = [intent];
    server.draft!.fields.sources = { choice: 'Intents', intentIds: [input.intentId] };
    server.draft!.fields.currentScreen = 'p-uncertain'; server.draft!.revision++;
    return structuredClone(intent);
  });
  vi.mocked(api.uploadSource).mockRejectedValue(new Error('Response interrupted; receipt unknown.'));
  vi.mocked(api.reconcileReceipt).mockResolvedValue({
    outcome: 'NotObserved', observedAt: '2026-09-30T12:00:00Z', nextAction: 'ReselectSameFiles',
  });
});
async function beginUpload() {
  await screen.findByRole('heading', { name: 'Add available records' });
  fireEvent.change(screen.getByLabelText('Package name'), { target: { value: 'Synthetic package' } });
  fireEvent.change(screen.getByLabelText('Select source files'), {
    target: { files: [new File(['synthetic source'], 'synthetic.txt', { type: 'text/plain' })] },
  });
  fireEvent.change(screen.getByLabelText('Declared source classification'), { target: { value: 'Unclassified' } });
  fireEvent.click(screen.getByLabelText('These files contain only synthetic data.'));
  fireEvent.click(screen.getByRole('button', { name: 'Upload package' }));
  await screen.findByRole('alert');
}
describe('provider source request recovery', () => {
  it('registers an unassociated exact manifest before bytes and recovers receipt after remount without reupload', async () => {
    // Arrange
    const first = render(<MemoryRouter><CspWizard /></MemoryRouter>);
    await beginUpload();
    // Act
    first.unmount();
    render(<MemoryRouter initialEntries={['/onboarding/csp?reentry=resume']}><CspWizard /></MemoryRouter>);
    await screen.findByRole('heading', { name: 'Check the package receipt' });
    const receipt = packageStatus({ processingState: 'Processing' });
    server.uploadIntents[0]!.receipt = receipt;
    vi.mocked(api.reconcileReceipt).mockResolvedValue({ outcome: 'Confirmed', observedAt: '2026-09-30T12:01:00Z', receipt, nextAction: 'OpenReceipt' });
    fireEvent.click(screen.getByRole('button', { name: 'Check existing receipt' }));
    // Assert
    await screen.findByText(/Receipt confirmed · Revision/);
    expect(api.uploadSource).toHaveBeenCalledOnce();
    const input = vi.mocked(api.prepareUpload).mock.calls[0]![1];
    expect(input.associationMode).toBe('Unassociated');
    expect(input.context).toBeNull();
    expect(input.intentId.length).toBeLessThanOrEqual(100);
    expect(input.files[0]).toEqual(expect.objectContaining({ ordinal: 0, fileName: 'synthetic.txt', mediaType: 'text/plain' }));
    expect(vi.mocked(api.prepareUpload).mock.invocationCallOrder[0]).toBeLessThan(vi.mocked(api.uploadSource).mock.invocationCallOrder[0]!);
  });

  it('rejects changed bytes after restart and preserves the existing request key', async () => {
    // Arrange
    const first = render(<MemoryRouter><CspWizard /></MemoryRouter>);
    await beginUpload();
    const original = server.uploadIntents[0]!.intentId;
    first.unmount();
    render(<MemoryRouter initialEntries={['/onboarding/csp?reentry=resume']}><CspWizard /></MemoryRouter>);
    await screen.findByRole('heading', { name: 'Check the package receipt' });
    // Act
    fireEvent.change(screen.getByLabelText('Reselect the same source files'), {
      target: { files: [new File(['different source'], 'synthetic.txt', { type: 'text/plain' })] },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Retry same upload' }));
    // Assert
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('do not match the saved source manifest'));
    expect(api.uploadSource).toHaveBeenCalledOnce();
    expect(api.prepareUpload).toHaveBeenCalledOnce();
    expect(server.uploadIntents[0]!.intentId).toBe(original);
    expect(screen.getByRole('button', { name: 'Skip sources for now' })).toBeDisabled();
  });

  it('does not label a response received when the authoritative intent still has no matching receipt', async () => {
    // Arrange
    vi.mocked(api.uploadSource).mockResolvedValue(packageStatus({ packageId: 'different-package', operationId: 'different-package' }));
    render(<MemoryRouter><CspWizard /></MemoryRouter>);
    await beginUpload();
    // Act
    await screen.findByRole('heading', { name: 'Check the package receipt' });
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent(/does not match.*retained request/i);
    expect(screen.queryByText('Receipt confirmed; analysis and publication have their own status.')).not.toBeInTheDocument();
    expect(server.uploadIntents[0]!.receipt).toBeNull();
  });

  it('does not replace an unresolved intent with a mismatched reconciliation response', async () => {
    // Arrange
    render(<MemoryRouter><CspWizard /></MemoryRouter>);
    await beginUpload();
    vi.mocked(api.reconcileReceipt).mockResolvedValue({
      outcome: 'Confirmed', observedAt: '2026-09-30T12:00:00Z', receipt: packageStatus(), nextAction: 'OpenReceipt',
    });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Check existing receipt' }));
    // Assert
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(/does not match.*retained request/i));
    expect(screen.getByRole('heading', { name: 'Check the package receipt' })).toBeInTheDocument();
  });
});
