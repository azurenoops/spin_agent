import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import '../helpers/dialog';
import SystemHistoryPage from '../../features/systems/SystemHistoryPage';
import { listSystemHistory } from '../../features/systems/systemHistoryApi';
vi.mock('../../features/systems/systemHistoryApi', () => ({ listSystemHistory: vi.fn() }));
const entry = { id: 'event-a', eventType: 'ProfileReviewed', timestamp: '2026-09-26T12:00:00Z',
  actor: 'Reviewer A', summary: 'Mission profile reviewed', relatedEntityType: 'SystemProfile', relatedEntityId: 'profile-a' };
function mount() { render(<MemoryRouter initialEntries={['/systems/a/history']}><Routes><Route path="/systems/:id/history" element={<SystemHistoryPage />} /></Routes></MemoryRouter>); }
describe('System activity history', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(listSystemHistory).mockResolvedValue({ systemId: 'a', source: 'DashboardActivity', items: [entry], totalCount: 26, page: 1, pageSize: 25 });
  });
  it('inspects the actual retained event without claiming a source version or mutating history', async () => {
    // Arrange
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'View record: Mission profile reviewed' }));
    // Assert
    const dialog = screen.getByRole('dialog', { name: 'Retained activity record' });
    expect(within(dialog).getByText('event-a')).toBeVisible();
    expect(within(dialog).getByText('profile-a')).toBeVisible();
    expect(within(dialog).getByText('Reviewer A')).toBeVisible();
  });
  it('filters and pages within the selected system through the real history contract', async () => {
    // Arrange
    mount();
    await screen.findByText('Mission profile reviewed');
    // Act
    fireEvent.change(screen.getByLabelText('Event type'), { target: { value: 'ProfileReviewed' } });
    fireEvent.click(screen.getByRole('button', { name: 'Filter history' }));
    // Assert
    await waitFor(() => expect(listSystemHistory).toHaveBeenLastCalledWith('a', expect.objectContaining({ eventType: 'ProfileReviewed', page: 1 }), expect.any(AbortSignal)));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    // Assert
    await waitFor(() => expect(listSystemHistory).toHaveBeenLastCalledWith('a', expect.objectContaining({ eventType: 'ProfileReviewed', page: 2 }), expect.any(AbortSignal)));
  });
  it('offers retry instead of false empty history when the endpoint denies access', async () => {
    // Arrange
    vi.mocked(listSystemHistory).mockRejectedValueOnce(new Error('History access denied'));
    mount();
    // Act
    expect(await screen.findByRole('alert')).toHaveTextContent('History access denied');
    fireEvent.click(screen.getByRole('button', { name: 'Retry history' }));
    // Assert
    expect(await screen.findByText('Mission profile reviewed')).toBeVisible();
  });
});
