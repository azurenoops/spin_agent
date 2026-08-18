import React, { useState, useRef, useEffect, useCallback, KeyboardEvent } from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { Prism as SyntaxHighlighter } from 'react-syntax-highlighter';
import { oneDark } from 'react-syntax-highlighter/dist/esm/styles/prism';
import { useChatContext } from '../contexts/ChatContext';
import {
  ChatMessage,
  MessageRole,
  MessageStatus,
  ConnectionStatus,
  SuggestedAction,
  ToolExecutionResult,
} from '../types/chat';
import { VerdictStoreProvider, useVerdictStore, getResultsForMessage } from '../lib/verdict-store';
import { VerdictSummaryChip } from './Chat/VerdictSummaryChip';
import { TraceabilityPanel } from './Chat/TraceabilityPanel';
import { ResearchSourcesCard, ResearchSource } from './Chat/ResearchSourcesCard';
import { isTraceabilityPanelEnabled, isCollaborationEnabled } from '../lib/featureFlags';
import ConflictResolutionBanner from './Collaboration/ConflictResolutionBanner';
import MobileCollaborationBanner from './Collaboration/MobileCollaborationBanner';
import './Chat/NliVerdictBadge.css';

const MAX_FILE_SIZE = 10_485_760;

const WELCOME_SUGGESTIONS = [
  {
    icon: '\u{1F4DD}',
    title: 'Register a New System',
    prompt:
      "Register a new system called 'ACME Portal' as a Major Application with mission-critical designation in Azure Government",
  },
  {
    icon: '\u{1F50D}',
    title: 'Run a Compliance Assessment',
    prompt:
      'Run a NIST 800-53 compliance assessment on my Azure subscription and show me the top findings',
  },
  {
    icon: '\u{1F4CB}',
    title: 'Generate SSP Document',
    prompt: 'Generate a System Security Plan (SSP) for my registered system',
  },
  {
    icon: '\u{1F4CA}',
    title: 'Show RMF Status',
    prompt: 'Show me all registered systems and their current RMF phase',
  },
];

// ── #256 EditorShell v2 ──────────────────────────────────────────
import type { LayoutMode } from '../contexts/EditorLayoutContext';

interface ChatWindowProps {
  /** Current layout mode — affects canvas width. (#256) */
  layoutMode?: LayoutMode;
  /** Current viewport width (px) — used for research-mode right-panel offset. */
  viewportWidth?: number;
}

export default function ChatWindow({ layoutMode, viewportWidth }: ChatWindowProps = {}) {
  return (
    <VerdictStoreProvider>
      <ChatWindowInner layoutMode={layoutMode} viewportWidth={viewportWidth} />
    </VerdictStoreProvider>
  );
}

