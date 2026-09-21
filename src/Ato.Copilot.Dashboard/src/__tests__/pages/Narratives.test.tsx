import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const route = vi.hoisted(() => ({ systemId: 'system-1' }));
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useParams: () => ({ id: route.systemId }) };
});

vi.mock('../../api/narratives', () => ({
  getNarratives: vi.fn(),
  bulkUpdateNarratives: vi.fn(),
  saveNarrative: vi.fn(),
  regenerateNarrative: vi.fn(),
  getAvailableControls: vi.fn(),
  createNarrative: vi.fn(),
}));
vi.mock('../../api/businessContext', () => ({ getBusinessContext: vi.fn(), getFlaggedControls: vi.fn() }));
vi.mock('../../hooks/usePolling', () => ({ usePolling: vi.fn() }));
vi.mock('../../hooks/useSettings', () => ({
  useSettings: () => ({ settings: { role: 'ISSO', sharePointSiteUrl: '', sourceDocuments: '' } }),
}));
vi.mock('../../components/EvidenceSection', () => ({ default: () => null }));
vi.mock('../../features/compliance/components/ValidationEvidencePanel', () => ({ default: () => null }));

import * as narrativeApi from '../../api/narratives';
import * as businessContextApi from '../../api/businessContext';
import { usePolling } from '../../hooks/usePolling';
import Narratives from '../../pages/Narratives';

const mockRegenerate = narrativeApi.regenerateNarrative as ReturnType<typeof vi.fn>;
const mockUsePolling = usePolling as ReturnType<typeof vi.fn>;
const refresh = vi.fn();

beforeEach(() => {
  vi.clearAllMocks();
  route.systemId = 'system-1';
  vi.mocked(businessContextApi.getBusinessContext).mockReset().mockResolvedValue(null);
  vi.mocked(businessContextApi.getFlaggedControls).mockReset().mockResolvedValue([]);
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

const ownerDraft = {
  id: 'draft-1', controlId: 'AC-1', content: 'Owner mission context',
  governanceStatus: 'Draft' as const, authoredBy: 'Synthetic owner',
  authoredAt: '2026-01-01T00:00:00Z', reviewerComments: null,
};
const flaggedControl = { controlId: 'AC-1', controlTitle: 'Policy', hasDraft: false };

describe('Business context handoff', () => {
  it.each([true, false])('shows Awaiting only for positively flagged absence: %s', async (flagged) => {
    // Arrange
    vi.mocked(businessContextApi.getFlaggedControls).mockResolvedValue(flagged ? [flaggedControl] : []);
    render(<Narratives />);
    // Act
    fireEvent.click(screen.getByTitle('Expand'));
    // Assert
    expect(await screen.findByText(flagged
      ? 'Awaiting business context from Mission Owner'
      : 'No business context provided')).toBeInTheDocument();
    if (!flagged) expect(screen.queryByText(/Awaiting business context/)).not.toBeInTheDocument();
  });

  it.each([404, 403, 500, undefined])('keeps draft retrieval failures distinct and retryable: %s', async (status) => {
    // Arrange
    vi.mocked(businessContextApi.getFlaggedControls).mockResolvedValue([flaggedControl]);
    vi.mocked(businessContextApi.getBusinessContext).mockRejectedValueOnce({ response: { status } }).mockResolvedValue(ownerDraft);
    render(<Narratives />);
    // Act
    fireEvent.click(screen.getByTitle('Expand'));
    // Assert
    expect(await screen.findByText('Unable to load business context')).toBeInTheDocument();
    expect(screen.queryByText(/Awaiting business context/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry business context' }));
    expect(await screen.findByText(ownerDraft.content)).toBeInTheDocument();
    expect(screen.getByLabelText('Policy narrative for AC-1')).toHaveValue('Existing policy narrative');
    expect(screen.getByLabelText('Technical narrative for AC-1')).toHaveValue('Original technical narrative');
  });

  it('shows loading, then a draft even when flags fail; copies only on explicit action', async () => {
    // Arrange
    let resolveDraft!: (value: typeof ownerDraft) => void;
    vi.mocked(businessContextApi.getBusinessContext).mockReturnValue(new Promise(resolve => { resolveDraft = resolve; }));
    vi.mocked(businessContextApi.getFlaggedControls).mockRejectedValue(new Error('Unavailable'));
    render(<Narratives />);
    // Act
    fireEvent.click(screen.getByTitle('Expand'));
    // Assert
    expect(screen.getByText('Loading business context...')).toBeInTheDocument();
    expect(screen.queryByText(/Awaiting business context/)).not.toBeInTheDocument();
    await act(async () => resolveDraft(ownerDraft));
    expect(await screen.findByText(ownerDraft.content)).toBeInTheDocument();
    expect(screen.getByLabelText('Policy narrative for AC-1')).toHaveValue('Existing policy narrative');
    fireEvent.click(screen.getByRole('button', { name: 'Copy to Narrative' }));
    expect(screen.getByLabelText('Policy narrative for AC-1')).toHaveValue(`Existing policy narrative\n\n${ownerDraft.content}`);
    expect(screen.getByLabelText('Technical narrative for AC-1')).toHaveValue('Original technical narrative');
  });

  it('retries failed flags before inferring awaiting and refreshes an existing draft', async () => {
    // Arrange
    vi.mocked(businessContextApi.getFlaggedControls).mockRejectedValueOnce(new Error('Unavailable')).mockResolvedValue([flaggedControl]);
    render(<Narratives />);
    fireEvent.click(screen.getByTitle('Expand'));
    // Act
    expect(await screen.findByText('Unable to load business context flags')).toBeInTheDocument();
    expect(screen.queryByText(/Awaiting business context/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry business context flags' }));
    // Assert
    expect(await screen.findByText('Awaiting business context from Mission Owner')).toBeInTheDocument();
    vi.mocked(businessContextApi.getBusinessContext).mockResolvedValue(ownerDraft);
    fireEvent.click(screen.getByRole('button', { name: 'Refresh business context' }));
    expect(await screen.findByText(ownerDraft.content)).toBeInTheDocument();
    vi.mocked(businessContextApi.getBusinessContext).mockResolvedValue({ ...ownerDraft, content: 'Revised context' });
    fireEvent.click(screen.getByRole('button', { name: 'Refresh business context' }));
    expect(await screen.findByText('Revised context')).toBeInTheDocument();
    expect(screen.queryByText(ownerDraft.content)).not.toBeInTheDocument();
  });

  it('ignores a previous system response after navigation', async () => {
    // Arrange
    let resolveOld!: (value: typeof ownerDraft) => void;
    vi.mocked(businessContextApi.getBusinessContext).mockReturnValueOnce(new Promise(resolve => { resolveOld = resolve; })).mockResolvedValue(null);
    const view = render(<Narratives />);
    fireEvent.click(screen.getByTitle('Expand'));
    // Act
    route.systemId = 'system-2';
    view.rerender(<Narratives />);
    await screen.findByText('No business context provided');
    await act(async () => resolveOld(ownerDraft));
    // Assert
    expect(screen.queryByText(ownerDraft.content)).not.toBeInTheDocument();
    expect(businessContextApi.getBusinessContext).toHaveBeenLastCalledWith('system-2', 'AC-1');
  });
});