import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import SystemComponentReview from '../../features/workspace-operations/system-capabilities/SystemComponentReview';
import * as design from '../../api/systemDesign';
import * as api from '../../features/workspace-operations/system-capabilities/systemCapabilityApi';
import type { SystemCapabilityDetail } from '../../features/workspace-operations/system-capabilities/systemCapabilityTypes';
import { componentDesignFixture } from '../fixtures/componentReview';

vi.mock('../../api/systemDesign', () => ({
  getSystemDesign: vi.fn(), getApprovedSystemDesign: vi.fn(), saveComponentScope: vi.fn(),
}));
vi.mock('../../features/workspace-operations/system-capabilities/systemCapabilityApi', () => ({
  getSystemComponentPlacements: vi.fn(), getSystemCapability: vi.fn(),
}));
vi.mock('../../features/workspace-operations/system-capabilities/ComponentFirstPass', () => ({
  default: ({ onUse }: { onUse: (text: string, source: { draftId: string; revision: number }) => void }) =>
    <button type="button" onClick={() => onUse('Source-supported proposal', { draftId: 'proposal', revision: 7 })}>Use fixture wording</button>,
}));
vi.mock('../../features/workspace-operations/system-capabilities/SystemComponentPlacements', () => ({
  default: ({ onChanged }: { onChanged: (notice: string) => void }) =>
    <button type="button" onClick={() => onChanged('Placement confirmed by server')}>Confirm fixture placement</button>,
}));
const data: SystemCapabilityDetail = {
  item: { source: 'provider', recordType: 'component', recordId: 'backup', name: 'Azure Backup',
    description: 'Synthetic demonstration source. Recovery Services vault backs up saved recovery points.',
    sourceName: 'Flankspeed', mutationAuthority: 'provider', sourceRevision: 'r1',
    componentType: 'Thing', subType: 'Service', isApplied: true, isAvailable: true, status: 'Applied',
    components: [], capabilities: [{ source: 'provider', recordType: 'capability', recordId: 'cap', name: 'Backup and recovery' }],
    placements: [{ id: '', boundaryId: null, boundaryName: null, state: 'Unassigned', revision: '' }],
    controlIds: ['CP-9'], reviewRequiredCount: 1 },
  permissions: { canRead: true, canManage: true, canManageEvidence: false,
    canReviewResponsibilities: false, canAuthorNarratives: false, canReviewNarratives: false },
  baselineId: 'baseline', controls: [], evidence: [], narratives: [], relationshipRevision: 'rel',
  responsibilityReviewUrl: '/systems/system/inheritance/subscriptions',
};
const graph = componentDesignFixture();
function mount(detail = data) {
  vi.mocked(api.getSystemCapability).mockResolvedValue(detail);
  return render(<MemoryRouter><SystemComponentReview data={detail} tenantId="org" systemId="system"
    onBusyChange={vi.fn()} onChanged={vi.fn()} /></MemoryRouter>);
}
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(design.getSystemDesign).mockResolvedValue(graph);
  vi.mocked(design.getApprovedSystemDesign).mockResolvedValue(null);
  vi.mocked(api.getSystemCapability).mockResolvedValue(data);
  vi.mocked(api.getSystemComponentPlacements).mockResolvedValue({
    source: 'provider', recordId: 'backup', sourceRevision: 'r1', relationshipRevision: 'rel',
    canAssignBoundary: true, assignBlockedReason: null, boundaries: [{ id: 'area', name: 'Mission API' }], placements: [],
  });
});
describe('task-oriented component review', () => {
  it('explains unavailable source and rejects new included use without losing notes', async () => {
    // Arrange
    mount({ ...data, item: { ...data.item, isAvailable: false } });
    await screen.findByText('System scope not recorded');
    // Act
    fireEvent.click(screen.getByRole('tab', { name: 'System scope' }));
    fireEvent.click(screen.getByLabelText('Used by this system'));
    fireEvent.change(screen.getByLabelText('System area supported'), { target: { value: 'area' } });
    fireEvent.change(screen.getByLabelText('How it is used'), { target: { value: 'Retained notes' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save scope draft' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('The source is unavailable for new included use');
    expect(screen.getByLabelText('How it is used')).toHaveValue('Retained notes');
    expect(design.saveComponentScope).not.toHaveBeenCalled();
  });
  it.each([true, false])('shows saved proposal provenance and user corrections (corrected=%s)', async userEdited => {
    // Arrange
    vi.mocked(design.getSystemDesign).mockResolvedValue({ ...graph, componentScopes: [{
      source: 'provider', componentId: 'backup', name: 'Azure Backup', sourceRevision: 'r1', decision: 'NeedsConfirmation',
      boundaryId: null, boundaryName: null, usage: 'Corrected wording',
      wordingBasis: { draftId: 'proposal', revision: 7, sourceHash: 'pinned', origin: 'AI proposed',
        originalWording: 'Original source-supported wording', userEdited, sourceIds: ['release-7'] },
    }] });
    // Act
    mount();
    await screen.findByText('Scope draft needs confirmation');
    fireEvent.click(screen.getByRole('tab', { name: 'System scope' }));
    fireEvent.click(screen.getByText('Saved wording provenance'));
    // Assert
    expect(screen.getByText(/Original proposal: Original source-supported wording/)).toBeVisible();
    expect(screen.getByText(/Source references: release-7/)).toBeVisible();
    expect(screen.getByText(userEdited ? 'AI proposed · user corrected' : 'AI proposed')).toBeVisible();
  });
  it('retains a copied proposal reference and human changes in the scope request', async () => {
    // Arrange
    mount();
    await screen.findByText('System scope not recorded');
    vi.mocked(design.saveComponentScope).mockResolvedValue({ ...graph, revision: 3 });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Use fixture wording' }));
    expect(screen.getByLabelText('How it is used')).toHaveValue('Source-supported proposal');
    fireEvent.change(screen.getByLabelText('How it is used'), { target: { value: 'Human-corrected source-supported proposal' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save scope draft' }));
    // Assert
    await screen.findByText(/Scope draft saved/);
    expect(design.saveComponentScope).toHaveBeenCalledWith('system',
      expect.objectContaining({ usage: 'Human-corrected source-supported proposal', wordingDraftId: 'proposal', wordingDraftRevision: 7 }),
      expect.any(AbortSignal));
  });
  it.each(['', 'Synthetic source only.', 'A'.repeat(300)])('handles empty, demo-only and long source descriptions without inventing facts', async description => {
    // Arrange
    const item = { ...data.item, source: 'local' as const, description, name: 'System service',
      componentType: null, subType: null, capabilities: [] };
    mount({ ...data, item });
    // Act
    await screen.findByText('System scope not recorded');
    fireEvent.click(screen.getByText('Provider source and technical details'));
    // Assert
    expect(screen.getByText('Subtype not recorded')).toBeVisible();
    expect(screen.getByText('No applied capability links this component.')).toBeVisible();
    if (!description) expect(screen.getByText('Source description unavailable.')).toBeVisible();
    // Act
    fireEvent.click(screen.getByRole('tab', { name: 'Evidence' }));
    // Assert
    expect(screen.getByText('System applicability')).toBeVisible();
  });
  it('opens the separate placement workflow and refreshes after confirmed placement', async () => {
    // Arrange
    mount();
    await screen.findByText('System scope not recorded');
    // Act
    fireEvent.click(screen.getByRole('tab', { name: 'System scope' }));
    fireEvent.click(screen.getByText('Existing infrastructure placements'));
    fireEvent.click(await screen.findByRole('button', { name: 'Confirm fixture placement' }));
    // Assert
    expect(await screen.findByText('Placement confirmed by server')).toBeVisible();
  });
  it.each(['Included', 'Excluded'] as const)('labels %s from the retained review rather than immediate placements', async decision => {
    // Arrange
    const scope: design.ComponentScopeUse = { source: 'provider', componentId: 'backup', name: 'Azure Backup', sourceRevision: 'r1',
      decision, boundaryId: decision === 'Included' ? 'area' : null, boundaryName: decision === 'Included' ? 'Mission API' : null, usage: 'Reviewed use' };
    const approvedGraph: design.SystemDesignGraph = { ...graph, governanceStatus: 'Approved', componentScopes: [scope],
      actions: { ...graph.actions, canEdit: false } };
    vi.mocked(design.getSystemDesign).mockResolvedValue(approvedGraph);
    vi.mocked(design.getApprovedSystemDesign).mockResolvedValue({ graph: approvedGraph, revision: 2,
      approvedBy: 'independent-reviewer', approvedAt: '2026-10-02', snapshotHash: 'approved-hash', sourceFingerprint: 'graph-source', sourcesStale: false });
    // Act
    mount();
    // Assert
    expect(await screen.findByText(decision === 'Excluded' ? 'Excluded from system scope' : 'System scope reviewed')).toBeVisible();
    fireEvent.click(screen.getByRole('tab', { name: 'System scope' }));
    expect(screen.getByLabelText('How it is used')).toHaveValue('Reviewed use');
    expect(screen.getByRole('button', { name: 'Save scope draft' })).toBeDisabled();
    expect(screen.getByText(/An approved design must first/)).toBeVisible();
  });
  it('identifies changed sources without replacing saved usage or the source revision', async () => {
    // Arrange
    vi.mocked(design.getSystemDesign).mockResolvedValue({ ...graph, sourcesStale: true, componentScopes: [{
      source: 'provider', componentId: 'backup', name: 'Azure Backup', sourceRevision: 'old',
      decision: 'Included', boundaryId: 'area', boundaryName: 'Mission API', usage: 'Retained usage',
    }] });
    // Act
    mount();
    // Assert
    expect(await screen.findByText(/Source changes affect this draft or review/)).toBeVisible();
    fireEvent.click(screen.getByRole('tab', { name: 'System scope' }));
    expect(screen.getByLabelText('How it is used')).toHaveValue('Retained usage');
  });
  it('reports scope-read failure instead of pretending no scope exists', async () => {
    // Arrange
    vi.mocked(design.getSystemDesign).mockRejectedValue(new Error('Design source unavailable'));
    // Act
    mount();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Design source unavailable');
    expect(screen.queryByText('System scope not recorded')).not.toBeInTheDocument();
  });
  it('shows only actual evidence and safe source links, not a completed verification claim', async () => {
    // Arrange
    const detail = { ...data, evidence: [{ id: 'evidence', fileName: 'Retention policy.pdf', owner: 'System owner',
      source: 'organization', state: 'Linked', controlId: 'CP-9', narrativeType: 'Technical', openUrl: '/api/dashboard/systems/system/evidence/evidence/download' }] };
    mount(detail);
    await screen.findByText('System scope not recorded');
    // Act
    fireEvent.click(screen.getByRole('tab', { name: 'Evidence' }));
    // Assert
    expect(screen.getByRole('link', { name: 'Retention policy.pdf' })).toHaveAttribute('href', detail.evidence[0]?.openUrl);
    expect(screen.getByText(/applicability and sufficiency still require review/)).toBeVisible();
    expect(screen.queryByText('Evidence unavailable for this component.')).not.toBeInTheDocument();
  });
  it('reloads changed source after a failed save without clearing corrected usage', async () => {
    // Arrange
    mount();
    await screen.findByText('System scope not recorded');
    vi.mocked(design.saveComponentScope).mockRejectedValueOnce(new Error('Source changed'));
    fireEvent.click(screen.getByRole('button', { name: 'Review system scope' }));
    fireEvent.click(screen.getByLabelText('Not used by this system'));
    fireEvent.change(screen.getByLabelText('How it is used'), { target: { value: 'Preserve this correction' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save scope draft' }));
    await screen.findByText('Source changed');
    vi.mocked(api.getSystemCapability).mockResolvedValue({ ...data, item: { ...data.item, sourceRevision: 'r2', description: 'Updated provider description' } });
    vi.mocked(design.getSystemDesign).mockResolvedValue({ ...graph, revision: 3, componentScopes: [{
      source: 'provider', componentId: 'backup', name: 'Azure Backup', sourceRevision: 'r2',
      decision: 'Excluded', boundaryId: null, boundaryName: null, usage: "Other editor's saved wording",
    }] });
    vi.mocked(api.getSystemComponentPlacements).mockResolvedValue({
      source: 'provider', recordId: 'backup', sourceRevision: 'r2', relationshipRevision: 'rel',
      canAssignBoundary: true, assignBlockedReason: null, boundaries: [], placements: [],
    });
    vi.mocked(design.saveComponentScope).mockResolvedValue({ ...graph, revision: 4, componentScopes: [{
      source: 'provider', componentId: 'backup', name: 'Azure Backup', sourceRevision: 'r2',
      decision: 'Excluded', boundaryId: null, boundaryName: null, usage: 'Preserve this correction',
    }] });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Reload saved scope' }));
    // Assert
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save scope draft' })).toBeEnabled());
    expect(screen.getByLabelText('How it is used')).toHaveValue('Preserve this correction');
    fireEvent.click(await screen.findByText('Compare saved scope with your entries'));
    expect(screen.getByText("Other editor's saved wording")).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Save scope draft' }));
    await screen.findByText(/Scope draft saved/);
    expect(design.saveComponentScope).toHaveBeenLastCalledWith('system', expect.objectContaining({ sourceRevision: 'r2', usage: 'Preserve this correction' }), expect.any(AbortSignal));
  });
  it('shows the next action, readable owner, three keyboard tabs and disclosed read-only source', async () => {
    // Arrange
    mount();
    // Act
    await screen.findByText('System scope not recorded');
    // Assert
    expect(screen.getByRole('heading', { name: 'Azure Backup' })).toBeVisible();
    expect(screen.getByText('Managed by Flankspeed')).toBeVisible();
    expect(screen.getByText('Synthetic demonstration source')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Review system scope' })).toBeVisible();
    expect(screen.getAllByRole('tab').map(tab => tab.textContent)).toEqual(['Overview', 'System scope', 'Evidence']);
    expect(screen.queryByText('24 hours')).not.toBeInTheDocument();
    const overview = screen.getByRole('tab', { name: 'Overview' });
    overview.focus();
    fireEvent.keyDown(overview, { key: 'ArrowRight' });
    expect(screen.getByRole('tab', { name: 'System scope' })).toHaveFocus();
  });
  it.each(['Included', 'Excluded', 'NeedsConfirmation'] as const)('saves %s only as a governed draft', async decision => {
    // Arrange
    mount();
    await screen.findByText('System scope not recorded');
    vi.mocked(design.saveComponentScope).mockResolvedValue({
      ...graph, revision: 3, componentScopes: [{ source: 'provider', componentId: 'backup', name: 'Azure Backup',
        sourceRevision: 'r1', decision, boundaryId: decision === 'Included' ? 'area' : null,
        boundaryName: decision === 'Included' ? 'Mission API' : null, usage: 'Human usage correction' }],
    });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Review system scope' }));
    fireEvent.click(screen.getByLabelText(decision === 'Included' ? 'Used by this system' : decision === 'Excluded' ? 'Not used by this system' : 'Needs confirmation'));
    if (decision === 'Included') fireEvent.change(screen.getByLabelText('System area supported'), { target: { value: 'area' } });
    fireEvent.change(screen.getByLabelText('How it is used'), { target: { value: 'Human usage correction' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save scope draft' }));
    // Assert
    await screen.findByText(/Scope draft saved/);
    expect(design.saveComponentScope).toHaveBeenCalledWith('system', {
      expectedRevision: 2, source: 'provider', componentId: 'backup', sourceRevision: 'r1',
      decision, boundaryId: decision === 'Included' ? 'area' : null, usage: 'Human usage correction',
    }, expect.any(AbortSignal));
    expect(screen.getByRole('link', { name: 'Review scope draft' })).toHaveAttribute('href', '/systems/system/profile/SystemDesign');
    expect(screen.getByText(/Provider infrastructure is not moved/)).toBeVisible();
  });
  it('validates included area and preserves entered text when the server rejects a save', async () => {
    // Arrange
    mount();
    await screen.findByText('System scope not recorded');
    vi.mocked(design.saveComponentScope).mockRejectedValue(new Error('Permission changed; save rejected.'));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Review system scope' }));
    fireEvent.click(screen.getByLabelText('Used by this system'));
    fireEvent.change(screen.getByLabelText('How it is used'), { target: { value: 'Keep my edits' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save scope draft' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Choose the system area');
    expect(design.saveComponentScope).not.toHaveBeenCalled();
    // Act
    fireEvent.change(screen.getByLabelText('System area supported'), { target: { value: 'area' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save scope draft' }));
    // Assert
    expect(await screen.findByText('Permission changed; save rejected.')).toBeVisible();
    expect(screen.getByLabelText('How it is used')).toHaveValue('Keep my edits');
    expect(screen.queryByText(/Scope draft saved/)).not.toBeInTheDocument();
  });
  it('does not claim scope is reviewed from immediate placements and disables denied writes', async () => {
    // Arrange
    vi.mocked(design.getSystemDesign).mockResolvedValue({ ...graph, actions: { ...graph.actions, canEdit: false } });
    mount({ ...data, permissions: { ...data.permissions, canManage: false },
      item: { ...data.item, placements: [{ id: 'placement', boundaryId: 'area', boundaryName: 'Mission API', state: 'InScope', revision: 'p1' }] } });
    // Act
    await screen.findByText('System scope not recorded');
    fireEvent.click(screen.getByRole('button', { name: 'Review system scope' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Save scope draft' })).toBeDisabled();
    expect(screen.getByText(/Read-only/)).toBeVisible();
  });
  it('uses design draft authority independently of infrastructure-placement management', async () => {
    // Arrange
    mount({ ...data, permissions: { ...data.permissions, canManage: false } });
    await screen.findByText('System scope not recorded');
    vi.mocked(design.saveComponentScope).mockResolvedValue({ ...graph, revision: 3, componentScopes: [{
      source: 'provider', componentId: 'backup', name: 'Azure Backup', sourceRevision: 'r1',
      decision: 'NeedsConfirmation', boundaryId: null, boundaryName: null, usage: '',
    }] });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Review system scope' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save scope draft' }));
    // Assert
    expect(await screen.findByText(/Scope draft saved/)).toBeVisible();
  });
  it('shows backup documentation gaps without inventing evidence or results', async () => {
    // Arrange
    mount();
    await screen.findByText('System scope not recorded');
    // Act
    fireEvent.click(screen.getByRole('tab', { name: 'Evidence' }));
    // Assert
    const pane = screen.getByRole('tabpanel');
    for (const label of ['Protected workloads', 'Configuration and retention records', 'Restore results', 'Responsibility review'])
      expect(within(pane).getByText(label)).toBeVisible();
    expect(within(pane).getByText('Evidence unavailable for this component.')).toBeVisible();
    expect(within(pane).getByRole('link', { name: 'Open evidence repository' })).toHaveAttribute('href', '/systems/system/evidence');
  });
});
