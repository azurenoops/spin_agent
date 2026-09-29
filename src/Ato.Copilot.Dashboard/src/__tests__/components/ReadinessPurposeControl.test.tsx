import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import ReadinessPurposeControl from '../../features/systems/ReadinessPurposeControl';
import '../helpers/dialog';

describe('Readiness package purpose', () => {
  it('does not silently replace Legacy or apply a changed purpose before confirmation', () => {
    // Arrange
    const change = vi.fn();
    render(<ReadinessPurposeControl purpose="Legacy" disabled={false} onChange={change} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Change package purpose' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Package purpose' }), { target: { value: 'InitialSubmission' } });
    // Assert
    expect(change).not.toHaveBeenCalled();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Use selected purpose' }));
    // Assert
    expect(change).toHaveBeenCalledExactlyOnceWith('InitialSubmission');
  });
  it('cancels a tentative change and preserves the selected purpose', () => {
    // Arrange
    const change = vi.fn();
    render(<ReadinessPurposeControl purpose="AuthorizedBaselineArchive" disabled={false} onChange={change} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Change package purpose' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Package purpose' }), { target: { value: 'ChangeSubmission' } });
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    // Assert
    expect(change).not.toHaveBeenCalled();
    expect(screen.getByText('Authorized baseline archive')).toBeVisible();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
  it('blocks purpose changes while validation is pending', () => {
    // Arrange / Act
    render(<ReadinessPurposeControl purpose="InitialSubmission" disabled onChange={vi.fn()} />);
    // Assert
    expect(screen.getByRole('button', { name: 'Change package purpose' })).toBeDisabled();
  });
});
