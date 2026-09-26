import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import '../helpers/dialog';
import SetupDialog from '../../features/workspace-operations/SetupDialog';

describe('Capability dialog dismissal', () => {
  it('renders outside layout spacing so page margins cannot move a full-height drawer', () => {
    // Arrange / Act
    render(<section data-testid="page-content" className="space-y-5">
      <p>Page content</p><SetupDialog placement="right" busy={false} onClose={vi.fn()}>Drawer</SetupDialog>
    </section>);
    // Assert
    expect(screen.getByTestId('page-content')).not.toContainElement(screen.getByRole('dialog'));
    expect(screen.getByRole('dialog').parentElement).toBe(document.body);
  });

  it('uses the same accessible modal and pending-write guard for a right-hand drawer', () => {
    // Arrange
    const close = vi.fn();
    render(<SetupDialog placement="right" title="Component details" busy onClose={close}>Provider managed</SetupDialog>);
    // Act
    const dialog = screen.getByRole('dialog', { name: 'Component details' });
    fireEvent(dialog, new Event('cancel', { bubbles: true, cancelable: true }));
    // Assert
    expect(dialog).toHaveClass('ml-auto', 'max-h-dvh');
    expect(dialog).toHaveAttribute('aria-busy', 'true');
    expect(close).not.toHaveBeenCalled();
  });

  it('cycles keyboard focus through visible enabled controls in both directions', () => {
    // Arrange
    render(<SetupDialog busy={false} onClose={vi.fn()}>
      <input aria-label="Name" />
      <button>Last action</button>
      <fieldset disabled><button>Disabled fieldset action</button></fieldset>
      <button hidden>Hidden action</button>
    </SetupDialog>);
    const dialog = screen.getByRole('dialog');
    const first = screen.getByRole('button', { name: 'Close dialog' });
    const input = screen.getByRole('textbox', { name: 'Name' });
    const last = screen.getByRole('button', { name: 'Last action' });
    const disabled = screen.getByRole('button', { name: 'Disabled fieldset action' });
    for (const element of [first, input, last, disabled])
      vi.spyOn(element, 'getClientRects').mockReturnValue(Object.assign([new DOMRect()], { item: () => new DOMRect() }));
    first.focus();
    // Act
    fireEvent.keyDown(first, { key: 'Tab', shiftKey: true });
    // Assert
    expect(last).toHaveFocus();
    // Act
    fireEvent.keyDown(last, { key: 'Tab' });
    // Assert
    expect(first).toHaveFocus();
    // Act
    input.focus();
    const middle = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
    fireEvent(input, middle);
    // Assert
    expect(middle.defaultPrevented).toBe(false);
    // Act
    dialog.focus();
    fireEvent.keyDown(dialog, { key: 'Tab' });
    // Assert
    expect(first).toHaveFocus();
    // Act
    dialog.focus();
    fireEvent.keyDown(dialog, { key: 'Tab', shiftKey: true });
    // Assert
    expect(last).toHaveFocus();
  });

  it('keeps keyboard focus on a busy dialog with no enabled controls', () => {
    // Arrange
    render(<SetupDialog busy onClose={vi.fn()}>Saving</SetupDialog>);
    const dialog = screen.getByRole('dialog');
    // Act
    fireEvent.keyDown(dialog, { key: 'Tab' });
    // Assert
    expect(dialog).toHaveFocus();
    // Act
    const key = new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true });
    fireEvent(dialog, key);
    // Assert
    expect(key.defaultPrevented).toBe(false);
  });

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
