import React, { useState, useCallback } from 'react';
import { useChatContext } from '../contexts/ChatContext';
import { Conversation } from '../types/chat';

// ────────────────────────────────────────────────────────────────
//  ConversationList — US2 Sidebar Component (T025)
// ────────────────────────────────────────────────────────────────

interface ConversationListProps {
  /**
   * When true the list renders in icon-only rail mode (research mode sidebar
   * at 56px). Labels and search are hidden; only conversation initials / icons
   * are shown. (#256 EditorShell v2)
   */
  iconOnly?: boolean;
}

export default function ConversationList({ iconOnly = false }: ConversationListProps) {
  const { state, selectConversation, createConversation, deleteConversation, searchConversations } =
    useChatContext();
  const [searchQuery, setSearchQuery] = useState('');
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null);

  const displayConversations =
    searchQuery.trim() && state.searchResults.length > 0
      ? state.searchResults
      : state.conversations;

  // ─── Handlers ──────────────────────────────────────────────────

  const handleSearch = useCallback(
    (query: string) => {
      setSearchQuery(query);
      searchConversations(query);
    },
    [searchConversations]
  );

  const handleNewConversation = useCallback(async () => {
    const conv = await createConversation();
    selectConversation(conv.id);
  }, [createConversation, selectConversation]);

  const handleDelete = useCallback(
    async (conversationId: string) => {
      await deleteConversation(conversationId);
      setConfirmDelete(null);
    },
    [deleteConversation]
  );

  // ─── Date Label Helper ────────────────────────────────────────

  function getDateLabel(dateStr: string): string {
    const date = new Date(dateStr);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffDays = Math.floor(diffMs / (1000 * 60 * 60 * 24));

    if (diffDays === 0) return 'Today';
    if (diffDays === 1) return 'Yesterday';
    if (diffDays < 7) return `${diffDays} days ago`;
    return date.toLocaleDateString();
  }

  return (
    <div className="flex flex-col h-full">
      {/* New Conversation Button */}
      <div className="p-3 border-b border-gray-200">
        <button
          onClick={handleNewConversation}
          className="w-full flex items-center justify-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors text-sm font-medium"
          aria-label="New conversation"
          title="New conversation"
        >
          <svg className="w-4 h-4 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
          </svg>
          {!iconOnly && <span>New Conversation</span>}
        </button>
      </div>

      {/* Search Input — hidden in icon-rail mode */}
      {!iconOnly && (
        <div className="p-3 border-b border-gray-200">
          <input
            type="text"
            value={searchQuery}
            onChange={(e) => handleSearch(e.target.value)}
            placeholder="Search conversations..."
            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
          />
        </div>
      )}

      {/* Conversation List */}
      <div className="flex-1 overflow-y-auto scrollbar-thin">
        {!iconOnly && displayConversations.length === 0 && (
          <div className="p-4 text-center text-gray-400 text-sm">
            {searchQuery ? 'No conversations found' : 'No conversations yet'}
          </div>
        )}

        {displayConversations.map((conversation: Conversation) => (
          <ConversationItem
            key={conversation.id}
            conversation={conversation}
            isActive={state.activeConversationId === conversation.id}
            isConfirmingDelete={confirmDelete === conversation.id}
            onSelect={() => selectConversation(conversation.id)}
            onDelete={() => handleDelete(conversation.id)}
            onConfirmDelete={() => setConfirmDelete(conversation.id)}
            onCancelDelete={() => setConfirmDelete(null)}
            dateLabel={getDateLabel(conversation.updatedAt)}
            iconOnly={iconOnly}
          />
        ))}
      </div>
    </div>
  );
}

// ────────────────────────────────────────────────────────────────
//  Conversation Item
// ────────────────────────────────────────────────────────────────

interface ConversationItemProps {
  conversation: Conversation;
  isActive: boolean;
  isConfirmingDelete: boolean;
  onSelect: () => void;
  onDelete: () => void;
  onConfirmDelete: () => void;
  onCancelDelete: () => void;
  dateLabel: string;
  iconOnly?: boolean;
}

function ConversationItem({
  conversation,
  isActive,
  isConfirmingDelete,
  onSelect,
  onDelete,
  onConfirmDelete,
  onCancelDelete,
  dateLabel,
  iconOnly = false,
}: ConversationItemProps) {
  // Icon-rail mode: show only a circular initial, no text or delete button.
  if (iconOnly) {
    const initial = (conversation.title || '?')[0].toUpperCase();
    return (
      <div
        className={`flex items-center justify-center py-2 cursor-pointer transition-colors ${
          isActive ? 'bg-blue-50' : 'hover:bg-gray-50'
        }`}
        onClick={onSelect}
        title={conversation.title}
        aria-label={conversation.title}
      >
        <div
          className={`w-8 h-8 rounded-full flex items-center justify-center text-xs font-semibold ${
            isActive
              ? 'bg-blue-600 text-white'
              : 'bg-gray-200 text-gray-600'
          }`}
        >
          {initial}
        </div>
      </div>
    );
  }

  return (
    <div
      className={`group px-3 py-3 cursor-pointer border-b border-gray-100 hover:bg-gray-50 transition-colors ${
        isActive ? 'bg-blue-50 border-l-2 border-l-blue-600' : ''
      }`}
      onClick={onSelect}
    >
      <div className="flex items-start justify-between">
        <div className="flex-1 min-w-0">
          <h3
            className={`text-sm font-medium truncate ${
              isActive ? 'text-blue-800' : 'text-gray-800'
            }`}
          >
            {conversation.title}
          </h3>
          <p className="text-xs text-gray-400 mt-0.5">{dateLabel}</p>
        </div>

        {/* Delete Button */}
        {isConfirmingDelete ? (
          <div className="flex items-center gap-1 ml-2" onClick={(e) => e.stopPropagation()}>
            <button
              onClick={onDelete}
              className="text-xs px-2 py-0.5 bg-red-500 text-white rounded hover:bg-red-600"
            >
              Delete
            </button>
            <button
              onClick={onCancelDelete}
              className="text-xs px-2 py-0.5 bg-gray-200 text-gray-600 rounded hover:bg-gray-300"
            >
              Cancel
            </button>
          </div>
        ) : (
          <button
            onClick={(e) => {
              e.stopPropagation();
              onConfirmDelete();
            }}
            className="opacity-0 group-hover:opacity-100 text-gray-400 hover:text-red-500 transition-opacity ml-2 p-1"
            aria-label={`Delete ${conversation.title}`}
          >
            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
            </svg>
          </button>
        )}
      </div>
    </div>
  );
}
