import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import ChatPanel from '../../components/chat/ChatPanel';
import { ChatPanelProvider, useChatPanel } from '../../components/chat/ChatPanelContext';
import { SettingsContext, useSettings, useSettingsProvider } from '../../hooks/useSettings';

vi.mock('../../features/workspaces/workspaceStorage', () => ({ useWorkspaceStorageKey: () => 'ato-chat-panel-state:workspace:synthetic' }));
vi.mock('../../hooks/useChat', () => ({ useChat: () => ({
  conversations: [], activeConversation: null, isProcessing: false, progressSteps: [],
  sendMessage: vi.fn(), newConversation: vi.fn(), selectConversation: vi.fn(), deleteConversation: vi.fn(),
  cancelStream: vi.fn(), context: {}, panelState: { activeConversationId: null },
}) }));
vi.mock('../../components/chat/ChatHeader', () => ({ default: () => null }));
vi.mock('../../components/chat/ChatMessages', () => ({ default: () => null }));
vi.mock('../../components/chat/ChatInput', () => ({ default: () => <textarea aria-label="Message" /> }));
vi.mock('../../components/chat/ConversationList', () => ({ default: () => null }));
vi.mock('../../components/chat/WelcomeMessage', () => ({ default: () => null }));
vi.mock('../../components/chat/QuickActions', () => ({ default: () => <div>Quick action choices</div> }));
vi.mock('../../components/chat/AiHealthBanner', () => ({ default: () => null }));
vi.mock('../../components/chat/McpToolChips', () => ({ default: () => null }));

function Panel() {
  const { panelState, setWidth, closePanel } = useChatPanel();
  const { updateSettings } = useSettings();
  return <>
    <button onClick={() => updateSettings({ chatPanelWidth: 510, showQuickActions: false })}>Update preferences</button>
    <ChatPanel isOpen onClose={closePanel} width={panelState.width} onWidthChange={setWidth} />
  </>;
}
function App() {
  const context = useSettingsProvider();
  return <SettingsContext.Provider value={context}><ChatPanelProvider><Panel /></ChatPanelProvider></SettingsContext.Provider>;
}
beforeEach(() => {
  localStorage.clear();
  vi.stubGlobal('innerWidth', 1200);
});
afterEach(() => vi.unstubAllGlobals());

describe('Observable assistant presentation', () => {
  it('renders preference changes immediately and preserves bounded drag resizing', () => {
    // Arrange
    render(<App />);
    const panel = screen.getByRole('complementary', { name: 'Chat panel' });
    expect(panel).toHaveStyle({ width: '420px' });
    expect(screen.getByText('Quick action choices')).toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Update preferences' }));
    // Assert
    expect(panel).toHaveStyle({ width: '510px' });
    expect(screen.queryByText('Quick action choices')).toBeNull();
    fireEvent.mouseDown(screen.getByRole('separator'), { clientX: 700 });
    fireEvent.mouseMove(document, { clientX: 100 });
    fireEvent.mouseUp(document);
    expect(panel).toHaveStyle({ width: '600px' });
    fireEvent.mouseDown(screen.getByRole('separator'), { clientX: 700 });
    fireEvent.mouseMove(document, { clientX: 1200 });
    fireEvent.mouseUp(document);
    expect(panel).toHaveStyle({ width: '320px' });
    expect(document.body.style.cursor).toBe('');
  });

  it('uses full viewport width on mobile without replacing the desktop preference', () => {
    // Arrange
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: 'Update preferences' }));
    // Act
    vi.stubGlobal('innerWidth', 390);
    fireEvent(window, new Event('resize'));
    // Assert
    expect(screen.getByRole('complementary')).toHaveStyle({ width: '100vw' });
    expect(screen.queryByRole('separator')).toBeNull();
    vi.stubGlobal('innerWidth', 1200);
    fireEvent(window, new Event('resize'));
    expect(screen.getByRole('complementary')).toHaveStyle({ width: '510px' });
  });
});
