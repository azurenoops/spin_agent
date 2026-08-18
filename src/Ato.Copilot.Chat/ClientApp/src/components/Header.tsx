import React, { useState, useCallback, useEffect, forwardRef } from 'react';
import { useChatContext } from '../contexts/ChatContext';
import type { LayoutMode } from '../contexts/EditorLayoutContext';
import { isCollaborationEnabled } from '../lib/featureFlags';
import PresenceAvatars from './Collaboration/PresenceAvatars';
import ShareAccessPopover from './Collaboration/ShareAccessPopover';

// ────────────────────────────────────────────────────────────────
//  Header Component — US6 (T042 + T043) + #256 EditorShell v2
// ────────────────────────────────────────────────────────────────

interface HeaderProps {
  /** Legacy toggle — still honoured when new props are absent. */
  sidebarOpen?: boolean;
  onToggleSidebar?: () => void;

  // ── #256 EditorShell v2 ──────────────────────────────────────
  /** Current layout mode; when provided the header adapts its chrome. */
  mode?: LayoutMode;
  /** Callback fired when the user clicks a mode-toggle button. */
  onSetMode?: (mode: LayoutMode) => void;
  /** Called when a modal (settings) opens/closes so App can suppress Escape-to-standard. */
  onModalStateChange?: (open: boolean) => void;
  /** Current viewport width (px) — hides research toggle below 1024px. */
  viewportWidth?: number;

  // ── #1357 Collaboration ───────────────────────────────────────
  /** Ref forwarded to the Share button for Cmd+Shift+S keyboard shortcut. */
  shareTriggerRef?: React.Ref<HTMLButtonElement>;
  /** The document/conversation id used for Share popover. */
  documentId?: string | null;
  /** Local user id — forwarded to ShareAccessPopover. */
  localUserId?: string;
}

