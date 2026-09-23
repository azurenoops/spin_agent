import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import '../helpers/dialog';
import SetupDialog from '../../features/workspace-operations/SetupDialog';

describe('Capability dialog dismissal', () => {
  it('labels the modal, handles Escape and returns focus to its invoker', () => {
    // Arrange
    const close = vi.fn();
    const invoker = document.createElement('button');
    document.body.append(invoker);
    invoker.focus();
    const view = render(<SetupDialog busy={false} onClose={close}><p>Capability steps</p></SetupDialog>);
    // Act
    fireEvent(screen.getByRole('dialog'), new Event('cancel', { bubbles: true, cancelable: true }));
    // Assert
    expect(close).toHaveBeenCalledOnce();
    expect(screen.getByRole('dialog')).toHaveAccessibleName('Add a security capability');
    view.unmount();
    expect(invoker).toHaveFocus();
    expect(document.body.style.overflow).toBe('');
    invoker.remove();
  });

  it('dismisses on the backdrop but not on content or dialog padding', () => {
    // Arrange
    const close = vi.fn();
    render(<SetupDialog busy={false} onClose={close}><button>Content</button></SetupDialog>);
    const dialog = screen.getByRole('dialog');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Content' }));
    fireEvent.click(dialog, { clientX: 0, clientY: 0 });
    fireEvent.click(dialog, { clientX: -1, clientY: -1 });
    // Assert
    expect(close).toHaveBeenCalledOnce();
  });

  it('blocks Escape, backdrop and close while saving', () => {
    // Arrange
    const close = vi.fn();
    render(<SetupDialog busy onClose={close}>Saving</SetupDialog>);
    const dialog = screen.getByRole('dialog');
    // Act
    fireEvent(dialog, new Event('cancel', { bubbles: true, cancelable: true }));
    fireEvent.click(dialog, { clientX: -1, clientY: -1 });
    fireEvent.click(screen.getByRole('button', { name: 'Close dialog' }));
    // Assert
    expect(close).not.toHaveBeenCalled();
    expect(dialog).toHaveAttribute('aria-busy', 'true');
  });
});
