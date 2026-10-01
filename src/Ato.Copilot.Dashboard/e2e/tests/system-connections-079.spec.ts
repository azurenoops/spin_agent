import { expect, test, type BrowserContext } from '@playwright/test';
import { installSystemCapabilityFixture } from '../fixtures/system-capabilities';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const root = '/workspaces/organizations/org-a/systems/system-a';
const profilePath = '/api/dashboard/systems/system-a/profile/PortsProtocolsAndServices';
const connectionPath = '/api/dashboard/systems/system-a/interconnections';
async function fixture(context: BrowserContext, baseURL: string, editable = true) {
  await installSystemCapabilityFixture(context, baseURL);
  let profile = { id: 'profile-a', sectionType: 'PortsProtocolsAndServices', governanceStatus: 'Draft',
    canEditProfile: editable, draftContent: '{"ppsOverview":"Allowed mission traffic","customSource":"preserved"}',
    approvedContent: null, reviewerComments: null, lastEditedAt: null, userCategories: [], dataTypeEntries: [], leveragedAuthorizations: [],
    ppsEntries: [
      { id: 'pps-a', serviceName: 'User access', portOrRange: '443', protocol: 'TCP', direction: 'Inbound', justification: 'Mission access', sortOrder: 0 },
      { id: 'pps-b', serviceName: 'SSH administration', portOrRange: '22', protocol: 'TCP', direction: 'Inbound', justification: 'Maintenance', sortOrder: 1 },
    ] };
  const source = { id: 'connection-a', interconnectionId: 'connection-a', systemId: 'system-a',
    targetSystemName: 'Partner exchange', targetSystemOwner: 'Partner owner', targetSystemAcronym: 'PART',
    interconnectionType: 'Api', dataFlowDirection: 'Bidirectional', dataClassification: 'CUI',
    dataDescription: 'Mission records', protocolsUsed: ['HTTPS'], portsUsed: ['443'],
    securityMeasures: ['Mutual TLS'], authenticationMethod: 'Certificates',
    status: 'Proposed', statusReason: null, authorizationToConnect: false, hasAgreement: false, agreements: [],
    createdBy: 'fixture-user', createdAt: '2026-09-28T17:00:00Z', modifiedAt: null as string | null,
    canManageInterconnections: editable };
  const connections = [source];
  const writes: { method: string; path: string; body: unknown }[] = [];
  let finishProfile: (() => void) | undefined;
  let delayProfile = false;
  let rejectNextUpdate = false;
  await context.route(`**${profilePath}`, async route => {
    if (route.request().method() === 'PUT') {
      expect(editable).toBe(true);
      const body = route.request().postDataJSON();
      writes.push({ method: 'PUT', path: profilePath, body });
      if (delayProfile) await new Promise<void>(resolve => { finishProfile = resolve; });
      profile = { ...profile, draftContent: body.content,
        ppsEntries: body.childItems.map((row: typeof profile.ppsEntries[number], index: number) => ({
          ...row, id: row.id || `new-pps-${index}`, sortOrder: index,
        })) };
    }
    return route.fulfill({ json: profile });
  });
  await context.route(`**${connectionPath}{,?*}`, route => {
    if (route.request().method() === 'POST') {
      expect(editable).toBe(true);
      const body = route.request().postDataJSON();
      writes.push({ method: 'POST', path: connectionPath, body });
      const added = { ...source, ...body, id: 'connection-new', interconnectionId: 'connection-new' };
      connections.push(added);
      return route.fulfill({ json: added });
    }
    return route.fulfill({ json: { items: connections, total: connections.length, page: 1, pageSize: 50, canManageInterconnections: editable } });
  });
  await context.route(`**${connectionPath}/*`, route => {
    const id = new URL(route.request().url()).pathname.split('/').pop();
    const index = connections.findIndex(item => item.id === id);
    if (index < 0) return route.fulfill({ status: 404, json: { error: 'Unknown fixture connection' } });
    if (route.request().method() === 'PUT') {
      expect(editable).toBe(true);
      if (rejectNextUpdate) {
        rejectNextUpdate = false;
        return route.fulfill({ status: 409, json: { error: 'Update rejected', errorCode: 'CONFLICT' } });
      }
      const body = route.request().postDataJSON();
      expect(body).not.toHaveProperty('status');
      expect(body).not.toHaveProperty('authorizationToConnect');
      expect(body).not.toHaveProperty('agreements');
      writes.push({ method: 'PUT', path: `${connectionPath}/${id}`, body });
      connections[index] = { ...connections[index]!, ...body, modifiedAt: '2026-09-28T18:00:00Z' };
    }
    return route.fulfill({ json: connections[index] });
  });
  return { writes, delayProfile: () => { delayProfile = true; }, finishProfile: () => finishProfile?.(),
    pending: () => !!finishProfile, rejectUpdate: () => { rejectNextUpdate = true; } };
}

