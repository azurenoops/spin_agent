# Playwright Conventions — 065: Persona-Driven E2E

## File Naming
- Journey specs: `e2e/tests/<NN>-<persona>-journey.spec.ts` (e.g., `20-isso-journey.spec.ts`)
- Page objects: `e2e/pages/<page-name>.page.ts`
- Fixtures: `e2e/fixtures/<fixture-name>.ts`

## Tags
Tests must use Playwright tags for filtering:
```typescript
test('ISSO completes narrative review', { tag: ['@persona:isso', '@journey'] }, async ({ page }) => { ... });
```

## Auth Fixtures
```typescript
// e2e/fixtures/auth.ts
export const issoUser = base.extend<{ issoPage: Page }>({
  issoPage: async ({ browser }, use) => {
    const context = await browser.newContext({ storageState: 'e2e/.auth/isso.json' });
    const page = await context.newPage();
    await use(page);
    await context.close();
  },
});
```

## Network Assertions
After every navigation that triggers an API call:
```typescript
await page.waitForResponse(r => r.url().includes('/api/') && r.status() === 200);
```

## Console Error Detection
At the end of each journey test:
```typescript
const errors: string[] = [];
page.on('console', msg => { if (msg.type() === 'error') errors.push(msg.text()); });
// ... test steps ...
expect(errors.filter(e => !e.includes('ResizeObserver'))).toHaveLength(0);
```
(ResizeObserver errors are benign browser noise — filter them out.)

## Page Object Interface
Every page object must implement:
```typescript
class AssessmentsPage {
  constructor(private page: Page) {}
  async goto(systemId: string): Promise<void>
  async waitForLoad(): Promise<void>
  async findingCount(): Promise<number>
  async openFinding(index: number): Promise<void>
}
```
