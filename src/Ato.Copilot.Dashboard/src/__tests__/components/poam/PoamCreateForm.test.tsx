import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import '../../helpers/dialog';
import PoamCreateForm from '../../../components/poam/PoamCreateForm';
import { getPoamWorkspace } from '../../../api/poamWorkspace';
import type { PoamWorkspace } from '../../../api/poamWorkspace';
vi.mock('../../../api/poamWorkspace', () => ({ getPoamWorkspace: vi.fn() }));
vi.mock('../../../components/permissions/useSystemMutationPermission', () => ({ useSystemMutationPermission: () => true }));
vi.mock('../../../components/poam/ComponentPicker', () => ({ default: () => <div>Component picker</div> }));

describe('Manual POA&M form', () => {
  it('offers existing commitments instead of duplicating a retained finding', async () => {
    // Arrange
    vi.mocked(getPoamWorkspace).mockResolvedValue({
      systemId: 'system-a', findings: [{ id: 'finding-a', title: 'Access review', description: 'Review access', controlId: 'AC-2', severity: 'High', poamIds: ['poam-a'] }],
      poams: [{ id: 'poam-a', findingId: 'finding-a' }],
    } as unknown as PoamWorkspace);
    const open = vi.fn();
    render(<PoamCreateForm systemId="system-a" loading={false} onClose={vi.fn()} onSubmit={vi.fn()} onOpenExisting={open} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Existing finding' }));
    fireEvent.change(await screen.findByLabelText('Assessment finding'), { target: { value: 'finding-a' } });
    // Assert
    expect(screen.getByRole('button', { name: 'Create POA&M' })).toBeDisabled();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Open existing POA&M' }));
    // Assert
    expect(open).toHaveBeenCalledWith('poam-a');
  });

  it('populates a retained finding while still requiring the commitment owner and date', async () => {
    // Arrange
    vi.mocked(getPoamWorkspace).mockResolvedValue({
      systemId: 'system-a', findings: [{ id: 'finding-a', title: 'Access review', description: 'Review access', controlId: 'AC-2', severity: 'High', poamIds: [] }],
      poams: [],
    } as unknown as PoamWorkspace);
    const submit = vi.fn().mockResolvedValue(undefined);
    render(<PoamCreateForm systemId="system-a" loading={false} onClose={vi.fn()} onSubmit={submit} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Existing finding' }));
    fireEvent.change(await screen.findByLabelText('Assessment finding'), { target: { value: 'finding-a' } });
    fireEvent.change(screen.getByLabelText('Point of Contact *'), { target: { value: 'Security team' } });
    fireEvent.change(screen.getByLabelText('Scheduled Completion Date *'), { target: { value: '2026-12-15' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create POA&M' }));
    // Assert
    await waitFor(() => expect(submit).toHaveBeenCalledWith(expect.objectContaining({ findingId: 'finding-a', controlId: 'AC-2', weakness: 'Review access' })));
  });
  it('does not mistake an indirect task relationship for a duplicate formal source record', async () => {
    // Arrange
    vi.mocked(getPoamWorkspace).mockResolvedValue({
      systemId: 'system-a', findings: [{ id: 'finding-a', title: 'Access review', description: 'Review access', controlId: 'AC-2', severity: 'High', poamIds: ['indirect-poam'] }],
      poams: [{ id: 'indirect-poam', findingId: 'another-finding' }],
    } as unknown as PoamWorkspace);
    render(<PoamCreateForm systemId="system-a" loading={false} onClose={vi.fn()} onSubmit={vi.fn()} onOpenExisting={vi.fn()} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Existing finding' }));
    fireEvent.change(await screen.findByLabelText('Assessment finding'), { target: { value: 'finding-a' } });
    // Assert
    expect(screen.getByRole('button', { name: 'Create POA&M' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Open related POA&M' })).toBeInTheDocument();
    expect(screen.queryByText(/A commitment already exists for this finding/)).toBeNull();
  });
  it('uses a named native drawer and submits required fields', async () => {
    // Arrange
    const submit = vi.fn().mockResolvedValue(undefined);
    render(<PoamCreateForm systemId="system-a" loading={false} onClose={vi.fn()} onSubmit={submit} />);
    // Act
    fireEvent.change(screen.getByLabelText('Weakness *'), { target: { value: 'Missing audit retention' } });
    fireEvent.change(screen.getByLabelText('Control ID *'), { target: { value: 'AU-11' } });
    fireEvent.change(screen.getByLabelText('Point of Contact *'), { target: { value: 'Security team' } });
    fireEvent.change(screen.getByLabelText('Scheduled Completion Date *'), { target: { value: '2026-12-15' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create POA&M' }));
    // Assert
    expect(screen.getByRole('dialog', { name: 'Add POA&M' })).toHaveClass('ml-auto');
    await waitFor(() => expect(submit).toHaveBeenCalledWith(expect.objectContaining({
      weakness: 'Missing audit retention', controlId: 'AU-11', poc: 'Security team', scheduledCompletionDate: '2026-12-15',
    })));
  });

  it('does not silently discard incomplete milestones', async () => {
    // Arrange
    render(<PoamCreateForm systemId="system-a" loading={false} onClose={vi.fn()} onSubmit={vi.fn()} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: '+ Add Milestone' }));
    // Assert
    expect(screen.getByLabelText('Milestone 1 description')).toBeRequired();
    await waitFor(() => expect(screen.getByLabelText('Milestone 1 target date')).toBeRequired());
  });
});
