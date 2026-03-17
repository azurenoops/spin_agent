import { describe, it, expect } from 'vitest';
import { renderHook } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { useChatContext } from '../../hooks/useChatContext';

function createWrapper(path: string) {
  return function Wrapper({ children }: { children: ReactNode }) {
    return <MemoryRouter initialEntries={[path]}>{children}</MemoryRouter>;
  };
}

describe('useChatContext', () => {
  it('returns portfolio context on root path', () => {
    const { result } = renderHook(() => useChatContext(), { wrapper: createWrapper('/') });
    expect(result.current.page).toBe('portfolio');
    expect(result.current.systemId).toBeNull();
  });

  it('returns capabilities context', () => {
    const { result } = renderHook(() => useChatContext(), { wrapper: createWrapper('/capabilities') });
    expect(result.current.page).toBe('capabilities');
  });

  it('returns null for non-entity fields when no entity selected', () => {
    const { result } = renderHook(() => useChatContext(), { wrapper: createWrapper('/') });
    expect(result.current.boundaryId).toBeNull();
    expect(result.current.entityType).toBeNull();
    expect(result.current.entityId).toBeNull();
  });

  it('returns unknown for unrecognized paths', () => {
    const { result } = renderHook(() => useChatContext(), { wrapper: createWrapper('/some/random/path') });
    expect(result.current.page).toBe('unknown');
  });
});
