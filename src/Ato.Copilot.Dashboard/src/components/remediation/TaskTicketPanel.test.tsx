import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import TaskTicketPanel from './TaskTicketPanel';
import * as tickets from '../../api/taskTickets';

vi.mock('../../api/taskTickets', () => ({
  getTaskTicket: vi.fn(), createTaskTicket: vi.fn(), linkTaskTicket: vi.fn(),
  refreshTaskTicket: vi.fn(), unlinkTaskTicket: vi.fn(),
}));

const empty = {
  configured: true, canManage: true, mode: 'ManualPullOnly' as const,
  webhooksSupported: false as const, bidirectionalSupported: false as const, link: null,
};

describe('TaskTicketPanel', () => {
  beforeEach(() => vi.resetAllMocks());

  it('links an existing task ticket without implying local closure', async () => {
    // Arrange
    vi.mocked(tickets.getTaskTicket).mockResolvedValue(empty);
    vi.mocked(tickets.linkTaskTicket).mockResolvedValue(empty);
    render(<TaskTicketPanel systemId="system" taskId="task" />);
    // Act
    fireEvent.change(await screen.findByLabelText('Existing ticket identifier'), { target: { value: 'TEST-42' } });
    fireEvent.click(screen.getByRole('button', { name: 'Link existing ticket' }));
    // Assert
    await waitFor(() => expect(tickets.linkTaskTicket).toHaveBeenCalledWith('system', 'task', 'TEST-42', undefined));
    expect(screen.getByText(/does not close/i)).toBeTruthy();
    expect(tickets.createTaskTicket).not.toHaveBeenCalled();
  });

  it('shows read-only snapshots and blocks uncertain create retries', async () => {
    // Arrange
    vi.mocked(tickets.getTaskTicket).mockResolvedValue({
      ...empty, link: {
        id: 'id', provider: 'Jira', externalRef: null, externalUrl: null, externalStatus: null,
        externalAssignee: null, lastSuccessfulSyncAt: null, state: 'Uncertain',
        rowVersion: 'version', correlationKey: 'stable-key', lastError: 'Creation uncertain',
      },
    });
    // Act
    render(<TaskTicketPanel systemId="system" taskId="task" />);
    // Assert
    expect(await screen.findByText('stable-key')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Create external ticket' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Link existing ticket' })).toBeTruthy();
  });

  it('does not show mutation controls without server permission', async () => {
    // Arrange
    vi.mocked(tickets.getTaskTicket).mockResolvedValue({ ...empty, canManage: false });
    // Act
    render(<TaskTicketPanel systemId="system" taskId="task" />);
    // Assert
    await screen.findByText(/read-only access/i);
    expect(screen.queryByRole('button', { name: 'Create external ticket' })).toBeNull();
  });

  it('provides dark-mode surfaces for its panel, input and actions', async () => {
    // Arrange
    vi.mocked(tickets.getTaskTicket).mockResolvedValue(empty);
    // Act
    render(<TaskTicketPanel systemId="system" taskId="task" />);
    // Assert
    expect(await screen.findByLabelText('Existing ticket identifier')).toHaveClass('dark:bg-slate-950');
    expect(screen.getByRole('region', { name: 'Task external ticket' })).toHaveClass('dark:bg-slate-900');
    expect(screen.getByRole('button', { name: 'Link existing ticket' })).toHaveClass('dark:text-slate-100');
  });

  it('aborts obsolete initial loads when switching tasks or unmounting', () => {
    // Arrange
    vi.mocked(tickets.getTaskTicket).mockImplementation(() => new Promise(() => {}));
    const { rerender, unmount } = render(<TaskTicketPanel systemId="system" taskId="task-a" />);
    // Act
    const firstSignal = vi.mocked(tickets.getTaskTicket).mock.calls[0]?.[2];
    rerender(<TaskTicketPanel systemId="system" taskId="task-b" />);
    const nextSignal = vi.mocked(tickets.getTaskTicket).mock.calls[1]?.[2];
    unmount();
    // Assert
    expect(firstSignal).toBeInstanceOf(AbortSignal);
    expect(firstSignal?.aborted).toBe(true);
    expect(nextSignal).toBeInstanceOf(AbortSignal);
    expect(nextSignal?.aborted).toBe(true);
  });

  it('aborts a pending explicit reload when the drawer closes', async () => {
    // Arrange
    vi.mocked(tickets.getTaskTicket).mockRejectedValueOnce(new Error('Read failed'))
      .mockImplementationOnce(() => new Promise(() => {}));
    const { unmount } = render(<TaskTicketPanel systemId="system" taskId="task" />);
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Reload ticket' }));
    const signal = vi.mocked(tickets.getTaskTicket).mock.calls[1]?.[2];
    unmount();
    // Assert
    expect(signal).toBeInstanceOf(AbortSignal);
    expect(signal?.aborted).toBe(true);
  });
});
