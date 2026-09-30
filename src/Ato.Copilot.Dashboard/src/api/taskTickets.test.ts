import { expect, it, vi } from 'vitest';
import apiClient from './client';
import { getTaskTicket } from './taskTickets';

vi.mock('./client', () => ({ default: { get: vi.fn() } }));

it('forwards the caller AbortSignal to the scoped GET request', async () => {
  // Arrange
  const controller = new AbortController();
  vi.mocked(apiClient.get).mockResolvedValue({ data: { link: null } });
  // Act
  await getTaskTicket('system/id', 'task/id', controller.signal);
  // Assert
  expect(apiClient.get).toHaveBeenCalledWith(
    '/systems/system%2Fid/tasks/task%2Fid/ticket', { signal: controller.signal });
});
