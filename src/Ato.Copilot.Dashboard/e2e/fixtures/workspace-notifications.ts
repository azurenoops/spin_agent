import type { BrowserContext, Page } from '@playwright/test';
import { installWorkspaceFixture, workspaceFixtureActor } from './workspace-shell';

export const notificationRoot = '/api/dashboard/notifications';
export const notificationIds = {
  'org-a': ['aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa2'],
  'org-b': ['bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb1', 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2'],
} as const;
type Organization = keyof typeof notificationIds;

interface SyntheticNotification {
  id: string;
  alertId: string;
  channel: string;
  subject: string;
  body: string | null;
  isRead: boolean;
  readAt: string | null;
  sentAt: string;
  alertTitle: string | null;
  alertSeverity: string | null;
}
interface NotificationRequest {
  tab: number;
  path: string;
  method: string;
  kind?: string;
  tenant?: string;
  mode?: string;
  queryKeys: string[];
  hasBearer: boolean;
  cookieSession: boolean;
  body: unknown;
}

function notices(organization: Organization): SyntheticNotification[] {
  const name = organization === 'org-a' ? 'Organization A' : 'Organization B';
  return notificationIds[organization].map((id, index) => ({
    id, alertId: id, channel: 'InApp', subject: `${name} ${index === 0 ? 'initial notice' : 'second notice'}`,
    body: null, isRead: false, readAt: null, sentAt: '2026-09-21T12:00:00Z',
    alertTitle: null, alertSeverity: 'Low',
  }));
}
function idsFrom(body: unknown): string[] | null {
  if (!body || typeof body !== 'object' || !('notificationIds' in body)
    || !Array.isArray(body.notificationIds) || !body.notificationIds.every(id => typeof id === 'string')) return null;
  return body.notificationIds;
}

/**
 * Stateful, synthetic actor/workspace responses for the real SPA. A synthetic
 * HttpOnly cookie verifies browser cookie transport, not server authentication.
 * No MSAL account, bearer token or authentication bypass is injected into the app.
 */
export async function installNotificationFixture(context: BrowserContext, baseURL: string, providerOnly = false) {
  const workspaceRequests = await installWorkspaceFixture(context, baseURL, { multiple: !providerOnly, providerOnly });
  await context.addCookies([{
    name: 'ato-simulation', value: 'synthetic-notification-cookie', url: baseURL,
    httpOnly: true, sameSite: 'Lax',
  }]);
  const requests: NotificationRequest[] = [];
  const hubRequests: string[] = [];
  const data: Record<Organization, SyntheticNotification[]> = { 'org-a': notices('org-a'), 'org-b': notices('org-b') };
  const tabs = new WeakMap<Page, number>();
  let nextTab = 1;
  let listError = false;
  let markError = false;
  let recipient = workspaceFixtureActor;
  const tabId = (page: Page) => {
    let id = tabs.get(page);
    if (id === undefined) { id = nextTab++; tabs.set(page, id); }
    return id;
  };
  await context.route(/^https?:\/\/[^/]+\/hubs\/notifications(?:[/?]|$)/, async route => {
    hubRequests.push(route.request().url());
    await route.fulfill({ status: 503, json: { error: 'Realtime is intentionally unavailable in this cookie fixture.' } });
  });
  await context.route(/^https?:\/\/[^/]+\/api\/dashboard\/notifications(?:[/?]|$)/, async route => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname.replace(/\/$/, '');
    const headers = await request.allHeaders();
    const cookieSession = headers.cookie?.includes('ato-simulation=synthetic-notification-cookie') === true;
    const body: unknown = request.postData() === null ? null : request.postDataJSON();
    requests.push({
      tab: tabId(request.frame().page()), path, method: request.method(),
      kind: headers['x-workspace-kind'], tenant: headers['x-workspace-tenant-id'], mode: headers['x-workspace-mode'],
      queryKeys: [...url.searchParams.keys()], hasBearer: Boolean(headers.authorization), cookieSession, body,
    });
    const deny = (status: number, message: string) => route.fulfill({
      status, json: { status: 'error', error: { errorCode: 'SYNTHETIC_NOTIFICATION_DENIED', message } },
    });
    if (!cookieSession) return deny(401, 'Synthetic cookie session is missing.');
    if ([...url.searchParams.keys()].some(key => key.toLowerCase() === 'userid')) {
      return deny(403, 'The client must not select a notification recipient.');
    }
    if (path.endsWith('/capabilities')) {
      const organization = headers['x-workspace-kind'] === 'organization';
      return route.fulfill({ json: {
        recipientId: recipient,
        rest: { available: organization, reasonCode: organization ? null : 'ORGANIZATION_WORKSPACE_REQUIRED' },
        realtime: { available: false, authentication: 'bearer', cookieSessionSupported: false,
          reasonCode: 'REALTIME_BEARER_REQUIRED', hubPaths: ['/hubs/notifications', '/hubs/package', '/hubs/import-progress'] },
        fallback: { transport: organization ? 'rest-polling' : 'none', pollIntervalSeconds: organization ? 30 : null },
      } });
    }
    const tenant = headers['x-workspace-tenant-id'];
    if ((tenant !== 'org-a' && tenant !== 'org-b') || headers['x-workspace-kind'] !== 'organization'
      || headers['x-workspace-mode'] !== 'ordinary') return deny(403, 'An authorized ordinary organization selector is required.');
    const items = data[tenant];
    if (request.method() === 'GET' && path === notificationRoot) {
      if (listError) return deny(503, 'Notification list temporarily unavailable.');
      return route.fulfill({ json: { items, totalCount: items.length } });
    }
    if (request.method() === 'GET' && path.endsWith('/summary')) {
      return route.fulfill({ json: { unreadCount: items.filter(item => !item.isRead).length, totalCount: items.length } });
    }
    if (request.method() === 'POST' && (path.endsWith('/mark-read') || path.endsWith('/mark-all-read'))) {
      if (markError) return deny(403, 'Notification read was denied.');
      const ids = path.endsWith('/mark-all-read') ? items.map(item => item.id) : idsFrom(body);
      if (!ids?.length || ids.some(id => !items.some(item => item.id === id))) return deny(404, 'Notification is not visible in this workspace.');
      let markedCount = 0;
      for (const item of items) {
        if (ids.includes(item.id) && !item.isRead) {
          item.isRead = true;
          item.readAt = '2026-09-21T12:00:31Z';
          markedCount++;
        }
      }
      return route.fulfill({ json: { markedCount } });
    }
    return route.fulfill({ status: 404, json: { error: 'Outside the synthetic notification contract.' } });
  });
  return {
    requests, hubRequests, workspaceRequests, tabId,
    setFirstSubject: (tenant: Organization, subject: string) => { data[tenant][0]!.subject = subject; },
    setListError: (value: boolean) => { listError = value; },
    setMarkError: (value: boolean) => { markError = value; },
    setRecipient: (value: string) => { recipient = value; },
  };
}
