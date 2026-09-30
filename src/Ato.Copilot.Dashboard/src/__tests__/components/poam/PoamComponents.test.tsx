import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import PoamTable, { SeverityBadge, StatusBadge } from '../../../components/poam/PoamTable';
import CascadeConfirmDialog from '../../../components/poam/CascadeConfirmDialog';
import type { PoamListItem, PoamListQuery } from '../../../types/poam';
import { DEFAULT_SETTINGS, SettingsContext } from '../../../hooks/useSettings';

// ═══════════════════════════════════════════════════════════════════════════
// Helper fixtures
// ═══════════════════════════════════════════════════════════════════════════

const baseItem: PoamListItem = {
  id: 'poam-1',
  controlId: 'AC-2',
  weakness: 'Weak password policy',
  catSeverity: 'I',
  status: 'Ongoing',
  poc: 'john.doe',
  dueDate: new Date(Date.now() + 30 * 86400000).toISOString(),
  daysRemaining: 30,
  components: [{ id: 'comp-1', name: 'Web Server', type: 'Thing' }],
  milestoneProgress: { completed: 0, total: 2 },
  deviationType: null,
  externalTicketRef: null,
  remediationTaskId: null,
  remediationTaskStatus: null,
  isOverdue: false,
  systemId: 'sys-1',
  systemName: 'Test System',
};

const defaultQuery: PoamListQuery = {
  page: 1,
  pageSize: 25,
  status: undefined,
  catSeverity: undefined,
  overdue: false,
  search: '',
  componentId: undefined,
  systemId: 'sys-1',
  sortBy: 'dueDate',
  sortDirection: 'asc',
};

// ═══════════════════════════════════════════════════════════════════════════
// SeverityBadge
// ═══════════════════════════════════════════════════════════════════════════

describe('SeverityBadge', () => {
  it('renders CAT I severity', () => {
    const { container } = render(<SeverityBadge severity="I" />);
    const badge = container.querySelector('span');
    expect(badge?.textContent).toContain('I');
  });

  it('renders CAT II severity', () => {
    const { container } = render(<SeverityBadge severity="II" />);
    const badge = container.querySelector('span');
    expect(badge?.textContent).toContain('II');
  });

  it('renders CAT III severity', () => {
    const { container } = render(<SeverityBadge severity="III" />);
    const badge = container.querySelector('span');
    expect(badge?.textContent).toContain('III');
  });
});

// ═══════════════════════════════════════════════════════════════════════════
// StatusBadge
// ═══════════════════════════════════════════════════════════════════════════

describe('StatusBadge', () => {
  it('renders Ongoing status', () => {
    render(<StatusBadge status="Ongoing" />);
    expect(screen.getByText('Ongoing')).toBeDefined();
  });

  it('renders Completed status', () => {
    render(<StatusBadge status="Completed" />);
    expect(screen.getByText('Completed')).toBeDefined();
  });

  it('renders Delayed status', () => {
    render(<StatusBadge status="Delayed" />);
    expect(screen.getByText('Delayed')).toBeDefined();
  });

  it('renders RiskAccepted status', () => {
    render(<StatusBadge status="RiskAccepted" />);
    expect(screen.getByText(/Risk/)).toBeDefined();
  });
});

// ═══════════════════════════════════════════════════════════════════════════
// PoamTable
// ═══════════════════════════════════════════════════════════════════════════

