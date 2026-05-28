import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import RestoreUnsavedChangesPrompt from '../../features/auth/RestoreUnsavedChangesPrompt';
import type { UnsavedSnapshot } from '../../features/auth/types';

beforeEach(() => {
  localStorage.clear();
});

afterEach(() => {
  localStorage.clear();
});

function seed(oid: string, formId: string, data: unknown, savedAt = '2026-05-28T12:00:00Z') {
  localStorage.setItem(
    `ato.unsavedChanges.${oid}.${formId}`,
    JSON.stringify({ savedAt, data }),
  );
}

describe('RestoreUnsavedChangesPrompt', () => {
  it('renders nothing when no ato.unsavedChanges.{oid}.* keys exist', () => {
    // Arrange + Act
    const { container } = render(<RestoreUnsavedChangesPrompt oid="user-a" />);

    // Assert
    expect(container.firstChild).toBeNull();
  });

  it('renders the prompt with formId + savedAt when a single key exists', () => {
    // Arrange
    seed('user-a', 'intake-step-2', { title: 'Draft' }, '2026-05-28T12:00:00Z');

    // Act
    render(<RestoreUnsavedChangesPrompt oid="user-a" />);

    // Assert — the formId is visible and the savedAt timestamp is
    // rendered (locale-dependent format; check via partial match).
    expect(screen.getByText(/intake-step-2/)).toBeInTheDocument();
    expect(screen.getByRole('region', { name: /unsaved changes/i })).toBeInTheDocument();
  });

  it("'Restore' dispatches ato:restore-unsaved with the snapshot and removes the key", () => {
    // Arrange
    seed('user-a', 'intake-step-2', { title: 'Draft' });
    render(<RestoreUnsavedChangesPrompt oid="user-a" />);

    const captured: UnsavedSnapshot[] = [];
    const handler = (e: Event) => {
      captured.push((e as CustomEvent<UnsavedSnapshot>).detail);
    };
    window.addEventListener('ato:restore-unsaved', handler as EventListener);

    // Act
    fireEvent.click(screen.getByRole('button', { name: /^restore$/i }));

    // Assert
    expect(captured).toHaveLength(1);
    expect(captured[0]?.formId).toBe('intake-step-2');
    expect(captured[0]?.data).toEqual({ title: 'Draft' });
    expect(localStorage.getItem('ato.unsavedChanges.user-a.intake-step-2')).toBeNull();

    window.removeEventListener('ato:restore-unsaved', handler as EventListener);
  });

  it("'Discard' removes the key without dispatching ato:restore-unsaved", () => {
    // Arrange
    seed('user-a', 'intake-step-2', { title: 'Draft' });
    render(<RestoreUnsavedChangesPrompt oid="user-a" />);

    const onRestore = vi.fn();
    window.addEventListener('ato:restore-unsaved', onRestore as EventListener);

    // Act
    fireEvent.click(screen.getByRole('button', { name: /discard/i }));

    // Assert
    expect(onRestore).not.toHaveBeenCalled();
    expect(localStorage.getItem('ato.unsavedChanges.user-a.intake-step-2')).toBeNull();

    window.removeEventListener('ato:restore-unsaved', onRestore as EventListener);
  });

  it('ignores keys belonging to a different oid', () => {
    // Arrange — seed only user-b.
    seed('user-b', 'intake-step-2', { title: 'Other user' });

    // Act
    const { container } = render(<RestoreUnsavedChangesPrompt oid="user-a" />);

    // Assert — nothing surfaces for user-a.
    expect(container.firstChild).toBeNull();
    expect(localStorage.getItem('ato.unsavedChanges.user-b.intake-step-2')).not.toBeNull();
  });
});