export function ChatWindowInner({ layoutMode, viewportWidth = 1280 }: ChatWindowProps = {}) {
  const { state, sendMessage, dispatch } = useChatContext();
  // GATE-2437: verdict store for badge count on toolbar toggle (AC#3, AC#8)
  const { state: verdictState } = useVerdictStore();
  const [input, setInput] = useState('');
  const [attachments, setAttachments] = useState<File[]>([]);
  const [expandedTools, setExpandedTools] = useState<Set<string>>(new Set());
  // TraceabilityPanel state: key = messageId, value = open flag
  const [panelOpenFor, setPanelOpenFor] = useState<string | null>(null);
  const [scrollToResultId, setScrollToResultId] = useState<string | undefined>();
  const [highlightSourceId, setHighlightSourceId] = useState<string | undefined>();
  // GATE-2437: error state for panel fetch failures — wires [Retry] button (AC#5)
  const [panelError, setPanelError] = useState(false);
  // a11y: live-region announcement text for screen readers
  const [panelAnnouncement, setPanelAnnouncement] = useState('');
  // focus-return: element that was focused when the panel opened
  const lastFocusedRef = useRef<HTMLElement | null>(null);
  // GATE-2437: first-run nudge — shown once per browser, dismissed permanently
  const NUDGE_KEY = 'tp_nudge_dismissed';
  const [nudgeDismissed, setNudgeDismissed] = useState(
    () => typeof localStorage !== 'undefined' && localStorage.getItem(NUDGE_KEY) === 'true'
  );
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [state.messages]);

  useEffect(() => {
    inputRef.current?.focus();
  }, [state.activeConversationId]);

  // Alt+T — toggle TraceabilityPanel for the most-recent assistant message.
  // Only active when the feature flag is on.
  useEffect(() => {
    if (!isTraceabilityPanelEnabled) return;
    function handleAltT(e: globalThis.KeyboardEvent) {
      if (e.altKey && (e.key === 't' || e.key === 'T')) {
        e.preventDefault();
        const lastAssistant = [...state.messages]
          .reverse()
          .find((m) => m.role === MessageRole.Assistant);
        if (!lastAssistant) return;
        setPanelOpenFor((prev) => {
          const opening = prev !== lastAssistant.id;
          if (opening) {
            lastFocusedRef.current = document.activeElement as HTMLElement;
            setPanelAnnouncement('Traceability panel opened');
          } else {
            setPanelAnnouncement('Traceability panel closed');
            setTimeout(() => lastFocusedRef.current?.focus(), 0);
          }
          return opening ? lastAssistant.id : null;
        });
      }
    }
    document.addEventListener('keydown', handleAltT);
    return () => document.removeEventListener('keydown', handleAltT);
  }, [state.messages]);

  const openPanelFor = useCallback((messageId: string, resultId?: string, trigger?: HTMLElement) => {
    lastFocusedRef.current = trigger ?? (document.activeElement as HTMLElement);
    setPanelOpenFor(messageId);
    setScrollToResultId(resultId);
    setPanelAnnouncement('Traceability panel opened');
  }, []);

  const closePanelFor = useCallback(() => {
    setPanelOpenFor(null);
    setScrollToResultId(undefined);
    setHighlightSourceId(undefined);
    setPanelError(false);
    setPanelAnnouncement('Traceability panel closed');
    setTimeout(() => lastFocusedRef.current?.focus(), 0);
  }, []);

  // GATE-2437: retry handler — clears error so parent can re-trigger fetch (AC#5).
  // The verdict store populates results via SignalR push; a retry here re-opens
  // the panel in a clean state and lets the store re-deliver results if available.
  const handlePanelRetry = useCallback(() => {
    setPanelError(false);
  }, []);

  const handleSend = useCallback(async () => {
    const content = input.trim();
    if (!content && attachments.length === 0) return;
    if (state.isProcessing) return;

    let messageContent = content;
    if (!content && attachments.length > 0) {
      messageContent = `Please analyze the attached file(s): ${attachments.map((f) => f.name).join(', ')}`;
    }

    setInput('');
    setAttachments([]);
    await sendMessage(messageContent);
  }, [input, attachments, state.isProcessing, sendMessage]);

  const handleKeyDown = useCallback(
    (e: KeyboardEvent<HTMLTextAreaElement>) => {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        handleSend();
      }
    },
    [handleSend]
  );

  const handleSuggestionSend = useCallback(
    async (prompt: string) => {
      if (state.isProcessing) return;
      setInput('');
      await sendMessage(prompt);
    },
    [state.isProcessing, sendMessage]
  );

  const handleFileSelect = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const files = Array.from(e.target.files || []);
      const validFiles = files.filter((f) => {
        if (f.size > MAX_FILE_SIZE) {
          dispatch({ type: 'SET_ERROR', payload: `File "${f.name}" exceeds the 10 MB limit` });
          return false;
        }
        return true;
      });
      setAttachments((prev) => [...prev, ...validFiles]);
      if (fileInputRef.current) fileInputRef.current.value = '';
    },
    [dispatch]
  );

  const removeAttachment = useCallback((index: number) => {
    setAttachments((prev) => prev.filter((_, i) => i !== index));
  }, []);

  const toggleTool = useCallback((messageId: string) => {
    setExpandedTools((prev) => {
      const next = new Set(prev);
      if (next.has(messageId)) next.delete(messageId);
      else next.add(messageId);
      return next;
    });
  }, []);

  const handleSuggestionClick = useCallback((prompt: string) => {
    setInput(prompt);
    inputRef.current?.focus();
  }, []);

  if (!state.activeConversationId) {
    return (
      <div className="flex-1 flex items-center justify-center bg-gradient-to-br from-gray-50 to-blue-50/30">
        <div className="text-center max-w-md">
          <div className="w-16 h-16 mx-auto mb-4 rounded-2xl bg-gradient-to-br from-blue-600 to-indigo-700 flex items-center justify-center shadow-lg">
            <ShieldIcon className="w-8 h-8 text-white" />
          </div>
          <h2 className="text-xl font-semibold text-gray-800 mb-2">ATO Copilot</h2>
          <p className="text-gray-500 text-sm">
            Select a conversation or create a new one to get started with compliance assessments, remediation, and more.
          </p>
        </div>
      </div>
    );
  }

  const hasMessages = state.messages.length > 0;

  // #256 EditorShell v2 — canvas width adapts to layout mode.
  // focused: full-bleed with 80px side padding, no max-width cap.
  // research: narrower to leave room for the 360px right panel.
  // standard (default): existing max-w-4xl behaviour.
  const canvasClass =
    layoutMode === 'focused'
      ? 'w-full px-20 mx-0'
      : layoutMode === 'research'
      ? 'max-w-2xl mx-auto'
      : 'max-w-4xl mx-auto';

  return (
    <div className="flex-1 flex flex-col h-full bg-gradient-to-br from-gray-50 to-blue-50/20">
      {/* GATE-2437: screen-reader live region for panel open/close announcements */}
      <div
        aria-live="polite"
        aria-atomic="true"
        className="sr-only"
        data-testid="panel-live-region"
      >
        {panelAnnouncement}
      </div>

      <ConnectionStatusBar status={state.connectionStatus} />

      {/* #1357: mobile editing-disabled banner */}
      {isCollaborationEnabled && (
        <MobileCollaborationBanner viewportWidth={viewportWidth} />
      )}

      <div className="flex-1 overflow-y-auto scrollbar-thin">
        {!hasMessages && !state.isProcessing ? (
          <WelcomeScreen onSuggestionClick={handleSuggestionSend} />
        ) : (
          <div className={`${canvasClass} px-4 py-6 space-y-6`}>
            {state.messages.map((message) => (
              <MessageBubble
                key={message.id}
                message={message}
                isToolExpanded={expandedTools.has(message.id)}
                onToggleTool={() => toggleTool(message.id)}
                onSuggestionClick={handleSuggestionSend}
                panelOpen={panelOpenFor === message.id}
                onTogglePanel={() =>
                  setPanelOpenFor((prev) =>
                    prev === message.id ? null : message.id
                  )
                }
                onOpenPanelAtResult={(resultId) => openPanelFor(message.id, resultId)}
                onClosePanel={closePanelFor}
                scrollToResultId={
                  panelOpenFor === message.id ? scrollToResultId : undefined
                }
                highlightSourceId={
                  panelOpenFor === message.id ? highlightSourceId : undefined
                }
                onViewSource={setHighlightSourceId}
                panelError={panelOpenFor === message.id ? panelError : false}
                onPanelRetry={panelOpenFor === message.id ? handlePanelRetry : undefined}
              />
            ))}

            {/* GATE-2437: first-run nudge — shown once after the first assistant
                message, only when the feature flag is on and the user hasn't
                dismissed it yet. */}
            {isTraceabilityPanelEnabled &&
              !nudgeDismissed &&
              state.messages.some((m) => m.role === MessageRole.Assistant && m.content) && (
                <TraceabilityNudge
                  onOpen={() => {
                    const lastAssistant = [...state.messages]
                      .reverse()
                      .find((m) => m.role === MessageRole.Assistant);
                    if (lastAssistant) openPanelFor(lastAssistant.id);
                  }}
                  onDismiss={() => {
                    if (typeof localStorage !== 'undefined') {
                      localStorage.setItem(NUDGE_KEY, 'true');
                    }
                    setNudgeDismissed(true);
                  }}
                />
              )}

            {state.isProcessing && (
              <div className="flex gap-3 animate-fade-in">
                <div className="flex-shrink-0 w-8 h-8 rounded-lg bg-gradient-to-br from-blue-600 to-indigo-700 flex items-center justify-center shadow-sm">
                  <ShieldIcon className="w-4 h-4 text-white" />
                </div>
                <div className="bg-white border border-gray-200 rounded-2xl rounded-tl-sm px-4 py-3 shadow-sm">
                  <div className="flex items-center gap-2 text-gray-500">
                    <div className="flex gap-1">
                      <span className="w-2 h-2 bg-blue-400 rounded-full animate-bounce" style={{ animationDelay: '0ms' }} />
                      <span className="w-2 h-2 bg-blue-400 rounded-full animate-bounce" style={{ animationDelay: '150ms' }} />
                      <span className="w-2 h-2 bg-blue-400 rounded-full animate-bounce" style={{ animationDelay: '300ms' }} />
                    </div>
                    <span className="text-sm font-medium text-gray-600">
                      {state.progressMessage || 'Analyzing your request...'}
                    </span>
                  </div>
                </div>
              </div>
            )}

            {state.typingUsers.length > 0 && (
              <div className="text-sm text-gray-400 italic pl-11">
                {state.typingUsers.join(', ')} {state.typingUsers.length === 1 ? 'is' : 'are'} typing...
              </div>
            )}

            {state.error && (
              <div className={`${canvasClass} bg-red-50 border border-red-200 rounded-xl p-4 text-red-700 text-sm flex items-start gap-3`}>
                <svg className="w-5 h-5 flex-shrink-0 mt-0.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                    d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4c-.77-.833-1.964-.833-2.732 0L4.082 16.5c-.77.833.192 2.5 1.732 2.5z" />
                </svg>
                <span>{state.error}</span>
              </div>
            )}

            <div ref={messagesEndRef} />
          </div>
        )}
      </div>

      {attachments.length > 0 && (
        <div className={`${canvasClass} px-4 py-2 flex flex-wrap gap-2 border-t border-gray-200 bg-white/80 backdrop-blur`}>
          {attachments.map((file, index) => (
            <div
              key={`${file.name}-${index}`}
              className="flex items-center gap-1.5 bg-blue-50 border border-blue-200 rounded-full px-3 py-1 text-sm text-blue-700"
            >
              <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                  d="M15.172 7l-6.586 6.586a2 2 0 102.828 2.828l6.414-6.586a4 4 0 00-5.656-5.656l-6.415 6.585a6 6 0 108.486 8.486L20.5 13" />
              </svg>
              <span className="truncate max-w-32">{file.name}</span>
              <button
                onClick={() => removeAttachment(index)}
                className="text-blue-400 hover:text-red-500 ml-0.5 transition-colors"
                aria-label={`Remove ${file.name}`}
              >
                &times;
              </button>
            </div>
          ))}
        </div>
      )}

      <div className="border-t border-gray-200 bg-white/80 backdrop-blur">
        <div className={`${canvasClass} px-4 py-3`}>
          <div className="flex items-end gap-2 bg-white border border-gray-300 rounded-xl shadow-sm focus-within:ring-2 focus-within:ring-blue-500 focus-within:border-transparent transition-all">
            <button
              onClick={() => fileInputRef.current?.click()}
              className="p-2.5 text-gray-400 hover:text-gray-600 transition-colors flex-shrink-0"
              aria-label="Attach file"
            >
              <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                  d="M15.172 7l-6.586 6.586a2 2 0 102.828 2.828l6.414-6.586a4 4 0 00-5.656-5.656l-6.415 6.585a6 6 0 108.486 8.486L20.5 13" />
              </svg>
            </button>
            <input ref={fileInputRef} type="file" className="hidden" onChange={handleFileSelect} multiple />

            <textarea
              ref={inputRef}
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="Type a message... (Enter to send, Shift+Enter for newline)"
              className="flex-1 resize-none py-2.5 px-1 focus:outline-none max-h-32 text-gray-800 placeholder-gray-400 bg-transparent"
              rows={1}
              disabled={state.isProcessing}
            />

             {/* GATE-2437: TraceabilityPanel toolbar toggle — only visible when flag is on.
                  Shows a badge count (AC#3, AC#8) with aria-live so screen readers
                  announce new citations without requiring the panel to open (AC#9). */}
             {isTraceabilityPanelEnabled && (() => {
               const lastAssistant = [...state.messages]
                 .reverse()
                 .find((m) => m.role === MessageRole.Assistant);
               const badgeCount = lastAssistant
                 ? getResultsForMessage(verdictState, lastAssistant.id).length
                 : 0;
               const isOpen = lastAssistant ? panelOpenFor === lastAssistant.id : false;
               return (
                 <button
                   data-testid="traceability-toggle"
                   onClick={() => {
                     if (!lastAssistant) return;
                     if (!isOpen) {
                       openPanelFor(lastAssistant.id);
                     } else {
                       closePanelFor();
                     }
                   }}
                   className="relative p-2.5 text-gray-400 hover:text-indigo-600 transition-colors flex-shrink-0"
                   aria-label="Toggle source traceability panel (Alt+T)"
                   aria-keyshortcuts="Alt+T"
                   title="View source traceability (Alt+T)"
                 >
                   {/* Link / chain icon */}
                   <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                     <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                       d="M13.828 10.172a4 4 0 00-5.656 0l-4 4a4 4 0 105.656 5.656l1.102-1.101m-.758-4.899a4 4 0 005.656 0l4-4a4 4 0 00-5.656-5.656l-1.1 1.1" />
                   </svg>
                   {/* Badge count — aria-live announces new citations to screen readers */}
                   {badgeCount > 0 && (
                     <span
                       aria-live="polite"
                       aria-label={`${badgeCount} citation${badgeCount === 1 ? '' : 's'} available`}
                       style={{
                         position: 'absolute',
                         top: '4px',
                         right: '4px',
                         minWidth: '16px',
                         height: '16px',
                         padding: '0 4px',
                         borderRadius: '8px',
                         background: '#4f46e5',
                         color: '#fff',
                         fontSize: '10px',
                         fontWeight: 700,
                         lineHeight: '16px',
                         textAlign: 'center',
                         pointerEvents: 'none',
                       }}
                     >
                       {badgeCount}
                     </span>
                   )}
                 </button>
               );
             })()}

            <button
              onClick={handleSend}
              disabled={state.isProcessing || (!input.trim() && attachments.length === 0)}
              className="p-2.5 bg-gradient-to-r from-blue-600 to-indigo-600 text-white rounded-lg hover:from-blue-700 hover:to-indigo-700 disabled:opacity-40 disabled:cursor-not-allowed transition-all m-1 flex-shrink-0 shadow-sm"
              aria-label="Send message"
            >
              <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 19l9 2-9-18-9 18 9-2zm0 0v-8" />
              </svg>
            </button>
          </div>
          <p className="text-xs text-gray-400 mt-1.5 text-center">
            ATO Copilot may produce inaccurate results. Always verify compliance findings.
          </p>
        </div>
      </div>

      {/* #1357: conflict resolution banner — mounted globally, self-hides when no conflict */}
      {isCollaborationEnabled && <ConflictResolutionBanner />}
    </div>
  );
}

