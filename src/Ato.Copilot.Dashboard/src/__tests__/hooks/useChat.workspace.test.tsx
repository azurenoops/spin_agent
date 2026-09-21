import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useChat } from '../../hooks/useChat';

const state = vi.hoisted(() => ({
  scope: {
    identity: { directoryTenantId: 'directory-a' as string | undefined, oid: 'actor-a' },
    workspace: { kind: 'organization', tenantId: 'org-alpha', mode: 'ordinary' },
    systemAccess: { systemId: 'system-a' },
  },
}));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => state.scope,
}));
vi.mock('../../hooks/useSseStream', () => ({
  useSseStream: () => ({ isStreaming: false, progressSteps: [], activeToolChips: new Map(), cancel: vi.fn(), stream: vi.fn() }),
}));
vi.mock('../../hooks/useChatContext', () => ({
  useChatContext: () => ({ page: 'portfolio', systemId: null, boundaryId: null, entityType: null, entityId: null }),
}));

beforeEach(() => {
  localStorage.clear();
  state.scope.identity.directoryTenantId = 'directory-a';
  state.scope.identity.oid = 'actor-a';
  state.scope.workspace.tenantId = 'org-alpha';
  state.scope.workspace.mode = 'ordinary';
  state.scope.systemAccess.systemId = 'system-a';
  vi.useFakeTimers();
});
afterEach(() => vi.useRealTimers());

describe('workspace chat history', () => {
  it('keeps alpha and beta histories separate, including rapid switches before debounce', () => {
    // Arrange
    const hook = renderHook(() => useChat());
    act(() => hook.result.current.newConversation());
    const alpha = hook.result.current.conversations[0]!.id;

    // Act
    state.scope.workspace.tenantId = 'org-beta';
    hook.rerender();
    expect(hook.result.current.conversations).toEqual([]);
    act(() => hook.result.current.newConversation());
    const beta = hook.result.current.conversations[0]!.id;
    state.scope.workspace.tenantId = 'org-alpha';
    hook.rerender();

    // Assert
    expect(beta).not.toBe(alpha);
    expect(hook.result.current.conversations.map(conversation => conversation.id)).toEqual([alpha]);
    expect(hook.result.current.activeConversation?.id).toBe(alpha);
  });

  it('does not import legacy conversations or reuse a same-oid history from another issuer', () => {
    // Arrange
    const legacy = JSON.stringify([{ id: 'legacy-canary', messages: [] }]);
    localStorage.setItem('ato-chat-conversations', legacy);
    const hook = renderHook(() => useChat());
    expect(hook.result.current.conversations).toEqual([]);
    act(() => hook.result.current.newConversation());

    // Act
    state.scope.identity.directoryTenantId = 'directory-b';
    hook.rerender();

    // Assert
    expect(hook.result.current.conversations).toEqual([]);
    expect(localStorage.getItem('ato-chat-conversations')).toBe(legacy);
  });

  it('keeps incomplete-identity history in memory without a guessed storage namespace', () => {
    // Arrange
    state.scope.identity.directoryTenantId = undefined;
    const hook = renderHook(() => useChat());

    // Act
    act(() => hook.result.current.newConversation());
    act(() => vi.advanceTimersByTime(200));

    // Assert
    expect(hook.result.current.historyPersistenceEnabled).toBe(false);
    expect(hook.result.current.conversations).toHaveLength(1);
    expect(localStorage.length).toBe(0);
  });
});
