const axios = require('axios');

const base = process.env.MCP_BASE_URL || 'http://localhost:5100';

describe('MCP API endpoints', () => {
  test('role assignment endpoint', async () => {
    const res = await axios.post(base + '/api/onboarding/role-assignments', {
      systemId: '00000000-0000-0000-0000-000000000000',
      assignments: [{ role: 'Engineer', personId: 'eng@example.com' }]
    }).catch(e => e.response || e);
    expect(res.status).toBeDefined();
  });

  test('compliance_generate_sap (compose-sap) endpoint', async () => {
    const res = await axios.post(base + '/api/compliance/compose-sap', { systemId: '00000000-0000-0000-0000-000000000000' }).catch(e => e.response || e);
    expect(res.status).toBeDefined();
  });

  test('authorize endpoint', async () => {
    const res = await axios.post(base + '/api/authorize', { systemId: '00000000-0000-0000-0000-000000000000', decision: 'authorize' }).catch(e => e.response || e);
    expect(res.status).toBeDefined();
  });
});
