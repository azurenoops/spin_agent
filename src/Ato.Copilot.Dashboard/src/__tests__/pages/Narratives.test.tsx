import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const route = vi.hoisted(() => ({ systemId: 'system-1' }));
const legacySources = vi.hoisted(() => ({ sharePointSiteUrl: '', sourceDocuments: '' }));
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
  useSettings: () => ({ settings: { role: 'ISSO', ...legacySources } }),
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

it('does not select generation sources from legacy browser settings (#1001)', async () => {
  // Arrange
  legacySources.sharePointSiteUrl = 'https://example.invalid/policies';
  legacySources.sourceDocuments = 'old-policy.docx';
  mockRegenerate.mockResolvedValue('Generated draft');
  render(<Narratives />);
  // Act
  fireEvent.click(screen.getByTitle('Expand'));
  fireEvent.click(screen.getByRole('button', { name: /Regenerate/i }));
  // Assert
  await waitFor(() => expect(mockRegenerate).toHaveBeenCalled());
  expect(mockRegenerate).toHaveBeenCalledWith('system-1', 'AC-1', { expectedVersion: 1 });
  expect(screen.queryByText(/configured source|No document sources configured/i)).not.toBeInTheDocument();
});

beforeEach(() => {
  vi.clearAllMocks();
  legacySources.sharePointSiteUrl = '';
  legacySources.sourceDocuments = '';
  route.systemId = 'system-1';
  vi.mocked(narrativeApi.saveNarrative).mockReset().mockResolvedValue({ currentVersion: 2 });
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
  it('keeps a compact Library entry point for screens without the system sidebar', async () => {
    // Arrange
    const openLibrary = vi.fn();
    render(<Narratives onOpenLibrary={openLibrary} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Narrative Library', hidden: true }));
    // Assert
    expect(openLibrary).toHaveBeenCalledOnce();
    await waitFor(() => expect(businessContextApi.getFlaggedControls).toHaveBeenCalled());
  });

  it.each(['Policy', 'Technical'] as const)('generates a %s proposal from its expanded control without changing active text', async type => {
    // Arrange
    const generate = vi.fn().mockResolvedValue(undefined);
    render(<Narratives onGenerateDraft={generate} canGenerate />);
    fireEvent.click(screen.getByTitle('Expand'));
    // Act
    fireEvent.click(screen.getByRole('button', { name: `Generate ${type} draft for AC-1` }));
    // Assert
    await waitFor(() => expect(generate).toHaveBeenCalledWith('AC-1', 1, type));
    expect(screen.getByLabelText('Policy narrative for AC-1')).toHaveAttribute('readonly');
    expect(screen.getByLabelText('Technical narrative for AC-1')).toHaveAttribute('readonly');
    expect(screen.getByLabelText('Technical narrative for AC-1')).toHaveValue('Original technical narrative');
    expect(mockRegenerate).not.toHaveBeenCalled();
    expect(narrativeApi.saveNarrative).not.toHaveBeenCalled();
  });

  it.each([
    { canGenerate: false, approvalStatus: 'Draft' },
    { canGenerate: true, approvalStatus: 'UnderReview' },
  ])('disables both draft actions when permission or review state forbids generation: %j', async ({ canGenerate, approvalStatus }) => {
    // Arrange
    const polling = mockUsePolling.getMockImplementation()!();
    polling.data[0].approvalStatus = approvalStatus;
    mockUsePolling.mockReturnValue(polling);
    const generate = vi.fn();
    render(<Narratives onGenerateDraft={generate} canGenerate={canGenerate} />);
    // Act
    fireEvent.click(screen.getByTitle('Expand'));
    // Assert
    expect(screen.getByRole('button', { name: 'Generate Policy draft for AC-1' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Generate Technical draft for AC-1' })).toBeDisabled();
    expect(generate).not.toHaveBeenCalled();
    await screen.findByText('No business context provided');
  });

  it('keeps both draft actions disabled while either type is generating', async () => {
    // Arrange
    let complete!: () => void;
    const generate = vi.fn().mockReturnValue(new Promise<void>(resolve => { complete = resolve; }));
    render(<Narratives onGenerateDraft={generate} canGenerate />);
    fireEvent.click(screen.getByTitle('Expand'));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Generate Policy draft for AC-1' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Generate Policy draft for AC-1' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Generate Technical draft for AC-1' })).toBeDisabled();
    expect(generate).toHaveBeenCalledTimes(1);
    // Act
    await act(async () => complete());
    // Assert
    expect(screen.getByRole('button', { name: 'Generate Technical draft for AC-1' })).toBeEnabled();
  });

  it('preserves the draft and reports a stale save without showing Saved', async () => {
    // Arrange
    vi.mocked(narrativeApi.saveNarrative).mockRejectedValue({
      errorCode: 'CONCURRENCY_CONFLICT', error: 'Reload before saving.',
    });
    render(<Narratives />);
    fireEvent.click(screen.getByTitle('Expand'));
    const editor = screen.getByLabelText('Policy narrative for AC-1');

    // Act
    fireEvent.change(editor, { target: { value: 'Unsaved policy' } });
    fireEvent.blur(editor);

    // Assert
    expect(await screen.findByText(/Save failed for AC-1: Reload before saving/)).toBeInTheDocument();
    expect(editor).toHaveValue('Unsaved policy');
    expect(narrativeApi.saveNarrative).toHaveBeenCalledWith('system-1', 'AC-1', {
      policyNarrative: 'Unsaved policy', expectedVersion: 1,
    });
    expect(screen.queryByText(/Saved/)).not.toBeInTheDocument();
    expect(refresh).not.toHaveBeenCalled();
  });

  it('makes under-review narratives read-only and disables regeneration', async () => {
    // Arrange
    const polling = mockUsePolling.getMockImplementation()!();
    polling.data[0].approvalStatus = 'UnderReview';
    mockUsePolling.mockReturnValue(polling);

    // Act
    render(<Narratives />);
    fireEvent.click(screen.getByTitle('Expand'));

    // Assert
    expect(screen.getByLabelText('Policy narrative for AC-1')).toHaveAttribute('readonly');
    expect(screen.getByLabelText('Technical narrative for AC-1')).toHaveAttribute('readonly');
    expect(screen.getByRole('button', { name: 'Regenerate' })).toBeDisabled();
    await screen.findByText('No business context provided');
  });

  it('keeps the draft version when polling observes a concurrent edit', async () => {
    // Arrange
    const view = render(<Narratives />);
    fireEvent.click(screen.getByTitle('Expand'));
    const editor = screen.getByLabelText('Policy narrative for AC-1');
    fireEvent.change(editor, { target: { value: 'Local draft' } });
    const polling = mockUsePolling.getMockImplementation()!();
    mockUsePolling.mockReturnValue({ ...polling, data: [{ ...polling.data[0], version: 4 }] });
    view.rerender(<Narratives />);

    // Act
    fireEvent.blur(editor);

    // Assert
    await waitFor(() => expect(narrativeApi.saveNarrative).toHaveBeenCalledWith('system-1', 'AC-1', {
      policyNarrative: 'Local draft', expectedVersion: 1,
    }));
    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1));
  });

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