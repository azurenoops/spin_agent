import { act, renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ChatPanelProvider, useChatPanel } from '../../components/chat/ChatPanelContext';
import { DEFAULT_SETTINGS, SettingsContext, useSettings, useSettingsProvider } from '../../hooks/useSettings';

const scope = vi.hoisted(() => ({ key: 'ato-chat-panel-state:workspace:synthetic' as string | null }));
vi.mock('../../features/workspaces/workspaceStorage', () => ({ useWorkspaceStorageKey: () => scope.key }));

function Wrapper({ children }: { children: ReactNode }) {
  const settings = useSettingsProvider();
  return <SettingsContext.Provider value={settings}><ChatPanelProvider>{children}</ChatPanelProvider></SettingsContext.Provider>;
}
const usePreferences = () => ({ chat: useChatPanel(), personal: useSettings() });
beforeEach(() => { localStorage.clear(); scope.key = 'ato-chat-panel-state:workspace:synthetic'; });

describe('Shared assistant width preference', () => {
  it('uses the personal preference for actual panel width and updates it through drag resize', () => {
    // Arrange
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({ ...DEFAULT_SETTINGS, chatPanelWidth: 500 }));
    const { result } = renderHook(usePreferences, { wrapper: Wrapper });
    // Act / Assert
    expect(result.current.chat.panelState.width).toBe(500);
    act(() => result.current.chat.setWidth(560));
    expect(result.current.personal.settings.chatPanelWidth).toBe(560);
    act(() => result.current.personal.updateSettings({ chatPanelWidth: 380 }));
    expect(result.current.chat.panelState.width).toBe(380);
    act(() => result.current.personal.resetSettings());
    expect(result.current.chat.panelState.width).toBe(420);
  });

  it.each(['ato-chat-panel-state', 'ato-chat-panel-state:workspace:synthetic'])('preserves previously dragged widths from %s once, then honors reset across remount', key => {
    // Arrange
    scope.key = key;
    const storageKey = key === 'ato-chat-panel-state' ? key : `${key}:presentation`;
    localStorage.setItem(storageKey, JSON.stringify({ isOpen: true, width: 570, activeConversationId: 'old-selection' }));
    const first = renderHook(usePreferences, { wrapper: Wrapper });
    // Act / Assert
    expect(first.result.current.chat.panelState.width).toBe(570);
    expect(first.result.current.personal.settings.chatPanelWidth).toBe(570);
    expect(first.result.current.chat.panelState.isOpen).toBe(true);
    expect(first.result.current.chat.panelState.activeConversationId).toBe('old-selection');
    act(() => first.result.current.personal.resetSettings());
    first.unmount();
    const second = renderHook(usePreferences, { wrapper: Wrapper });
    expect(second.result.current.chat.panelState.width).toBe(420);
    expect(second.result.current.chat.panelState.isOpen).toBe(true);
  });

  it('retains opening behavior and bounds even without workspace persistence', () => {
    // Arrange
    scope.key = null;
    const { result } = renderHook(usePreferences, { wrapper: Wrapper });
    // Act
    act(() => { result.current.chat.togglePanel(); result.current.chat.setWidth(900); });
    // Assert
    expect(result.current.chat.panelState.isOpen).toBe(true);
    expect(result.current.chat.panelState.width).toBe(600);
    act(() => result.current.chat.closePanel());
    expect(result.current.chat.panelState.isOpen).toBe(false);
  });

  it('does not reimport obsolete width when legacy conversation selection writes its own state', () => {
    // Arrange
    scope.key = 'ato-chat-panel-state';
    const legacy = { isOpen: true, width: 570, activeConversationId: 'old-selection' };
    localStorage.setItem(scope.key, JSON.stringify(legacy));
    const first = renderHook(usePreferences, { wrapper: Wrapper });
    act(() => first.result.current.personal.resetSettings());
    first.unmount();
    // Act
    localStorage.setItem(scope.key, JSON.stringify({ ...legacy, activeConversationId: 'new-selection' }));
    const second = renderHook(usePreferences, { wrapper: Wrapper });
    // Assert
    expect(second.result.current.chat.panelState.width).toBe(420);
    expect(JSON.parse(localStorage.getItem(scope.key)!)).toEqual({ ...legacy, activeConversationId: 'new-selection' });
  });
});
