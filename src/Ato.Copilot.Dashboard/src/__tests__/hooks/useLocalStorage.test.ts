import { describe, it, expect, beforeEach, vi } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useLocalStorage } from '../../hooks/useLocalStorage';

describe('useLocalStorage', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.useFakeTimers();
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
