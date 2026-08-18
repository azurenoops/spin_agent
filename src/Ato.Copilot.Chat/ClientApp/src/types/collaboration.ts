// =============================================================================
// types/collaboration.ts — #1357 Workspace Collaboration & Document Sharing
//
// TypeScript types that mirror the C# CollaborationModels DTOs and
// the SignalR hub payloads from CollaborationHub.cs.
// =============================================================================

// ─── Permission tiers ────────────────────────────────────────────────────────

export type CollaboratorPermission = 'View' | 'Comment' | 'Edit';

// ─── REST: Collaborators ──────────────────────────────────────────────────────

export interface CollaboratorDto {
  id: string;
  email: string;
  displayName: string | null;
  permission: CollaboratorPermission;
  invitedAt: string; // ISO-8601
  accepted: boolean;
}

export interface InviteCollaboratorRequest {
  email: string;
  permission: CollaboratorPermission;
}

export interface UpdateCollaboratorRequest {
  permission: CollaboratorPermission;
}

// ─── REST: Link sharing ───────────────────────────────────────────────────────

export interface LinkSharingDto {
  linkPermission: CollaboratorPermission | null;
}

export interface SetLinkSharingRequest {
  linkPermission: CollaboratorPermission | null;
}

// ─── REST: Visibility badge ───────────────────────────────────────────────────

export interface VisibilityDto {
  badge: string; // "Private" | "Shared with N" | "Link sharing on"
  collaboratorCount: number;
  linkPermission: string | null;
}

// ─── REST: Comments ───────────────────────────────────────────────────────────

export interface CommentReplyDto {
  id: string;
  userId: string;
  displayName: string | null;
  text: string;
  createdAt: string;
}

export interface CommentThreadDto {
  id: string;
  documentId: string;
  anchorBlockId: string;
  anchorStart: number;
  anchorEnd: number;
  userId: string;
  displayName: string | null;
  text: string;
  createdAt: string;
  resolved: boolean;
  resolvedAt: string | null;
  replies: CommentReplyDto[];
}

export interface CreateCommentRequest {
  anchorBlockId: string;
  anchorStart: number;
  anchorEnd: number;
  text: string;
  userId: string;
  displayName?: string;
}

export interface ReplyToCommentRequest {
  text: string;
  userId: string;
  displayName?: string;
}

export interface ResolveCommentRequest {
  resolvedByUserId: string;
}

// ─── SignalR: Presence ────────────────────────────────────────────────────────

export type PresenceStatus = 'active' | 'idle' | 'left';

export interface CursorPositionPayload {
  blockId: string;
  offset: number;
  viewportY?: number;
}

export interface PresenceUpdatePayload {
  documentId: string;
  userId: string;
  displayName: string;
  color: string; // hex, e.g. "#3b82f6"
  status: PresenceStatus;
  cursor?: CursorPositionPayload | null;
}

// ─── SignalR: Section locks ───────────────────────────────────────────────────

export interface LockClaimPayload {
  documentId: string;
  blockId: string;
  userId: string;
  displayName: string;
  color: string;
}

export interface LockReleasePayload {
  documentId: string;
  blockId: string;
  userId: string;
}

export interface ConflictPayload {
  documentId: string;
  blockId: string;
  incumbentUserId: string;
  incumbentContent: string;
  challengerUserId: string;
  challengerContent: string;
}

// ─── In-memory presence state ────────────────────────────────────────────────

export interface PresenceEntry {
  userId: string;
  displayName: string;
  color: string;
  status: PresenceStatus;
  cursor?: CursorPositionPayload | null;
  lastSeen: number; // Date.now()
}

/** blockId → { userId, displayName, color } */
export type SectionLockMap = Map<string, { userId: string; displayName: string; color: string }>;
