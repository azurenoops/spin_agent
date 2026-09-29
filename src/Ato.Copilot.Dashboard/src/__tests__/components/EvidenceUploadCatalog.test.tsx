import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import EvidenceUploadDialog from '../../components/EvidenceUploadDialog';
import apiClient from '../../api/client';
import { uploadEvidence } from '../../api/evidence';

vi.mock('../../api/client', () => ({ default: { get: vi.fn() } }));
vi.mock('../../api/evidence', () => ({ uploadEvidence: vi.fn() }));
vi.mock('../../components/permissions/useSystemMutationPermission', () => ({ useSystemMutationPermission: () => true }));
beforeEach(() => vi.clearAllMocks());
afterEach(cleanup);
const mount = () => render(<MemoryRouter><EvidenceUploadDialog systemId="system-a" onClose={vi.fn()} onUploaded={vi.fn()} /></MemoryRouter>);
it('surfaces a failed control lookup with retry instead of claiming loading forever', async () => {
  // Arrange
  vi.mocked(apiClient.get).mockRejectedValueOnce(new Error('Controls unavailable'));
  // Act
  mount();
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('Controls unavailable');
  expect(screen.getByRole('button', { name: 'Retry controls' })).toBeVisible();
});
it('requires a real upload target rather than sending a known-invalid request', async () => {
  // Arrange
  vi.mocked(apiClient.get).mockResolvedValue({ data: { items: [{ controlId: 'AC-2', controlTitle: 'Account Management' }] } });
  const view = mount();
  await act(async () => {});
  // Act
  fireEvent.change(view.container.querySelector('input[type="file"]')!, { target: { files: [new File(['{}'], 'record.json', { type: 'application/json' })] } });
  // Assert
  expect(screen.getByRole('button', { name: 'Upload Evidence' })).toBeDisabled();
  expect(uploadEvidence).not.toHaveBeenCalled();
});
