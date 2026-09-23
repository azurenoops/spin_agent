import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { useMe } from '../features/auth/useMe';
import { acquireBearer, getMsalInstance } from '../features/auth/msalInstance';
import { msalAccountKey } from '../features/auth/accountSelection';
import { captureWorkspaceSnapshot, isWorkspaceSnapshotCurrent } from '../features/workspaces/workspaceTransport';
import { workspaceHubUrl } from '../features/workspaces/workspaceHubUrl';
import { getNotificationCapabilities, type NotificationCapabilities } from '../features/notifications/capabilities';

export interface ProgressRequest {
  signal: AbortSignal;
  isCurrent: () => boolean;
  cancel: () => void;
  complete: () => void;
}

export interface ProgressSession {
  key: string;
  actorId: string | null;
  ready: boolean;
  error: string | null;
  isCurrent: () => boolean;
  request: () => ProgressRequest;
}

export function progressError(reason: unknown): string {
  if (reason instanceof Error) return reason.message;
  if (reason && typeof reason === 'object' && 'error' in reason) {
    if (typeof reason.error === 'string') return reason.error;
    if (reason.error && typeof reason.error === 'object' && 'message' in reason.error
      && typeof reason.error.message === 'string') return reason.error.message;
  }
  return 'Unable to retrieve progress. Retry in the authorized workspace.';
}

export function isProgressEvent(
  payload: unknown,
  field: 'packageId' | 'exportId' | 'jobId',
  jobId: string | null,
): payload is Record<string, unknown> {
  return jobId !== null && payload !== null && typeof payload === 'object' && !Array.isArray(payload)
    && field in payload && Reflect.get(payload, field) === jobId;
}

export function useProgressSession(systemId: string): ProgressSession {
  const { data: identity, isLoading, error } = useMe();
  const [, accountChanged] = useState(0);
  const accountKey = msalAccountKey(getMsalInstance());
  const snapshot = captureWorkspaceSnapshot();
  const actorId = !isLoading && !error && identity?.oid ? identity.oid : null;
  const key = JSON.stringify([
    systemId, actorId, identity?.directoryTenantId, identity?.workspace?.kind, identity?.workspace?.tenantId,
    identity?.workspace?.mode, identity?.workspace?.personId, identity?.effectiveTenant?.id,
    identity?.homeTenant?.id, snapshot.key, accountKey,
  ]);
  const currentKey = useRef(key);
  currentKey.current = key;
  const alive = useRef(true);
  const requests = useMemo(() => new Set<AbortController>(), [key]);

  useLayoutEffect(() => {
    alive.current = true;
    return () => {
      alive.current = false;
      for (const controller of requests) controller.abort();
      requests.clear();
    };
  }, [requests]);
  useEffect(() => {
    const instance = getMsalInstance();
    if (!instance) return;
    let active = true;
    let observed = msalAccountKey(instance);
    const subscription = instance.addEventCallback(() => {
      if (!active) return;
      const next = msalAccountKey(instance);
      if (next !== observed) {
        observed = next;
        accountChanged(revision => revision + 1);
      }
    });
    return () => {
      active = false;
      if (subscription) instance.removeEventCallback(subscription);
    };
  }, []);

  const isCurrent = () => alive.current && currentKey.current === key
    && isWorkspaceSnapshotCurrent(snapshot) && msalAccountKey(getMsalInstance()) === accountKey;
  return {
    key, actorId, ready: Boolean(actorId),
    error: error ? progressError(error) : actorId ? null
      : isLoading ? 'Waiting for authenticated workspace context.' : 'Authenticated workspace context is required.',
    isCurrent,
    request: () => {
      if (!actorId || !isCurrent()) throw new Error('Authenticated workspace context changed. Retry in the current workspace.');
      const controller = new AbortController();
      requests.add(controller);
      return {
        signal: controller.signal,
        isCurrent: () => !controller.signal.aborted && isCurrent(),
        cancel: () => { controller.abort(); },
        complete: () => { requests.delete(controller); },
      };
    },
  };
}