function WelcomeScreen({ onSuggestionClick }: { onSuggestionClick: (prompt: string) => void }) {
  return (
    <div className="flex-1 flex flex-col items-center justify-center px-4 py-12">
      <div className="w-20 h-20 mb-6 rounded-2xl bg-gradient-to-br from-blue-600 to-indigo-700 flex items-center justify-center shadow-lg">
        <ShieldIcon className="w-10 h-10 text-white" />
      </div>
      <h2 className="text-2xl font-bold text-gray-800 mb-2">How can I help you?</h2>
      <p className="text-gray-500 text-sm mb-8 max-w-lg text-center">
        {"I'm your ATO Copilot. I guide DoD teams through every step of the NIST Risk Management Framework — from system registration through continuous monitoring."}
      </p>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 max-w-2xl w-full">
        {WELCOME_SUGGESTIONS.map((suggestion, idx) => (
          <button
            key={idx}
            onClick={() => onSuggestionClick(suggestion.prompt)}
            className="group flex items-start gap-3 p-4 bg-white border border-gray-200 rounded-xl hover:border-blue-300 hover:shadow-md transition-all text-left"
          >
            <span className="text-2xl flex-shrink-0 mt-0.5">{suggestion.icon}</span>
            <div>
              <h3 className="text-sm font-semibold text-gray-800 group-hover:text-blue-700 transition-colors">
                {suggestion.title}
              </h3>
              <p className="text-xs text-gray-500 mt-0.5 line-clamp-2">{suggestion.prompt}</p>
            </div>
          </button>
        ))}
      </div>
    </div>
  );
}

