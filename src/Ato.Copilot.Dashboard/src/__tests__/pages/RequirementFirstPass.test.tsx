import { act, fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import RequirementCoveragePanel from '../../features/narratives/RequirementCoveragePanel';
import * as api from '../../api/requirementCoverage';
import { MemoryRouter } from 'react-router-dom';
import { StrictMode } from 'react';

vi.mock('../../api/requirementCoverage', () => ({
  getRequirementCoverage: vi.fn(), generateRequirementFirstPass: vi.fn(), saveRequirementResponses: vi.fn(),
  reviewRequirementResponses: vi.fn(), proposeEnhancement: vi.fn(), acceptEnhancement: vi.fn(), returnEnhancement: vi.fn(),
}));
vi.mock('../../api/evidence', () => ({ listEvidence: vi.fn() }));
const detail = {
  systemId: 'system-a', controlId: 'PT-2', framework: 'SYNTHETIC', catalogVersion: '1', sourceUri: null,
  baselineRevision: 1, narrativeVersion: 2, parent: null, enhancements: [],
  requirements: [{ id: 'pt2.a', label: 'a', text: 'Determine {{ insert: param, authority }} for recorded processing.',
    responses: [], responseState: 'Missing', reviewed: false, evidenceGap: true }],
  parameters: [{ id: 'authority', definition: '{"id":"authority","label":"Legal authority"}' }],
  parameterValues: {}, gaps: [], proposals: [], canAuthor: true, canReview: false, canBind: false,
};
const suggestion: api.RequirementFirstPass = {
  systemId: 'system-a', controlId: 'PT-2', kind: 'Policy', expectedVersion: 2, contextHash: 'hash', token: 'protected-proof',
  generatedAt: '2026-10-05T18:00:00Z', sources: [{ id: 'profile:a', kind: 'System profile', title: 'Recorded Mission',
    version: '1', contentHash: 'source-hash', reviewState: 'Working source' }],
  responses: [{ statementId: 'pt2.a', response: 'Recorded first-pass response; legal authority still needs review.',
    sourceIds: ['profile:a'], explanation: 'Based on the entered mission.' }],
  parameters: [], questions: ['Record the actual legal authority.'], conflicts: [],
};
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getRequirementCoverage).mockResolvedValue(detail);
  vi.mocked(api.generateRequirementFirstPass).mockResolvedValue(suggestion);
});
const setup = () => render(<MemoryRouter><RequirementCoveragePanel systemId="system-a" controlId="PT-2" kind="policy"
  onNavigate={vi.fn()} onChanged={vi.fn()} /></MemoryRouter>);
