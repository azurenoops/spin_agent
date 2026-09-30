import { describe, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import { resolveConflict } from '../../api/emass-status';

vi.mock('../../api/client', () => ({ default: { put: vi.fn() } }));

describe('eMASS conflict resolution request', () => {
  it.each([undefined, 'Confirmed the returned value with the owner.'])('preserves optional rationale compatibility (%s)', async rationale => {
    // Arrange
    const returned = { id: 'conflict', conflictStatus: 'KeepSpin', rationale: rationale ?? null, resolvedBy: 'server-actor' };
    vi.mocked(apiClient.put).mockResolvedValueOnce({ data: { data: returned } });
    // Act
    const response = await resolveConflict('system', 'conflict', 'KeepSpin', rationale);
    // Assert
    expect(apiClient.put).toHaveBeenLastCalledWith('/systems/system/emass/conflicts/conflict',
      rationale === undefined ? { resolution: 'KeepSpin' } : { resolution: 'KeepSpin', rationale });
    expect(response).toEqual(returned);
  });
});
