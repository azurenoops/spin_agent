import { test, expect, request } from '@playwright/test';

// High-level E2E flows (API-driven for stability).
// Environment: MCP_BASE_URL points to running mcp API (docker-compose.mcp.yml defaults)

const roles = require('../fixtures/roles.json');

test.describe('MCP E2E flows', () => {
  test('Register system -> Assign RMF roles -> Select baseline -> Generate SAP (positive)', async ({ request: playwrightRequest }) => {
    const base = process.env.MCP_BASE_URL || 'http://localhost:5100';

    // 1) Register system
    const registerRes = await playwrightRequest.post(base + '/api/onboarding/systems', {
      data: { name: 'E2E Test System', identifier: 'e2e-test-system' }
    });
    expect(registerRes.ok()).toBeTruthy();
    const system = await registerRes.json();
    expect(system.id).toBeTruthy();

    // 2) Assign RMF roles (AO, ISSM, ISSO, SCA, Engineer)
    const assignRes = await playwrightRequest.post(base + '/api/onboarding/role-assignments', {
      data: {
        systemId: system.id,
        assignments: [
          { role: 'AO', personId: 'ao@example.com' },
          { role: 'ISSM', personId: 'issm@example.com' },
          { role: 'ISSO', personId: 'isso@example.com' },
          { role: 'SCA', personId: 'sca@example.com' },
          { role: 'Engineer', personId: 'eng@example.com' }
        ]
      }
    });
    expect(assignRes.ok()).toBeTruthy();

    // 3) Select baseline
    const baselineRes = await playwrightRequest.post(base + '/api/compliance/select-baseline', {
      data: { systemId: system.id, baseline: 'low' }
    });
    expect(baselineRes.ok()).toBeTruthy();

    // 4) Generate SAP (happy path)
    const sapRes = await playwrightRequest.post(base + '/api/compliance/compose-sap', { data: { systemId: system.id } });
    expect(sapRes.ok()).toBeTruthy();
    const sap = await sapRes.json();
    expect(sap.summary).toBeTruthy();
  });

  test('SAP generation - negative: ControlBaseline precondition', async ({ request: playwrightRequest }) => {
    const base = process.env.MCP_BASE_URL || 'http://localhost:5100';

    // Register a minimal system without baseline
    const r = await playwrightRequest.post(base + '/api/onboarding/systems', { data: { name: 'NoBaseline', identifier: 'e2e-no-baseline' } });
    expect(r.ok()).toBeTruthy();
    const s = await r.json();

    // Attempt SAP generation should fail when no ControlBaseline exists
    const sapRes = await playwrightRequest.post(base + '/api/compliance/compose-sap', { data: { systemId: s.id } });
    expect(sapRes.status()).toBeGreaterThanOrEqual(400);
  });

  test('Assessment import -> SAR/POA&M generation -> AO authorization decision -> drift/ConMon alerts', async ({ request: playwrightRequest }) => {
    const base = process.env.MCP_BASE_URL || 'http://localhost:5100';

    // Create system and baseline as earlier
    const reg = await playwrightRequest.post(base + '/api/onboarding/systems', { data: { name: 'AssessMe', identifier: 'e2e-assess' } });
    expect(reg.ok()).toBeTruthy();
    const sys = await reg.json();

    await playwrightRequest.post(base + '/api/compliance/select-baseline', { data: { systemId: sys.id, baseline: 'moderate' } });

    // Import assessment (simulate upload)
    const importRes = await playwrightRequest.post(base + '/api/assessments/import', {
      data: { systemId: sys.id, source: 'e2e-test', summary: 'Assessment import for E2E' }
    });
    expect(importRes.ok()).toBeTruthy();

    // Generate SAR
    const sarRes = await playwrightRequest.post(base + '/api/reports/generate-sar', { data: { systemId: sys.id } });
    expect(sarRes.ok()).toBeTruthy();

    // Generate POA&M
    const poamRes = await playwrightRequest.post(base + '/api/reports/generate-poam', { data: { systemId: sys.id } });
    expect(poamRes.ok()).toBeTruthy();

    // AO decision - authorize
    const authRes = await playwrightRequest.post(base + '/api/authorize', { data: { systemId: sys.id, decision: 'authorize' } });
    expect(authRes.ok()).toBeTruthy();

    // Simulate a drift alert (ConMon) publish
    const alertRes = await playwrightRequest.post(base + '/api/observability/alerts', { data: { systemId: sys.id, type: 'drift', message: 'Configuration drift detected' } });
    expect(alertRes.ok()).toBeTruthy();
  });
});
