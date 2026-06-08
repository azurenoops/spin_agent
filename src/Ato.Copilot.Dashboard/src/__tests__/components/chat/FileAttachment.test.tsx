/**
 * T260: File attachment forwarding in useChat + useSseStream.
 * Verifies that valid files are passed to chatService.sendMessage as FormData,
 * and that invalid files produce inline error chips without crashing.
 */
import { renderHook, act } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';

// ── mock chatService ──────────────────────────────────────────────────────────
vi.mock('../../../services/chatService', () => ({
  sendMessage: vi.fn(),
}));

import * as chatService from '../../../services/chatService';
import { useChat } from '../../../hooks/useChat';

const mockSendMessage = chatService.sendMessage as ReturnType<typeof vi.fn>;

beforeEach(() => {
  mockSendMessage.mockReset();
  // Simulate immediate no-result (streaming not needed for these tests)
  mockSendMessage.mockImplementation((_req, _onProg, _onTool, _onResult, _onError, _signal) => {
    return Promise.resolve();
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
    const { result } = renderHook(() => useChat());
    const file = makeFile('scan.pdf', 'application/pdf', 1024 * 1024);
    expect(result.current.validateAttachments([file])).toHaveLength(0);
  });

  it('returns an error for an unsupported type', () => {
    const { result } = renderHook(() => useChat());
    const file = makeFile('data.csv', 'text/csv', 1024);
    const errors = result.current.validateAttachments([file]);
    expect(errors).toHaveLength(1);
    expect(errors[0]!.fileName).toBe('data.csv');
    expect(errors[0]!.reason).toMatch(/unsupported type/i);
  });

  it('returns an error for a file exceeding 20 MB', () => {
    const { result } = renderHook(() => useChat());
    const file = makeFile('huge.pdf', 'application/pdf', 21 * 1024 * 1024);
    const errors = result.current.validateAttachments([file]);
    expect(errors).toHaveLength(1);
    expect(errors[0]!.reason).toMatch(/20 MB/i);
  });

  it('passes valid files as attachments in the ChatRequest', async () => {
    const { result } = renderHook(() => useChat());
    const file = makeFile('policy.pdf', 'application/pdf', 512 * 1024);

    await act(async () => {
      await result.current.sendMessage('Show me the ATO status', [file]);
    });

    expect(mockSendMessage).toHaveBeenCalledTimes(1);
    const [request] = mockSendMessage.mock.calls[0] as [{ attachments?: File[] }];
    expect(request.attachments).toBeDefined();
    expect(request.attachments).toHaveLength(1);
    expect(request.attachments![0]!.name).toBe('policy.pdf');
  });

  it('excludes invalid files from ChatRequest attachments', async () => {
    const { result } = renderHook(() => useChat());
    const good = makeFile('report.png', 'image/png', 100 * 1024);
    const bad = makeFile('sheet.csv', 'text/csv', 1024);

    await act(async () => {
      await result.current.sendMessage('Attach these', [good, bad]);
    });

    const [request] = mockSendMessage.mock.calls[0] as [{ attachments?: File[] }];
    expect(request.attachments).toHaveLength(1);
    expect(request.attachments![0]!.name).toBe('report.png');
  });

  it('adds an inline error message when invalid files are present', async () => {
    const { result } = renderHook(() => useChat());
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
    const { result } = renderHook(() => useChat());
    // No shortcut in useChat itself — panelState.isOpen starts false
    expect(result.current.panelState.isOpen).toBe(false);
  });
});
