import React, { useState, useEffect, useCallback, useRef } from 'react';
import { ChatProvider, useChatContext } from './contexts/ChatContext';
import { EditorLayoutProvider, useEditorLayout, LayoutMode } from './contexts/EditorLayoutContext';
import { CollaborationProvider } from './contexts/CollaborationContext';
import ChatWindow from './components/ChatWindow';
import ConversationList from './components/ConversationList';
import Header from './components/Header';
import { isCollaborationEnabled } from './lib/featureFlags';
import './styles/App.css';

// ────────────────────────────────────────────────────────────────
//  App Root — wraps in ChatProvider + EditorLayoutProvider
// ────────────────────────────────────────────────────────────────

function App() {
  return (
    <ChatProvider>
      <AppLayoutBridge />
    </ChatProvider>
  );
}

/**
 * Bridge: reads activeConversationId from ChatContext so EditorLayoutProvider
 * can key localStorage persistence per conversation.
 */
function AppLayoutBridge() {
  const { state } = useChatContext();
  const conversationId = state.activeConversationId ?? null;

  // Stable transient identity for this browser session.
  // Replace with real auth once auth middleware is wired.
  const userId = useRef(`anon-${Math.random().toString(36).slice(2)}`).current;
  const displayName = useRef('Anonymous').current;

  const inner = (
    <EditorLayoutProvider conversationId={conversationId}>
      <AppLayout />
    </EditorLayoutProvider>
  );

  if (!isCollaborationEnabled) return inner;

  return (
    <CollaborationProvider
      documentId={conversationId}
      userId={userId}
      displayName={displayName}
    >
      {inner}
    </CollaborationProvider>
  );
}

// ─── Responsive breakpoints ───────────────────────────────────────────────────
// Used to clamp modes on smaller viewports.

function useViewportWidth(): number {
  const [width, setWidth] = useState(() =>
    typeof window !== 'undefined' ? window.innerWidth : 1280
  );
  useEffect(() => {
    const handler = () => setWidth(window.innerWidth);
    window.addEventListener('resize', handler, { passive: true });
    return () => window.removeEventListener('resize', handler);
  }, []);
  return width;
}

// ─── AppLayout ────────────────────────────────────────────────────────────────