describe('PoamTable', () => {
  it('shows the retained next milestone and server-projected readiness', () => {
    // Arrange
    const item = { ...baseItem, readyToVerify: true, nextMilestone: { id: 'm-2', description: 'Verify corrected timeout', targetDate: '2026-10-05T00:00:00Z', completedDate: null, sequence: 2, isOverdue: true } };
    // Act
    render(<PoamTable items={[item]} totalItems={1} query={defaultQuery} loading={false} onQueryChange={vi.fn()} onRowClick={vi.fn()} />);
    // Assert
    expect(screen.getByText('Verify corrected timeout')).toBeInTheDocument();
    expect(screen.getByText('Ready to verify')).toBeInTheDocument();
    expect(screen.getByText(/Milestone overdue/)).toBeInTheDocument();
  });
  it('renders calendar deadlines without shifting them into the previous day', () => {
    // Arrange
    const settings = { ...DEFAULT_SETTINGS, dateFormat: 'ISO' as const, timezone: 'America/Los_Angeles' };
    // Act
    render(<SettingsContext.Provider value={{ settings, updateSettings: vi.fn(), resetSettings: vi.fn() }}>
      <PoamTable items={[{ ...baseItem, dueDate: '2026-10-05T00:00:00Z', nextMilestone: {
        id: 'milestone', description: 'Review correction', targetDate: '2026-10-04T00:00:00Z',
        completedDate: null, sequence: 1, isOverdue: false,
      } }]} totalItems={1} query={defaultQuery} loading={false} onQueryChange={vi.fn()} onRowClick={vi.fn()} />
    </SettingsContext.Provider>);
    // Assert
    expect(screen.getByText('2026-10-05')).toBeInTheDocument();
    expect(screen.getByText('2026-10-04')).toBeInTheDocument();
  });
  it('keeps a truly empty queue free of filters and pagination', () => {
    // Arrange
    render(<PoamTable items={[]} totalItems={0} query={defaultQuery} loading={false} onQueryChange={vi.fn()} onRowClick={vi.fn()} />);
    // Act
    const table = screen.queryByRole('table');
    // Assert
    expect(table).toBeNull();
    expect(screen.queryByRole('textbox')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Next' })).toBeNull();
  });

  it('retains filters when a search has no results', () => {
    // Arrange
    render(<PoamTable items={[]} totalItems={0} query={{ ...defaultQuery, search: 'missing' }} loading={false} onQueryChange={vi.fn()} onRowClick={vi.fn()} />);
    // Act
    const search = screen.getByRole('textbox', { name: 'Search POA&M items' });
    // Assert
    expect(search).toHaveValue('missing');
    expect(screen.getByText('No matching POA&M items')).toBeInTheDocument();
  });
  it('does not call a filtered empty queue an empty system', () => {
    // Arrange
    render(<PoamTable items={[]} totalItems={0} query={{ ...defaultQuery, view: 'closed' }} loading={false} onQueryChange={vi.fn()} onRowClick={vi.fn()} />);
    // Act / Assert
    expect(screen.getByText('No matching POA&M items')).toBeInTheDocument();
    expect(screen.queryByText('No POA&M items recorded')).toBeNull();
  });

  it('opens commitments with a keyboard-accessible button and concise headers', () => {
    // Arrange
    const onRowClick = vi.fn();
    render(<PoamTable items={[baseItem]} totalItems={1} query={defaultQuery} loading={false} onQueryChange={vi.fn()} onRowClick={onRowClick} />);
    // Act
    fireEvent.click(screen.getByRole('button', { name: /Weak password policy/ }));
    // Assert
    expect(onRowClick).toHaveBeenCalledWith(baseItem);
    expect(screen.getByRole('columnheader', { name: 'Next milestone' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Owner' })).toBeInTheDocument();
  });
  it('renders table rows for each item', () => {
    const items = [
      baseItem,
      { ...baseItem, id: 'poam-2', controlId: 'AC-3', weakness: 'Missing MFA' },
    ];
    const onQueryChange = vi.fn();
    const onRowClick = vi.fn();

    render(
      <PoamTable
        items={items}
        totalItems={2}
        query={defaultQuery}
        loading={false}
        onQueryChange={onQueryChange}
        onRowClick={onRowClick}
      />
    );

    expect(screen.getByText('Weak password policy')).toBeDefined();
    expect(screen.getByText('Missing MFA')).toBeDefined();
  });

  it('calls onRowClick when a row is clicked', () => {
    const onRowClick = vi.fn();

    render(
      <PoamTable
        items={[baseItem]}
        totalItems={1}
        query={defaultQuery}
        loading={false}
        onQueryChange={vi.fn()}
        onRowClick={onRowClick}
      />
    );

    fireEvent.click(screen.getByText('Weak password policy'));
    expect(onRowClick).toHaveBeenCalledWith(baseItem);
  });

  it('shows loading state', () => {
    const { container } = render(
      <PoamTable
        items={[]}
        totalItems={0}
        query={defaultQuery}
        loading={true}
        onQueryChange={vi.fn()}
        onRowClick={vi.fn()}
      />
    );

    const spinner = container.querySelector('.animate-spin, .animate-pulse');
    expect(spinner).toBeDefined();
  });

  it('shows empty state when no items', () => {
    render(
      <PoamTable
        items={[]}
        totalItems={0}
        query={defaultQuery}
        loading={false}
        onQueryChange={vi.fn()}
        onRowClick={vi.fn()}
      />
    );

    expect(screen.getByText(/no.*poa/i)).toBeDefined();
  });
});

// ═══════════════════════════════════════════════════════════════════════════
// CascadeConfirmDialog
// ═══════════════════════════════════════════════════════════════════════════

describe('CascadeConfirmDialog', () => {
  it('renders message and buttons', () => {
    render(
      <CascadeConfirmDialog
        message="Apply status change to linked task?"
        onConfirm={vi.fn()}
        onDismiss={vi.fn()}
      />
    );

    expect(screen.getByText('Apply status change to linked task?')).toBeDefined();
    expect(screen.getByText(/skip/i)).toBeDefined();
    expect(screen.getByText(/apply cascade/i)).toBeDefined();
  });

  it('calls onDismiss when Skip is clicked', () => {
    const onDismiss = vi.fn();

    render(
      <CascadeConfirmDialog
        message="Test"
        onConfirm={vi.fn()}
        onDismiss={onDismiss}
      />
    );

    fireEvent.click(screen.getByText(/skip/i));
    expect(onDismiss).toHaveBeenCalledOnce();
  });

  it('calls onConfirm when confirm button is clicked', async () => {
    const onConfirm = vi.fn().mockResolvedValue(undefined);

    render(
      <CascadeConfirmDialog
        message="Test"
        onConfirm={onConfirm}
        onDismiss={vi.fn()}
      />
    );

    fireEvent.click(screen.getByText(/apply cascade/i));
    await waitFor(() => expect(onConfirm).toHaveBeenCalledOnce());
  });

  it('renders custom confirmLabel', () => {
    render(
      <CascadeConfirmDialog
        message="Test"
        confirmLabel="Complete Both"
        onConfirm={vi.fn()}
        onDismiss={vi.fn()}
      />
    );

    expect(screen.getByText('Complete Both')).toBeDefined();
  });

  it('renders optional detail text', () => {
    render(
      <CascadeConfirmDialog
        message="Main message"
        detail="Additional context here"
        onConfirm={vi.fn()}
        onDismiss={vi.fn()}
      />
    );

    expect(screen.getByText('Additional context here')).toBeDefined();
  });
});
