import { act, fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useParams: () => ({ id: 'system-1' }) };
});
vi.mock('../../api/narratives', () => ({
  getNarratives: vi.fn(), bulkUpdateNarratives: vi.fn(), saveNarrative: vi.fn(),
  regenerateNarrative: vi.fn(), getAvailableControls: vi.fn(), createNarrative: vi.fn(),
}));
vi.mock('../../api/businessContext', () => ({
  getBusinessContext: vi.fn().mockResolvedValue(null),
  getFlaggedControls: vi.fn().mockResolvedValue([]),
}));
vi.mock('../../hooks/usePolling', () => ({ usePolling: vi.fn() }));
vi.mock('../../hooks/useSettings', () => ({
  useSettings: () => ({ settings: { role: 'ISSO', sharePointSiteUrl: '', sourceDocuments: '' } }),
}));
vi.mock('../../components/EvidenceSection', () => ({ default: () => null }));
vi.mock('../../features/compliance/components/ValidationEvidencePanel', () => ({ default: () => null }));

import { usePolling } from '../../hooks/usePolling';
import Narratives from '../../pages/Narratives';

beforeEach(() => vi.clearAllMocks());

describe('Narrative provenance', () => {
  it.each([
    { source: 'template', aiSuggested: false, isAutoPopulated: true, technicalNarrative: 'Template', migratedFromLegacy: false, expectedAi: 0, badge: 'Auto' },
    { source: 'model with human Policy', aiSuggested: true, isAutoPopulated: true, technicalNarrative: 'Model draft', migratedFromLegacy: false, expectedAi: 1, badge: 'AI' },
    { source: 'fallback', aiSuggested: false, isAutoPopulated: true, technicalNarrative: 'Fallback', migratedFromLegacy: false, expectedAi: 0, badge: 'Auto' },
    { source: 'migrated', aiSuggested: true, isAutoPopulated: true, technicalNarrative: 'Legacy', migratedFromLegacy: true, expectedAi: 0, badge: 'Migrated' },
    { source: 'human', aiSuggested: false, isAutoPopulated: false, technicalNarrative: 'Manual', migratedFromLegacy: false, expectedAi: 0, badge: null },
    { source: 'missing Technical', aiSuggested: true, isAutoPopulated: false, technicalNarrative: null, migratedFromLegacy: false, expectedAi: 0, badge: null },
    { source: 'blank Technical', aiSuggested: true, isAutoPopulated: false, technicalNarrative: '  ', migratedFromLegacy: false, expectedAi: 0, badge: null },
  ])('labels $source without implying approval', async ({ expectedAi, badge, ...provenance }) => {
    // Arrange
    (usePolling as ReturnType<typeof vi.fn>).mockReturnValue({
      data: [{
        id: 'implementation-1', controlId: 'AC-1', family: 'AC', narrative: 'Legacy',
        policyNarrative: 'Human policy', implementationStatus: 'Planned', approvalStatus: 'Draft',
        authoredBy: 'test-user', authoredAt: '2026-01-01T00:00:00Z', version: 1,
        ...provenance,
      }],
      loading: false, error: null, refresh: vi.fn(),
    });

    // Act
    await act(async () => { render(<Narratives />); });
    await act(async () => { fireEvent.click(screen.getByTitle('Expand')); });

    // Assert
    expect(screen.getByText('AI Suggested').parentElement).toHaveTextContent(String(expectedAi));
    expect(screen.queryAllByTitle('AI-assisted Technical narrative')).toHaveLength(expectedAi);
    expect(screen.queryByText('AI-assisted Technical narrative')).toBe(expectedAi ? screen.getByText('AI-assisted Technical narrative') : null);
    if (badge) expect(screen.getByText(badge, { exact: true })).toBeInTheDocument();
    expect(screen.getByLabelText('Policy narrative for AC-1')).toHaveValue('Human policy');
    expect(screen.getByLabelText('Technical narrative for AC-1')).toHaveValue(provenance.technicalNarrative ?? '');
    expect(screen.getAllByText('Planned').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Draft').length).toBeGreaterThan(0);
  });
});