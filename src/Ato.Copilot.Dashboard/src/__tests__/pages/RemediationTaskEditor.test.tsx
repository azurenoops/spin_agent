import '../helpers/dialog';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import RemediationTaskEditor, { type TaskDraft } from '../../features/remediation-workspace/RemediationTaskEditor';

const initial: TaskDraft = { title: 'Correct session timeout', description: 'Update the setting.',
  controlId: 'AC-12', severity: 'Medium', assigneeId: 'owner-a', dueDate: '2026-10-05',
  affectedResources: ['/resource/a'], validationCriteria: 'Retest the configured timeout.' };
const owners = [{ id: 'owner-a', name: 'Application team' }];

describe('Focused corrective task editor', () => {
  it('retains corrective scope and a named owner without assigning roles', async () => {
    // Arrange
    const save = vi.fn().mockResolvedValue(undefined);
    render(<RemediationTaskEditor initial={initial} owners={owners} canSave onSave={save} onClose={vi.fn()} />);
    // Act
    fireEvent.change(screen.getByRole('textbox', { name: 'Corrective action' }), { target: { value: 'Set timeout to fifteen minutes.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save task' }));
    // Assert
    await waitFor(() => expect(save).toHaveBeenCalledWith({ ...initial, description: 'Set timeout to fifteen minutes.' }));
    expect(screen.getByRole('combobox', { name: 'Owner' })).toHaveDisplayValue('Application team');
  });

  it('preserves draft data and exposes stale-revision failures instead of closing', async () => {
    // Arrange
    const close = vi.fn(), save = vi.fn().mockRejectedValue(new Error('Task changed. Reload before saving.'));
    render(<RemediationTaskEditor initial={initial} owners={owners} canSave onSave={save} onClose={close} />);
    // Act
    fireEvent.change(screen.getByRole('textbox', { name: 'Task title' }), { target: { value: 'My unsaved correction' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save task' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Task changed');
    expect(screen.getByRole('textbox', { name: 'Task title' })).toHaveValue('My unsaved correction');
    expect(close).not.toHaveBeenCalled();
  });

  it('guards permission revocation inside an already-open editor', async () => {
    // Arrange
    const save = vi.fn(), props = { initial, owners, onSave: save, onClose: vi.fn() };
    const { rerender, container } = render(<RemediationTaskEditor {...props} canSave />);
    // Act
    rerender(<RemediationTaskEditor {...props} canSave={false} />);
    fireEvent.submit(container.ownerDocument.querySelector('form')!);
    // Assert
    expect(save).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Save task' })).toBeDisabled();
    expect(screen.getByRole('alert')).toHaveTextContent(/permission/i);
  });

  it('cancels without writes and asks before discarding changed corrective work', () => {
    // Arrange
    const save = vi.fn(), close = vi.fn();
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    render(<RemediationTaskEditor initial={initial} owners={owners} canSave onSave={save} onClose={close} />);
    // Act
    fireEvent.change(screen.getByRole('textbox', { name: 'Task title' }), { target: { value: 'Changed' } });
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    // Assert
    expect(window.confirm).toHaveBeenCalled();
    expect(save).not.toHaveBeenCalled();
    expect(close).not.toHaveBeenCalled();
    vi.restoreAllMocks();
  });
});
