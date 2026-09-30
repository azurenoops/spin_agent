import { createContext, useContext, useCallback, useEffect, useLayoutEffect, useMemo, type ReactNode } from 'react';
import { useLocalStorage } from '../../hooks/useLocalStorage';
import { useSettings } from '../../hooks/useSettings';
import type { ChatPanelState } from '../../types/chat';
import { useWorkspaceStorageKey } from '../../features/workspaces/workspaceStorage';

interface StoredPanelState extends ChatPanelState {
  usesPersonalWidth?: boolean;
}

const DEFAULT_PANEL_STATE: StoredPanelState = {
  isOpen: false,
  width: 420,
  activeConversationId: null,
  usesPersonalWidth: true,
};

interface ChatPanelContextValue {
  panelState: ChatPanelState;
  togglePanel: () => void;
  closePanel: () => void;
  setWidth: (width: number) => void;
}

const ChatPanelContext = createContext<ChatPanelContextValue | null>(null);

export function ChatPanelProvider({ children }: { children: ReactNode }) {
  const { settings, updateSettings } = useSettings();
  const scopedKey = useWorkspaceStorageKey('ato-chat-panel-state');
  const presentationKey = scopedKey ? `${scopedKey}:presentation` : null;
  const initialState = useMemo(() => {
    if (scopedKey !== 'ato-chat-panel-state') return DEFAULT_PANEL_STATE;
    // Legacy chat selection and presentation shared a key. Never write it back.
    try {
      const legacy = JSON.parse(localStorage.getItem(scopedKey) ?? 'null') as Partial<StoredPanelState> | null;
      return legacy ? { ...DEFAULT_PANEL_STATE, ...legacy, usesPersonalWidth: false } : DEFAULT_PANEL_STATE;
    } catch {
      console.warn('Legacy assistant presentation could not be read; using defaults.');
      return DEFAULT_PANEL_STATE;
    }
  }, [scopedKey]);
  const [storedPanelState, setPanelState] = useLocalStorage<StoredPanelState>(presentationKey, initialState);
  const panelState = { ...storedPanelState, width: settings.chatPanelWidth };

  useLayoutEffect(() => {
    if (storedPanelState.usesPersonalWidth) return;
    // Preserve the width actually used before Settings became the presentation source.
    if (Number.isFinite(storedPanelState.width)) updateSettings({ chatPanelWidth: storedPanelState.width });
    setPanelState(previous => ({ ...previous, usesPersonalWidth: true }));
  }, [storedPanelState.usesPersonalWidth, storedPanelState.width, updateSettings, setPanelState]);

  const togglePanel = useCallback(() => {
    setPanelState((prev) => ({ ...prev, isOpen: !prev.isOpen }));
  }, [setPanelState]);

  const closePanel = useCallback(() => {
    setPanelState((prev) => ({ ...prev, isOpen: false }));
  }, [setPanelState]);

  const setWidth = useCallback((width: number) => {
    updateSettings({ chatPanelWidth: width });
  }, [updateSettings]);

  // T271: Ctrl+Shift+C global keyboard shortcut — must work from any route
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.ctrlKey && e.shiftKey && e.key === 'C') {
        e.preventDefault();
        togglePanel();
      }
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [togglePanel]);

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
