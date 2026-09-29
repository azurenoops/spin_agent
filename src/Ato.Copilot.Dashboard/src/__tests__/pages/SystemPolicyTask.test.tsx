import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import LegalRegulatory from '../../pages/LegalRegulatory';
import * as api from '../../api/policyWorkspace';

vi.mock('../../api/policyWorkspace', async original => ({
  ...await original<typeof api>(), getPolicyWorkspace: vi.fn(), getPolicyLibrary: vi.fn(),
  getPolicySource: vi.fn(), createPolicySource: vi.fn(), addPolicyReference: vi.fn(),
  getPolicyReference: vi.fn(), editPolicyReference: vi.fn(), unlinkPolicyReference: vi.fn(),
}));
const source = { id: 'policy-a', name: 'Access control policy', description: 'Access responsibilities',
  subType: 'Access control', status: 'Active', owner: 'Policy team', revision: 'source-r1',
  versionLabel: 'Updated Sep 25, 2026', modifiedAt: '2026-09-25T10:00:00Z',
  alreadyLinked: false, relatedControls: ['AC-2'] };
const reference = { id: 'reference-a', policyId: source.id, name: source.name, rationale: 'Defines system access responsibilities.',
  retainedVersionLabel: source.versionLabel, sourceStatus: 'Active', sourceChanged: false,
  retention: 'Retained' as const, revision: 1, canEdit: true, canRemove: true, actionReason: null };
const empty = { systemId: 'system-a', systemName: 'SPIN Demo System', items: [], totalCount: 0, unfilteredTotal: 0,
  page: 1, pageSize: 25, permissions: { canAssign: true, assignReason: null, canCreateLibrary: true, createReason: null } };
const detail = { systemId: 'system-a', systemName: 'SPIN Demo System', reference, retainedSource: source,
  currentSource: source, relatedControls: ['AC-2'], reviewMessage: 'Applicability review is not implemented.',
  history: [{ id: 'event-a', action: 'Reference added', actor: 'System manager', at: '2026-09-28T10:00:00Z', description: 'Source retained.' }],
  removalImpact: ['Only this system reference will be unlinked.', 'The organization policy and other assignments will remain.'] };
function mount(query = '') {
  return render(<MemoryRouter initialEntries={[`/systems/system-a/legal${query}`]}>
    <Routes><Route path="/systems/:id/legal" element={<LegalRegulatory />} /></Routes>
  </MemoryRouter>);
}
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getPolicyWorkspace).mockResolvedValue(empty);
  vi.mocked(api.getPolicyLibrary).mockResolvedValue({ systemId: 'system-a', items: [source], totalCount: 1, page: 1, pageSize: 25 });
  vi.mocked(api.getPolicySource).mockResolvedValue(source);
  vi.mocked(api.getPolicyReference).mockResolvedValue(detail);
  vi.mocked(api.addPolicyReference).mockResolvedValue(reference);
  vi.mocked(api.createPolicySource).mockResolvedValue(source);
  vi.mocked(api.editPolicyReference).mockResolvedValue(reference);
  vi.mocked(api.unlinkPolicyReference).mockResolvedValue(undefined);
});
afterEach(cleanup);

