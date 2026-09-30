import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import '../../helpers/dialog';
import SetupDialog from '../../../features/workspace-operations/SetupDialog';

describe('POA&M nested request dialog labels', () => {
  it('dismisses only the top dialog when its Escape cancellation follows React portal ancestry', () => {
    // Arrange
    const closeParent = vi.fn();
    const closeChild = vi.fn();
    render(<SetupDialog title="Commitment" busy={false} onClose={closeParent}>
      <SetupDialog title="Request exception" busy={false} onClose={closeChild}>Request fields</SetupDialog>
    </SetupDialog>);
    // Act
    fireEvent(screen.getByRole('dialog', { name: 'Request exception' }), new Event('cancel', { bubbles: true, cancelable: true }));
    // Assert
    expect(closeChild).toHaveBeenCalledOnce();
    expect(closeParent).not.toHaveBeenCalled();
  });
  it('keeps each native dialog associated with its own title and description', () => {
    // Arrange / Act
    render(<SetupDialog title="Commitment" description="Parent record" busy={false} onClose={vi.fn()}>
      <SetupDialog title="Request exception" description="Separate decision" busy={false} onClose={vi.fn()}>Request fields</SetupDialog>
    </SetupDialog>);
    // Assert
    expect(screen.getByRole('dialog', { name: 'Commitment' })).toHaveAccessibleDescription('Parent record');
    expect(screen.getByRole('dialog', { name: 'Request exception' })).toHaveAccessibleDescription('Separate decision');
  });
});
