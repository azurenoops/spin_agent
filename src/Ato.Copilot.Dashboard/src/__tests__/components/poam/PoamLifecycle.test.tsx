import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import PoamLifecycleActions from '../../../components/poam/PoamLifecycleActions';
import { updatePoamStatus } from '../../../api/poam';
import type { PoamDetail } from '../../../types/poam';
vi.mock('../../../components/permissions/useSystemMutationPermission', () => ({ useSystemMutationPermission: () => true }));
vi.mock('../../../api/poam', () => ({ updatePoamStatus: vi.fn() }));
const detail = { id: 'poam-a', systemId: 'system-a', rowVersion: 'revision-a', status: 'Ongoing', milestones: [] } as unknown as PoamDetail;
describe('POA&M retained lifecycle', () => {
  it('keeps deadline reason and concurrency token in the authorized update', async () => {
    // Arrange
    vi.mocked(updatePoamStatus).mockResolvedValue({ poam: detail });
    render(<PoamLifecycleActions detail={detail} onStatusChanged={vi.fn()} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Mark Delayed' }));
    fireEvent.change(screen.getByLabelText('Delay reason *'), { target: { value: 'Change window unavailable' } });
    fireEvent.change(screen.getByLabelText('Revised completion date *'), { target: { value: '2026-12-01' } });
    fireEvent.click(screen.getByRole('button', { name: 'Confirm' }));
    // Assert
    await waitFor(() => expect(updatePoamStatus).toHaveBeenCalledWith('poam-a', expect.objectContaining({
      rowVersion: 'revision-a', status: 'Delayed', delayReason: 'Change window unavailable', revisedDate: '2026-12-01',
    })));
  });
  it('does not claim an authorized completion is a verified outcome', () => {
    // Arrange / Act
    render(<PoamLifecycleActions detail={detail} onStatusChanged={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Mark completed (manual disposition)' }));
    // Assert
    expect(screen.getByText(/does not certify task verification or evidence review/)).toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).toBeNull();
  });
});