for (const width of [1440, 390]) {
  test(`one mock register edits both connection types in right drawers at ${width}px`, async ({ page, context, baseURL }, info) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    const data = await fixture(context, baseURL!);
    await page.goto(`${root}/profile/PortsProtocolsAndServices`);
    await expect(page.getByRole('cell', { name: 'Partner exchange', exact: true })).toBeVisible();
    await expect(page.getByRole('table')).toHaveCount(1);
    await expect(page.getByRole('button', { name: 'Add connection', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Save Draft', exact: true })).toHaveCount(0);
    await expect(page.getByRole('navigation', { name: 'System task views' }).getByRole('link')).toHaveCount(7);
    // Assert: compare visible table/title metrics with the actual reference.
    const reference = await page.context().browser()!.newPage({ viewport: { width, height: 1100 } });
    try {
      await reference.goto(`${pathToFileURL(resolve('../../docs/design/system-overview-mock/pages.html')).href}#Ports%20%26%20interconnections`);
      for (const [live, mock] of [['.ports-workspace h1', '#title'], ['.connection-register th', '.panel th'], ['.connection-register td button', '.panel td button']]) {
        const styles = (element: Element) => { const s = getComputedStyle(element); return [s.fontSize, s.lineHeight, s.paddingTop, s.paddingLeft]; };
        expect(await page.locator(live!).first().evaluate(styles)).toEqual(await reference.locator(mock!).first().evaluate(styles));
      }
      await page.locator('main').screenshot({ path: info.outputPath(`ports-register-${width}.png`) });
    } finally { await reference.close(); }
    // Act: edit a network interface with a delayed real-profile-shaped response.
    const networkOpen = page.getByRole('button', { name: 'Open network interface User access', exact: true });
    await networkOpen.click();
    let drawer = page.getByRole('dialog', { name: 'Network interface · User access', exact: true });
    const box = (await drawer.boundingBox())!;
    expect(box.y).toBe(0); expect(Math.abs(box.x + box.width - width)).toBeLessThan(2);
    await drawer.getByLabel('Ports or range', { exact: true }).fill('8443');
    data.delayProfile();
    await drawer.getByRole('button', { name: 'Save network interface', exact: true }).click();
    await expect(drawer).toHaveAttribute('aria-busy', 'true');
    await page.keyboard.press('Escape');
    await expect(drawer).toBeVisible();
    await expect.poll(data.pending).toBe(true); data.finishProfile();
    await expect(drawer).toHaveCount(0);
    await expect(networkOpen).toBeFocused();
    await expect(page.getByRole('cell', { name: 'TCP / 8443', exact: true })).toBeVisible();
    expect(data.writes[0]).toMatchObject({ method: 'PUT', path: profilePath,
      body: { content: '{"ppsOverview":"Allowed mission traffic","customSource":"preserved"}',
        childItems: [{ id: 'pps-a', portOrRange: '8443' }, { id: 'pps-b', portOrRange: '22' }] } });
    // Act: edit the external record; failure retains input, retry persists it without changing approval.
    await page.getByRole('button', { name: 'Open interconnection Partner exchange', exact: true }).click();
    drawer = page.getByRole('dialog', { name: 'Interconnection · Partner exchange', exact: true });
    await expect(drawer.getByLabel('System owner', { exact: true })).toHaveValue('Partner owner');
    await drawer.getByLabel('Ports (one per line)', { exact: true }).fill('443\n9443');
    data.rejectUpdate();
    await drawer.getByRole('button', { name: 'Save interconnection', exact: true }).click();
    await expect(drawer.getByRole('alert')).toBeVisible();
    await expect(drawer.getByLabel('Ports (one per line)', { exact: true })).toHaveValue('443\n9443');
    await drawer.getByRole('button', { name: 'Save interconnection', exact: true }).click();
    await expect(drawer).toHaveCount(0);
    await page.reload();
    await expect(page.getByRole('cell', { name: 'TCP / 8443', exact: true })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'HTTPS / 443, 9443', exact: true })).toBeVisible();
    await expect(page.getByText('Proposed', { exact: true })).toBeVisible();
    expect(data.writes).toHaveLength(2);
    const geometry = await page.evaluate(() => ({
      viewport: innerWidth, width: document.documentElement.scrollWidth,
      overflowing: [...document.querySelectorAll('body *')].filter(element => element.getBoundingClientRect().right > innerWidth + 1)
        .slice(0, 12).map(element => ({ tag: element.tagName, className: element.className, right: element.getBoundingClientRect().right })),
    }));
    expect(geometry.width, JSON.stringify(geometry.overflowing)).toBeLessThanOrEqual(geometry.viewport);
  });

  test(`Add connection chooses the correct persisted record and context is separate at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    const data = await fixture(context, baseURL!);
    await page.goto(`${root}/profile/PortsProtocolsAndServices`);
    // Act
    await page.getByRole('button', { name: 'Add connection', exact: true }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Add network interface', exact: true }).click();
    let drawer = page.getByRole('dialog', { name: 'Add network interface', exact: true });
    await drawer.getByLabel('Service / interface name', { exact: true }).fill('Audit service');
    await drawer.getByLabel('Ports or range', { exact: true }).fill('6514');
    await drawer.getByRole('button', { name: 'Save network interface', exact: true }).click();
    await expect(drawer).toHaveCount(0);
    await expect(page.getByRole('cell', { name: 'Audit service', exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Add connection', exact: true }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Add interconnection', exact: true }).click();
    drawer = page.getByRole('dialog', { name: 'Add interconnection', exact: true });
    await drawer.getByLabel('External system', { exact: true }).fill('Identity provider');
    await drawer.getByLabel('Data classification', { exact: true }).fill('CUI');
    await drawer.getByRole('button', { name: 'Save interconnection', exact: true }).click();
    await expect(drawer).toHaveCount(0);
    await page.reload();
    await expect(page.getByRole('cell', { name: 'Audit service', exact: true })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'Identity provider', exact: true })).toBeVisible();
    // Assert: overview/review is separate and cancellation creates no write.
    await page.getByRole('button', { name: 'Manage communication context & review', exact: true }).click();
    const review = page.getByRole('dialog', { name: 'Communication context & review', exact: true });
    await expect(review.getByLabel('PPS Overview', { exact: true })).toHaveValue('Allowed mission traffic');
    await expect(review.getByRole('table')).toHaveCount(0);
    await page.keyboard.press('Escape');
    await expect(review).toHaveCount(0);
    await page.getByRole('button', { name: 'Open network interface Audit service', exact: true }).click();
    const removal = page.getByRole('dialog', { name: 'Network interface · Audit service', exact: true });
    await removal.getByRole('button', { name: 'Remove network interface', exact: true }).click();
    await expect(page.getByRole('dialog')).toHaveCount(1);
    await removal.getByRole('button', { name: 'Confirm removal', exact: true }).click();
    await expect(removal).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Open network interface User access', exact: true })).toBeFocused();
    await page.reload();
    await expect(page.getByRole('cell', { name: 'Partner exchange', exact: true })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'Audit service', exact: true })).toHaveCount(0);
    expect(data.writes.map(write => write.method)).toEqual(['PUT', 'POST', 'PUT']);
  });

  test(`read-only records open without granting write controls at ${width}px`, async ({ page, context, baseURL }) => {
    // Arrange
    await page.setViewportSize({ width, height: 1100 });
    const data = await fixture(context, baseURL!, false);
    await page.goto(`${root}/profile/PortsProtocolsAndServices`);
    await expect(page.getByRole('cell', { name: 'Partner exchange', exact: true })).toBeVisible();
    // Act / Assert
    await expect(page.getByRole('button', { name: 'Add connection', exact: true })).toBeDisabled();
    await page.getByRole('button', { name: 'Open network interface User access', exact: true }).click();
    await expect(page.getByRole('dialog').getByLabel('Ports or range', { exact: true })).toBeDisabled();
    await expect(page.getByRole('dialog').getByRole('button', { name: 'Save network interface', exact: true })).toHaveCount(0);
    await page.keyboard.press('Escape');
    await page.getByRole('button', { name: 'Open interconnection Partner exchange', exact: true }).click();
    await expect(page.getByRole('dialog').getByLabel('External system', { exact: true })).toBeDisabled();
    await expect(page.getByRole('dialog').getByRole('button', { name: 'Save interconnection', exact: true })).toHaveCount(0);
    expect(data.writes).toEqual([]);
  });
}
