import { useState, useCallback, useRef } from 'react';
import { sendMessage } from '../services/chatService';
import type {
  ChatRequest,
  SseProgressEvent,
  SseResultEvent,
  SseErrorEvent,
  SseMcpToolEvent,
} from '../types/chat';

export interface UseSseStreamReturn {
  isStreaming: boolean;
  progressSteps: SseProgressEvent[];
  // T270: Active MCP tool executions (name → start time ms)
  activeToolChips: Map<string, number>;
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
  // T270: Track active MCP tool invocations as name → start time
  const [activeToolChips, setActiveToolChips] = useState<Map<string, number>>(new Map());
  const abortControllerRef = useRef<AbortController | null>(null);

  const cancel = useCallback(() => {
    abortControllerRef.current?.abort();
    abortControllerRef.current = null;
    setIsStreaming(false);
    setProgressSteps([]);
    setActiveToolChips(new Map());
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
      setActiveToolChips(new Map());

      sendMessage(
        request,
        (progress) => {
          setProgressSteps((prev) => [...prev, progress]);
        },
        // T270: MCP tool start event — add chip
        (toolEvent: SseMcpToolEvent) => {
          if (toolEvent.phase === 'start') {
            setActiveToolChips((prev) => {
              const next = new Map(prev);
              next.set(toolEvent.toolName, Date.now());
              return next;
            });
          } else if (toolEvent.phase === 'end') {
            setActiveToolChips((prev) => {
              const next = new Map(prev);
              next.delete(toolEvent.toolName);
              return next;
            });
          }
        },
        (result) => {
          setIsStreaming(false);
          setProgressSteps([]);
          setActiveToolChips(new Map());
          abortControllerRef.current = null;
          onResult(result);
        },
        (error) => {
          setIsStreaming(false);
          setProgressSteps([]);
          setActiveToolChips(new Map());
          abortControllerRef.current = null;
          onError(error);
        },
        controller.signal,
      );
    },
    [],
  );

  return { isStreaming, progressSteps, activeToolChips, cancel, stream };
}
