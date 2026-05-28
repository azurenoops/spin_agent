import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, act } from '@testing-library/react';
import {
  useIdleFormStateBackup,
  purgeUnsavedChanges,
} from '../../features/auth/useIdleFormStateBackup';
import type { UseIdleFormStateBackupResult } from '../../features/auth/types';

// ─── Harness ────────────────────────────────────────────────────────────

function Harness({
  oid,
  hookRef,
}: {
  oid: string;
  hookRef: { current: UseIdleFormStateBackupResult | null };
}) {
  const result = useIdleFormStateBackup(oid);
  hookRef.current = result;
  return null;
}

function keysForOid(oid: string): string[] {
  const prefix = `ato.unsavedChanges.${oid}.`;
  const keys: string[] = [];
  for (let i = 0; i < localStorage.length; i += 1) {
    const k = localStorage.key(i);
    if (k && k.startsWith(prefix)) keys.push(k);
  }
  return keys;
}

// ─── Tests ──────────────────────────────────────────────────────────────

describe('useIdleFormStateBackup', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    localStorage.clear();
  });

  afterEach(() => {
    vi.useRealTimers();
    localStorage.clear();
  });

  it('invokes a registered serializer synchronously on ato:idle-warning', () => {
    // Arrange
    const oid = 'user-a';
    const ref: { current: UseIdleFormStateBackupResult | null } = { current: null };
    render(<Harness oid={oid} hookRef={ref} />);

    const serialize = vi.fn(() => ({ title: 'Draft system intake' }));
    ref.current!.register({ formId: 'intake-step-2', serialize });

    // Act
    act(() => {
      window.dispatchEvent(
        new CustomEvent('ato:idle-warning', { detail: { secondsUntilSignOut: 60 } }),
      );
    });

    // Assert
    expect(serialize).toHaveBeenCalledTimes(1);
  });

  it('writes the snapshot to localStorage under ato.unsavedChanges.{oid}.{formId} with savedAt', () => {
    // Arrange
    const oid = 'user-a';
    const ref: { current: UseIdleFormStateBackupResult | null } = { current: null };
    render(<Harness oid={oid} hookRef={ref} />);

    ref.current!.register({
      formId: 'intake-step-2',
      serialize: () => ({ title: 'Hello' }),
    });

    // Act
    act(() => {
      window.dispatchEvent(
        new CustomEvent('ato:idle-warning', { detail: { secondsUntilSignOut: 60 } }),
      );
    });

    // Assert
    const raw = localStorage.getItem('ato.unsavedChanges.user-a.intake-step-2');
    expect(raw).not.toBeNull();
    const parsed = JSON.parse(raw!) as { savedAt: string; data: { title: string } };
    expect(parsed.data).toEqual({ title: 'Hello' });
    // savedAt is a valid ISO-8601 timestamp.
    expect(() => new Date(parsed.savedAt).toISOString()).not.toThrow();
    expect(Number.isNaN(Date.parse(parsed.savedAt))).toBe(false);
  });

  it('register adds, unregister removes, and re-registering the same formId replaces', () => {
    // Arrange
    const oid = 'user-a';
    const ref: { current: UseIdleFormStateBackupResult | null } = { current: null };
    render(<Harness oid={oid} hookRef={ref} />);

    const first = vi.fn(() => ({ v: 1 }));
    const second = vi.fn(() => ({ v: 2 }));

    // Act 1 — register, dispatch ⇒ first serializer called.
    ref.current!.register({ formId: 'f1', serialize: first });
    act(() => {
      window.dispatchEvent(
        new CustomEvent('ato:idle-warning', { detail: { secondsUntilSignOut: 60 } }),
      );
    });
    expect(first).toHaveBeenCalledTimes(1);

    // Act 2 — re-register with the same formId; dispatch again ⇒ only
    // the second serializer should be called (not the first).
    ref.current!.register({ formId: 'f1', serialize: second });
    act(() => {
      window.dispatchEvent(
        new CustomEvent('ato:idle-warning', { detail: { secondsUntilSignOut: 60 } }),
      );
    });
    expect(first).toHaveBeenCalledTimes(1);
    expect(second).toHaveBeenCalledTimes(1);

    // Act 3 — unregister; dispatch again ⇒ neither runs.
    first.mockClear();
    second.mockClear();
    ref.current!.unregister('f1');
    act(() => {
      window.dispatchEvent(
        new CustomEvent('ato:idle-warning', { detail: { secondsUntilSignOut: 60 } }),
      );
    });
    expect(first).not.toHaveBeenCalled();
    expect(second).not.toHaveBeenCalled();
  });

  it('purgeUnsavedChanges clears ALL keys for the given oid and leaves other oids intact', () => {
    // Arrange
    localStorage.setItem(
      'ato.unsavedChanges.user-a.f1',
      JSON.stringify({ savedAt: '2026-05-28T00:00:00Z', data: { a: 1 } }),
    );
    localStorage.setItem(
      'ato.unsavedChanges.user-a.f2',
      JSON.stringify({ savedAt: '2026-05-28T00:00:00Z', data: { b: 2 } }),
    );
    localStorage.setItem(
      'ato.unsavedChanges.user-b.f1',
      JSON.stringify({ savedAt: '2026-05-28T00:00:00Z', data: { c: 3 } }),
    );
    localStorage.setItem('unrelated-key', 'untouched');

    // Act
    purgeUnsavedChanges('user-a');

    // Assert
    expect(keysForOid('user-a')).toEqual([]);
    expect(keysForOid('user-b')).toEqual(['ato.unsavedChanges.user-b.f1']);
    expect(localStorage.getItem('unrelated-key')).toBe('untouched');
  });

  it('does NOT run any serializer on mount — only on ato:idle-warning', () => {
    // Arrange
    const serialize = vi.fn(() => ({ v: 1 }));
    const oid = 'user-a';
    const ref: { current: UseIdleFormStateBackupResult | null } = { current: null };
    render(<Harness oid={oid} hookRef={ref} />);

    // Act — register but do NOT dispatch the warning.
    ref.current!.register({ formId: 'f1', serialize });

    // Assert
    expect(serialize).not.toHaveBeenCalled();
    expect(localStorage.getItem('ato.unsavedChanges.user-a.f1')).toBeNull();
  });
});
