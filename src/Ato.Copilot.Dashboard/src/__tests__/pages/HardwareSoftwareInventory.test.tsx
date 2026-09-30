import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import '../helpers/dialog';
import HardwareSoftwareInventory from '../../features/systems/HardwareSoftwareInventory';
import * as api from '../../api/inventoryRegister';
vi.mock('../../api/inventoryRegister', async importOriginal => ({
  ...await importOriginal<typeof import('../../api/inventoryRegister')>(),
  listInventoryRecords: vi.fn(), saveInventoryRecord: vi.fn(), retireInventoryRecord: vi.fn(),
  inventoryWorkbookUrl: () => '/api/dashboard/systems/a/inventory-items/export',
}));
vi.mock('../../components/AuthenticatedDownload', () => ({ default: ({ children }: { children: React.ReactNode }) => <button>{children}</button> }));
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.listInventoryRecords).mockResolvedValue({ systemId: 'a', items: [], totalCount: 0, page: 1, pageSize: 50, canManage: true });
});
describe('Hardware and software source register', () => {
  it('records standalone cloud software without requiring physical hardware identifiers', async () => {
    // Arrange
    vi.mocked(api.saveInventoryRecord).mockResolvedValue({ id: 'new' } as api.InventoryRecord);
    render(<MemoryRouter><HardwareSoftwareInventory systemId="a" /></MemoryRouter>);
    await screen.findByText('No active inventory records.');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Add inventory item' }));
    fireEvent.change(screen.getByLabelText('Item name'), { target: { value: 'Mission managed database' } });
    fireEvent.change(screen.getByLabelText('Function'), { target: { value: '1' } });
    fireEvent.change(screen.getByLabelText('Vendor'), { target: { value: 'Documented provider' } });
    fireEvent.change(screen.getByLabelText('Version'), { target: { value: '2026.09' } });
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Save inventory item' })); });
    // Assert
    expect(api.saveInventoryRecord).toHaveBeenCalledWith('a', undefined, expect.objectContaining({
      type: 1, softwareFunction: 1, vendor: 'Documented provider', version: '2026.09',
    }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
  it('shows source failures rather than empty successful inventory', async () => {
    // Arrange
    vi.mocked(api.listInventoryRecords).mockRejectedValue(new Error('Inventory unavailable'));
    // Act
    render(<MemoryRouter><HardwareSoftwareInventory systemId="a" /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Inventory unavailable');
    expect(screen.queryByText('No active inventory records.')).not.toBeInTheDocument();
  });
  it('does not grant mutations to a reader', async () => {
    // Arrange
    vi.mocked(api.listInventoryRecords).mockResolvedValue({ systemId: 'a', items: [], totalCount: 0, page: 1, pageSize: 50, canManage: false });
    // Act
    render(<MemoryRouter><HardwareSoftwareInventory systemId="a" /></MemoryRouter>);
    // Assert
    await screen.findByText('No active inventory records.');
    expect(screen.getByRole('button', { name: 'Add inventory item' })).toBeDisabled();
  });
});
