import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import PoamManagement from '../../../pages/PoamManagement';
import '../../helpers/dialog';

const state = vi.hoisted(() => ({
  systemId: 'system-a',
  data: { items: [], totalCount: 0, page: 1, pageSize: 25, totalPages: 0 } as unknown,
  metrics: { totalOpen: 0, overdue: 0, byStatus: [], queue: { all: 0, overdue: 0, readyToVerify: 0, closed: 0 } } as unknown,
  error: null as Error | null,
}));
vi.mock('../../../components/layout/SystemLayout', () => ({ useSystemContext: () => ({ detail: { systemId: state.systemId } }) }));
vi.mock('../../../components/permissions/useSystemMutationPermission', () => ({ useSystemMutationPermission: () => true }));
vi.mock('../../../hooks/usePoam', () => ({
  usePoamList: () => ({ data: state.data, error: state.error, loading: false, refresh: vi.fn() }),
  usePoamMetrics: () => ({ data: state.metrics, error: null, loading: false, refresh: vi.fn() }),
  useCreatePoam: () => ({ create: vi.fn(), loading: false }),
}));
vi.mock('../../../hooks/usePoamQueue', () => ({
  usePoamQueue: () => ({ data: state.data ? { ...state.data as object, counts: (state.metrics as { queue: unknown }).queue } : null, error: state.error, loading: false, refresh: vi.fn() }),
}));
vi.mock('../../../components/poam/PoamTrendCharts', () => ({ default: () => <div>Trends content</div> }));
vi.mock('../../../components/poam/TicketingConfig', () => ({ default: () => <div>Ticketing content</div> }));
vi.mock('../../../components/poam/PoamDetailDrawer', () => ({ default: () => <div>Commitment details</div> }));
vi.mock('../../../components/poam/PoamCreateForm', () => ({ default: () => <div>Creation form</div> }));

describe('POA&M commitment workspace', () => {
  beforeEach(() => {
    state.systemId = 'system-a';
    state.data = { items: [], totalCount: 0, page: 1, pageSize: 25, totalPages: 0 };
    state.metrics = { totalOpen: 0, overdue: 0, byStatus: [], queue: { all: 0, overdue: 0, readyToVerify: 0, closed: 0 } };
    state.error = null;
  });
  it('shows one entry path and actionable emptiness without a table', () => {
    // Arrange / Act
    render(<MemoryRouter><PoamManagement /></MemoryRouter>);
    // Assert
    expect(screen.getByRole('heading', { name: 'Track remediation commitments' })).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Add POA&M' })).toHaveLength(1);
    expect(screen.getByText('No POA&M items recorded')).toBeInTheDocument();
    expect(screen.queryByRole('table')).toBeNull();
    expect(screen.queryByRole('textbox')).toBeNull();
  });
  it('uses backend counts rather than the current page', () => {
    // Arrange
    state.metrics = { totalOpen: 80, overdue: 9, byStatus: [], queue: { all: 100, overdue: 9, readyToVerify: 12, closed: 20 } };
    // Act
    render(<MemoryRouter><PoamManagement /></MemoryRouter>);
    // Assert
    expect(screen.getByRole('button', { name: 'All items (100)' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ready to verify (12)' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Closed (20)' })).toBeInTheDocument();
  });
  it('does not turn list failure into an empty success', () => {
    // Arrange
    state.data = null;
    state.error = new Error('Unavailable');
    // Act
    render(<MemoryRouter><PoamManagement /></MemoryRouter>);
    // Assert
    expect(screen.getByText('Unable to load POA&M items.')).toBeInTheDocument();
    expect(screen.queryByText('No POA&M items recorded')).toBeNull();
  });
  it('clears an open create flow when the system changes', () => {
    // Arrange
    const view = render(<MemoryRouter><PoamManagement /></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: 'Add POA&M' }));
    expect(screen.getByText('Creation form')).toBeInTheDocument();
    // Act
    state.systemId = 'system-b';
    view.rerender(<MemoryRouter><PoamManagement /></MemoryRouter>);
    // Assert
    expect(screen.queryByText('Creation form')).toBeNull();
  });
});
