import { afterEach, describe, expect, it, vi } from 'vitest';
import { boundedRequest, sameScope } from '../../features/workspace-operations/system-capabilities/systemCapabilityRequests';

describe('System capability request bounds', () => {
  afterEach(() => vi.useRealTimers());

  it('stops waiting at the deadline and aborts the actual read signal', async () => {
    // Arrange
    vi.useFakeTimers();
    let signal: AbortSignal | undefined;
    const result = boundedRequest(current => { signal = current; return new Promise<string>(() => undefined); }, undefined, 50);
    const rejection = expect(result).rejects.toThrow(/timed out.*saved server state/i);
    // Act
    await vi.advanceTimersByTimeAsync(51);
    // Assert
    await rejection;
    expect(signal?.aborted).toBe(true);
  });

  it('cancels read completion when the route or actor changes', async () => {
    // Arrange
    const controller = new AbortController();
    const result = boundedRequest(() => new Promise<string>(() => undefined), controller.signal);
    const rejection = expect(result).rejects.toThrow('The request was cancelled.');
    // Act
    controller.abort();
    // Assert
    await rejection;
  });

  it('requires matching tenant, system and operation kind on recovery', () => {
    // Arrange
    const operation = { tenantId: 'TENANT-A', systemId: 'SYSTEM-A', kind: 'Setup' };
    // Act
    const same = sameScope(operation, 'tenant-a', 'system-a', 'Setup');
    // Assert
    expect(same).toBe(true);
    expect(sameScope(operation, 'tenant-b', 'system-a', 'Setup')).toBe(false);
    expect(sameScope(operation, 'tenant-a', 'system-b', 'Setup')).toBe(false);
    expect(sameScope(operation, 'tenant-a', 'system-a', 'Removal')).toBe(false);
  });
});
