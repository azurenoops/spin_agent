// =============================================================================
// CommentThread.tsx — #1357 Sub-task 4: Comment/Annotation System
//
// Renders a single comment thread with all replies and actions:
//   - Reply input
//   - Resolve / re-open toggle
//
// Props:
//   threadId    — the thread to render
//   localUserId — identity of the currently signed-in user
//   documentId  — used for REST calls
// =============================================================================

import React, { useState } from 'react';
import { useCollaboration } from '../../contexts/CollaborationContext';

interface CommentThreadProps {
  threadId: string;
  documentId: string;
  localUserId: string;
  localDisplayName?: string;
}

export default function CommentThread({
  threadId,
  documentId,
  localUserId,
  localDisplayName = 'You',
}: CommentThreadProps) {
  const { threads, addReply, resolveComment } = useCollaboration();
  const thread = threads.find((t) => t.id === threadId);

  const [replyText, setReplyText] = useState('');
  const [submitting, setSubmitting] = useState(false);

  if (!thread) return null;

  const handleReply = async () => {
    const text = replyText.trim();
    if (!text) return;
    setSubmitting(true);
    try {
      await addReply(documentId, threadId, {
        text,
        userId: localUserId,
        displayName: localDisplayName,
      });
      setReplyText('');
    } finally {
      setSubmitting(false);
    }
  };

  const handleResolve = async () => {
    await resolveComment(documentId, threadId, {
      resolvedByUserId: localUserId,
    });
  };

  return (
    <article
      aria-label={`Comment thread by ${thread.displayName ?? thread.userId}`}
      className={`rounded-lg border p-3 text-sm ${
        thread.resolved ? 'border-gray-200 bg-gray-50 opacity-60' : 'border-gray-200 bg-white'
      }`}
    >
      {/* Opening comment */}
      <div className="flex items-start gap-2 mb-2">
        <div
          className="w-7 h-7 rounded-full bg-blue-500 text-white text-[10px] font-bold
                     flex items-center justify-center flex-shrink-0"
          aria-hidden="true"
        >
          {(thread.displayName ?? thread.userId).slice(0, 2).toUpperCase()}
        </div>
        <div className="flex-1 min-w-0">
          <p className="font-semibold text-gray-800 text-xs truncate">
            {thread.displayName ?? thread.userId}
          </p>
          <p className="text-gray-700 mt-0.5 break-words">{thread.text}</p>
          <p className="text-gray-400 text-[10px] mt-1">
            {new Date(thread.createdAt).toLocaleString()}
          </p>
        </div>
      </div>

      {/* Replies */}
      {thread.replies.length > 0 && (
        <ul className="ml-9 space-y-2 mb-2" aria-label="Replies">
          {thread.replies.map((reply) => (
            <li key={reply.id} className="text-xs text-gray-700">
              <span className="font-semibold">{reply.displayName ?? reply.userId}: </span>
              {reply.text}
            </li>
          ))}
        </ul>
      )}

      {/* Actions */}
      {!thread.resolved && (
        <div className="ml-9 mt-2 flex flex-col gap-2">
          <div className="flex gap-1">
            <input
              type="text"
              value={replyText}
              onChange={(e) => setReplyText(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && !e.shiftKey && void handleReply()}
              placeholder="Reply…"
              aria-label="Write a reply"
              className="flex-1 border border-gray-200 rounded px-2 py-1 text-xs
                         focus:outline-none focus:ring-2 focus:ring-blue-300"
            />
            <button
              disabled={submitting || !replyText.trim()}
              onClick={handleReply}
              className="px-2 py-1 bg-blue-600 text-white rounded text-xs
                         disabled:opacity-40 hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-400"
            >
              {submitting ? '…' : 'Reply'}
            </button>
          </div>
          <button
            onClick={handleResolve}
            className="self-end text-[10px] text-gray-400 hover:text-green-600 underline"
          >
            Mark resolved
          </button>
        </div>
      )}

      {thread.resolved && (
        <p className="ml-9 text-[10px] text-gray-400 mt-1 italic">Resolved</p>
      )}
    </article>
  );
}
