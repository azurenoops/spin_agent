import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import FindingsQueue, { type FindingQueueItem } from '../../features/remediation-workspace/FindingsQueue';

const finding = (id: string, changes: Partial<FindingQueueItem> = {}): FindingQueueItem => ({
  id, title: `Finding ${id}`, controlId: 'AC-12', severity: 'Medium', disposition: 'Open',
  owner: null, workStatus: 'Not assigned', readyToVerify: false, closed: false,
  sourceName: 'Azure configuration checks', planRevision: 2, ...changes,
});

describe('Finding-led work queue', () => {
  it('filters owner, verification, closed and severity against the full queue', () => {
    // Arrange
    render(<FindingsQueue items={[finding('a', { severity: 'High' }), finding('b', { owner: 'Team', readyToVerify: true }),
      finding('c', { owner: 'Team', closed: true, disposition: 'Remediated' })]} onOpen={vi.fn()} />);
    // Act / Assert
    fireEvent.click(screen.getByRole('button', { name: 'Needs owner (1)' }));
    expect(screen.getByRole('button', { name: 'Finding a' })).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Finding b' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Ready to verify (1)' }));
    expect(screen.getByRole('button', { name: 'Finding b' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Closed (1)' }));
    expect(screen.getByRole('button', { name: 'View history' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'All findings (3)' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Severity' }), { target: { value: 'High' } });
    expect(screen.getByRole('button', { name: 'Finding a' })).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Finding c' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Board' }));
    fireEvent.click(screen.getByRole('button', { name: 'List' }));
    expect(screen.getByRole('table')).toBeVisible();
  });
  it('shows an empty state without search, table or pagination and never claims a clean assessment', () => {
    // Arrange / Act
    render(<FindingsQueue items={[]} onOpen={vi.fn()} emptyAction={<button>Review assessment results</button>} />);
    // Assert
    expect(screen.getByText('No findings recorded')).toBeVisible();
    expect(screen.getByText(/does not establish that the system passed/i)).toBeVisible();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Next page' })).not.toBeInTheDocument();
  });

  it('counts findings rather than tasks and keeps independent disposition visible', () => {
    // Arrange
    const items = [finding('a'), finding('b', { owner: 'Application team', workStatus: 'Work completed',
      readyToVerify: true }), finding('c', { owner: 'Security team', closed: true, disposition: 'Remediated' })];
    // Act
    render(<FindingsQueue items={items} onOpen={vi.fn()} />);
    // Assert
    expect(screen.getByRole('button', { name: 'All findings (3)' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Needs owner (1)' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Ready to verify (1)' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Closed (1)' })).toBeVisible();
    const row = screen.getByRole('row', { name: /Finding b/ });
    expect(within(row).getByText('Open')).toBeVisible();
    expect(within(row).getByText('Work completed')).toBeVisible();
    expect(within(row).getByRole('button', { name: /Review evidence/ })).toBeVisible();
  });

  it('uses the same filters in List and Board without allowing drag-to-close', () => {
    // Arrange
    render(<FindingsQueue items={[finding('a'), finding('b', { title: 'Session timeout', owner: 'App team', readyToVerify: true })]} onOpen={vi.fn()} />);
    // Act
    fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'timeout' } });
    fireEvent.click(screen.getByRole('button', { name: 'Board' }));
    // Assert
    expect(screen.getByRole('button', { name: /Session timeout/ })).toBeVisible();
    expect(screen.queryByText('Finding a')).not.toBeInTheDocument();
    expect(document.querySelector('[draggable="true"]')).toBeNull();
    expect(screen.getByRole('button', { name: 'All findings (2)' })).toBeVisible();
  });

  it('paginates the filtered findings and opens stable identity with retained source version', () => {
    // Arrange
    const open = vi.fn();
    render(<FindingsQueue items={Array.from({ length: 26 }, (_, i) => finding(`${i}`))} onOpen={open} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Next page' }));
    // Assert
    expect(screen.getByText('26–26 of 26 findings')).toBeVisible();
    expect(screen.getByText('Azure configuration checks · Plan revision 2')).toBeVisible();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Finding 25' }));
    // Assert
    expect(open).toHaveBeenCalledWith('25');
  });

  it('offers a reset for no matches rather than presenting a system empty state', () => {
    // Arrange
    render(<FindingsQueue items={[finding('a')]} onOpen={vi.fn()} />);
    // Act
    fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'no match' } });
    // Assert
    expect(screen.getByText('No matching findings')).toBeVisible();
    expect(screen.queryByText('No findings recorded')).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Finding a' })).toBeVisible();
  });
});
