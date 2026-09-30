import { expect, test, type Page } from '@playwright/test';
import { installWorkspaceFixture } from '../fixtures/workspace-shell';

const route = '/workspaces/organizations/org-a/systems/system-a/poam';
const item = {
  id: 'poam-a', systemId: 'system-a', systemName: 'Synthetic Mission System', controlId: 'AC-12',
  weakness: 'Session management', weaknessSource: 'Assessment finding', catSeverity: 'II', status: 'Ongoing',
  poc: 'Application team', dueDate: '2026-10-05T00:00:00Z', scheduledCompletionDate: '2026-10-05T00:00:00Z',
  daysRemaining: 6, milestoneProgress: { completed: 0, total: 1 }, isOverdue: false,
  nextMilestone: { id: 'milestone-a', description: 'Verify corrected timeout', targetDate: '2026-10-05T00:00:00Z', completedDate: null, sequence: 1, isOverdue: false },
  readyToVerify: true,
  remediationTaskId: null, remediationTaskStatus: null, components: [], rowVersion: 'version-a',
  findingId: null, deviationId: null, milestones: [{ id: 'milestone-a', description: 'Verify corrected timeout',
    targetDate: '2026-10-05T00:00:00Z', completedDate: null, sequence: 1, isOverdue: false }],
  history: [{ id: 'history-a', eventType: 'Created', actingUserName: 'Synthetic reviewer',
    timestamp: '2026-09-29T00:00:00Z', details: 'Original commitment retained', oldValue: null, newValue: null }],
};
const linkedWorkspace = {
  systemId: 'system-a',
  findings: [{ id: 'finding-a', title: 'Timeout configuration', description: 'Correct session timeout', controlId: 'AC-12',
    severity: 'High', status: 'Open', source: 'Assessment', assessmentId: 'assessment-a', importRecordId: null,
    taskIds: ['task-a', 'task-b'], poamIds: ['poam-a'], provenance: { sourceId: 'assessment-a', sourceName: 'Azure configuration checks',
      sourceType: 'Assessment', plan: { id: 'plan-a', revision: 1, title: 'Original plan' } } }],
  poams: [
    { id: 'poam-a', poamId: 'poam-a', taskIds: ['task-a', 'task-b'], findingId: 'finding-a', deviationId: 'exception-a', rowVersion: 'version-a',
      weakness: item.weakness, securityControlNumber: item.controlId, catSeverity: 'CatII', pointOfContact: item.poc,
      scheduledCompletionDate: item.scheduledCompletionDate, status: 'Ongoing', milestones: item.milestones },
    { id: 'poam-overdue', poamId: 'poam-overdue', taskIds: [], findingId: null, deviationId: null, rowVersion: 'version-b',
      weakness: 'Audit configuration', securityControlNumber: 'AU-2', catSeverity: 'CatII', pointOfContact: 'Security team',
      scheduledCompletionDate: '2027-12-01T00:00:00Z', status: 'Delayed',
      milestones: [{ id: 'overdue-milestone', description: 'Update settings', targetDate: '2025-01-01T00:00:00Z', completedDate: null, sequence: 1 }] },
    { id: 'poam-closed', poamId: 'poam-closed', taskIds: [], findingId: null, deviationId: null, rowVersion: 'version-c',
      weakness: 'Legacy remote access', securityControlNumber: 'AC-17', catSeverity: 'CatIII', pointOfContact: 'Infrastructure team',
      scheduledCompletionDate: '2026-10-05T00:00:00Z', status: 'Completed', milestones: [] },
  ],
  tasks: [
    { id: 'task-a', taskNumber: 'REM-024', title: 'Update timeout settings', status: 'Done', assigneeName: 'Application team',
      verificationStatus: 'Passed', verificationNotes: 'Configuration reviewed', evidence: [], poamIds: ['poam-a', 'poam-b'] },
    { id: 'task-b', taskNumber: 'REM-025', title: 'Verify corrected configuration', status: 'InReview', assigneeName: null,
      verificationStatus: 'NotVerified', verificationNotes: null, evidence: [], poamIds: ['poam-a'] },
  ],
  exceptions: [{ id: 'exception-a', type: 'RiskAcceptance', status: 'Pending', controlId: 'AC-12', justification: 'Requested separately',
    expirationDate: '2026-12-01', isEffective: false, findingId: 'finding-a', poamEntryId: 'poam-a' }],
  permissions: { canManageRemediation: false, canCreateTasks: false, canMoveTasks: false, canMoveAnyTasks: false, reason: 'Read-only fixture' },
};

async function fixture(page: Page, baseURL: string, empty = false) {
  await installWorkspaceFixture(page.context(), baseURL);
  await page.route('**/api/dashboard/**', async intercepted => {
    const url = new URL(intercepted.request().url());
    if (url.pathname.endsWith('/poam/poam-a')) return intercepted.fulfill({ json: item });
    if (url.pathname.endsWith('/remediation-workspace')) return intercepted.fulfill({ json: empty
      ? { ...linkedWorkspace, poams: [], findings: [], tasks: [], exceptions: [] } : linkedWorkspace });
    if (/\/tasks\/task-[ab]\/ticket$/.test(url.pathname)) return intercepted.fulfill({ json: {
      configured: false, canManage: false, mode: 'ManualPullOnly', webhooksSupported: false, bidirectionalSupported: false, link: null,
    } });
    return intercepted.fallback();
  });
}

