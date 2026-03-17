import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { useChat } from '../../hooks/useChat';

// Mock useSseStream
const mockStream = vi.fn();
const mockCancel = vi.fn();
vi.mock('../../hooks/useSseStream', () => ({
  useSseStream: () => ({
    isStreaming: false,
    progressSteps: [],
    cancel: mockCancel,
    stream: mockStream,
  }),
}));

// Mock useChatContext
vi.mock('../../hooks/useChatContext', () => ({
  useChatContext: () => ({
    page: 'portfolio',
    systemId: null,
    boundaryId: null,
    entityType: null,
    entityId: null,
  }),
}));

function wrapper({ children }: { children: ReactNode }) {
  return <BrowserRouter>{children}</BrowserRouter>;
}

describe('useChat', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.clearAllMocks();
  });

  it('starts with empty conversations and no active conversation', () => {
    const { result } = renderHook(() => useChat(), { wrapper });
    expect(result.current.conversations).toEqual([]);
    expect(result.current.activeConversation).toBeNull();
  });

  it('creates a new conversation', () => {
    const { result } = renderHook(() => useChat(), { wrapper });

    act(() => {
      result.current.newConversation();
    });

    expect(result.current.conversations).toHaveLength(1);
    expect(result.current.conversations[0]!.title).toBe('New Conversation');
    expect(result.current.panelState.activeConversationId).toBe(result.current.conversations[0]!.id);
  });

  it('selects a conversation', () => {
    const { result } = renderHook(() => useChat(), { wrapper });

    act(() => {
      result.current.newConversation();
    });
    const firstId = result.current.conversations[0]!.id;

    act(() => {
      result.current.newConversation();
    });
    const secondId = result.current.conversations[0]!.id;

    act(() => {
      result.current.selectConversation(firstId);
    });
    expect(result.current.panelState.activeConversationId).toBe(firstId);

    act(() => {
      result.current.selectConversation(secondId);
    });
    expect(result.current.panelState.activeConversationId).toBe(secondId);
  });

  it('deletes a conversation', () => {
    const { result } = renderHook(() => useChat(), { wrapper });

    act(() => {
      result.current.newConversation();
    });
    const id = result.current.conversations[0]!.id;

    act(() => {
      result.current.deleteConversation(id);
    });

    expect(result.current.conversations).toHaveLength(0);
    expect(result.current.panelState.activeConversationId).toBeNull();
  });

  it('auto-creates conversation on sendMessage when none active', () => {
    const { result } = renderHook(() => useChat(), { wrapper });

    act(() => {
      result.current.sendMessage('Hello AI');
    });

    expect(result.current.conversations).toHaveLength(1);
    expect(result.current.conversations[0]!.title).toBe('Hello AI');
    expect(result.current.conversations[0]!.messages).toHaveLength(2); // user + assistant placeholder
    expect(mockStream).toHaveBeenCalledTimes(1);
  });

  it('generates title from first message (truncated at 50 chars)', () => {
    const { result } = renderHook(() => useChat(), { wrapper });
    const longMessage = 'A'.repeat(60);

    act(() => {
      result.current.sendMessage(longMessage);
    });

    expect(result.current.conversations[0]!.title).toBe('A'.repeat(50) + '…');
  });

  it('enforces LRU eviction at 50 conversations', () => {
    const { result } = renderHook(() => useChat(), { wrapper });

    for (let i = 0; i < 52; i++) {
      act(() => {
        result.current.newConversation();
      });
    }

    expect(result.current.conversations.length).toBeLessThanOrEqual(50);
  });

  it('cancelStream calls cancel on stream hook', () => {
    const { result } = renderHook(() => useChat(), { wrapper });

    act(() => {
      result.current.cancelStream();
    });

    expect(mockCancel).toHaveBeenCalledTimes(1);
  });
});
