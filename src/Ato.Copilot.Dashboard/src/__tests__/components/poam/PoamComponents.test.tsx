import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import PoamTable, { SeverityBadge, StatusBadge } from '../../../components/poam/PoamTable';
import CascadeConfirmDialog from '../../../components/poam/CascadeConfirmDialog';
import type { PoamListItem, PoamListQuery } from '../../../types/poam';

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