function AppLayout() {
  const { mode, setMode, sidebarWidth, reducedMotion } = useEditorLayout();
  const { state, createConversation, selectConversation } = useChatContext();
  const viewportWidth = useViewportWidth();
  const conversationId = state.activeConversationId ?? null;

  // Track whether any modal is open so Escape only resets layout when no modal.
  // We detect open modals via a ref populated by Header (settingsOpen prop).
  const modalOpenRef = useRef(false);
  // Share popover + add-comment trigger refs for keyboard shortcuts
  const shareTriggerRef = useRef<HTMLButtonElement | null>(null);
  const addCommentTriggerRef = useRef<HTMLButtonElement | null>(null);

  // Clamp mode on tablet/mobile —————————————————————————————————
  // On mobile (<768px) only focused mode is valid.
  // On tablet (768–1024px) research mode is not available.
  useEffect(() => {
    if (viewportWidth < 768 && mode !== 'focused') {
      setMode('focused');
    } else if (viewportWidth < 1024 && mode === 'research') {
      setMode('standard');
    }
  }, [viewportWidth, mode, setMode]);

  // ─── Global Keyboard Shortcuts ───────────────────────────────

  const handleKeyDown = useCallback(
    (e: globalThis.KeyboardEvent) => {
      const mod = e.ctrlKey || e.metaKey;

      // Ctrl+K / Cmd+K: Toggle sidebar (legacy shortcut — preserved)
      if (mod && !e.shiftKey && e.key === 'k') {
        e.preventDefault();
        setMode(mode === 'standard' ? 'focused' : 'standard');
        return;
      }

      // Ctrl+N / Cmd+N: New conversation
      if (mod && !e.shiftKey && e.key === 'n') {
        e.preventDefault();
        createConversation().then((conv) => {
          selectConversation(conv.id);
        });
        return;
      }

      // Cmd/Ctrl+Shift+F → Focused mode (#256)
      if (mod && e.shiftKey && e.key === 'F') {
        e.preventDefault();
        setMode('focused');
        return;
      }

      // Cmd/Ctrl+Shift+R → Research mode (#256) — desktop only
      if (mod && e.shiftKey && e.key === 'R') {
        e.preventDefault();
        if (viewportWidth >= 1024) {
          setMode('research');
        }
        return;
      }

      // Cmd/Ctrl+Shift+S → Open Share panel (#1357)
      if (mod && e.shiftKey && e.key === 'S') {
        e.preventDefault();
        shareTriggerRef.current?.click();
        return;
      }

      // Cmd/Ctrl+Shift+K → Add comment (#1357)
      if (mod && e.shiftKey && e.key === 'K') {
        e.preventDefault();
        addCommentTriggerRef.current?.click();
        return;
      }

      // Escape → return to standard from focused (only when no modal open)
      if (e.key === 'Escape' && mode === 'focused' && !modalOpenRef.current) {
        setMode('standard');
        return;
      }
    },
    [mode, setMode, createConversation, selectConversation, viewportWidth]
  );

  useEffect(() => {
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [handleKeyDown]);

  // ─── Sidebar geometry ─────────────────────────────────────────
  // On tablet (768–1024px) standard mode auto-collapses sidebar to icon-rail (56px).
  // We override the context sidebarWidth for this breakpoint.
  let effectiveSidebarWidth = sidebarWidth;
  if (viewportWidth >= 768 && viewportWidth < 1024 && mode === 'standard') {
    effectiveSidebarWidth = 56; // icon-rail on tablet
  }
  if (viewportWidth < 768) {
    effectiveSidebarWidth = 0; // fully hidden on mobile — accessible via bottom sheet
  }

  const isSidebarCollapsed = effectiveSidebarWidth === 0;

  // Transition style — skip animation when prefers-reduced-motion is on
  const sidebarTransition = reducedMotion
    ? undefined
    : 'width 220ms cubic-bezier(0.4, 0, 0.2, 1)';

  // Content opacity fade: 150ms delay so text doesn't reflow mid-transition
  const contentOpacity = isSidebarCollapsed ? 0 : 1;
  const contentTransition = reducedMotion
    ? undefined
    : 'opacity 150ms ease 150ms';

  // ─── Mobile bottom sheet (FAB) — only shown on mobile ─────────
  const showFab = viewportWidth < 768;
  const [bottomSheetOpen, setBottomSheetOpen] = useState(false);

  return (
    <div className="flex flex-col h-screen bg-gray-50">
      <Header
        mode={mode}
        onSetMode={setMode}
        onModalStateChange={(open) => { modalOpenRef.current = open; }}
        viewportWidth={viewportWidth}
        shareTriggerRef={shareTriggerRef}
        documentId={conversationId}
      />

      <div className="flex flex-1 overflow-hidden">
        {/* Sidebar */}
        <div
          aria-hidden={isSidebarCollapsed ? 'true' : undefined}
          style={{
            width: effectiveSidebarWidth,
            flexShrink: 0,
            overflow: 'hidden',
            borderRight: '1px solid #e5e7eb',
            backgroundColor: '#fff',
            transition: sidebarTransition,
          }}
        >
          {/* Inner wrapper: opacity fade prevents text reflow flash */}
          <div
            style={{
              opacity: contentOpacity,
              transition: contentTransition,
              width: mode === 'research' ? 56 : 240,
              // When sidebar is 0px, ensure children are inert
              pointerEvents: isSidebarCollapsed ? 'none' : undefined,
            }}
            // All sidebar children get tabIndex={-1} via CSS pointer-events
            // but we also need to remove them from the tab order.
            // We achieve this by wrapping in an element with inert when collapsed.
            {...(isSidebarCollapsed ? ({ inert: '' } as unknown as React.HTMLAttributes<HTMLDivElement>) : {})}
          >
            <ConversationList
              iconOnly={effectiveSidebarWidth <= 56 && effectiveSidebarWidth > 0}
            />
          </div>
        </div>

        {/* Main Chat Area */}
        <ChatWindow layoutMode={mode} viewportWidth={viewportWidth} />
      </div>

      {/* Mobile FAB — triggers bottom sheet with sidebar + panel content */}
      {showFab && (
        <>
          <button
            className="fixed bottom-6 right-6 z-50 w-14 h-14 rounded-full bg-blue-600 text-white shadow-lg flex items-center justify-center"
            aria-label="Open navigation panel"
            onClick={() => setBottomSheetOpen(true)}
          >
            <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                d="M4 6h16M4 12h16M4 18h16" />
            </svg>
          </button>

          {/* Bottom sheet overlay */}
          {bottomSheetOpen && (
            <div
              className="fixed inset-0 z-40 flex flex-col justify-end"
              onClick={() => setBottomSheetOpen(false)}
            >
              <div
                className="bg-white rounded-t-2xl shadow-2xl max-h-[80vh] overflow-y-auto p-4"
                onClick={(e) => e.stopPropagation()}
              >
                <div className="flex items-center justify-between mb-3">
                  <h2 className="text-sm font-semibold text-gray-700">Navigation</h2>
                  <button
                    className="p-1 text-gray-400 hover:text-gray-600 rounded"
                    aria-label="Close navigation"
                    onClick={() => setBottomSheetOpen(false)}
                  >
                    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                        d="M6 18L18 6M6 6l12 12" />
                    </svg>
                  </button>
                </div>
                <ConversationList iconOnly={false} />
              </div>
            </div>
          )}
        </>
      )}
    </div>
  );
}

export default App;
