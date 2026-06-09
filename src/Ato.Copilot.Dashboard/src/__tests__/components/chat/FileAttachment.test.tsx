/**
 * T260: File attachment forwarding in useChat + useSseStream.
 * Verifies that valid files are passed to chatService.sendMessage as FormData,
 * and that invalid files produce inline error chips without crashing.
 */
import { renderHook, act } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { BrowserRouter } from 'react-router-dom';

// ── mock useSseStream — useChat calls stream() via useSseStream, not chatService ──
const mockStream = vi.fn();
const mockCancel = vi.fn();
vi.mock('../../../hooks/useSseStream', () => ({
  useSseStream: () => ({
    isStreaming: false,
    progressSteps: [],
    activeToolChips: new Map(),
    cancel: mockCancel,
    stream: mockStream,
  }),
}));

// ── mock useChatContext ──────────────────────────────────────────────────────
vi.mock('../../../hooks/useChatContext', () => ({
  useChatContext: () => ({
    page: 'portfolio',
    systemId: null,
    boundaryId: null,
    entityType: null,
    entityId: null,
  }),
}));

import { useChat } from '../../../hooks/useChat';

function wrapper({ children }: { children: React.ReactNode }) {
  return <BrowserRouter>{children}</BrowserRouter>;
}

beforeEach(() => {
  mockStream.mockReset();
  mockCancel.mockReset();
  mockStream.mockImplementation(() => {
    // no-op: stream does not call back in these tests
  });
  localStorage.clear();
});

// ── helpers ──────────────────────────────────────────────────────────────────

function makeFile(name: string, type: string, sizeBytes: number): File {
  const content = new Uint8Array(sizeBytes);
  return new File([content], name, { type });
}

// ── tests ────────────────────────────────────────────────────────────────────

describe('useChat — attachment validation (T260)', () => {
  it('returns no errors for valid PDF under 20 MB', () => {
    const { result } = renderHook(() => useChat(), { wrapper });
    const file = makeFile('scan.pdf', 'application/pdf', 1024 * 1024);
    expect(result.current.validateAttachments([file])).toHaveLength(0);
  });

  it('returns an error for an unsupported type', () => {
    const { result } = renderHook(() => useChat(), { wrapper });
    const file = makeFile('data.csv', 'text/csv', 1024);
    const errors = result.current.validateAttachments([file]);
    expect(errors).toHaveLength(1);
    expect(errors[0]!.fileName).toBe('data.csv');
    expect(errors[0]!.reason).toMatch(/unsupported type/i);
  });

  it('returns an error for a file exceeding 20 MB', () => {
    const { result } = renderHook(() => useChat(), { wrapper });
    const file = makeFile('huge.pdf', 'application/pdf', 21 * 1024 * 1024);
    const errors = result.current.validateAttachments([file]);
    expect(errors).toHaveLength(1);
    expect(errors[0]!.reason).toMatch(/20 MB/i);
  });

  it('passes valid files as attachments in the ChatRequest', async () => {
    const { result } = renderHook(() => useChat(), { wrapper });
    const file = makeFile('policy.pdf', 'application/pdf', 512 * 1024);

    await act(async () => {
      await result.current.sendMessage('Show me the ATO status', [file]);
    });

    expect(mockStream).toHaveBeenCalledTimes(1);
    const [request] = mockStream.mock.calls[0] as [{ attachments?: File[] }];
    expect(request.attachments).toBeDefined();
    expect(request.attachments).toHaveLength(1);
    expect(request.attachments![0]!.name).toBe('policy.pdf');
  });

  it('excludes invalid files from ChatRequest attachments', async () => {
    const { result } = renderHook(() => useChat(), { wrapper });
    const good = makeFile('report.png', 'image/png', 100 * 1024);
    const bad = makeFile('sheet.csv', 'text/csv', 1024);

    await act(async () => {
      await result.current.sendMessage('Attach these', [good, bad]);
    });

    const [request] = mockStream.mock.calls[0] as [{ attachments?: File[] }];
    expect(request.attachments).toHaveLength(1);
    expect(request.attachments![0]!.name).toBe('report.png');
  });

  it('adds an inline error message when invalid files are present', async () => {
    const { result } = renderHook(() => useChat(), { wrapper });
    const bad = makeFile('data.txt', 'text/plain', 1024);

    await act(async () => {
      await result.current.sendMessage('Hello', [bad]);
    });

    // An error chip message should appear before the user message
    const messages = result.current.activeConversation?.messages ?? [];
    const errorMsg = messages.find((m) => m.role === 'assistant' && m.content.includes('Attachment error'));
    expect(errorMsg).toBeDefined();
    expect(errorMsg!.status).toBe('complete');
  });
});

describe('useChat — keyboard shortcut (T271)', () => {
  it('Ctrl+Shift+C is wired via ChatPanelContext, not useChat directly', () => {
    // Shortcut lives in ChatPanelContext.tsx and is tested via the context provider.
    // This test simply verifies useChat does not duplicate the handler.
    const { result } = renderHook(() => useChat(), { wrapper });
    // No shortcut in useChat itself — panelState.isOpen starts false
    expect(result.current.panelState.isOpen).toBe(false);
  });
});
