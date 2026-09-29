import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import '../helpers/dialog';
import NetworkInterfaceDrawer from '../../features/systems/NetworkInterfaceDrawer';
import type { PpsItem } from '../../types/dashboard';

const item: PpsItem = { id: 'pps-a', serviceName: 'Mission HTTPS', portOrRange: '443', protocol: 'TCP',
  direction: 'Inbound', justification: 'User access', sortOrder: 3 };

describe('Network interface drawer', () => {
  it('prefills the selected record and persists its stable identity only on save', async () => {
    // Arrange
    const save = vi.fn().mockResolvedValue(true);
    const close = vi.fn();
    render(<NetworkInterfaceDrawer item={item} readOnly={false} error={null} onSave={save} onClose={close} />);
    // Act
    fireEvent.change(screen.getByLabelText('Ports or range'), { target: { value: '8443' } });
    fireEvent.change(screen.getByLabelText('Service / interface name'), { target: { value: 'Mission admin API' } });
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Save network interface' })); });
    // Assert
    await vi.waitFor(() => expect(save).toHaveBeenCalledExactlyOnceWith({ ...item, portOrRange: '8443', serviceName: 'Mission admin API' }));
    expect(close).toHaveBeenCalledOnce();
    expect(screen.getByRole('dialog')).toHaveClass('ml-auto', 'h-dvh');
  });

  it('retains failed input and prevents Escape and edits while saving', async () => {
    // Arrange
    let finish!: (saved: boolean) => void;
    const save = vi.fn(() => new Promise<boolean>(resolve => { finish = resolve; }));
    const close = vi.fn();
    render(<NetworkInterfaceDrawer item={item} readOnly={false} error="Save failed" onSave={save} onClose={close} />);
    fireEvent.change(screen.getByLabelText('Ports or range'), { target: { value: '8443' } });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Save network interface' }));
    const drawer = screen.getByRole('dialog');
    fireEvent(drawer, new Event('cancel', { cancelable: true }));
    // Assert
    expect(drawer).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByLabelText('Ports or range')).toBeDisabled();
    expect(close).not.toHaveBeenCalled();
    // Act
    await act(async () => finish(false));
    // Assert
    expect(screen.getByLabelText('Ports or range')).toHaveValue('8443');
    expect(screen.getByRole('alert')).toHaveTextContent('Save failed');
    expect(drawer).toHaveAttribute('aria-busy', 'false');
    expect(close).not.toHaveBeenCalled();
  });

  it('allows read-only inspection without offering writes', () => {
    // Arrange
    const save = vi.fn();
    // Act
    render(<NetworkInterfaceDrawer item={item} readOnly error={null} onSave={save} onClose={vi.fn()} />);
    // Assert
    expect(screen.getByLabelText('Service / interface name')).toHaveValue('Mission HTTPS');
    expect(screen.getByLabelText('Service / interface name')).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Save network interface' })).not.toBeInTheDocument();
    expect(save).not.toHaveBeenCalled();
  });

  it('confirms removal within the same drawer rather than nesting dialogs', async () => {
    // Arrange
    const remove = vi.fn().mockResolvedValue(true);
    const close = vi.fn();
    render(<NetworkInterfaceDrawer item={item} readOnly={false} error={null}
      onSave={vi.fn()} onRemove={remove} onClose={close} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Remove network interface' }));
    // Assert
    expect(remove).not.toHaveBeenCalled();
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    // Act
    await act(async () => { fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Confirm removal' })); });
    // Assert
    await vi.waitFor(() => expect(close).toHaveBeenCalledOnce());
    expect(remove).toHaveBeenCalledExactlyOnceWith('pps-a');
  });
});
