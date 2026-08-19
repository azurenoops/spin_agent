// =============================================================================
// contexts/CollaborationContext.tsx — #1357 Workspace Collaboration
//
// Provides:
//  - SignalR connection to /hubs/collaboration
//  - Real-time presence map (userId → PresenceEntry)
//  - Section lock map (blockId → { userId, displayName, color })
//  - Comment threads (fetched from REST + updated via SignalR events)
//  - Collaborator list + link-sharing setting (fetched from REST)
//  - Actions: joinDocument, updateCursor, claimLock, releaseLock,
//             inviteCollaborator, removeCollaborator, setLinkSharing,
//             postComment, addReply, resolveComment
// =============================================================================

import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
} from 'react';
import * as signalR from '@microsoft/signalr';
import type {
  CollaboratorDto,
  CommentThreadDto,
  ConflictPayload,
  CreateCommentRequest,
  CursorPositionPayload,
  InviteCollaboratorRequest,
  LinkSharingDto,
  LockClaimPayload,
  LockReleasePayload,
  PresenceEntry,
  PresenceUpdatePayload,
  ReplyToCommentRequest,
  ResolveCommentRequest,
  SectionLockMap,
  SetLinkSharingRequest,
  VisibilityDto,
} from '../types/collaboration';

// ─── Context shape ────────────────────────────────────────────────────────────

export interface CollaborationContextValue {
  /** Whether the SignalR connection is live. */
  connected: boolean;

  /** userId → PresenceEntry for all active remote participants. */
  presence: Map<string, PresenceEntry>;

  /** blockId → lock holder for all currently locked sections. */
  sectionLocks: SectionLockMap;

  /** Open + resolved comment threads for the active document. */
  threads: CommentThreadDto[];

  /** Explicit collaborators invited by email. */
  collaborators: CollaboratorDto[];

  /** Current link-sharing setting for the active document. */
  linkSharing: LinkSharingDto;

  /** AppBar visibility badge string. */
  visibilityBadge: string;

  /** Active conflict waiting for user resolution, or null. */
  activeConflict: ConflictPayload | null;

  // ─── SignalR actions ──────────────────────────────────────────

  joinDocument: (documentId: string, userId: string, displayName: string) => Promise<void>;
  updateCursor: (documentId: string, cursor: CursorPositionPayload) => Promise<void>;
  claimLock: (payload: LockClaimPayload) => Promise<void>;
  releaseLock: (payload: LockReleasePayload) => Promise<void>;
  dismissConflict: () => void;

  // ─── REST actions ─────────────────────────────────────────────

  inviteCollaborator: (documentId: string, req: InviteCollaboratorRequest) => Promise<void>;
  removeCollaborator: (documentId: string, collaboratorId: string) => Promise<void>;
  setLinkSharing: (documentId: string, req: SetLinkSharingRequest) => Promise<void>;
  postComment: (documentId: string, req: CreateCommentRequest) => Promise<void>;
  addReply: (documentId: string, threadId: string, req: ReplyToCommentRequest) => Promise<void>;
  resolveComment: (documentId: string, threadId: string, req: ResolveCommentRequest) => Promise<void>;
  refreshComments: (documentId: string) => Promise<void>;
}

const CollaborationContext = createContext<CollaborationContextValue | null>(null);

// ─── Provider ─────────────────────────────────────────────────────────────────

export interface CollaborationProviderProps {
  children: React.ReactNode;
  /** Current conversation / document id. Null means no document is open. */
  documentId: string | null;
  /** Identity of the local user — sourced from auth context or a transient id. */
  userId: string;
  displayName: string;
}

