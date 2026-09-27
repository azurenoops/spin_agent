import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import ConMon from '../../pages/ConMon';
import { generateConMonReport } from '../../api/conmon';

const state = vi.hoisted(() => ({ refresh: vi.fn() }));
vi.mock('../../components/layout/SystemLayout', () => ({ useSystemContext: () => ({ detail: { systemId: 'system-a' } }) }));
vi.mock('../../hooks/usePolling', () => ({ usePolling: () => ({
  data: { systemId: 'system-a', systemName: 'Mission Alpha', reports: [] }, error: null, loading: false, refresh: state.refresh,
}) }));
vi.mock('../../api/conmon', () => ({ generateConMonReport: vi.fn(), getConMonOverview: vi.fn() }));

describe('System monitoring reports task', () => {
  beforeEach(() => vi.clearAllMocks());
  it('generates a report with the selected period using the existing system service', async () => {
    // Arrange
    vi.mocked(generateConMonReport).mockResolvedValue({} as Awaited<ReturnType<typeof generateConMonReport>>);
    render(<MemoryRouter><ConMon view="reports" /></MemoryRouter>);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Generate Report' }));
    fireEvent.change(screen.getByLabelText('Report Type'), { target: { value: 'Quarterly' } });
    fireEvent.change(screen.getByLabelText('Period'), { target: { value: '2026-09' } });
    fireEvent.click(screen.getByRole('button', { name: 'Generate' }));
    // Assert
    await waitFor(() => expect(generateConMonReport).toHaveBeenCalledWith('system-a', { reportType: 'Quarterly', period: '2026-09' }));
    expect(state.refresh).toHaveBeenCalled();
    expect(screen.getByRole('heading', { name: 'Continuous monitoring reports' })).toBeVisible();
    expect(screen.queryByText('Monitoring Status')).not.toBeInTheDocument();
  });

  it('keeps a rejected generation visible instead of indicating completion', async () => {
    // Arrange
    vi.mocked(generateConMonReport).mockRejectedValue(new Error('Report generation is not permitted.'));
    render(<MemoryRouter><ConMon view="reports" /></MemoryRouter>);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Generate Report' }));
    fireEvent.click(screen.getByRole('button', { name: 'Generate' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Report generation is not permitted.');
    expect(state.refresh).not.toHaveBeenCalled();
  });
});
