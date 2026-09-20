import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useParams: () => ({ id: 'system-1' }) };
});

vi.mock('../../api/narratives', () => ({
  getNarratives: vi.fn(),
  bulkUpdateNarratives: vi.fn(),
  saveNarrative: vi.fn(),
  regenerateNarrative: vi.fn(),
  getAvailableControls: vi.fn(),
  createNarrative: vi.fn(),
}));
vi.mock('../../api/businessContext', () => ({ getBusinessContext: vi.fn().mockResolvedValue(null) }));
vi.mock('../../hooks/usePolling', () => ({ usePolling: vi.fn() }));
vi.mock('../../hooks/useSettings', () => ({
  useSettings: () => ({ settings: { role: 'ISSO', sharePointSiteUrl: '', sourceDocuments: '' } }),
}));
vi.mock('../../components/EvidenceSection', () => ({ default: () => null }));
vi.mock('../../features/compliance/components/ValidationEvidencePanel', () => ({ default: () => null }));

import * as narrativeApi from '../../api/narratives';
import { usePolling } from '../../hooks/usePolling';
import Narratives from '../../pages/Narratives';

const mockRegenerate = narrativeApi.regenerateNarrative as ReturnType<typeof vi.fn>;
const mockUsePolling = usePolling as ReturnType<typeof vi.fn>;
const refresh = vi.fn();

beforeEach(() => {
  vi.clearAllMocks();
  mockUsePolling.mockReturnValue({
    data: [{
      id: 'implementation-1',
      controlId: 'AC-1',
      family: 'AC',
      narrative: 'Original technical narrative',
      policyNarrative: 'Existing policy narrative',
      technicalNarrative: 'Original technical narrative',
      migratedFromLegacy: false,
      implementationStatus: 'Implemented',
      approvalStatus: 'Draft',
      authoredBy: 'test-user',
      authoredAt: '2026-01-01T00:00:00Z',
      version: 1,
      isAutoPopulated: false,
      aiSuggested: false,
    }],
    loading: false,
    error: null,
    refresh,
  });
});

describe('Narratives regeneration', () => {
  it('places regenerated content in the Technical editor and preserves Policy', async () => {
    // Arrange
    mockRegenerate.mockResolvedValue('Regenerated technical narrative');
    render(<Narratives />);
    fireEvent.click(screen.getByTitle('Expand'));

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Regenerate' }));

    // Assert
    await waitFor(() => {
      expect(screen.getByLabelText('Technical narrative for AC-1')).toHaveValue('Regenerated technical narrative');
    });
    expect(screen.getByLabelText('Policy narrative for AC-1')).toHaveValue('Existing policy narrative');
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it('shows a configuration error without replacing either editor', async () => {
    // Arrange
    mockRegenerate.mockRejectedValue({ response: { status: 503 } });
    render(<Narratives />);
    fireEvent.click(screen.getByTitle('Expand'));

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Regenerate' }));

    // Assert
    expect(await screen.findByText('Regeneration failed for AC-1: AI service is not configured.')).toBeInTheDocument();
    expect(screen.getByLabelText('Policy narrative for AC-1')).toHaveValue('Existing policy narrative');
    expect(screen.getByLabelText('Technical narrative for AC-1')).toHaveValue('Original technical narrative');
    expect(refresh).not.toHaveBeenCalled();
  });
});