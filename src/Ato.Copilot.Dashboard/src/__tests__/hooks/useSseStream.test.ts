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

    mockSendMessage.mockImplementation((_req, _onProgress, onRes) => {
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

    mockSendMessage.mockImplementation((_req, _onProgress, _onResult, onErr) => {
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
});
