import { createContext, useContext, useCallback, type ReactNode } from 'react';
import { useLocalStorage } from '../../hooks/useLocalStorage';
import type { ChatPanelState } from '../../types/chat';

const DEFAULT_PANEL_STATE: ChatPanelState = {
  isOpen: false,
  width: 420,
  activeConversationId: null,
};

interface ChatPanelContextValue {
  panelState: ChatPanelState;
  togglePanel: () => void;
  closePanel: () => void;
  setWidth: (width: number) => void;
}

const ChatPanelContext = createContext<ChatPanelContextValue | null>(null);

export function ChatPanelProvider({ children }: { children: ReactNode }) {
  const [panelState, setPanelState] = useLocalStorage<ChatPanelState>('ato-chat-panel-state', DEFAULT_PANEL_STATE);

  const togglePanel = useCallback(() => {
    setPanelState((prev) => ({ ...prev, isOpen: !prev.isOpen }));
  }, [setPanelState]);

  const closePanel = useCallback(() => {
    setPanelState((prev) => ({ ...prev, isOpen: false }));
  }, [setPanelState]);

  const setWidth = useCallback((width: number) => {
    setPanelState((prev) => ({ ...prev, width }));
  }, [setPanelState]);

  return (
    <ChatPanelContext.Provider value={{ panelState, togglePanel, closePanel, setWidth }}>
      {children}
    </ChatPanelContext.Provider>
  );
}

export function useChatPanel(): ChatPanelContextValue {
  const ctx = useContext(ChatPanelContext);
  if (!ctx) throw new Error('useChatPanel must be used within ChatPanelProvider');
  return ctx;
}