export function CollaborationProvider({
  children,
  documentId,
  userId,
  displayName,
}: CollaborationProviderProps) {
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  const [connected, setConnected] = useState(false);
  const [presence, setPresence] = useState<Map<string, PresenceEntry>>(new Map());
  const [sectionLocks, setSectionLocks] = useState<SectionLockMap>(new Map());
  const [threads, setThreads] = useState<CommentThreadDto[]>([]);
  const [collaborators, setCollaborators] = useState<CollaboratorDto[]>([]);
  const [linkSharing, setLinkSharingState] = useState<LinkSharingDto>({ linkPermission: null });
  const [visibilityBadge, setVisibilityBadge] = useState('Private');
  const [activeConflict, setActiveConflict] = useState<ConflictPayload | null>(null);

  // ─── Build/tear-down SignalR connection ───────────────────────────────────

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/collaboration')
      .withAutomaticReconnect()
      .build();

    connectionRef.current = connection;

    // ── Event handlers ────────────────────────────────────────────────────────

    connection.on('UserJoined', (payload: PresenceUpdatePayload) => {
      setPresence((prev) => {
        const next = new Map(prev);
        next.set(payload.userId, {
          userId: payload.userId,
          displayName: payload.displayName,
          color: payload.color,
          status: 'active',
          cursor: payload.cursor,
          lastSeen: Date.now(),
        });
        return next;
      });
    });

    connection.on('UserLeft', (payload: PresenceUpdatePayload) => {
      setPresence((prev) => {
        const next = new Map(prev);
        next.delete(payload.userId);
        return next;
      });
    });

    connection.on('CursorMoved', (payload: PresenceUpdatePayload) => {
      setPresence((prev) => {
        const entry = prev.get(payload.userId);
        if (!entry) return prev;
        const next = new Map(prev);
        next.set(payload.userId, { ...entry, cursor: payload.cursor ?? null, lastSeen: Date.now() });
        return next;
      });
    });

    connection.on('BlockLocked', (payload: { blockId: string; userId: string; displayName: string; color: string }) => {
      setSectionLocks((prev) => {
        const next = new Map(prev);
        next.set(payload.blockId, {
          userId: payload.userId,
          displayName: payload.displayName,
          color: payload.color,
        });
        return next;
      });
    });

    connection.on('BlockUnlocked', (payload: { blockId: string }) => {
      setSectionLocks((prev) => {
        const next = new Map(prev);
        next.delete(payload.blockId);
        return next;
      });
    });

    connection.on('ConflictDetected', (payload: ConflictPayload) => {
      setActiveConflict(payload);
    });

    // ── Start ─────────────────────────────────────────────────────────────────

    connection.start()
      .then(() => {
        setConnected(true);
      })
      .catch((err) => {
        console.error('[CollaborationHub] connection failed', err);
      });

    connection.onreconnected(() => setConnected(true));
    connection.onclose(() => setConnected(false));

    return () => {
      connection.stop();
      connectionRef.current = null;
      setConnected(false);
    };
  }, []); // connection is stable for the lifetime of the provider

  // ─── Join document when documentId changes ────────────────────────────────

  useEffect(() => {
    const conn = connectionRef.current;
    if (!conn || !documentId || conn.state !== signalR.HubConnectionState.Connected) return;

    conn.invoke('JoinDocument', documentId, userId, displayName).catch((err) => {
      console.error('[CollaborationHub] JoinDocument failed', err);
    });

    return () => {
      conn.invoke('LeaveDocument', documentId, userId).catch(() => { /* ignore on unmount */ });
    };
  }, [documentId, userId, displayName]);

  // ─── Fetch REST data when documentId changes ──────────────────────────────

  const fetchCollaborators = useCallback(async (docId: string) => {
    try {
      const res = await fetch(`/api/documents/${docId}/collaborators`);
      if (res.ok) setCollaborators(await res.json());
    } catch { /* non-fatal */ }
  }, []);

  const fetchLinkSharing = useCallback(async (docId: string) => {
    try {
      const res = await fetch(`/api/documents/${docId}/link-sharing`);
      if (res.ok) setLinkSharingState(await res.json());
    } catch { /* non-fatal */ }
  }, []);

  const fetchVisibility = useCallback(async (docId: string) => {
    try {
      const res = await fetch(`/api/documents/${docId}/visibility`);
      if (res.ok) {
        const dto: VisibilityDto = await res.json();
        setVisibilityBadge(dto.badge);
      }
    } catch { /* non-fatal */ }
  }, []);

  const refreshComments = useCallback(async (docId: string) => {
    try {
      const res = await fetch(`/api/documents/${docId}/comments`);
      if (res.ok) setThreads(await res.json());
    } catch { /* non-fatal */ }
  }, []);

  useEffect(() => {
    if (!documentId) {
      setThreads([]);
      setCollaborators([]);
      setLinkSharingState({ linkPermission: null });
      setVisibilityBadge('Private');
      setPresence(new Map());
      setSectionLocks(new Map());
      return;
    }
    void fetchCollaborators(documentId);
    void fetchLinkSharing(documentId);
    void fetchVisibility(documentId);
    void refreshComments(documentId);
  }, [documentId, fetchCollaborators, fetchLinkSharing, fetchVisibility, refreshComments]);

  // ─── SignalR actions ──────────────────────────────────────────────────────

  const joinDocument = useCallback(async (docId: string, uid: string, name: string) => {
    await connectionRef.current?.invoke('JoinDocument', docId, uid, name);
  }, []);

  const updateCursor = useCallback(async (docId: string, cursor: CursorPositionPayload) => {
    await connectionRef.current?.invoke('UpdateCursor', docId, cursor);
  }, []);

  const claimLock = useCallback(async (payload: LockClaimPayload) => {
    await connectionRef.current?.invoke('ClaimLock', payload);
  }, []);

  const releaseLock = useCallback(async (payload: LockReleasePayload) => {
    await connectionRef.current?.invoke('ReleaseLock', payload);
  }, []);

  const dismissConflict = useCallback(() => setActiveConflict(null), []);

  // ─── REST actions ─────────────────────────────────────────────────────────

  const inviteCollaborator = useCallback(async (docId: string, req: InviteCollaboratorRequest) => {
    await fetch(`/api/documents/${docId}/collaborators`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req),
    });
    await fetchCollaborators(docId);
    await fetchVisibility(docId);
  }, [fetchCollaborators, fetchVisibility]);

  const removeCollaborator = useCallback(async (docId: string, collaboratorId: string) => {
    await fetch(`/api/documents/${docId}/collaborators/${collaboratorId}`, { method: 'DELETE' });
    await fetchCollaborators(docId);
    await fetchVisibility(docId);
  }, [fetchCollaborators, fetchVisibility]);

  const setLinkSharing = useCallback(async (docId: string, req: SetLinkSharingRequest) => {
    await fetch(`/api/documents/${docId}/link-sharing`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req),
    });
    await fetchLinkSharing(docId);
    await fetchVisibility(docId);
  }, [fetchLinkSharing, fetchVisibility]);

  const postComment = useCallback(async (docId: string, req: CreateCommentRequest) => {
    await fetch(`/api/documents/${docId}/comments`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req),
    });
    await refreshComments(docId);
  }, [refreshComments]);

  const addReply = useCallback(async (docId: string, threadId: string, req: ReplyToCommentRequest) => {
    await fetch(`/api/documents/${docId}/comments/${threadId}/replies`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req),
    });
    await refreshComments(docId);
  }, [refreshComments]);

  const resolveComment = useCallback(async (docId: string, threadId: string, req: ResolveCommentRequest) => {
    await fetch(`/api/documents/${docId}/comments/${threadId}/resolve`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req),
    });
    await refreshComments(docId);
  }, [refreshComments]);

  // ─── Value ────────────────────────────────────────────────────────────────

  const value: CollaborationContextValue = {
    connected,
    presence,
    sectionLocks,
    threads,
    collaborators,
    linkSharing,
    visibilityBadge,
    activeConflict,
    joinDocument,
    updateCursor,
    claimLock,
    releaseLock,
    dismissConflict,
    inviteCollaborator,
    removeCollaborator,
    setLinkSharing,
    postComment,
    addReply,
    resolveComment,
    refreshComments,
  };

  return (
    <CollaborationContext.Provider value={value}>
      {children}
    </CollaborationContext.Provider>
  );
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useCollaboration(): CollaborationContextValue {
  const ctx = useContext(CollaborationContext);
  if (!ctx) {
    throw new Error('useCollaboration must be used inside <CollaborationProvider>');
  }
  return ctx;
}
