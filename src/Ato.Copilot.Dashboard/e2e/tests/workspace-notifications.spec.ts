import { expect, test, type Page } from '@playwright/test';
import { installNotificationFixture, notificationIds, notificationRoot } from '../fixtures/workspace-notifications';
import { switchWorkspace } from '../fixtures/workspace-shell';

const workspace = (tenant: string) => `/workspaces/organizations/${tenant}`;
const notificationPanel = (page: Page) => page.locator('div.absolute')
  .filter({ has: page.getByRole('heading', { name: 'Notifications', exact: true }) })
  .filter({ has: page.getByRole('button', { name: 'Close notifications', exact: true }) });
async function freezeClock(page: Page) {
  await page.clock.install({ time: new Date('2026-09-21T12:00:00Z') });
  await page.clock.pauseAt(new Date('2026-09-21T12:00:01Z'));
}
async function openNotifications(page: Page) {
  await page.getByRole('button', { name: 'Notifications', exact: true }).click();
  const panel = notificationPanel(page);
  await expect(panel).toHaveCount(1);
  await expect(panel.getByRole('heading', { name: 'Notifications', exact: true })).toBeVisible();
  const viewport = page.viewportSize();
  const bounds = await panel.boundingBox();
  if (!viewport || !bounds) throw new Error('Notification panel viewport bounds are unavailable.');
  await test.info().attach('notification-panel-bounds', {
    body: JSON.stringify({ viewport, panel: bounds }),
    contentType: 'application/json',
  });
  expect(bounds.x).toBeGreaterThanOrEqual(0);
  expect(bounds.x + bounds.width).toBeLessThanOrEqual(viewport.width);
  await expect(panel.getByRole('button', { name: 'Close notifications', exact: true })).toBeInViewport({ ratio: 1 });
}
const notificationStatus = (page: Page) => notificationPanel(page).getByRole('status');
async function clickPanelAction(page: Page, name: string) {
  const action = notificationPanel(page).getByRole('button', { name, exact: true });
  await expect(action).toBeInViewport({ ratio: 1 });
  await action.click();
}
async function markFirstRead(page: Page) {
  const mark = notificationPanel(page).getByRole('button', { name: 'Mark read', exact: true }).first();
  await test.info().attach('notification-mark-read-bounds', {
    body: JSON.stringify({ viewport: page.viewportSize(), button: await mark.boundingBox() }),
    contentType: 'application/json',
  });
  await expect(mark).toBeInViewport({ ratio: 1 });
  await mark.click();
}
function assertOrdinaryRequests(fixture: Awaited<ReturnType<typeof installNotificationFixture>>) {
  expect(fixture.requests.length).toBeGreaterThan(0);
  for (const request of fixture.requests) {
    expect(request.queryKeys.some(key => key.toLowerCase() === 'userid')).toBe(false);
    expect(request.hasBearer).toBe(false);
    expect(request.cookieSession).toBe(true);
    expect(request.kind).toBe('organization');
    expect(request.mode).toBe('ordinary');
    expect(['org-a', 'org-b']).toContain(request.tenant);
  }
  expect(fixture.hubRequests).toEqual([]);
}

