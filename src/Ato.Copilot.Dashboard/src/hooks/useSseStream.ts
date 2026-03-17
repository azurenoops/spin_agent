import { useState, useCallback, useRef } from 'react';
import { sendMessage } from '../services/chatService';
import type {
  ChatRequest,
  SseProgressEvent,
  SseResultEvent,
  SseErrorEvent,
} from '../types/chat';

export interface UseSseStreamReturn {
  isStreaming: boolean;
  progressSteps: SseProgressEvent[];
  cancel: () => void;
  stream: (
    request: ChatRequest,
    onResult: (event: SseResultEvent) => void,
    onError: (error: SseErrorEvent | Error) => void,
  ) => void;
}

export function useSseStream(): UseSseStreamReturn {
  const [isStreaming, setIsStreaming] = useState(false);
  const [progressSteps, setProgressSteps] = useState<SseProgressEvent[]>([]);
  const abortControllerRef = useRef<AbortController | null>(null);

  const cancel = useCallback(() => {
    abortControllerRef.current?.abort();
    abortControllerRef.current = null;
    setIsStreaming(false);
    setProgressSteps([]);
  }, []);

  const stream = useCallback(
    (
      request: ChatRequest,
      onResult: (event: SseResultEvent) => void,
      onError: (error: SseErrorEvent | Error) => void,
    ) => {
      // Abort any existing stream
      abortControllerRef.current?.abort();

      const controller = new AbortController();
      abortControllerRef.current = controller;

      setIsStreaming(true);
      setProgressSteps([]);

      sendMessage(
        request,
        (progress) => {
          setProgressSteps((prev) => [...prev, progress]);
        },
        (result) => {
          setIsStreaming(false);
          setProgressSteps([]);
          abortControllerRef.current = null;
          onResult(result);
        },
        (error) => {
          setIsStreaming(false);
          setProgressSteps([]);
          abortControllerRef.current = null;
          onError(error);
        },
        controller.signal,
      );
    },
    [],
  );

  return { isStreaming, progressSteps, cancel, stream };
}
