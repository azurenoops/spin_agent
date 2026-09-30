import { act, fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import '../helpers/dialog';
import SystemOperationalStatus from '../../features/systems/SystemOperationalStatus';
import client from '../../api/client';
vi.mock('../../api/client', () => ({ default: { get: vi.fn(), put: vi.fn() } }));
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(client.get).mockResolvedValue({ data: { systemId: 'a', operationalStatus: null, canManage: true } });
});
describe('Explicit operational status', () => {
  it('does not default missing source data and saves only an explicit selection', async () => {
    // Arrange
    vi.mocked(client.put).mockResolvedValue({ data: { systemId: 'a', operationalStatus: 'UnderDevelopment', canManage: true } });
    render(<SystemOperationalStatus systemId="a" />);
    await screen.findByText('Not recorded');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Manage operational status' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Operational status' }), { target: { value: 'UnderDevelopment' } });
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Save operational status' })); });
    // Assert
    expect(client.put).toHaveBeenCalledWith('/systems/a/operational-status', { operationalStatus: 'UnderDevelopment' });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByText('Under development')).toBeVisible();
  });
  it('does not offer edit controls without server permission', async () => {
    // Arrange
    vi.mocked(client.get).mockResolvedValue({ data: { systemId: 'a', operationalStatus: 'Operational', canManage: false } });
    // Act
    render(<SystemOperationalStatus systemId="a" />);
    // Assert
    await screen.findByText('Operational');
    expect(screen.queryByRole('button', { name: 'Manage operational status' })).not.toBeInTheDocument();
    expect(client.put).not.toHaveBeenCalled();
  });
  it('retains the selected value and displays a failed save', async () => {
    // Arrange
    vi.mocked(client.put).mockRejectedValue(new Error('Save rejected'));
    render(<SystemOperationalStatus systemId="a" />);
    fireEvent.click(await screen.findByRole('button', { name: 'Manage operational status' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Operational status' }), { target: { value: 'Operational' } });
    // Act
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Save operational status' })); });
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Save rejected');
    expect(screen.getByRole('combobox', { name: 'Operational status' })).toHaveValue('Operational');
  });
});
