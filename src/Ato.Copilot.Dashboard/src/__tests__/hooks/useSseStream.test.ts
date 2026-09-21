import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useSseStream } from '../../hooks/useSseStream';

vi.mock('../../services/chatService', () => ({
  sendMessage: vi.fn(),
}));

import { sendMessage } from '../../services/chatService';

const mockSendMessage = vi.mocked(sendMessage);

describe('useSseStream', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('starts with isStreaming=false and empty progressSteps', () => {
    const { result } = renderHook(() => useSseStream());
    expect(result.current.isStreaming).toBe(false);
    expect(result.current.progressSteps).toEqual([]);
  });

  it('sets isStreaming to true when stream is called', () => {
    mockSendMessage.mockImplementation(() => Promise.resolve());
    const { result } = renderHook(() => useSseStream());

    act(() => {
      result.current.stream(
        { message: 'hi', conversationId: null, context: null, conversationHistory: [], action: null, actionContext: null },
        vi.fn(),
        vi.fn(),
      );
    });

    expect(result.current.isStreaming).toBe(true);
    expect(mockSendMessage).toHaveBeenCalledTimes(1);
  });

  it('accumulates progress steps', () => {
    mockSendMessage.mockImplementation((_req, onProgress) => {
      onProgress({ step: 'Step 1', detail: 'd1', timestamp: 't1' });
      onProgress({ step: 'Step 2', detail: 'd2', timestamp: 't2' });
      return Promise.resolve();
    });

    const { result } = renderHook(() => useSseStream());

    act(() => {
      result.current.stream(
        { message: 'hi', conversationId: null, context: null, conversationHistory: [], action: null, actionContext: null },
        vi.fn(),
        vi.fn(),
      );
    });

    expect(result.current.progressSteps).toHaveLength(2);
    expect(result.current.progressSteps[0]!.step).toBe('Step 1');
  });

  it('calls onResult and resets state on result event', () => {
    const onResult = vi.fn();
    const resultData = { success: true, response: 'answer', conversationId: 'c1', agentUsed: 'a', intentType: 'q', processingTimeMs: 100, toolsExecuted: [], errors: [], suggestedActions: [], requiresFollowUp: false };

    mockSendMessage.mockImplementation((_req, _onProgress, _onTool, onRes) => {
      onRes(resultData);
      return Promise.resolve();
    });

    const { result } = renderHook(() => useSseStream());

    act(() => {
      result.current.stream(
        { message: 'hi', conversationId: null, context: null, conversationHistory: [], action: null, actionContext: null },
        onResult,
        vi.fn(),
      );
    });

    expect(onResult).toHaveBeenCalledWith(resultData);
    expect(result.current.isStreaming).toBe(false);
    expect(result.current.progressSteps).toEqual([]);
  });

  it('calls onError and resets state on error event', () => {
    const onError = vi.fn();
    const error = new Error('Network failed');

    mockSendMessage.mockImplementation((_req, _onProgress, _onTool, _onResult, onErr) => {
      onErr(error);
      return Promise.resolve();
    });

    const { result } = renderHook(() => useSseStream());

    act(() => {
      result.current.stream(
        { message: 'hi', conversationId: null, context: null, conversationHistory: [], action: null, actionContext: null },
        vi.fn(),
        onError,
      );
    });

    expect(onError).toHaveBeenCalledWith(error);
    expect(result.current.isStreaming).toBe(false);
  });

  it('cancel resets streaming state', () => {
    mockSendMessage.mockImplementation(() => new Promise(() => {})); // never resolves
    const { result } = renderHook(() => useSseStream());

    act(() => {
      result.current.stream(
        { message: 'hi', conversationId: null, context: null, conversationHistory: [], action: null, actionContext: null },
        vi.fn(),
        vi.fn(),
      );
    });

    expect(result.current.isStreaming).toBe(true);

    act(() => {
      result.current.cancel();
    });

    expect(result.current.isStreaming).toBe(false);
    expect(result.current.progressSteps).toEqual([]);
  });

  it('aborts the active request when its workspace view unmounts', () => {
    // Arrange
    mockSendMessage.mockImplementation(() => new Promise(() => {}));
    const hook = renderHook(() => useSseStream());
    act(() => hook.result.current.stream(
      { message: 'hi', conversationId: null, context: null, conversationHistory: [], action: null, actionContext: null },
      vi.fn(), vi.fn(),
    ));
    const signal = mockSendMessage.mock.calls[0]?.[5];

    // Act
    hook.unmount();

    // Assert
    expect(signal?.aborted).toBe(true);
  });

  it('ignores callbacks from a stream replaced by a newer request', () => {
    // Arrange
    mockSendMessage.mockImplementation(() => new Promise(() => {}));
    const firstResult = vi.fn();
    const firstError = vi.fn();
    const { result } = renderHook(() => useSseStream());
    const request = { message: 'hi', conversationId: null, context: null, conversationHistory: [], action: null, actionContext: null };
    act(() => result.current.stream(request, firstResult, firstError));
    const first = mockSendMessage.mock.calls[0]!;
    act(() => result.current.stream(request, vi.fn(), vi.fn()));

    // Act
    act(() => {
      first[1]({ step: 'Obsolete progress', detail: '', timestamp: '2026-09-21T00:00:00Z' });
      first[2]({ phase: 'start', toolName: 'obsolete-tool' });
      first[3]({ success: true, response: 'Obsolete result', conversationId: 'old', agentUsed: 'test',
        intentType: 'query', processingTimeMs: 1, toolsExecuted: [], errors: [], suggestedActions: [], requiresFollowUp: false });
      first[4](new Error('Obsolete failure'));
    });

    // Assert
    expect(result.current.isStreaming).toBe(true);
    expect(result.current.progressSteps).toEqual([]);
    expect(result.current.activeToolChips.size).toBe(0);
    expect(firstResult).not.toHaveBeenCalled();
    expect(firstError).not.toHaveBeenCalled();
  });
});