export default function Header({
  sidebarOpen,
  onToggleSidebar,
  mode,
  onSetMode,
  onModalStateChange,
  viewportWidth = 1280,
  shareTriggerRef,
  documentId,
  localUserId = 'anon',
}: HeaderProps) {
  const { state, createConversation, selectConversation } = useChatContext();
  const [settingsOpen, setSettingsOpen] = useState(false);

  const activeConversation = state.conversations.find(
    (c) => c.id === state.activeConversationId
  );
  const title = activeConversation?.title || 'ATO Copilot';

  const openSettings = useCallback(() => {
    setSettingsOpen(true);
    onModalStateChange?.(true);
  }, [onModalStateChange]);

  const closeSettings = useCallback(() => {
    setSettingsOpen(false);
    onModalStateChange?.(false);
  }, [onModalStateChange]);

  const handleNewConversation = useCallback(async () => {
    const conv = await createConversation();
    selectConversation(conv.id);
  }, [createConversation, selectConversation]);

  // Close settings on Escape (FR-045)
  useEffect(() => {
    if (!settingsOpen) return;
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape') closeSettings();
    };
    window.addEventListener('keydown', handleEscape);
    return () => window.removeEventListener('keydown', handleEscape);
  }, [settingsOpen, closeSettings]);

  // ── Derived flags ─────────────────────────────────────────────
  const isFocused = mode === 'focused';
  const isResearch = mode === 'research';
  const isStandard = !mode || mode === 'standard';
  const canResearch = viewportWidth >= 1024;

  // ── Focused mode: 36px micro-bar ─────────────────────────────
  if (isFocused) {
    return (
      <>
        <header
          role="banner"
          className="flex items-center justify-between px-4 bg-white border-b border-gray-200"
          style={{ height: 36 }}
        >
          {/* Left: logo + title */}
          <div className="flex items-center gap-2 min-w-0">
            <div className="w-5 h-5 rounded bg-gradient-to-br from-blue-600 to-indigo-700 flex items-center justify-center flex-shrink-0">
              <svg className="w-3 h-3 text-white" fill="none" stroke="currentColor" strokeWidth={1.5} viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round"
                  d="M9 12.75L11.25 15 15 9.75m-3-7.036A11.959 11.959 0 013.598 6 11.99 11.99 0 003 9.749c0 5.592 3.824 10.29 9 11.623 5.176-1.332 9-6.03 9-11.622 0-1.31-.21-2.571-.598-3.751h-.152c-3.196 0-6.1-1.248-8.25-3.285z" />
              </svg>
            </div>
            <span className="text-xs font-medium text-gray-600 truncate max-w-xs">{title}</span>
            {/* Autosave indicator */}
            <span
              className="w-1.5 h-1.5 rounded-full bg-green-400 flex-shrink-0"
              title="Autosaved"
              aria-label="Autosaved"
            />
          </div>

          {/* Right: exit focused mode */}
          <button
            onClick={() => onSetMode?.('standard')}
            className="flex items-center gap-1 px-2 py-0.5 text-xs text-gray-400 hover:text-gray-700 hover:bg-gray-100 rounded transition-colors"
            aria-label="Switch to standard layout"
            aria-pressed={false}
            title="Exit focused mode (Esc)"
          >
            <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                d="M4 8V4m0 0h4M4 4l5 5m11-1V4m0 0h-4m4 0l-5 5M4 16v4m0 0h4m-4 0l5-5m11 5l-5-5m5 5v-4m0 4h-4" />
            </svg>
            <span className="hidden sm:inline">Exit Focus</span>
          </button>
        </header>

        {settingsOpen && <SettingsModal onClose={closeSettings} />}
      </>
    );
  }

  // ── Standard / Research mode: full header ────────────────────
  return (
    <>
      <header
        role="banner"
        className="flex items-center justify-between px-4 py-2.5 bg-white border-b border-gray-200 shadow-sm"
      >
        <div className="flex items-center gap-3">
          {/* Hamburger — legacy sidebar toggle when no mode system */}
          {onToggleSidebar && (
            <button
              onClick={onToggleSidebar}
              className="p-1.5 text-gray-500 hover:text-gray-700 hover:bg-gray-100 rounded-lg transition-colors"
              aria-label={sidebarOpen ? 'Close sidebar' : 'Open sidebar'}
            >
              <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                  d="M4 6h16M4 12h16M4 18h16" />
              </svg>
            </button>
          )}

          {/* Logo + Title */}
          <div className="flex items-center gap-2">
            <div className="w-7 h-7 rounded-lg bg-gradient-to-br from-blue-600 to-indigo-700 flex items-center justify-center flex-shrink-0">
              <svg className="w-4 h-4 text-white" fill="none" stroke="currentColor" strokeWidth={1.5} viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round"
                  d="M9 12.75L11.25 15 15 9.75m-3-7.036A11.959 11.959 0 013.598 6 11.99 11.99 0 003 9.749c0 5.592 3.824 10.29 9 11.623 5.176-1.332 9-6.03 9-11.622 0-1.31-.21-2.571-.598-3.751h-.152c-3.196 0-6.1-1.248-8.25-3.285z" />
              </svg>
            </div>
            <h1 className="text-base font-semibold text-gray-800 truncate max-w-md">{title}</h1>
          </div>
        </div>

        <div className="flex items-center gap-1">
          {/* Layout mode toggles — only when mode system is wired */}
          {onSetMode && (
            <div className="flex items-center gap-0.5 mr-2 rounded-lg bg-gray-100 p-0.5" role="group" aria-label="Layout mode">
              {/* Standard */}
              <button
                onClick={() => onSetMode('standard')}
                className={`px-2.5 py-1 text-xs rounded-md transition-colors ${
                  isStandard
                    ? 'bg-white shadow-sm text-gray-800 font-medium'
                    : 'text-gray-500 hover:text-gray-700'
                }`}
                aria-label="Switch to standard layout"
                aria-pressed={isStandard}
              >
                Standard
              </button>
              {/* Focused */}
              <button
                onClick={() => onSetMode('focused')}
                className={`px-2.5 py-1 text-xs rounded-md transition-colors ${
                  isFocused
                    ? 'bg-white shadow-sm text-gray-800 font-medium'
                    : 'text-gray-500 hover:text-gray-700'
                }`}
                aria-label="Switch to focused layout"
                aria-pressed={isFocused}
              >
                Focus
              </button>
              {/* Research — desktop only */}
              {canResearch && (
                <button
                  onClick={() => onSetMode('research')}
                  className={`px-2.5 py-1 text-xs rounded-md transition-colors ${
                    isResearch
                      ? 'bg-white shadow-sm text-gray-800 font-medium'
                      : 'text-gray-500 hover:text-gray-700'
                  }`}
                  aria-label="Switch to research layout"
                  aria-pressed={isResearch}
                >
                  Research
                </button>
              )}
            </div>
          )}

          {/* #1357: Presence avatars + Share button — only when collaboration is on */}
          {isCollaborationEnabled && documentId && (
            <>
              <PresenceAvatars className="mr-1" />
              <ShareAccessPopover documentId={documentId} localUserId={localUserId}>
                <button
                  ref={shareTriggerRef as React.Ref<HTMLButtonElement>}
                  className="flex items-center gap-1 px-3 py-1.5 text-sm text-gray-600
                             hover:text-gray-800 hover:bg-gray-100 rounded-lg transition-colors mr-1"
                  aria-label="Share document (⌘⇧S)"
                  title="Share (⌘⇧S)"
                >
                  <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                      d="M8.684 13.342C8.886 12.938 9 12.482 9 12c0-.482-.114-.938-.316-1.342m0 2.684a3 3 0 110-2.684m0 2.684l6.632 3.316m-6.632-6l6.632-3.316m0 0a3 3 0 105.367-2.684 3 3 0 00-5.367 2.684zm0 9.316a3 3 0 105.368 2.684 3 3 0 00-5.368-2.684z" />
                  </svg>
                  <span className="hidden sm:inline">Share</span>
                </button>
              </ShareAccessPopover>
            </>
          )}

          {/* New Conversation */}
          <button
            onClick={handleNewConversation}
            className="flex items-center gap-1 px-3 py-1.5 text-sm text-gray-600 hover:text-gray-800 hover:bg-gray-100 rounded-lg transition-colors"
            aria-label="New conversation"
          >
            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
            </svg>
            <span className="hidden sm:inline">New</span>
          </button>

          {/* Settings */}
          <button
            onClick={openSettings}
            className="p-1.5 text-gray-500 hover:text-gray-700 hover:bg-gray-100 rounded-lg transition-colors"
            aria-label="Settings"
          >
            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.066 2.573c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.573 1.066c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.066-2.573c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
            </svg>
          </button>
        </div>
      </header>

      {settingsOpen && <SettingsModal onClose={closeSettings} />}
    </>
  );
}