for (const width of [1440, 390]) {
  for (const theme of ['light', 'dark']) {
    test(`POA&M queue and native drawer ${width}px ${theme}`, async ({ page, baseURL }) => {
      // Arrange
      await page.setViewportSize({ width, height: 1000 });
      await fixture(page, baseURL!);
      await page.goto(route);
      await expect(page.getByRole('heading', { name: 'Track remediation commitments' })).toBeVisible();
      await page.locator('html').evaluate((element, dark) => element.classList.toggle('dark', dark), theme === 'dark');
      // Act / Assert
      await expect(page.getByRole('button', { name: 'All items (3)' })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Add POA&M' })).toBeDisabled();
      await page.getByRole('textbox', { name: 'Search POA&M items' }).fill('Session');
      const invoker = page.getByRole('button', { name: 'Session management', exact: true });
      await invoker.click();
      const drawer = page.getByRole('dialog', { name: 'Session management', exact: true });
      await expect(drawer).toBeVisible();
      await expect(drawer.getByText('Verify corrected timeout')).toBeVisible();
      await drawer.getByRole('button', { name: 'Evidence & history' }).click();
      await expect(drawer.getByText('Original commitment retained')).toBeVisible();
      await expect(drawer.getByRole('link', { name: 'Review supporting evidence' })).toHaveAttribute('href', '/workspaces/organizations/org-a/systems/system-a/evidence');
      await page.screenshot({ path: `test-results/poam-drawer-${width}-${theme}.png`, fullPage: true });
      if (width < 650) expect((await drawer.boundingBox())?.width).toBe(width);
      await page.keyboard.press('Escape');
      await expect(drawer).toBeHidden();
      await expect(invoker).toBeFocused();
      await expect(page.getByRole('textbox', { name: 'Search POA&M items' })).toHaveValue('Session');
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
      expect(overflow).toBe(false);
      await page.screenshot({ path: `test-results/poam-queue-${width}-${theme}.png`, fullPage: true });
    });
  }
}

test('POA&M empty and failed queues stay distinct', async ({ page, baseURL }) => {
  // Arrange
  await fixture(page, baseURL!, true);
  await page.goto(route);
  // Act / Assert
  await expect(page.getByText('No POA&M items recorded')).toBeVisible();
  await expect(page.getByRole('table')).toHaveCount(0);
  await expect(page.getByRole('textbox', { name: 'Search POA&M items' })).toHaveCount(0);
  await page.route('**/api/dashboard/systems/system-a/remediation-workspace', intercepted => intercepted.fulfill({ status: 503, json: { error: 'Synthetic outage' } }));
  await page.reload();
  await expect(page.getByText('Unable to load POA&M items.')).toBeVisible();
  await expect(page.getByText('No POA&M items recorded')).toHaveCount(0);
});

test('POA&M linked work retains provenance, shared tasks, and pending decision truth', async ({ page, baseURL }) => {
  // Arrange
  await fixture(page, baseURL!);
  await page.goto(route);
  await page.getByRole('button', { name: 'Session management', exact: true }).click();
  const drawer = page.getByRole('dialog', { name: 'Session management', exact: true });
  // Act
  await drawer.getByRole('button', { name: 'Linked work' }).click();
  // Assert
  await expect(drawer.getByText('Linked tasks (2)')).toBeVisible();
  await expect(drawer.getByText('Shared with 1 other commitment')).toBeVisible();
  await expect(drawer.getByText('Plan: Original plan · Revision 1')).toBeVisible();
  await expect(drawer.getByRole('link', { name: 'View assessment' })).toHaveAttribute('href', '/workspaces/organizations/org-a/systems/system-a/assessments?result=assessment%3Aassessment-a');
  await expect(drawer.getByText('Not effective — no current risk acceptance')).toBeVisible();
  await expect(drawer.getByRole('button', { name: 'Link task', exact: true })).toBeDisabled();
  await expect(drawer.getByRole('button', { name: 'Create task', exact: true })).toBeDisabled();
  await drawer.getByText('Task ticket', { exact: true }).first().click();
  await expect(drawer.getByText('Manual read-only snapshots.', { exact: false }).first()).toBeVisible();
  await page.screenshot({ path: 'test-results/poam-linked-work.png', fullPage: true });
});

test('POA&M creation offers existing-finding duplicates and required manual fields', async ({ page, baseURL }) => {
  // Arrange
  await fixture(page, baseURL!);
  await page.route('**/api/**/workspace-access', intercepted => intercepted.fulfill({ json: { status: 'success', data: {
    systemId: 'system-a', roles: ['Isso'], permissions: { canRead: true, canEditProfile: false, canManageSystem: false,
      canAuthorNarratives: false, canReviewNarratives: false, canManageEvidence: false, canRunAssessments: false,
      canManageRemediation: true, canDecideAuthorization: false },
  } } }));
  const writes: unknown[] = [];
  await page.route('**/api/dashboard/systems/system-a/poam', async intercepted => {
    if (intercepted.request().method() !== 'POST') return intercepted.fallback();
    writes.push(intercepted.request().postDataJSON());
    return intercepted.fulfill({ status: 201, json: item });
  });
  await page.goto(route);
  await page.getByRole('button', { name: 'Add POA&M', exact: true }).click();
  const drawer = page.getByRole('dialog', { name: 'Add POA&M', exact: true });
  // Act / Assert
  await drawer.getByRole('button', { name: 'Existing finding', exact: true }).click();
  await drawer.getByLabel('Assessment finding').selectOption('finding-a');
  await expect(drawer.getByRole('button', { name: 'Create POA&M', exact: true })).toBeDisabled();
  await expect(drawer.getByRole('button', { name: 'Open existing POA&M' })).toBeVisible();
  await drawer.getByRole('button', { name: 'Manual entry', exact: true }).click();
  await drawer.getByLabel('Weakness *', { exact: true }).fill('Synthetic manual weakness');
  await drawer.getByLabel('Control ID *', { exact: true }).fill('AC-2');
  await drawer.getByLabel('Point of Contact *', { exact: true }).fill('Synthetic owner');
  await drawer.getByLabel('Scheduled Completion Date *', { exact: true }).fill('2026-12-01');
  await drawer.getByRole('button', { name: 'Create POA&M', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Session management', exact: true })).toBeVisible();
  expect(writes).toEqual([expect.objectContaining({
    weakness: 'Synthetic manual weakness', controlId: 'AC-2', poc: 'Synthetic owner', scheduledCompletionDate: '2026-12-01',
  })]);
  expect(writes[0]).not.toHaveProperty('findingId');
});

test('POA&M views use complete arrays and keep global counts during search', async ({ page, baseURL }) => {
  // Arrange
  await fixture(page, baseURL!);
  await page.goto(route);
  // Act / Assert
  await page.getByRole('button', { name: 'Overdue (1)', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Audit configuration', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Session management', exact: true })).toHaveCount(0);
  await expect(page.getByText('Milestone overdue')).toBeVisible();
  await page.getByRole('button', { name: 'Closed (1)', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Legacy remote access', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'All items (3)', exact: true }).click();
  await page.getByRole('textbox', { name: 'Search POA&M items' }).fill('missing');
  await expect(page.getByText('No matching POA&M items')).toBeVisible();
  await expect(page.getByRole('button', { name: 'All items (3)', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Ready to verify (0)', exact: true }).click();
  await expect(page.getByText('Review candidates have completed milestones', { exact: false })).toBeVisible();
});

test('POA&M starts a prefilled pending exception request without accepting risk', async ({ page, baseURL }) => {
  // Arrange
  await fixture(page, baseURL!);
  await page.route('**/api/**/workspace-access', intercepted => intercepted.fulfill({ json: { status: 'success', data: {
    systemId: 'system-a', roles: ['Isso'], permissions: { canRead: true, canEditProfile: false, canManageSystem: false,
      canAuthorNarratives: false, canReviewNarratives: false, canManageEvidence: false, canRunAssessments: false,
      canManageRemediation: true, canDecideAuthorization: false },
  } } }));
  await page.route('**/api/dashboard/systems/system-a/remediation-workspace', intercepted => intercepted.fulfill({
    json: { ...linkedWorkspace, permissions: { ...linkedWorkspace.permissions, canManageRemediation: true } },
  }));
  const writes: unknown[] = [];
  await page.route('**/api/dashboard/systems/system-a/deviations', intercepted => {
    writes.push(intercepted.request().postDataJSON());
    return intercepted.fulfill({ status: 201, json: { id: 'pending-exception', status: 'Pending' } });
  });
  await page.goto(route);
  await page.getByRole('button', { name: 'Session management', exact: true }).click();
  const detail = page.getByRole('dialog', { name: 'Session management', exact: true });
  await detail.getByRole('button', { name: 'Linked work' }).click();
  // Act
  await detail.getByRole('button', { name: 'Start exception request' }).click();
  const request = page.getByRole('dialog', { name: 'Request exception', exact: true });
  await expect(request).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(request).toBeHidden();
  await expect(detail).toBeVisible();
  await expect(detail.getByRole('button', { name: 'Start exception request' })).toBeFocused();
  await detail.getByRole('button', { name: 'Start exception request' }).click();
  await request.getByRole('button', { name: /Risk Acceptance/ }).click();
  await request.getByLabel('Severity *').selectOption('CatII');
  await request.getByLabel('Justification *').fill('Synthetic exception request, not an approval');
  await request.getByRole('button', { name: 'Submit Deviation' }).click();
  // Assert
  await expect(request).toBeHidden();
  await expect(detail).toBeVisible();
  expect(writes).toEqual([expect.objectContaining({ poamEntryId: 'poam-a', findingId: 'finding-a', controlId: 'AC-12' })]);
  expect(writes[0]).not.toHaveProperty('status');
  await expect(detail.getByText('Not effective — no current risk acceptance')).toBeVisible();
});
