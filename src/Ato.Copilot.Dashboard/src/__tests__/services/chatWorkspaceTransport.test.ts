import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { sendMessage } from '../../services/chatService';
import type { ChatRequest } from '../../types/chat';

const auth = vi.hoisted(() => ({ acquireBearer: vi.fn() }));
vi.mock('../../features/auth/msalInstance', () => auth);

const request: ChatRequest = {
  message: 'Synthetic question', conversationId: null, context: null,
  conversationHistory: [], action: null, actionContext: null,
};
const terminal = 'event: result\ndata: {"type":"result","data":{"success":true,"response":"synthetic answer"}}\n\n';

beforeEach(() => {
  vi.clearAllMocks();
  auth.acquireBearer.mockResolvedValue('');
  window.history.replaceState({}, '', '/workspaces/organizations/org-alpha');
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  window.history.replaceState({}, '', '/');
});

describe('workspace chat streaming transport', () => {
  it('carries the same ordinary workspace selectors as other API requests', async () => {
    // Arrange
    const fetchMock = vi.fn().mockResolvedValue(new Response(terminal));
    vi.stubGlobal('fetch', fetchMock);
    const result = vi.fn();
    const error = vi.fn();

    // Act
    await sendMessage(request, vi.fn(), vi.fn(), result, error);

    // Assert
    expect(fetchMock).toHaveBeenCalledWith('/api/mcp/chat/stream', expect.objectContaining({
      headers: expect.objectContaining({
        'X-Workspace-Kind': 'organization',
        'X-Workspace-Tenant-Id': 'org-alpha',
        'X-Workspace-Mode': 'ordinary',
      }),
    }));
    expect(result).toHaveBeenCalledWith(expect.objectContaining({ response: 'synthetic answer' }));
    expect(error).not.toHaveBeenCalled();
  });

  it('does not deliver an old workspace result after navigation', async () => {
    // Arrange
    vi.stubGlobal('fetch', vi.fn(async () => {
      window.history.replaceState({}, '', '/workspaces/organizations/org-beta');
      return new Response(terminal);
    }));
    const result = vi.fn();
    const error = vi.fn();

    // Act
    await sendMessage(request, vi.fn(), vi.fn(), result, error);

    // Assert
    expect(result).not.toHaveBeenCalled();
    expect(error).toHaveBeenCalledWith(expect.objectContaining({ message: 'Workspace changed; chat request cancelled.' }));
  });

  it('enforces the 120-second handshake timeout even with an external cancellation signal', async () => {
    // Arrange
    vi.useFakeTimers();
    const external = new AbortController();
    let requestSignal: AbortSignal | null | undefined;
    vi.stubGlobal('fetch', vi.fn((_url: string, init?: RequestInit) => new Promise<Response>((_resolve, reject) => {
      requestSignal = init?.signal;
      requestSignal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')));
    })));
    const error = vi.fn();
    const sending = sendMessage(request, vi.fn(), vi.fn(), vi.fn(), error, external.signal);
    await Promise.resolve();
    await vi.advanceTimersByTimeAsync(119_999);
    expect(requestSignal?.aborted).toBe(false);

    // Act
    await vi.advanceTimersByTimeAsync(1);
    await sending;

    // Assert
    expect(requestSignal?.aborted).toBe(true);
    expect(external.signal.aborted).toBe(false);
    expect(error).toHaveBeenCalledWith(expect.objectContaining({ message: 'Chat request timed out.' }));
  });

  it('does not send an already-cancelled request', async () => {
    // Arrange
    const controller = new AbortController();
    controller.abort();
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    const error = vi.fn();

    // Act
    await sendMessage(request, vi.fn(), vi.fn(), vi.fn(), error, controller.signal);

    // Assert
    expect(fetchMock).not.toHaveBeenCalled();
    expect(auth.acquireBearer).not.toHaveBeenCalled();
    expect(error).not.toHaveBeenCalled();
  });
});
