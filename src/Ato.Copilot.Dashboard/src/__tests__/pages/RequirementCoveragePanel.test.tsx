import { act, fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import RequirementCoveragePanel from '../../features/narratives/RequirementCoveragePanel';
import * as api from '../../api/requirementCoverage';
import * as evidenceApi from '../../api/evidence';
import { MemoryRouter } from 'react-router-dom';

vi.mock('../../api/requirementCoverage', () => ({
  getRequirementCoverage: vi.fn(), saveRequirementResponses: vi.fn(),
  reviewRequirementResponses: vi.fn(), proposeEnhancement: vi.fn(), acceptEnhancement: vi.fn(), returnEnhancement: vi.fn(),
  getRequirementCatalogs: vi.fn(), bindRequirementCatalog: vi.fn(),
  generateRequirementFirstPass: vi.fn(),
}));
vi.mock('../../api/evidence', () => ({ listEvidence: vi.fn().mockResolvedValue({ items: [], totalCount: 0 }) }));

const detail = {
  systemId: 'system-1', controlId: 'AC-11', framework: 'SYNTHETIC', catalogVersion: 'test-1',
  sourceUri: 'https://example.invalid/catalog', baselineRevision: 1, narrativeVersion: 2,
  parent: null, enhancements: [{ controlId: 'AC-11(1)', title: 'Synthetic concealment', selected: true, hasNarrative: false }],
  requirements: [{ id: 'source-a', label: 'a.', text: 'Synthetic source text', responses: [], responseState: 'Missing', reviewed: false, evidenceGap: true }],
  parameters: [], parameterValues: {}, gaps: ['Existing narratives have unreviewed requirement mappings.'],
  proposals: [], canAuthor: false, canReview: false, canBind: false,
};
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getRequirementCoverage).mockResolvedValue(detail);
  vi.mocked(api.generateRequirementFirstPass).mockImplementation(async (systemId, controlId, input) => ({
    systemId, controlId, kind: input.kind, expectedVersion: input.expectedVersion, contextHash: 'synthetic-hash',
    token: 'synthetic-proof', generatedAt: '2026-10-05T18:00:00Z', sources: [], responses: [], parameters: [],
    questions: ['Synthetic source context has no first-pass suggestion.'], conflicts: [],
  }));
});

