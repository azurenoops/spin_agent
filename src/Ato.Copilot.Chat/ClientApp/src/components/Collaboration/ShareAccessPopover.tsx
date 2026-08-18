// =============================================================================
// ShareAccessPopover.tsx — #1357 Sub-task 1: Share & Access Panel
//
// A Radix Popover that lets the document owner:
//   - View and remove current collaborators
//   - Invite a new collaborator by email with a permission picker
//   - Toggle link sharing on/off with a permission tier
//
// Trigger: the "Share" button in Header.tsx
//
// Props:
//   documentId  — the conversation/document being shared
//   localUserId — identity of the current user (for X-User-Id header)
//   children    — the trigger element (the Share button)
// =============================================================================

import React, { useState } from 'react';
import * as Popover from '@radix-ui/react-popover';
import { useCollaboration } from '../../contexts/CollaborationContext';
import type { CollaboratorPermission } from '../../types/collaboration';

const PERMISSION_LABELS: Record<CollaboratorPermission, string> = {
  View: 'Can view',
  Comment: 'Can comment',
  Edit: 'Can edit',
};

interface ShareAccessPopoverProps {
  documentId: string;
  localUserId: string;
  children: React.ReactNode;
}

export default function ShareAccessPopover({
  documentId,
  localUserId,
  children,
}: ShareAccessPopoverProps) {
  const {
    collaborators,
    linkSharing,
    visibilityBadge,
    inviteCollaborator,
    removeCollaborator,
    setLinkSharing,
  } = useCollaboration();

  const [email, setEmail] = useState('');
  const [permission, setPermission] = useState<CollaboratorPermission>('View');
  const [inviting, setInviting] = useState(false);
  const [inviteError, setInviteError] = useState('');

  const handleInvite = async () => {
    const trimmed = email.trim();
    if (!trimmed) return;
    setInviting(true);
    setInviteError('');
    try {
      await inviteCollaborator(documentId, { email: trimmed, permission });
      setEmail('');
    } catch {
      setInviteError('Could not send invite. Please try again.');
    } finally {
      setInviting(false);
    }
  };

  const toggleLinkSharing = async () => {
    const current = linkSharing.linkPermission;
    await setLinkSharing(documentId, {
      linkPermission: current ? null : 'View',
    });
  };

  return (
    <Popover.Root>
      <Popover.Trigger asChild>{children}</Popover.Trigger>

      <Popover.Portal>
        <Popover.Content
          className="z-50 bg-white rounded-xl shadow-2xl border border-gray-200
                     p-4 w-80 text-sm"
          sideOffset={8}
          align="end"
        >
          {/* Header */}
          <div className="flex items-center justify-between mb-3">
            <h2 className="font-semibold text-gray-800">Share document</h2>
            <span className="text-xs text-gray-400 bg-gray-100 px-2 py-0.5 rounded-full">
              {visibilityBadge}
            </span>
          </div>

          {/* Invite form */}
          <div className="flex gap-1 mb-3">
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && void handleInvite()}
              placeholder="Email address"
              aria-label="Invite by email"
              className="flex-1 border border-gray-200 rounded px-2 py-1.5 text-xs
                         focus:outline-none focus:ring-2 focus:ring-blue-300"
            />
            <select
              value={permission}
              onChange={(e) => setPermission(e.target.value as CollaboratorPermission)}
              aria-label="Permission level"
              className="border border-gray-200 rounded px-1 py-1.5 text-xs
                         focus:outline-none focus:ring-2 focus:ring-blue-300 bg-white"
            >
              {(Object.keys(PERMISSION_LABELS) as CollaboratorPermission[]).map((p) => (
                <option key={p} value={p}>{PERMISSION_LABELS[p]}</option>
              ))}
            </select>
            <button
              disabled={inviting || !email.trim()}
              onClick={handleInvite}
              className="px-2 py-1 bg-blue-600 text-white rounded text-xs
                         disabled:opacity-40 hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-400"
            >
              {inviting ? '…' : 'Invite'}
            </button>
          </div>
          {inviteError && (
            <p role="alert" className="text-red-600 text-xs mb-2">{inviteError}</p>
          )}

          {/* Collaborator list */}
          {collaborators.length > 0 && (
            <ul className="mb-3 space-y-1.5" aria-label="Current collaborators">
              {collaborators.map((c) => (
                <li key={c.id} className="flex items-center justify-between gap-2">
                  <div className="min-w-0">
                    <p className="text-xs font-medium text-gray-700 truncate">
                      {c.displayName ?? c.email}
                    </p>
                    {c.displayName && (
                      <p className="text-[10px] text-gray-400 truncate">{c.email}</p>
                    )}
                  </div>
                  <div className="flex items-center gap-1 flex-shrink-0">
                    <span className="text-[10px] text-gray-400">
                      {PERMISSION_LABELS[c.permission]}
                    </span>
                    <button
                      aria-label={`Remove ${c.email}`}
                      onClick={() => removeCollaborator(documentId, c.id)}
                      className="text-gray-300 hover:text-red-500 focus:outline-none p-0.5 rounded"
                    >
                      <svg className="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                          d="M6 18L18 6M6 6l12 12" />
                      </svg>
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          )}

          {/* Link sharing */}
          <div className="border-t border-gray-100 pt-3">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-xs font-medium text-gray-700">Link sharing</p>
                <p className="text-[10px] text-gray-400">
                  {linkSharing.linkPermission
                    ? `Anyone with the link — ${PERMISSION_LABELS[linkSharing.linkPermission]}`
                    : 'Off — only invited people can access'}
                </p>
              </div>
              <button
                role="switch"
                aria-checked={linkSharing.linkPermission !== null}
                onClick={toggleLinkSharing}
                className={`relative inline-flex h-5 w-9 items-center rounded-full
                  transition-colors focus:outline-none focus:ring-2 focus:ring-blue-400
                  ${linkSharing.linkPermission ? 'bg-blue-600' : 'bg-gray-300'}`}
              >
                <span className="sr-only">Toggle link sharing</span>
                <span
                  className={`inline-block h-3.5 w-3.5 rounded-full bg-white shadow
                    transition-transform
                    ${linkSharing.linkPermission ? 'translate-x-4' : 'translate-x-0.5'}`}
                />
              </button>
            </div>
          </div>

          <Popover.Arrow className="fill-white drop-shadow" />
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );
}