describe('User-friendly requirement first pass', () => {
  it('prepares a preview and named placeholders without automatically saving or fabricating authority', async () => {
    // Arrange / Act
    setup();
    // Assert
    expect(await screen.findByText('Determine [Legal authority — not recorded] for recorded processing.')).toBeVisible();
    expect(await screen.findByText('Recorded first-pass response; legal authority still needs review.')).toBeVisible();
    expect(screen.getByText('Record the actual legal authority.')).toBeVisible();
    expect(api.generateRequirementFirstPass).toHaveBeenCalledWith('system-a', 'PT-2', expect.objectContaining({
      expectedVersion: 2, expectedBaselineRevision: 1, kind: 'Policy',
    }), expect.any(AbortSignal));
    expect(api.saveRequirementResponses).not.toHaveBeenCalled();
    expect(screen.getByRole('textbox', { name: 'Policy response for a' })).toHaveValue('');
  });
  it('uses suggestions only in empty fields and retains source proof for explicit draft save', async () => {
    // Arrange
    vi.mocked(api.saveRequirementResponses).mockResolvedValue(detail);
    setup();
    await screen.findByText('Recorded first-pass response; legal authority still needs review.');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Use first pass in empty fields' }));
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Save requirement responses' })); });
    // Assert
    expect(api.saveRequirementResponses).toHaveBeenCalledWith('system-a', 'PT-2', expect.objectContaining({
      firstPassToken: 'protected-proof', responses: [{ statementId: 'pt2.a', kind: 'Policy',
        response: suggestion.responses[0]!.response, evidence: [] }],
    }));
  });
  it('does not overwrite text entered while generation was pending', async () => {
    // Arrange
    let finish!: (value: typeof suggestion) => void;
    vi.mocked(api.generateRequirementFirstPass).mockImplementation(() => new Promise(resolve => { finish = resolve; }));
    setup();
    const editor = await screen.findByRole('textbox', { name: 'Policy response for a' });
    fireEvent.change(editor, { target: { value: 'My own draft' } });
    // Act
    await act(async () => finish(suggestion));
    fireEvent.click(screen.getByRole('button', { name: 'Use first pass in empty fields' }));
    // Assert
    expect(editor).toHaveValue('My own draft');
  });
  it('keeps failures visible and never calls generation for a viewer', async () => {
    // Arrange
    vi.mocked(api.getRequirementCoverage).mockResolvedValue({ ...detail, canAuthor: false });
    // Act
    setup();
    // Assert
    expect(await screen.findByText('Determine [Legal authority — not recorded] for recorded processing.')).toBeVisible();
    expect(api.generateRequirementFirstPass).not.toHaveBeenCalled();
  });
  it('shows AI failure and retry without losing a manual response', async () => {
    // Arrange
    vi.mocked(api.generateRequirementFirstPass).mockRejectedValueOnce(new Error('AI unavailable')).mockResolvedValue(suggestion);
    setup();
    await screen.findByRole('alert');
    const editor = screen.getByRole('textbox', { name: 'Policy response for a' });
    fireEvent.change(editor, { target: { value: 'Manual draft stays' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry first pass' }));
    await screen.findByText(suggestion.responses[0]!.response);
    fireEvent.click(screen.getByRole('button', { name: 'Use first pass in empty fields' }));
    // Assert
    expect(editor).toHaveValue('Manual draft stays');
    expect(screen.getByRole('status')).toHaveTextContent('No empty fields');
  });
  it('renders a recorded authority value and keeps the original token in source disclosure', async () => {
    // Arrange
    vi.mocked(api.getRequirementCoverage).mockResolvedValue({ ...detail, canAuthor: false, parameterValues: { authority: 'Recorded legal authority' } });
    // Act
    setup();
    // Assert
    expect(await screen.findByText('Determine Recorded legal authority for recorded processing.')).toBeVisible();
    const raw = screen.getByText(detail.requirements[0]!.text);
    expect(raw).not.toBeVisible();
    fireEvent.click(screen.getByText('View original requirement source'));
    expect(raw.closest('details')).toBeInTheDocument();
  });
  it('survives StrictMode cleanup and never applies a late result to another statement type', async () => {
    // Arrange
    const finish: ((value: api.RequirementFirstPass) => void)[] = [];
    vi.mocked(api.generateRequirementFirstPass).mockImplementation(() => new Promise(resolve => { finish.push(resolve); }));
    const props = { systemId: 'system-a', controlId: 'PT-2', onNavigate: vi.fn(), onChanged: vi.fn() };
    const view = render(<StrictMode><MemoryRouter><RequirementCoveragePanel {...props} kind="policy" /></MemoryRouter></StrictMode>);
    await screen.findByRole('textbox', { name: 'Policy response for a' });
    // Act
    view.rerender(<StrictMode><MemoryRouter><RequirementCoveragePanel {...props} kind="technical" /></MemoryRouter></StrictMode>);
    await screen.findByRole('textbox', { name: 'Technical response for a' });
    await act(async () => { for (const resolve of finish) resolve(suggestion); });
    // Assert
    expect(screen.getByRole('textbox', { name: 'Technical response for a' })).toHaveValue('');
    expect(api.saveRequirementResponses).not.toHaveBeenCalled();
    expect(api.generateRequirementFirstPass).toHaveBeenCalledWith('system-a', 'PT-2', expect.objectContaining({ kind: 'Technical' }), expect.any(AbortSignal));
  });
});