function ConnectionStatusBar({ status }: { status: ConnectionStatus }) {
  if (status === ConnectionStatus.Connected) {
    return (
      <div className="px-4 py-1.5 flex items-center justify-center gap-2 text-xs text-emerald-700 bg-emerald-50 border-b border-emerald-100" role="status">
        <span className="w-1.5 h-1.5 bg-emerald-500 rounded-full" />
        Connected
      </div>
    );
  }
  if (status === ConnectionStatus.Reconnecting) {
    return (
      <div className="px-4 py-1.5 flex items-center justify-center gap-2 text-xs text-amber-700 bg-amber-50 border-b border-amber-100" role="status">
        <span className="w-1.5 h-1.5 bg-amber-500 rounded-full animate-pulse" />
        Reconnecting...
      </div>
    );
  }
  return (
    <div className="px-4 py-1.5 flex items-center justify-center gap-2 text-xs text-red-700 bg-red-50 border-b border-red-100" role="status">
      <span className="w-1.5 h-1.5 bg-red-500 rounded-full" />
      Disconnected
    </div>
  );
}

interface MessageBubbleProps {
  message: ChatMessage;
  isToolExpanded: boolean;
  onToggleTool: () => void;
  onSuggestionClick: (prompt: string) => void;
  panelOpen: boolean;
  onTogglePanel: () => void;
  onOpenPanelAtResult: (resultId: string) => void;
  onClosePanel: () => void;
  scrollToResultId?: string;
  highlightSourceId?: string;
  onViewSource: (sourceId: string) => void;
  /** When true, TraceabilityPanel shows the [Retry] error state (AC#5). */
  panelError?: boolean;
  /** Called when user clicks [Retry] in the error state (AC#5). */
  onPanelRetry?: () => void;
}

