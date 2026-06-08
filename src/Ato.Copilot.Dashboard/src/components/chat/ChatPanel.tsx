import { useEffect, useRef, useCallback, useState } from 'react';
import ChatHeader from './ChatHeader';
import ChatMessages from './ChatMessages';
import ChatInput from './ChatInput';
import ConversationList from './ConversationList';
import WelcomeMessage from './WelcomeMessage';
import QuickActions from './QuickActions';
import AiHealthBanner from './AiHealthBanner';
import McpToolChips from './McpToolChips';
import { useChat } from '../../hooks/useChat';
import { useSettings } from '../../hooks/useSettings';

const MIN_WIDTH = 320;
const MAX_WIDTH = 600;
const MOBILE_BREAKPOINT = 768;

export interface ChatPanelProps {
  isOpen: boolean;
  onClose: () => void;
  width: number;
  onWidthChange: (width: number) => void;
}

export default function ChatPanel({ isOpen, onClose, width, onWidthChange }: ChatPanelProps) {
  const { settings } = useSettings();
  const {
    conversations,
    activeConversation,
    isProcessing,
    progressSteps,
    sendMessage,
    newConversation,
    selectConversation,
    deleteConversation,
    cancelStream,
    context,
    panelState,
  } = useChat();

  const panelRef = useRef<HTMLDivElement>(null);
  const previousFocusRef = useRef<Element | null>(null);
  const [isMobile, setIsMobile] = useState(window.innerWidth < MOBILE_BREAKPOINT);
  const isDraggingRef = useRef(false);

  // T272: Health check state — trigger on panel open
  const [healthCheckTrigger, setHealthCheckTrigger] = useState(0);
  const prevIsOpen = useRef(isOpen);

  useEffect(() => {
    if (isOpen && !prevIsOpen.current) {
      // Panel just opened — trigger health check
      setHealthCheckTrigger((n) => n + 1);
    }
    prevIsOpen.current = isOpen;
  }, [isOpen]);

  // T030: Keyboard shortcut — Escape to close
  useEffect(() => {
    if (!isOpen) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose();
      }
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [isOpen, onClose]);

  // T032 / T271: Focus management — focus message input when panel opens
  useEffect(() => {
    if (isOpen) {
      previousFocusRef.current = document.activeElement;
      requestAnimationFrame(() => {
        const textarea = panelRef.current?.querySelector('textarea');
        textarea?.focus();
      });
    } else if (previousFocusRef.current instanceof HTMLElement) {
      previousFocusRef.current.focus();
      previousFocusRef.current = null;
    }
  }, [isOpen]);

  // T031: Drag-to-resize
  const handleMouseDown = useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault();
      isDraggingRef.current = true;
      const startX = e.clientX;
      const startWidth = width;

      const handleMouseMove = (e: MouseEvent) => {
        if (!isDraggingRef.current) return;
        const delta = startX - e.clientX;
        const newWidth = Math.min(MAX_WIDTH, Math.max(MIN_WIDTH, startWidth + delta));
        onWidthChange(newWidth);
      };

      const handleMouseUp = () => {
        isDraggingRef.current = false;
        document.removeEventListener('mousemove', handleMouseMove);
        document.removeEventListener('mouseup', handleMouseUp);
        document.body.style.cursor = '';
        document.body.style.userSelect = '';
      };

      document.body.style.cursor = 'col-resize';
      document.body.style.userSelect = 'none';
      document.addEventListener('mousemove', handleMouseMove);
      document.addEventListener('mouseup', handleMouseUp);
    },
    [width, onWidthChange],
  );

  // T033: Responsive full-width overlay
  useEffect(() => {
    const handleResize = () => {
      setIsMobile(window.innerWidth < MOBILE_BREAKPOINT);
    };
    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
  }, []);

  // T272: trigger health check before send
  const handleSend = useCallback(
    async (content: string, attachments?: File[]) => {
      setHealthCheckTrigger((n) => n + 1);
      await sendMessage(content, attachments);
    },
    [sendMessage],
  );

  const panelWidth = isMobile ? '100vw' : `${width}px`;

  return (
    <div
      ref={panelRef}
      className={`fixed right-0 top-14 bottom-0 z-40 flex flex-col border-l border-gray-200 bg-gray-50 shadow-xl transition-transform duration-300 ease-in-out ${
        isOpen ? 'translate-x-0' : 'translate-x-full'
      }`}
      style={{ width: panelWidth }}
      role="complementary"
      aria-label="Chat panel"
    >
      {/* Drag handle — left edge */}
      {!isMobile && (
        <div
          onMouseDown={handleMouseDown}
          className="absolute left-0 top-0 bottom-0 w-1 cursor-col-resize hover:bg-indigo-400 transition-colors"
          role="separator"
          aria-orientation="vertical"
          aria-label="Resize chat panel"
        />
      )}
      <ChatHeader
        title="Chat with ATO Copilot"
        onClose={onClose}
        onNewConversation={newConversation}
        conversationCount={conversations.length}
        context={context}
      />

      {/* T272: Non-blocking degraded-mode banner */}
      <AiHealthBanner triggerCheck={healthCheckTrigger > 0} />

      {conversations.length > 0 && (
        <ConversationList
          conversations={conversations}
          activeId={panelState.activeConversationId}
          onSelect={selectConversation}
          onDelete={deleteConversation}
          onNew={newConversation}
        />
      )}

      {activeConversation && activeConversation.messages.length > 0 ? (
        <ChatMessages
          messages={activeConversation.messages}
          progressSteps={progressSteps}
          isProcessing={isProcessing}
        />
      ) : (
        <WelcomeMessage context={context} onSendExample={sendMessage} />
      )}

      {/* T270: MCP tool execution chips — shown during streaming */}
      <McpToolChips />

      {settings.showQuickActions && (
        <QuickActions context={context} onSend={sendMessage} disabled={isProcessing} />
      )}

      <ChatInput
        onSend={handleSend}
        onCancel={cancelStream}
        isProcessing={isProcessing}
        disabled={false}
      />
    </div>
  );
}
