import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useLocalStorage } from '../../hooks/useLocalStorage';

describe('useLocalStorage', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('never exposes the previous scope when the storage key changes', () => {
    // Arrange
    localStorage.setItem('scope-a', JSON.stringify('alpha'));
    localStorage.setItem('scope-b', JSON.stringify('beta'));
    const seen: string[] = [];
    const hook = renderHook(({ storageKey }) => {
      const value = useLocalStorage(storageKey, 'empty');
      seen.push(`${storageKey}:${value[0]}`);
      return value;
    }, { initialProps: { storageKey: 'scope-a' } });

    // Act
    hook.rerender({ storageKey: 'scope-b' });

    // Assert
    expect(hook.result.current[0]).toBe('beta');
    expect(seen).not.toContain('scope-b:alpha');
  });

  it('flushes a pending write to its original scope before unmount', () => {
    // Arrange
    const hook = renderHook(() => useLocalStorage('scope-a', 'empty'));
    act(() => hook.result.current[1]('last alpha message'));

    // Act
    hook.unmount();

    // Assert
    expect(localStorage.getItem('scope-a')).toBe(JSON.stringify('last alpha message'));
  });

  it('ignores a setter captured by a previous scope', () => {
    // Arrange
    localStorage.setItem('scope-b', JSON.stringify('beta'));
    const hook = renderHook(({ storageKey }) => useLocalStorage(storageKey, 'empty'),
      { initialProps: { storageKey: 'scope-a' } });
    const oldSetter = hook.result.current[1];
    hook.rerender({ storageKey: 'scope-b' });

    // Act
    act(() => oldSetter('obsolete response'));
    act(() => vi.advanceTimersByTime(150));

    // Assert
    expect(hook.result.current[0]).toBe('beta');
    expect(localStorage.getItem('scope-a')).toBeNull();
    expect(localStorage.getItem('scope-b')).toBe(JSON.stringify('beta'));
  });

  it('uses memory only when qualified persistence is disabled', () => {
    // Arrange
    localStorage.setItem('null', JSON.stringify('foreign data'));
    const { result } = renderHook(() => useLocalStorage<string>(null, 'empty'));

    // Act
    act(() => result.current[1]('ephemeral'));
    act(() => vi.advanceTimersByTime(150));

    // Assert
    expect(result.current[0]).toBe('ephemeral');
    expect(localStorage.getItem('null')).toBe(JSON.stringify('foreign data'));
  });

  it('returns initial value when key is not in localStorage', () => {
    const { result } = renderHook(() => useLocalStorage('test-key', 'default'));
    expect(result.current[0]).toBe('default');
  });

  it('reads existing value from localStorage', () => {
    localStorage.setItem('test-key', JSON.stringify('stored'));
    const { result } = renderHook(() => useLocalStorage('test-key', 'default'));
    expect(result.current[0]).toBe('stored');
  });

  it('updates state and writes to localStorage (debounced)', () => {
    const { result } = renderHook(() => useLocalStorage('test-key', 'initial'));

    act(() => {
      result.current[1]('updated');
    });

    expect(result.current[0]).toBe('updated');
    // Not yet written (debounce)
    expect(localStorage.getItem('test-key')).toBeNull();

    act(() => {
      vi.advanceTimersByTime(150);
    });
    expect(localStorage.getItem('test-key')).toBe(JSON.stringify('updated'));
  });

  it('supports functional updates', () => {
    const { result } = renderHook(() => useLocalStorage<number>('count', 0));

    act(() => {
      result.current[1]((prev) => prev + 1);
    });
    expect(result.current[0]).toBe(1);

    act(() => {
      result.current[1]((prev) => prev + 5);
    });
    expect(result.current[0]).toBe(6);
  });

  it('handles JSON parse errors gracefully', () => {
    localStorage.setItem('bad-key', 'not-json');
    const { result } = renderHook(() => useLocalStorage('bad-key', 'fallback'));
    expect(result.current[0]).toBe('fallback');
  });

  it('stores complex objects', () => {
    const obj = { a: 1, b: [2, 3], c: { d: true } };
    const { result } = renderHook(() => useLocalStorage('obj-key', obj));

    const newObj = { a: 10, b: [20], c: { d: false } };
    act(() => {
      result.current[1](newObj);
    });
    expect(result.current[0]).toEqual(newObj);

    act(() => {
      vi.advanceTimersByTime(150);
    });
    expect(JSON.parse(localStorage.getItem('obj-key')!)).toEqual(newObj);
  });
});