for (const width of [1440, 390, 320]) {
  test(`cookie-only notification panel polls at 30 seconds and sends authorized read selectors at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const fixture = await installNotificationFixture(context, baseURL!);
    const tokenRequests: string[] = [];
    const sockets: string[] = [];
    page.on('request', request => { if (/\/oauth2\/.*token/.test(request.url())) tokenRequests.push(request.url()); });
    page.on('websocket', socket => { if (socket.url().includes('/hubs/notifications')) sockets.push(socket.url()); });
    await freezeClock(page);
    await page.goto(workspace('org-a'));
    await openNotifications(page);
    await expect(notificationPanel(page).getByText('Organization A initial notice', { exact: true })).toBeVisible();
    await expect(notificationStatus(page)).toContainText('REALTIME_BEARER_REQUIRED');
    await expect(notificationStatus(page)).toContainText('every 30 seconds');
    const count = () => fixture.requests.filter(request => request.path === notificationRoot).length;
    const initialReads = count();
    expect(initialReads).toBeGreaterThan(0);
    expect(fixture.requests[0]?.path).toBe(`${notificationRoot}/capabilities`);
    fixture.setFirstSubject('org-a', 'Organization A polled notice');

    // Act
    await page.clock.runFor(29_999);
    // Assert
    expect(count()).toBe(initialReads);
    await expect(notificationPanel(page).getByText('Organization A initial notice', { exact: true })).toBeVisible();
    await page.clock.runFor(1);
    await expect(notificationPanel(page).getByText('Organization A polled notice', { exact: true })).toBeVisible();
    expect(count()).toBeGreaterThan(initialReads);
    assertOrdinaryRequests(fixture);
    expect(tokenRequests).toEqual([]);
    expect(sockets).toEqual([]);

    // Act
    await markFirstRead(page);
    await expect(notificationPanel(page).getByRole('button', { name: 'Mark read', exact: true })).toHaveCount(1);
    await clickPanelAction(page, 'Mark all read');
    // Assert
    await expect(notificationPanel(page).getByRole('button', { name: 'Mark read', exact: true })).toHaveCount(0);
    const marks = fixture.requests.filter(request => request.method === 'POST');
    expect(marks).toHaveLength(2);
    expect(marks[0]).toMatchObject({
      path: `${notificationRoot}/mark-read`, tenant: 'org-a', mode: 'ordinary',
      body: { notificationIds: [notificationIds['org-a'][0]] },
    });
    expect(marks[1]).toMatchObject({ path: `${notificationRoot}/mark-all-read`, tenant: 'org-a', body: null });
    assertOrdinaryRequests(fixture);
    expect(tokenRequests).toEqual([]);
    expect(sockets).toEqual([]);
  });

  test(`cookie notification tabs retain independent workspace data through switching and history at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const fixture = await installNotificationFixture(context, baseURL!);
    await freezeClock(page);
    const other = await context.newPage();
    await other.setViewportSize({ width, height: 1000 });
    await freezeClock(other);
    await page.goto(workspace('org-a'));
    await other.goto(workspace('org-b'));
    await openNotifications(page);
    await openNotifications(other);
    await expect(notificationPanel(page).getByText('Organization A initial notice', { exact: true })).toBeVisible();
    await expect(notificationPanel(other).getByText('Organization B initial notice', { exact: true })).toBeVisible();

    // Act
    fixture.setFirstSubject('org-a', 'Organization A independent update');
    await page.clock.runFor(30_000);
    await expect(notificationPanel(page).getByText('Organization A independent update', { exact: true })).toBeVisible();
    await expect(notificationPanel(other).getByText('Organization B initial notice', { exact: true })).toBeVisible();
    await switchWorkspace(page, 'Organization B');
    await expect(page).toHaveURL(`${baseURL}${workspace('org-b')}`);
    await openNotifications(page);
    // Assert
    await expect(notificationPanel(page).getByText('Organization B initial notice', { exact: true })).toBeVisible();
    await expect(notificationPanel(page).getByText('Organization A independent update', { exact: true })).toHaveCount(0);
    await expect(notificationPanel(page).getByRole('button', { name: 'Mark read', exact: true })).toHaveCount(2);
    await expect(other).toHaveURL(`${baseURL}${workspace('org-b')}`);
    await expect(notificationPanel(other).getByRole('button', { name: 'Mark read', exact: true })).toHaveCount(2);

    // Act
    await page.goBack();
    await expect(page).toHaveURL(`${baseURL}${workspace('org-a')}`);
    await openNotifications(page);
    await other.reload();
    await openNotifications(other);
    // Assert
    await expect(notificationPanel(page).getByText('Organization A independent update', { exact: true })).toBeVisible();
    await expect(notificationPanel(page).getByRole('button', { name: 'Mark read', exact: true })).toHaveCount(2);
    await expect(notificationPanel(other).getByText('Organization B initial notice', { exact: true })).toBeVisible();
    const otherRequests = fixture.requests.filter(request => request.tab === fixture.tabId(other));
    expect(otherRequests.length).toBeGreaterThan(0);
    expect(otherRequests.every(request => request.tenant === 'org-b')).toBe(true);
    expect(fixture.requests.filter(request => request.method === 'POST')).toHaveLength(0);
    expect(fixture.workspaceRequests.filter(request => /impersonat|select-tenant/.test(request.path))).toEqual([]);
    assertOrdinaryRequests(fixture);
    await other.close();
  });

  test(`cookie notification read and list failures stay visible and retryable at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const fixture = await installNotificationFixture(context, baseURL!);
    fixture.setListError(true);
    await freezeClock(page);
    await page.goto(workspace('org-a'));
    await openNotifications(page);
    // Assert
    await expect(notificationPanel(page).getByRole('alert')).toContainText('Notification list temporarily unavailable.');
    await expect(notificationPanel(page).getByText('No notifications', { exact: true })).toHaveCount(0);

    // Act
    fixture.setListError(false);
    await clickPanelAction(page, 'Retry');
    await expect(notificationPanel(page).getByText('Organization A initial notice', { exact: true })).toBeVisible();
    fixture.setMarkError(true);
    await markFirstRead(page);
    // Assert
    await expect(notificationPanel(page).getByRole('alert')).toContainText('Notification read was denied.');
    await expect(notificationPanel(page).getByRole('button', { name: 'Mark read', exact: true })).toHaveCount(2);
    await expect(notificationStatus(page)).toContainText('REALTIME_BEARER_REQUIRED');

    // Act
    fixture.setMarkError(false);
    await clickPanelAction(page, 'Retry');
    await expect(notificationPanel(page).getByRole('alert')).toHaveCount(0);
    await markFirstRead(page);
    // Assert
    await expect(notificationPanel(page).getByRole('button', { name: 'Mark read', exact: true })).toHaveCount(1);
    assertOrdinaryRequests(fixture);
  });

  test(`cookie notification recipient mismatch blocks private REST reads at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const fixture = await installNotificationFixture(context, baseURL!);
    fixture.setRecipient('another-synthetic-actor');
    await freezeClock(page);
    // Act
    await page.goto(workspace('org-a'));
    await openNotifications(page);
    // Assert
    await expect(notificationPanel(page).getByRole('alert')).toContainText('Notification identity does not match the authenticated session.');
    expect(fixture.requests.every(request => request.path === `${notificationRoot}/capabilities`)).toBe(true);
    await expect(notificationPanel(page).getByRole('button', { name: 'Mark read', exact: true })).toHaveCount(0);
    assertOrdinaryRequests(fixture);
  });

  test(`provider cookie workspace explains unavailable personal notifications without negotiating at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1000 });
    const fixture = await installNotificationFixture(context, baseURL!, true);
    await freezeClock(page);
    // Act
    await page.goto('/workspaces/csp');
    await openNotifications(page);
    // Assert
    await expect(notificationStatus(page)).toHaveText('Choose an organization workspace to view personal notifications.');
    expect(fixture.requests.length).toBeGreaterThan(0);
    expect(fixture.requests.every(request => request.path === `${notificationRoot}/capabilities` && request.kind === 'csp'
      && request.mode === 'ordinary' && request.tenant === undefined && !request.hasBearer && request.cookieSession)).toBe(true);
    expect(fixture.hubRequests).toEqual([]);
  });
}
