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
  // ── Original baseline tests ───────────────────────────────────────────────

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

  // ── Existing sub-page aliases that already work ───────────────────────────

  it('resolves /systems/abc/profile/ to system-profile', () => {
    const { result } = renderHook(() => useChatContext(), {
      wrapper: createWrapper('/systems/abc/profile/'),
    });
    expect(result.current.page).toBe('system-profile');
  });

  // ── fix/#526: missing slug aliases → must resolve to system-profile ───────

  it('resolves /systems/abc/mission-purpose to system-profile (fix #526)', () => {
    const { result } = renderHook(() => useChatContext(), {
      wrapper: createWrapper('/systems/abc/mission-purpose'),
    });
    expect(result.current.page).toBe('system-profile');
  });

  it('resolves /systems/abc/users-access to system-profile (fix #526)', () => {
    const { result } = renderHook(() => useChatContext(), {
      wrapper: createWrapper('/systems/abc/users-access'),
    });
    expect(result.current.page).toBe('system-profile');
  });

  it('resolves /systems/abc/environment to system-profile (fix #526)', () => {
    const { result } = renderHook(() => useChatContext(), {
      wrapper: createWrapper('/systems/abc/environment'),
    });
    expect(result.current.page).toBe('system-profile');
  });

  it('resolves /systems/abc/data-types to system-profile (fix #526)', () => {
    const { result } = renderHook(() => useChatContext(), {
      wrapper: createWrapper('/systems/abc/data-types'),
    });
    expect(result.current.page).toBe('system-profile');
  });

  it('resolves /systems/abc/ports-protocols to system-profile (fix #526)', () => {
    const { result } = renderHook(() => useChatContext(), {
      wrapper: createWrapper('/systems/abc/ports-protocols'),
    });
    expect(result.current.page).toBe('system-profile');
  });

  it('resolves /systems/abc/leveraged-auth to system-profile (fix #526)', () => {
    const { result } = renderHook(() => useChatContext(), {
      wrapper: createWrapper('/systems/abc/leveraged-auth'),
    });
    expect(result.current.page).toBe('system-profile');
  });

  // ── fix/#722: systemId must be extracted from pathname, not useParams ─────
  // ChatPanel renders outside <Routes>, so useParams() always returned {}.
  // The regex-based extraction works regardless of render position.

  it('extracts systemId from /systems/:id/narratives (fix #722)', () => {
    const { result } = renderHook(() => useChatContext(), {
      wrapper: createWrapper('/systems/abc-123/narratives'),
    });
    expect(result.current.systemId).toBe('abc-123');
    expect(result.current.page).toBe('narratives');
  });

  it('extracts systemId from /systems/:id root (fix #722)', () => {
    const { result } = renderHook(() => useChatContext(), {
      wrapper: createWrapper('/systems/guid-456'),
    });
    expect(result.current.systemId).toBe('guid-456');
    expect(result.current.page).toBe('system-detail');
  });

  it('returns null systemId on portfolio page (fix #722)', () => {
    const { result } = renderHook(() => useChatContext(), {
      wrapper: createWrapper('/portfolio'),
    });
    expect(result.current.systemId).toBeNull();
  });
});
