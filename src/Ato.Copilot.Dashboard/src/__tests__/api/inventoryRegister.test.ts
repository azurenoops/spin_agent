import { beforeEach, describe, expect, it, vi } from 'vitest';
import client from '../../api/client';
import { saveInventoryRecord, listInventoryRecords } from '../../api/inventoryRegister';
vi.mock('../../api/client', () => ({ default: { get: vi.fn(), put: vi.fn(), post: vi.fn() } }));
const record = { id: 'item-a', systemId: 'a', itemName: 'Database', type: 'Software', status: 'Active',
  softwareFunction: 'Database', vendor: 'Provider', version: '1', parentHardwareId: null };
beforeEach(() => vi.clearAllMocks());
describe('Inventory save confirmation', () => {
  it('verifies the canonical reread rather than trusting an HTTP success', async () => {
    // Arrange
    vi.mocked(client.put).mockResolvedValue({ data: record });
    vi.mocked(client.get).mockResolvedValue({ data: { ...record, version: 'old' } });
    // Act / Assert
    await expect(saveInventoryRecord('a', 'item-a', { itemName: 'Database', type: 1, softwareFunction: 1, version: '1' }))
      .rejects.toThrow('did not confirm');
  });
  it('rejects a response from another system', async () => {
    // Arrange
    vi.mocked(client.post).mockResolvedValue({ data: { ...record, systemId: 'other' } });
    // Act / Assert
    await expect(saveInventoryRecord('a', undefined, { itemName: 'Database', type: 1 })).rejects.toThrow('does not match');
    expect(client.get).not.toHaveBeenCalled();
  });
  it('rejects unknown permission or count shapes rather than rendering an empty successful list', async () => {
    // Arrange
    vi.mocked(client.get).mockResolvedValue({ data: { systemId: 'a', items: [], totalCount: null, page: 1, pageSize: 50 } });
    // Act / Assert
    await expect(listInventoryRecords('a')).rejects.toThrow('could not be confirmed');
  });
  it('accepts an exact saved managed-software record without physical hardware', async () => {
    // Arrange
    vi.mocked(client.post).mockResolvedValue({ data: record });
    vi.mocked(client.get).mockResolvedValue({ data: record });
    // Act
    const result = await saveInventoryRecord('a', undefined, { itemName: 'Database', type: 1, softwareFunction: 1, version: '1' });
    // Assert
    expect(result.id).toBe('item-a');
    expect(result.parentHardwareId).toBeNull();
  });
});
