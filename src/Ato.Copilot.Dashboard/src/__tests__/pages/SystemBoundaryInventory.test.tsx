import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import SystemBoundaryInventory from '../../features/systems/SystemBoundaryInventory';
import type { BoundaryDefinitionDto } from '../../types/dashboard';

const boundary: BoundaryDefinitionDto = {
  id: 'boundary-a', registeredSystemId: 'system-a', name: 'mission-api', description: null,
  boundaryType: 'Logical', isPrimary: true, componentCount: 0, resourceCount: 0,
  coveragePercent: 0, createdAt: '2026-09-28',
};

describe('Recorded boundary inventory', () => {
  it('renders an empty boundary as a real row rather than an empty component table', () => {
    // Arrange / Act
    render(<SystemBoundaryInventory boundaries={[boundary]} onOpenBoundary={vi.fn()} />);
    // Assert
    const row = screen.getByRole('row', { name: /mission-api/ });
    expect(within(row).getByRole('cell', { name: 'Logical' })).toBeVisible();
    expect(within(row).getByRole('cell', { name: 'No description recorded' })).toBeVisible();
    expect(within(row).getByRole('cell', { name: 'Primary' })).toBeVisible();
    expect(screen.getAllByRole('row')).toHaveLength(2);
    expect(screen.queryByText('No boundaries defined yet.')).not.toBeInTheDocument();
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    expect(screen.getAllByRole('columnheader').map(cell => cell.textContent))
      .toEqual(['Boundary', 'Type', 'Description', 'Role', 'Open']);
  });

  it('lists every boundary, not only the primary boundary or those with components', () => {
    // Arrange
    const recovery = { ...boundary, id: 'boundary-b', name: 'Recovery', isPrimary: false, componentCount: 3 };
    // Act
    render(<SystemBoundaryInventory boundaries={[recovery, boundary]} onOpenBoundary={vi.fn()} />);
    // Assert
    expect(screen.getAllByRole('row')).toHaveLength(3);
    expect(screen.getByRole('cell', { name: 'mission-api' })).toBeVisible();
    expect(screen.getByRole('cell', { name: 'Recovery' })).toBeVisible();
    expect(screen.getByRole('cell', { name: 'Additional' })).toBeVisible();
  });

  it('opens the exact boundary even when names are identical', () => {
    // Arrange
    const open = vi.fn();
    render(<SystemBoundaryInventory boundaries={[boundary, { ...boundary, id: 'boundary-b', isPrimary: false }]} onOpenBoundary={open} />);
    // Act
    fireEvent.click(screen.getAllByRole('button', { name: 'Open boundary mission-api' })[1]!);
    // Assert
    expect(open).toHaveBeenCalledExactlyOnceWith('boundary-b');
  });

  it('retains the compact table and offers a heading action in empty and populated registers only when supplied', () => {
    // Arrange
    const create = vi.fn();
    // Act
    const view = render(<SystemBoundaryInventory boundaries={[]} onOpenBoundary={vi.fn()} />);
    // Assert
    expect(screen.getByText('No boundaries defined yet.')).toBeVisible();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
    expect(screen.getByRole('table')).toHaveClass('text-xs');
    // Act
    view.rerender(<SystemBoundaryInventory boundaries={[]} onOpenBoundary={vi.fn()}
      action={<button onClick={create}>Add System Boundary</button>} />);
    const trigger = screen.getByRole('button', { name: 'Add System Boundary' });
    expect(trigger.parentElement).toContainElement(screen.getByRole('heading', { name: 'Recorded boundary definitions' }));
    fireEvent.click(trigger);
    // Assert
    expect(create).toHaveBeenCalledOnce();
    // Act
    view.rerender(<SystemBoundaryInventory boundaries={[boundary]} onOpenBoundary={vi.fn()}
      action={<button onClick={create}>Add System Boundary</button>} />);
    // Assert
    expect(screen.getByRole('button', { name: 'Add System Boundary' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Open boundary mission-api' })).toBeVisible();
  });

  it('keeps the boundary row and its trigger stable when assignment counts refresh', () => {
    // Arrange
    const open = vi.fn();
    const view = render(<SystemBoundaryInventory boundaries={[boundary]} onOpenBoundary={open} />);
    const trigger = screen.getByRole('button', { name: 'Open boundary mission-api' });
    // Act
    view.rerender(<SystemBoundaryInventory boundaries={[{ ...boundary, componentCount: 1 }]} onOpenBoundary={open} />);
    // Assert
    expect(screen.getByRole('button', { name: 'Open boundary mission-api' })).toBe(trigger);
    expect(screen.getAllByRole('row')).toHaveLength(2);
  });
});