function MessageBubble({
  message,
  isToolExpanded,
  onToggleTool,
  onSuggestionClick,
  panelOpen,
  onTogglePanel,
  onOpenPanelAtResult,
  onClosePanel,
  scrollToResultId,
  highlightSourceId,
  onViewSource,
  panelError = false,
  onPanelRetry,
}: MessageBubbleProps) {
  const isUser = message.role === MessageRole.User;
  const isAssistant = message.role === MessageRole.Assistant;

  if (isUser) {
    return (
      <div className="flex justify-end animate-fade-in">
        <div className="flex gap-3 max-w-[75%]">
          <div className="bg-gradient-to-br from-blue-600 to-indigo-600 text-white rounded-2xl rounded-tr-sm px-4 py-3 shadow-sm">
            {message.status !== MessageStatus.Completed && (
              <div className="text-xs opacity-70 mb-1">
                {message.status === MessageStatus.Sending && 'Sending...'}
                {message.status === MessageStatus.Error && '\u26A0 Failed to send'}
              </div>
            )}
            <p className="whitespace-pre-wrap text-sm leading-relaxed">{message.content}</p>
          </div>
          <div className="flex-shrink-0 w-8 h-8 rounded-full bg-blue-100 flex items-center justify-center">
            <UserIcon className="w-4 h-4 text-blue-600" />
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex justify-start animate-fade-in">
      <div className="flex gap-3 max-w-[85%]">
        <div className="flex-shrink-0 w-8 h-8 rounded-lg bg-gradient-to-br from-blue-600 to-indigo-700 flex items-center justify-center shadow-sm">
          <ShieldIcon className="w-4 h-4 text-white" />
        </div>
        <div className="flex-1 min-w-0">
          <div className="bg-white border border-gray-200 rounded-2xl rounded-tl-sm px-5 py-4 shadow-sm">
            <div className="prose prose-sm max-w-none prose-headings:mt-3 prose-headings:mb-2 prose-p:my-1.5 prose-ul:my-2 prose-ol:my-2 prose-li:my-0.5 prose-hr:my-3">
              <ReactMarkdown
                remarkPlugins={[remarkGfm]}
                components={{
                  code(props) {
                    const { children, className, ...rest } = props;
                    const match = /language-(\w+)/.exec(className || '');
                    const inline = !match;
                    return !inline ? (
                      <div className="rounded-lg overflow-hidden my-3">
                        <SyntaxHighlighter
                          style={oneDark}
                          language={match[1]}
                          PreTag="div"
                          customStyle={{ margin: 0, borderRadius: '0.5rem' }}
                        >
                          {String(children).replace(/\n$/, '')}
                        </SyntaxHighlighter>
                      </div>
                    ) : (
                      <code className={className} {...rest}>
                        {children}
                      </code>
                    );
                  },
                  table(props) {
                    return (
                      <div className="overflow-x-auto my-3">
                        <table className="min-w-full divide-y divide-gray-200 border border-gray-200 rounded-lg" {...props} />
                      </div>
                    );
                  },
                  th(props) {
                    return <th className="px-3 py-2 bg-gray-50 text-left text-xs font-semibold text-gray-600 uppercase" {...props} />;
                  },
                  td(props) {
                    return <td className="px-3 py-2 text-sm border-t border-gray-100" {...props} />;
                  },
                }}
              >
                {message.content}
              </ReactMarkdown>
            </div>

            {message.attachments && message.attachments.length > 0 && (
              <div className="mt-3 pt-3 border-t border-gray-100 flex flex-wrap gap-1.5">
                {message.attachments.map((att) => (
                  <span key={att.id} className="inline-flex items-center px-2.5 py-1 rounded-lg text-xs bg-gray-50 text-gray-600 border border-gray-200">
                    {'\u{1F4CE}'} {att.fileName}
                  </span>
                ))}
              </div>
            )}

            {isAssistant && message.metadata?.intentType && (
              <div className="mt-3 pt-3 border-t border-gray-100">
                <span className="inline-flex items-center px-2.5 py-1 rounded-full text-xs font-medium bg-purple-50 text-purple-700 border border-purple-200">
                  {String(message.metadata.intentType)}
                  {message.metadata.confidence && (
                    <span className="ml-1 opacity-75">
                      {` \u2014 ${Math.round(Number(message.metadata.confidence) * 100)}%`}
                    </span>
                  )}
                </span>
              </div>
            )}

            {isAssistant && message.toolResult && (
              <div className="mt-3 border border-gray-200 rounded-xl overflow-hidden">
                <button
                  onClick={onToggleTool}
                  className="w-full flex items-center justify-between px-3 py-2.5 text-sm font-medium text-gray-700 hover:bg-gray-50 transition-colors"
                >
                  <span className="flex items-center gap-2">
                    {(message.toolResult as ToolExecutionResult).success ? (
                      <span className="w-5 h-5 rounded-full bg-green-100 flex items-center justify-center">
                        <svg className="w-3 h-3 text-green-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={3} d="M5 13l4 4L19 7" />
                        </svg>
                      </span>
                    ) : (
                      <span className="w-5 h-5 rounded-full bg-red-100 flex items-center justify-center">
                        <svg className="w-3 h-3 text-red-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={3} d="M6 18L18 6M6 6l12 12" />
                        </svg>
                      </span>
                    )}
                    {'Tool: ' + (message.toolResult as ToolExecutionResult).toolName}
                  </span>
                  <svg
                    className={`w-4 h-4 text-gray-400 transition-transform ${isToolExpanded ? 'rotate-180' : ''}`}
                    fill="none" stroke="currentColor" viewBox="0 0 24 24"
                  >
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
                  </svg>
                </button>
                {isToolExpanded && (
                  <div className="px-3 py-3 border-t bg-gray-50">
                    <pre className="text-xs text-gray-600 overflow-x-auto whitespace-pre-wrap font-mono leading-relaxed">
                      {JSON.stringify((message.toolResult as ToolExecutionResult).result, null, 2)}
                    </pre>
                  </div>
                )}
              </div>
            )}

            {isAssistant && message.metadata?.toolChain && (
              <WorkflowProgress metadata={message.metadata} />
            )}
          </div>

          {isAssistant && message.metadata?.suggestedActions && (
            <div className="mt-2 flex flex-wrap gap-2">
              {(message.metadata.suggestedActions as unknown as SuggestedAction[]).map(
                (action, index) => (
                  <button
                    key={index}
                    onClick={() => onSuggestionClick(action.prompt)}
                    className="group flex items-center gap-1.5 px-3 py-1.5 bg-blue-50 border border-blue-200 rounded-full text-sm text-blue-700 hover:bg-blue-100 hover:border-blue-300 transition-all"
                  >
                    <span className="font-medium">{action.title}</span>
                    <svg className="w-3 h-3 opacity-0 group-hover:opacity-100 transition-opacity" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
                    </svg>
                  </button>
                )
              )}
            </div>
          )}

          {/* GATE-2437: VerdictSummaryChip + TraceabilityPanel — only when flag is on */}
          {isAssistant && isTraceabilityPanelEnabled && (
            <>
              <VerdictSummaryChip
                messageId={message.id}
                panelOpen={panelOpen}
                onTogglePanel={onTogglePanel}
              />
              {panelOpen && (
                <TraceabilityPanel
                  messageId={message.id}
                  open={panelOpen}
                  onClose={onClosePanel}
                  scrollToResultId={scrollToResultId}
                  onViewSource={onViewSource}
                  error={panelError}
                  onRetry={onPanelRetry}
                />
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
}

function WorkflowProgress({ metadata }: { metadata: Record<string, unknown> }) {
  const toolChain = metadata.toolChain as { completed: number; total: number; status: string; successRate: number } | undefined;
  if (!toolChain) return null;

  const percentage = toolChain.total > 0 ? (toolChain.completed / toolChain.total) * 100 : 0;

  return (
    <div className="mt-3 p-3 bg-gray-50 rounded-xl border border-gray-200">
      <div className="flex items-center justify-between text-xs text-gray-600 mb-2">
        <span className="font-medium">{toolChain.completed}/{toolChain.total} steps</span>
        <span className="px-2 py-0.5 bg-white rounded-full border border-gray-200 text-gray-500">{toolChain.status}</span>
      </div>
      <div className="w-full bg-gray-200 rounded-full h-1.5">
        <div
          className="bg-gradient-to-r from-blue-500 to-indigo-500 h-1.5 rounded-full transition-all duration-500"
          style={{ width: `${percentage}%` }}
        />
      </div>
      <div className="text-xs text-gray-500 mt-1.5">
        {'Success rate: ' + Math.round(toolChain.successRate * 100) + '%'}
      </div>
    </div>
  );
}

function ShieldIcon({ className }: { className?: string }) {
  return (
    <svg className={className} fill="none" stroke="currentColor" strokeWidth={1.5} viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round"
        d="M9 12.75L11.25 15 15 9.75m-3-7.036A11.959 11.959 0 013.598 6 11.99 11.99 0 003 9.749c0 5.592 3.824 10.29 9 11.623 5.176-1.332 9-6.03 9-11.622 0-1.31-.21-2.571-.598-3.751h-.152c-3.196 0-6.1-1.248-8.25-3.285z" />
    </svg>
  );
}

function UserIcon({ className }: { className?: string }) {
  return (
    <svg className={className} fill="none" stroke="currentColor" strokeWidth={1.5} viewBox="0 0 24 24">
      <path strokeLinecap="round" strokeLinejoin="round"
        d="M15.75 6a3.75 3.75 0 11-7.5 0 3.75 3.75 0 017.5 0zM4.501 20.118a7.5 7.5 0 0114.998 0A17.933 17.933 0 0112 21.75c-2.676 0-5.216-.584-7.499-1.632z" />
    </svg>
  );
}

// =============================================================================
// GATE-2437 — TraceabilityNudge
//
// A single dismissible inline callout shown once (per browser) after the first
// assistant response, when the traceability feature flag is enabled.
// Informs users that source links are available and invites them to open the
// TraceabilityPanel.
//
// Dismiss is permanent: sets localStorage key `tp_nudge_dismissed`.
// =============================================================================
interface TraceabilityNudgeProps {
  /** Called when the user clicks "View trace →" */
  onOpen: () => void;
  /** Called when the user dismisses the nudge */
  onDismiss: () => void;
}

export function TraceabilityNudge({ onOpen, onDismiss }: TraceabilityNudgeProps) {
  return (
    <div
      role="status"
      data-testid="traceability-nudge"
      className="flex items-start gap-3 px-4 py-3 bg-indigo-50 border border-indigo-200 rounded-xl text-sm text-indigo-800 animate-fade-in"
    >
      {/* Link icon */}
      <svg className="w-4 h-4 mt-0.5 flex-shrink-0 text-indigo-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
          d="M13.828 10.172a4 4 0 00-5.656 0l-4 4a4 4 0 105.656 5.656l1.102-1.101m-.758-4.899a4 4 0 005.656 0l4-4a4 4 0 00-5.656-5.656l-1.1 1.1" />
      </svg>
      <span className="flex-1">
        Source links available — see how this answer was built.{' '}
        <button
          onClick={onOpen}
          className="font-semibold underline underline-offset-2 hover:text-indigo-900 transition-colors"
        >
          View trace →
        </button>
      </span>
      <button
        onClick={onDismiss}
        aria-label="Dismiss source traceability nudge"
        className="text-indigo-400 hover:text-indigo-700 transition-colors flex-shrink-0 ml-1"
      >
        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
        </svg>
      </button>
    </div>
  );
}