describe('System policy workspace', () => {
  it('shows the complete empty state with one add action and no empty chrome', async () => {
    // Arrange / Act
    mount();
    // Assert
    expect(await screen.findByText('No policies linked yet')).toBeVisible();
    expect(screen.getByRole('heading', { name: 'Policies for this system' })).toBeVisible();
    expect(screen.getByText('Keep the policies you rely on and explain why they apply.')).toBeVisible();
    expect(screen.getAllByRole('button', { name: 'Add policy' })).toHaveLength(1);
    expect(screen.getAllByRole('link', { name: /Organization policy library/ })).toHaveLength(1);
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Policy pages' })).not.toBeInTheDocument();
  });
  it('receives a policy from the existing component workflow without automatically assigning it', async () => {
    // Arrange / Act
    mount('?policyAction=add&policySource=policy-a');
    // Assert
    await waitFor(() => expect(screen.getByRole('radio', { name: /Access control policy/ })).toBeChecked());
    expect(api.getPolicySource).toHaveBeenCalledWith('system-a', 'policy-a', expect.any(AbortSignal));
    expect(api.addPolicyReference).not.toHaveBeenCalled();
  });
  it('shows only one step and saves the exact policy revision with rationale', async () => {
    // Arrange
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Add policy' }));
    fireEvent.click(await screen.findByRole('radio', { name: /Access control policy/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
    // Assert
    expect(screen.queryByRole('textbox', { name: 'Find a policy by name or topic' })).not.toBeInTheDocument();
    expect(screen.getByText('SPIN Demo System', { selector: '.pw-system-name' })).toBeVisible();
    // Act
    const reason = 'Defines access responsibilities.';
    fireEvent.change(screen.getByRole('textbox', { name: 'Why does this apply to this system?' }), { target: { value: reason } });
    // Assert
    expect(screen.getByText(`${reason.length}/500`)).toBeVisible();
    expect(api.addPolicyReference).not.toHaveBeenCalled();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Add system reference' }));
    // Assert
    await waitFor(() => expect(api.addPolicyReference).toHaveBeenCalledWith('system-a',
      { policyId: 'policy-a', expectedSourceRevision: 'source-r1', rationale: reason }));
    expect(api.createPolicySource).not.toHaveBeenCalled();
  });
  it('does not write on selection, Back or Cancel', async () => {
    // Arrange
    mount('?policyAction=add');
    // Act
    fireEvent.click(await screen.findByRole('radio', { name: /Access control policy/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    fireEvent.keyDown(document, { key: 'Tab' });
    expect(screen.getByRole('button', { name: 'Close policy drawer' })).toHaveFocus();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    // Assert
    expect(api.addPolicyReference).not.toHaveBeenCalled();
    expect(api.createPolicySource).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
  it('explicitly creates a library policy and returns to selection without assigning it', async () => {
    // Arrange
    mount('?policyAction=add');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Create a library policy' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Policy name' }), { target: { value: 'Access control policy' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save library policy' }));
    // Assert
    expect(await screen.findByText(/Library policy created/)).toBeVisible();
    expect(await screen.findByRole('radio', { name: /Access control policy/ })).toBeChecked();
    expect(api.createPolicySource).toHaveBeenCalledWith('system-a', { name: 'Access control policy', subType: '', description: '' });
    expect(api.addPolicyReference).not.toHaveBeenCalled();
  });
  it('marks linked policies and prevents duplicate selection', async () => {
    // Arrange
    vi.mocked(api.getPolicyLibrary).mockResolvedValue({ systemId: 'system-a', items: [{ ...source, alreadyLinked: true }], totalCount: 1, page: 1, pageSize: 25 });
    // Act
    mount('?policyAction=add');
    // Assert
    expect(await screen.findByRole('radio', { name: /Access control policy/ })).toBeDisabled();
    expect(screen.getByText('Already linked')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Continue' })).toBeDisabled();
  });
  it('keeps source conflicts explicit and does not claim the assignment saved', async () => {
    // Arrange
    vi.mocked(api.addPolicyReference).mockRejectedValue(new Error('The source revision changed. Choose the current source.'));
    mount('?policyAction=add');
    fireEvent.click(await screen.findByRole('radio', { name: /Access control policy/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
    fireEvent.change(screen.getByRole('textbox', { name: 'Why does this apply to this system?' }), { target: { value: 'Supports the system.' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Add system reference' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('source revision changed');
    expect(screen.getByRole('textbox', { name: 'Why does this apply to this system?' })).toHaveValue('Supports the system.');
    // Act: returning from a conflict must load a fresh selectable revision.
    vi.mocked(api.getPolicyLibrary).mockResolvedValue({ systemId: 'system-a',
      items: [{ ...source, revision: 'source-r2', versionLabel: 'Updated Sep 28, 2026' }], totalCount: 1, page: 1, pageSize: 25 });
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    // Assert
    expect(await screen.findByRole('radio', { name: /Updated Sep 28, 2026/ })).not.toBeChecked();
    expect(screen.getByRole('button', { name: 'Continue' })).toBeDisabled();
  });
  it('does not treat failed list or library reads as empty', async () => {
    // Arrange
    vi.mocked(api.getPolicyWorkspace).mockRejectedValue(new Error('Policy records unavailable'));
    // Act
    mount();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Policy records unavailable');
    expect(screen.queryByText('No policies linked yet')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry policies' })).toBeVisible();
  });
  it('does not grant assignment or source creation from a displayed role', async () => {
    // Arrange
    vi.mocked(api.getPolicyWorkspace).mockResolvedValue({ ...empty, permissions: {
      canAssign: false, assignReason: 'Your assignments do not permit policy changes.',
      canCreateLibrary: false, createReason: 'Library authoring requires organization permission.' } });
    // Act
    mount();
    // Assert
    expect(await screen.findByRole('button', { name: 'Add policy' })).toBeDisabled();
    expect(screen.getByText('Your assignments do not permit policy changes.')).toBeVisible();
  });
  it('shows retained and changed source separately and edits only the rationale', async () => {
    // Arrange
    vi.mocked(api.getPolicyReference).mockResolvedValue({ ...detail,
      reference: { ...reference, sourceChanged: true },
      currentSource: { ...source, description: 'New library content', revision: 'source-r2' } });
    // Act
    mount('?reference=reference-a');
    const drawer = await screen.findByRole('dialog', { name: 'Policy reference details' });
    // Assert
    expect(await within(drawer).findByText('The library source has changed. Your retained reference is unchanged.')).toBeVisible();
    expect(within(drawer).getByText('Access responsibilities')).toBeVisible();
    expect(within(drawer).getByText('Applicability review is not implemented.')).toBeVisible();
    // Act
    fireEvent.click(within(drawer).getByRole('button', { name: 'Edit rationale' }));
    fireEvent.change(within(drawer).getByRole('textbox', { name: 'Why does this apply to this system?' }), { target: { value: 'Revised system rationale.' } });
    fireEvent.click(within(drawer).getByRole('button', { name: 'Save rationale' }));
    // Assert
    await waitFor(() => expect(api.editPolicyReference).toHaveBeenCalledWith('system-a', 'reference-a',
      { expectedRevision: 1, rationale: 'Revised system rationale.' }));
  });
  it('previews scoped unlink impact and never deletes the shared source', async () => {
    // Arrange
    mount('?reference=reference-a');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Unlink from system' }));
    // Assert
    expect(screen.getByText('The organization policy and other assignments will remain.')).toBeVisible();
    expect(api.unlinkPolicyReference).not.toHaveBeenCalled();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Confirm unlink' }));
    // Assert
    await waitFor(() => expect(api.unlinkPolicyReference).toHaveBeenCalledWith('system-a', 'reference-a', 1));
  });
  it('retains search and returns focus when a detail closes', async () => {
    // Arrange
    vi.mocked(api.getPolicyWorkspace).mockResolvedValue({ ...empty, items: [reference], totalCount: 1, unfilteredTotal: 1 });
    mount('?search=access');
    const button = await screen.findByRole('button', { name: 'View reference Access control policy' });
    // Act
    button.focus(); fireEvent.click(button);
    await screen.findByRole('region', { name: 'Retained source' });
    fireEvent.keyDown(document, { key: 'Escape' });
    await act(async () => {});
    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Search policy references' })).toHaveValue('access');
    expect(button).toHaveFocus();
  });
  it('keeps the search field mounted while a filtered request is pending', async () => {
    // Arrange
    vi.mocked(api.getPolicyWorkspace).mockResolvedValueOnce({ ...empty, items: [reference], totalCount: 1, unfilteredTotal: 1 });
    mount();
    const input = await screen.findByRole('textbox', { name: 'Search policy references' });
    vi.mocked(api.getPolicyWorkspace).mockReturnValue(new Promise(() => {}));
    // Act
    input.focus();
    fireEvent.change(input, { target: { value: 'access' } });
    // Assert
    expect(screen.getByRole('textbox', { name: 'Search policy references' })).toBe(input);
    expect(input).toHaveFocus();
  });
  it('shows library failure and retry instead of an empty library', async () => {
    // Arrange
    vi.mocked(api.getPolicyLibrary).mockRejectedValueOnce(new Error('Library access denied'));
    // Act
    mount('?policyAction=add');
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Library access denied');
    expect(screen.queryByText('The policy library is empty.')).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry library' }));
    // Assert
    expect(await screen.findByRole('radio', { name: /Access control policy/ })).toBeVisible();
  });
  it('treats common references as suggestions and requires an explicit source creation', async () => {
    // Arrange
    vi.mocked(api.getPolicyLibrary).mockResolvedValue({ systemId: 'system-a', items: [], totalCount: 0, page: 1, pageSize: 25 });
    mount('?policyAction=add');
    // Act
    fireEvent.click(await screen.findByRole('tab', { name: 'Common references' }));
    const suggestion = screen.getByText('FISMA 2014').closest('article')!;
    fireEvent.click(within(suggestion).getByRole('button', { name: 'Find in library' }));
    // Assert
    expect(await screen.findByText('No matching library policies.')).toBeVisible();
    expect(api.createPolicySource).not.toHaveBeenCalled();
    expect(api.addPolicyReference).not.toHaveBeenCalled();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Create a library policy' }));
    // Assert
    expect(screen.getByRole('textbox', { name: 'Policy name' })).toHaveValue('FISMA 2014');
    expect(screen.getByText(/This saves a shared organization-library source/)).toBeVisible();
    expect(api.createPolicySource).not.toHaveBeenCalled();
  });
  it('authorizes View source independently and hides forbidden library creation', async () => {
    // Arrange
    vi.mocked(api.getPolicyWorkspace).mockResolvedValue({ ...empty,
      permissions: { ...empty.permissions, canCreateLibrary: false, createReason: 'Library authoring denied.' } });
    mount('?policyAction=add');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'View source' }));
    // Assert
    expect(await screen.findByRole('region', { name: 'Current library source' })).toBeVisible();
    expect(api.getPolicySource).toHaveBeenCalledWith('system-a', 'policy-a', expect.any(AbortSignal));
    expect(screen.queryByRole('button', { name: 'Create a library policy' })).not.toBeInTheDocument();
  });
  it('cancels old scoped detail when navigating to another system', async () => {
    // Arrange
    let complete!: (value: typeof detail) => void;
    vi.mocked(api.getPolicyReference).mockReturnValueOnce(new Promise(resolve => { complete = resolve; }));
    function SwitchingPage() {
      const navigate = useNavigate();
      return <><button onClick={() => navigate('/systems/system-b/legal')}>Switch system</button><LegalRegulatory /></>;
    }
    render(<MemoryRouter initialEntries={['/systems/system-a/legal?reference=reference-a']}>
      <Routes><Route path="/systems/:id/legal" element={<SwitchingPage />} /></Routes></MemoryRouter>);
    await screen.findByText('Loading retained policy reference…');
    const signal = vi.mocked(api.getPolicyReference).mock.calls[0]![2]!;
    vi.mocked(api.getPolicyWorkspace).mockResolvedValue({ ...empty, systemId: 'system-b' });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Switch system' }));
    await act(async () => complete(detail));
    // Assert
    expect(signal.aborted).toBe(true);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.queryByText('Access responsibilities')).not.toBeInTheDocument();
  });
});