// ────────────────────────────────────────────────────────────────
//  Settings Modal (T043)
// ────────────────────────────────────────────────────────────────

function SettingsModal({ onClose }: { onClose: () => void }) {
  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black bg-opacity-50"
      onClick={onClose}
    >
      <div
        className="bg-white rounded-xl shadow-xl max-w-md w-full mx-4 p-6"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-xl font-semibold text-gray-800">Settings</h2>
          <button
            onClick={onClose}
            className="p-1 text-gray-400 hover:text-gray-600 rounded"
            aria-label="Close settings"
          >
            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        {/* App Info */}
        <div className="mb-6">
          <h3 className="text-sm font-medium text-gray-500 uppercase tracking-wider mb-2">
            Application
          </h3>
          <div className="space-y-1 text-sm text-gray-700">
            <p><span className="font-medium">Name:</span> ATO Copilot</p>
            <p><span className="font-medium">Version:</span> 1.0.0</p>
          </div>
        </div>

        {/* Features */}
        <div className="mb-6">
          <h3 className="text-sm font-medium text-gray-500 uppercase tracking-wider mb-2">
            Features
          </h3>
          <ul className="text-sm text-gray-600 space-y-1">
            <li>• AI-powered compliance assistance</li>
            <li>• Real-time messaging via SignalR</li>
            <li>• File attachment analysis</li>
            <li>• Multi-conversation management</li>
            <li>• Markdown rendering with code highlighting</li>
          </ul>
        </div>

        {/* Keyboard Shortcuts */}
        <div>
          <h3 className="text-sm font-medium text-gray-500 uppercase tracking-wider mb-2">
            Keyboard Shortcuts
          </h3>
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-gray-500">
                <th className="py-1 font-medium">Shortcut</th>
                <th className="py-1 font-medium">Action</th>
              </tr>
            </thead>
            <tbody className="text-gray-700">
              <tr>
                <td className="py-1"><kbd className="px-1.5 py-0.5 bg-gray-100 border rounded text-xs">⌘⇧F</kbd></td>
                <td className="py-1">Focused mode</td>
              </tr>
              <tr>
                <td className="py-1"><kbd className="px-1.5 py-0.5 bg-gray-100 border rounded text-xs">⌘⇧R</kbd></td>
                <td className="py-1">Research mode (desktop)</td>
              </tr>
              <tr>
                <td className="py-1"><kbd className="px-1.5 py-0.5 bg-gray-100 border rounded text-xs">Esc</kbd></td>
                <td className="py-1">Exit focused mode / close modal</td>
              </tr>
              <tr>
                <td className="py-1"><kbd className="px-1.5 py-0.5 bg-gray-100 border rounded text-xs">Ctrl+K</kbd></td>
                <td className="py-1">Toggle sidebar</td>
              </tr>
              <tr>
                <td className="py-1"><kbd className="px-1.5 py-0.5 bg-gray-100 border rounded text-xs">Ctrl+N</kbd></td>
                <td className="py-1">New conversation</td>
              </tr>
              <tr>
                <td className="py-1"><kbd className="px-1.5 py-0.5 bg-gray-100 border rounded text-xs">Alt+T</kbd></td>
                <td className="py-1">Toggle traceability panel</td>
              </tr>
              <tr>
                <td className="py-1"><kbd className="px-1.5 py-0.5 bg-gray-100 border rounded text-xs">Enter</kbd></td>
                <td className="py-1">Send message</td>
              </tr>
              <tr>
                <td className="py-1"><kbd className="px-1.5 py-0.5 bg-gray-100 border rounded text-xs">Shift+Enter</kbd></td>
                <td className="py-1">New line in message</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