describe('Requirement coverage', () => {
  it('shows source requirements and gaps without granting viewer mutation controls', async () => {
    // Arrange / Act
    render(<RequirementCoveragePanel systemId="system-1" controlId="AC-11" kind="policy" onNavigate={vi.fn()} onChanged={vi.fn()} />);

    // Assert
    expect(await screen.findByText('Synthetic source text')).toBeVisible();
    expect(screen.getByText(/Response needed/)).toBeVisible();
    expect(screen.getByText(/unreviewed requirement mappings/)).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Save requirement responses' })).not.toBeInTheDocument();
    expect(screen.queryByText('Satisfied')).not.toBeInTheDocument();
  });

  it('links selected enhancements even when their narratives are missing', async () => {
    // Arrange
    const navigate = vi.fn();
    render(<RequirementCoveragePanel systemId="system-1" controlId="AC-11" kind="policy" onNavigate={navigate} onChanged={vi.fn()} />);

    // Act
    fireEvent.click(await screen.findByRole('button', { name: /AC-11\(1\).*Synthetic concealment/ }));

    // Assert
    expect(navigate).toHaveBeenCalledWith('AC-11(1)');
    expect(screen.getByText(/Selected.*narrative missing/)).toBeVisible();
  });

  it('shows parent navigation from catalog relationships', async () => {
    // Arrange
    vi.mocked(api.getRequirementCoverage).mockResolvedValue({ ...detail,
      controlId: 'AC-11(1)', enhancements: [], parent: { controlId: 'AC-11', title: 'Synthetic parent', selected: true, hasNarrative: true } });
    const navigate = vi.fn();
    render(<RequirementCoveragePanel systemId="system-1" controlId="AC-11(1)" kind="technical" onNavigate={navigate} onChanged={vi.fn()} />);

    // Act
    fireEvent.click(await screen.findByRole('button', { name: /Parent control: AC-11/ }));

    // Assert
    expect(navigate).toHaveBeenCalledWith('AC-11');
  });

  it('sends explicit draft responses with the current revision, never coverage inferred from prose', async () => {
    // Arrange
    vi.mocked(api.getRequirementCoverage).mockResolvedValue({ ...detail, canAuthor: true });
    vi.mocked(api.saveRequirementResponses).mockResolvedValue({ ...detail, canAuthor: true });
    render(<RequirementCoveragePanel systemId="system-1" controlId="AC-11" kind="policy" onNavigate={vi.fn()} onChanged={vi.fn()} />);
    const editor = await screen.findByRole('textbox', { name: 'Policy response for a.' });

    // Act
    fireEvent.change(editor, { target: { value: 'Synthetic response needing review' } });
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Save requirement responses' })); });

    // Assert
    expect(api.saveRequirementResponses).toHaveBeenCalledWith('system-1', 'AC-11', {
      expectedVersion: 2, parameters: {},
      responses: [{ statementId: 'source-a', kind: 'Policy', response: 'Synthetic response needing review', evidence: [] }],
    });
  });

  it('keeps a failed save visible and retains the unsaved response', async () => {
    // Arrange
    vi.mocked(api.getRequirementCoverage).mockResolvedValue({ ...detail, canAuthor: true });
    vi.mocked(api.saveRequirementResponses).mockRejectedValue({ error: 'CONCURRENCY_CONFLICT: Reload before saving.' });
    render(<RequirementCoveragePanel systemId="system-1" controlId="AC-11" kind="policy" onNavigate={vi.fn()} onChanged={vi.fn()} />);
    const editor = await screen.findByRole('textbox', { name: 'Policy response for a.' });
    fireEvent.change(editor, { target: { value: 'Retain my draft' } });

    // Act
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Save requirement responses' })); });

    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('CONCURRENCY_CONFLICT');
    expect(editor).toHaveValue('Retain my draft');
  });

  it('requires rationale and a separate draft before submitting a missing enhancement', async () => {
    // Arrange
    vi.mocked(api.getRequirementCoverage).mockResolvedValue({ ...detail, canAuthor: true,
      enhancements: [{ ...detail.enhancements[0]!, selected: false }] });
    render(<RequirementCoveragePanel systemId="system-1" controlId="AC-11" kind="technical" onNavigate={vi.fn()} onChanged={vi.fn()} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Propose enhancement AC-11(1)' }));
    expect(screen.getByRole('button', { name: 'Submit enhancement proposal' })).toBeDisabled();

    // Act
    fireEvent.change(screen.getByLabelText('Required rationale'), { target: { value: 'Synthetic scope' } });
    fireEvent.change(screen.getByLabelText('Separate technical draft'), { target: { value: 'Separate response' } });
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Submit enhancement proposal' })); });

    // Assert
    expect(api.proposeEnhancement).toHaveBeenCalledWith('system-1', {
      parentControlId: 'AC-11', controlId: 'AC-11(1)', expectedBaselineRevision: 1,
      rationale: 'Synthetic scope', policyDraft: null, technicalDraft: 'Separate response',
    });
  });

  it('notifies the drawer about unsaved responses so close and Escape can protect them', async () => {
    // Arrange
    vi.mocked(api.getRequirementCoverage).mockResolvedValue({ ...detail, canAuthor: true });
    const dirty = vi.fn();
    render(<RequirementCoveragePanel systemId="system-1" controlId="AC-11" kind="policy"
      onNavigate={vi.fn()} onChanged={vi.fn()} onDirtyChange={dirty} />);

    // Act
    fireEvent.change(await screen.findByRole('textbox', { name: 'Policy response for a.' }), { target: { value: 'Unsaved draft' } });

    // Assert
    expect(dirty).toHaveBeenLastCalledWith(true);
  });

  it('does not ask the user to select a catalog or provide reconciliation rationale', async () => {
    // Arrange
    vi.mocked(api.getRequirementCoverage).mockResolvedValue({ ...detail, framework: null, canBind: true });
    render(<MemoryRouter><RequirementCoveragePanel systemId="system-1" controlId="AC-11" kind="policy" onNavigate={vi.fn()} onChanged={vi.fn()} /></MemoryRouter>);

    // Act / Assert
    expect(await screen.findByText(/links the source automatically/)).toBeVisible();
    expect(screen.queryByLabelText('Authoritative catalog')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Source reconciliation rationale')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Confirm catalog binding' })).not.toBeInTheDocument();
    expect(api.bindRequirementCatalog).not.toHaveBeenCalled();
  });

  it('pins selected evidence and records parameter values without claiming review', async () => {
    // Arrange
    vi.mocked(api.getRequirementCoverage).mockResolvedValue({ ...detail, canAuthor: true,
      parameters: [{ id: 'period', definition: '{"label":"period"}' }] });
    vi.mocked(api.saveRequirementResponses).mockResolvedValue({ ...detail, canAuthor: true });
    vi.mocked(evidenceApi.listEvidence).mockResolvedValue({ page: 1, pageSize: 50, totalCount: 1, items: [{
      id: 'artifact-a', fileName: 'Synthetic policy.txt', source: 'Manual', contentType: 'text/plain', fileSizeBytes: 10,
      artifactCategory: 'PolicyDocument', narrativeType: 'Policy', controlId: 'AC-11',
      controlImplementationId: null, securityCapabilityId: null, description: 'Synthetic evidence',
      uploadedBy: 'author', uploadedAt: '2026-01-01', contentHash: 'synthetic-hash',
    }] });
    render(<RequirementCoveragePanel systemId="system-1" controlId="AC-11" kind="policy" onNavigate={vi.fn()} onChanged={vi.fn()} />);
    fireEvent.change(await screen.findByLabelText('Policy response for a.'), { target: { value: 'Draft' } });

    // Act
    fireEvent.change(screen.getByLabelText('Recorded value: Period (parameter 1)'), { target: { value: 'Synthetic recorded value' } });
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Find evidence' })); });
    fireEvent.change(screen.getByLabelText('Supporting evidence for a.'), { target: { value: 'artifact-a' } });
    fireEvent.click(screen.getByRole('button', { name: 'Remove evidence association' }));
    fireEvent.change(screen.getByLabelText('Supporting evidence for a.'), { target: { value: 'artifact-a' } });
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Save requirement responses' })); });

    // Assert
    expect(api.saveRequirementResponses).toHaveBeenCalledWith('system-1', 'AC-11', {
      expectedVersion: 2, responses: [{ statementId: 'source-a', kind: 'Policy', response: 'Draft',
        evidence: [{ artifactId: 'artifact-a', contentHash: 'synthetic-hash' }] }],
      parameters: { period: 'Synthetic recorded value' },
    });
  });

  it('allows a server-authorized reviewer to accept selection or request revision', async () => {
    // Arrange
    vi.mocked(api.getRequirementCoverage).mockResolvedValue({ ...detail, proposals: [{
      id: 'proposal-a', controlId: 'AC-11(1)', rationale: 'Required scope', policyDraft: 'Policy draft', technicalDraft: null,
      status: 'Pending', revision: 4, createdBy: 'author', createdAt: '2026-01-01',
      reviewedBy: null, reviewedAt: null, canAccept: true,
    }] });
    render(<RequirementCoveragePanel systemId="system-1" controlId="AC-11" kind="policy" onNavigate={vi.fn()} onChanged={vi.fn()} />);

    // Act
    const accept = await screen.findByRole('button', { name: /Accept AC-11\(1\) selection/ });
    await act(async () => { fireEvent.click(accept); });
    fireEvent.change(screen.getByLabelText('Revision note for AC-11(1)'), { target: { value: 'Clarify source' } });
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Request revision for AC-11(1)' })); });

    // Assert
    expect(api.acceptEnhancement).toHaveBeenCalledWith('system-1', 'proposal-a', 4);
    expect(api.returnEnhancement).toHaveBeenCalledWith('system-1', 'proposal-a', 4, 'Clarify source');
  });

  it('changes coverage to reviewed only after an explicit successful review response', async () => {
    // Arrange
    const draft: api.RequirementCoverageDetail = { ...detail, canReview: true, gaps: [],
      requirements: [{ ...detail.requirements[0]!, responseState: 'Draft', evidenceGap: false,
        responses: [{ statementId: 'source-a', kind: 'Policy', response: 'Reviewed source-backed response', evidence: [] }] }] };
    vi.mocked(api.getRequirementCoverage).mockResolvedValue(draft);
    vi.mocked(api.reviewRequirementResponses).mockResolvedValue({ ...draft,
      requirements: draft.requirements.map(item => ({ ...item, responseState: 'Reviewed', reviewed: true })) });
    render(<RequirementCoveragePanel systemId="system-1" controlId="AC-11" kind="policy" onNavigate={vi.fn()} onChanged={vi.fn()} />);
    const review = await screen.findByRole('button', { name: 'Review requirement coverage' });
    expect(screen.getByText(/Draft response/)).toBeVisible();

    // Act
    await act(async () => { fireEvent.click(review); });

    // Assert
    expect(api.reviewRequirementResponses).toHaveBeenCalledWith('system-1', 'AC-11', 2);
    expect(screen.getByText('Reviewed coverage')).toBeVisible();
  });

  it.each([new Error('Source unavailable'), null])('surfaces load errors and supports explicit reload %#', async failure => {
    // Arrange
    vi.mocked(api.getRequirementCoverage).mockRejectedValueOnce(failure).mockResolvedValue(detail);
    render(<RequirementCoveragePanel systemId="system-1" controlId="AC-11" kind="policy" onNavigate={vi.fn()} onChanged={vi.fn()} />);

    // Act
    const alert = await screen.findByRole('alert');

    // Assert
    expect(alert).toHaveTextContent(failure ? 'Source unavailable' : 'Requirement coverage could not be updated');
    fireEvent.click(screen.getByRole('button', { name: 'Reload coverage' }));
    expect(await screen.findByText('Synthetic source text')).toBeVisible();
  });

  it('preserves review feedback for a returned enhancement proposal', async () => {
    // Arrange
    vi.mocked(api.getRequirementCoverage).mockResolvedValue({ ...detail, proposals: [{
      id: 'returned', controlId: 'AC-11(1)', rationale: 'Scope', policyDraft: null, technicalDraft: 'Draft',
      status: 'NeedsRevision', revision: 2, createdBy: 'author', createdAt: '2026-01-01',
      reviewedBy: 'independent-reviewer', reviewedAt: '2026-01-02', canAccept: false,
      reviewNote: 'Cite the applicable source.',
    }] });

    // Act
    render(<RequirementCoveragePanel systemId="system-1" controlId="AC-11" kind="policy" onNavigate={vi.fn()} onChanged={vi.fn()} />);

    // Assert
    expect(await screen.findByText('Review note: Cite the applicable source.')).toBeVisible();
    expect(screen.getByText(/Reviewed by independent-reviewer/)).toBeVisible();
    expect(screen.queryByRole('button', { name: /Accept AC-11/ })).not.toBeInTheDocument();
  });
});
