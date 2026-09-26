import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { usePublicationIntent } from '../../features/package-imports/usePublicationIntent';

beforeEach(() => sessionStorage.clear());
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });

describe('publication retry identity without local success state', () => {
  it('retains only a package-scoped preview identifier through remount and clears it explicitly', () => {
    // Arrange
    const first = renderHook(() => usePublicationIntent('package-1'));
    // Act
    act(() => first.result.current.begin('preview-1'));
    first.unmount();
    const restored = renderHook(() => usePublicationIntent('package-1'));
    const other = renderHook(() => usePublicationIntent('package-2'));
    // Assert
    expect(restored.result.current.pendingPreviewId).toBe('preview-1');
    expect(other.result.current.pendingPreviewId).toBeNull();
    expect(sessionStorage.getItem('ato:package-publication-intent:package-1')).toBe('preview-1');
    act(() => restored.result.current.clear());
    expect(sessionStorage.getItem('ato:package-publication-intent:package-1')).toBeNull();
  });

  it('surfaces unavailable storage rather than assuming there is no pending publication', () => {
    // Arrange
    vi.stubGlobal('sessionStorage', { getItem: () => { throw new Error('Storage denied.'); } });
    // Act
    const { result } = renderHook(() => usePublicationIntent('package-1'));
    // Assert
    expect(result.current.error).toContain('Unable to restore publication retry intent');
    expect(result.current.error).toContain('Storage denied.');
  });

  it('fails before creating an untracked publication request when storing its identity is denied', () => {
    // Arrange
    const { result } = renderHook(() => usePublicationIntent('package-1'));
    vi.stubGlobal('sessionStorage', { setItem: () => { throw new Error('Storage is full.'); } });
    // Act
    const begin = () => result.current.begin('preview-1');
    // Assert
    expect(begin).toThrow('Storage is full.');
    expect(result.current.pendingPreviewId).toBeNull();
  });

  it('reports a failed intent cleanup instead of concealing it', () => {
    // Arrange
    const { result } = renderHook(() => usePublicationIntent('package-1'));
    act(() => result.current.begin('preview-1'));
    vi.stubGlobal('sessionStorage', { removeItem: () => { throw new Error('Storage denied.'); } });
    // Act
    act(() => result.current.clear());
    // Assert
    expect(result.current.error).toContain('Unable to clear publication retry intent');
    expect(result.current.pendingPreviewId).toBeNull();
  });
});