interface JobProgressOptions<T> {
  session: ProgressSession;
  jobId: string | null;
  hubPath: string;
  pollIntervalMs?: number;
  events: readonly string[];
  matchesEvent: (payload: unknown) => boolean;
  subscribe: (connection: signalR.HubConnection, capabilities: NotificationCapabilities) => Promise<void>;
  poll: (signal: AbortSignal) => Promise<T>;
  isTerminal: (status: T) => boolean;
  onStatus: (status: T, terminal: boolean) => void;
  onEvent?: (name: string, payload: unknown) => T | undefined;
}

/** Capabilities gate SignalR; every job status endpoint independently authorizes its REST request. */
export function useJobProgress<T>(options: JobProgressOptions<T>) {
  const { session, jobId, hubPath } = options;
  const pollMs = options.pollIntervalMs ?? 5000;
  const key = `${session.key}:${jobId ?? ''}:${hubPath}`;
  const latest = useRef(options);
  latest.current = options;
  const [revision, setRevision] = useState(0);
  const effectKey = `${key}:${revision}:${pollMs}`;
  const currentEffect = useRef(effectKey);
  currentEffect.current = effectKey;
  const completedKey = useRef<string | null>(null);
  const alive = useRef(true);
  useLayoutEffect(() => {
    alive.current = true;
    return () => { alive.current = false; };
  }, []);
  const [state, setState] = useState<{ key: string; transportMessage: string | null; error: string | null }>({
    key, transportMessage: null, error: null,
  });

  useEffect(() => {
    const controller = new AbortController();
    let connection: signalR.HubConnection | null = null;
    let starting: Promise<void> = Promise.resolve();
    let timer: ReturnType<typeof setTimeout> | undefined;
    let inFlight: Promise<void> | null = null;
    let finished = completedKey.current === key;
    let connectionFailed = false;
    let capabilityNotice = '';
    const current = () => alive.current && currentEffect.current === effectKey
      && !controller.signal.aborted && session.isCurrent();
    const update = (change: Partial<typeof state>) => {
      if (current()) setState(previous => ({ ...previous, ...change, key }));
    };
    const stopConnection = () => {
      const previous = connection;
      connection = null;
      if (previous) void starting.then(() => previous.stop()).catch(() => {
        if (current()) update({ error: 'Unable to stop the previous progress connection.' });
        else console.error('[JobProgress] Unable to stop a disposed progress connection.');
      });
    };
    const publish = (status: T) => {
      if (!current() || finished) return;
      const terminal = latest.current.isTerminal(status);
      finished = terminal;
      if (terminal) completedKey.current = key;
      latest.current.onStatus(status, terminal);
      if (terminal) {
        if (timer) clearTimeout(timer);
        update({ error: null, transportMessage: `${capabilityNotice ? `${capabilityNotice}. ` : ''}Job finished; progress polling stopped.` });
        stopConnection();
      }
    };
    const validateRecipient = (capabilities: NotificationCapabilities) => {
      if (capabilities.recipientId.toLowerCase() !== session.actorId?.toLowerCase()) {
        throw new Error('Progress capability identity does not match the authenticated workspace identity.');
      }
    };
    const supported = (capabilities: NotificationCapabilities) =>
      capabilities.realtime.available === true && capabilities.realtime.hubPaths.includes(hubPath);
    const applyCapabilities = (capabilities: NotificationCapabilities) => {
      if (!supported(capabilities)) {
        connectionFailed = false;
        stopConnection();
        const reason = capabilities.realtime.available
          ? 'This progress hub is not supported by the server'
          : `Real-time progress unavailable (${capabilities.realtime.reasonCode ?? 'unsupported authentication'})`;
        capabilityNotice = reason;
        update({ transportMessage: `${reason}. Checking authorized job status by polling every ${pollMs / 1000} seconds.` });
      } else {
        capabilityNotice = '';
      }
    };
    const connect = (capabilities: NotificationCapabilities) => {
      if (connection || connectionFailed || finished || !current() || !supported(capabilities)) return;
      const baseUrl = (import.meta.env.VITE_API_BASE_URL || '').replace(/\/api\/dashboard\/?$/, '');
      const hub = new signalR.HubConnectionBuilder()
        .withUrl(workspaceHubUrl(`${baseUrl}${hubPath}`), {
          accessTokenFactory: async () => {
            if (!current()) throw new Error('Progress workspace or account changed.');
            const token = await acquireBearer();
            if (!current()) throw new Error('Progress workspace or account changed during token acquisition.');
            if (!token) throw new Error('A compatible bearer token is unavailable for real-time progress.');
            return token;
          },
        })
        .withAutomaticReconnect()
        .build();
      connection = hub;
      for (const name of latest.current.events) {
        hub.on(name, (payload: unknown) => {
          if (!current() || finished || connection !== hub || !latest.current.matchesEvent(payload)) return;
          try {
            const status = latest.current.onEvent?.(name, payload);
            if (status !== undefined) publish(status);
            else void refresh();
          } catch (reason) {
            update({ error: progressError(reason) });
          }
        });
      }
      hub.onreconnecting(() => {
        if (connection === hub) update({ transportMessage: 'Real-time progress reconnecting. Authorized status polling continues.' });
      });
      hub.onreconnected(async () => {
        if (!current() || finished || connection !== hub) return;
        try {
          const capabilities = await getNotificationCapabilities(controller.signal);
          if (!current() || finished || connection !== hub) return;
          validateRecipient(capabilities);
          applyCapabilities(capabilities);
          if (!supported(capabilities) || connection !== hub) return;
          await latest.current.subscribe(hub, capabilities);
          if (current() && connection === hub) update({ transportMessage: 'Real-time progress connected. Authorized status polling remains available.' });
        } catch (reason) {
          update({ error: progressError(reason) });
          connectionFailed = true;
          stopConnection();
        }
      });
      hub.onclose(() => {
        if (connection !== hub) return;
        connection = null;
        connectionFailed = true;
        update({ transportMessage: 'Real-time progress disconnected. Authorized status polling continues.' });
      });
      starting = hub.start().then(async () => {
        if (!current() || finished || connection !== hub) return;
        await latest.current.subscribe(hub, capabilities);
        if (current() && connection === hub) update({ transportMessage: 'Real-time progress connected. Authorized status polling remains available.' });
      }).catch(reason => {
        if (!current() || finished) return;
        connectionFailed = true;
        update({ error: progressError(reason), transportMessage: 'Real-time progress unavailable. Authorized status polling continues.' });
        stopConnection();
      });
    };
    const refresh = (): Promise<void> => {
      if (!current() || !session.ready || !jobId || finished) return Promise.resolve();
      if (inFlight) return inFlight;
      if (timer) clearTimeout(timer);
      inFlight = (async () => {
        try {
          const capabilities = await getNotificationCapabilities(controller.signal);
          if (!current() || finished) return;
          validateRecipient(capabilities);
          applyCapabilities(capabilities);
          connect(capabilities);
          const status = await latest.current.poll(controller.signal);
          publish(status);
          if (current() && !finished) timer = setTimeout(() => { void refresh(); }, pollMs);
        } catch (reason) {
          if (current()) {
            update({ error: progressError(reason) });
            stopConnection();
          }
        } finally {
          inFlight = null;
        }
      })();
      return inFlight;
    };
    setState({ key, transportMessage: session.error, error: null });
    if (session.ready && jobId) void refresh();
    return () => {
      controller.abort();
      if (timer) clearTimeout(timer);
      stopConnection();
    };
  }, [key, revision, session.ready, pollMs]);

  const visible = state.key === key ? state : { transportMessage: session.error, error: null };
  return {
    ...visible,
    retry: () => {
      if (alive.current && currentEffect.current === effectKey && completedKey.current !== key) {
        setRevision(value => value + 1);
      }
    },
  };
}
