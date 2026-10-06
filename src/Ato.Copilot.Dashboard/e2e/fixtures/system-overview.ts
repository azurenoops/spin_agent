import { expect, type BrowserContext } from '@playwright/test';
import { installSystemCapabilityFixture } from './system-capabilities';
import { overviewWorkspace, overviewWork, overviewGroups } from '../../src/__tests__/fixtures/systemOverview';
import type { PackageReadinessWorkspace } from '../../src/api/packageReadiness';

export const systemOverviewRoot = '/workspaces/organizations/org-a/systems/system-a';
export async function installSystemOverviewFixture(context: BrowserContext, baseURL: string, options: {
  large?: boolean; readOnly?: boolean; failure?: boolean;
} = {}) {
  await installSystemCapabilityFixture(context, baseURL);
  let workspace: PackageReadinessWorkspace = { ...overviewWorkspace(), systemId: 'system-a' };
  workspace.rmf.canConfirm = !options.readOnly;
  const work = { ...overviewWork(), systemId: 'system-a', actorPersonId: 'person-a' };
  const groups = overviewGroups();
  groups[1] = { ...groups[1]!, owner: { ...groups[1]!.owner!, personId: 'person-a' } };
  if (options.large) {
    work.counts = { total: 2408, blocking: 2405, warnings: 3 };
    groups[1] = { ...groups[1]!, total: 2407, blocking: 2404, warnings: 3, findings: {
      totalCount: 2407, limit: 20, offset: 0, items: Array.from({ length: 20 }, (_, index) => ({
        id: `finding-ac1-${index}`, severity: 'Error' as const, category: 'requirement-coverage',
        artifactType: 'ssp', description: `Recorded AC-1 requirement gap ${index}`, remediation: 'Review source requirements.',
        controlId: 'AC-1', recordId: `ac-1_smt.${index}`,
      })),
    } };
  }
  const writes: { path: string; body: unknown }[] = [];
  await context.route('**/api/dashboard/systems/system-a/package-readiness**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    if (request.method() !== 'GET') writes.push({ path, body: request.postDataJSON() });
    if (path.endsWith('/rmf-phase')) {
      if (options.readOnly) return route.fulfill({ status: 403, json: { error: 'Phase confirmation denied' } });
      const body = request.postDataJSON();
      expect(body.expectedPhase).toBe(workspace.rmf.phase);
      workspace = { ...workspace, rmf: { ...workspace.rmf, phase: body.phase, confirmed: true,
        source: 'Explicit RMF confirmation', actor: 'person-a', recordedAt: '2026-10-05T14:00:00Z' } };
      return route.fulfill({ json: workspace.rmf });
    }
    if (path.endsWith('/work/explain')) {
      const body = request.postDataJSON();
      return route.fulfill({ json: {
        systemId: 'system-a', runId: 'run-a', groupId: body.groupId, origin: 'AI proposed',
        sourceHash: 'a'.repeat(64), content: 'Review the saved source and unresolved requirements. Do not assume implementation.',
        sources: [{ id: 'source', title: 'Saved readiness finding', origin: 'From system records', version: 'run-a',
          content: 'System design requires review before final SSP output.', href: null }],
        questions: ['Which reviewed system record provides the missing fact?'],
      } });
    }
    if (path.endsWith('/work')) {
      let selected = url.searchParams.get('mine') === 'true' ? groups.filter(group => group.owner?.personId === 'person-a') : groups;
      const groupId = url.searchParams.get('groupId');
      if (groupId) selected = selected.filter(group => group.id === groupId);
      const limit = Number(url.searchParams.get('limit') ?? 10);
      const offset = Number(url.searchParams.get('offset') ?? 0);
      const findingOffset = Number(url.searchParams.get('findingOffset') ?? 0);
      return route.fulfill({ json: { ...work, recommendedGroupId: url.searchParams.get('mine') === 'true' ? null : 'design',
        groups: { items: selected.slice(offset, offset + limit).map(group => ({ ...group, findings: findingOffset
          ? { ...group.findings, offset: findingOffset, items: Array.from({ length: Math.min(20, group.total - findingOffset) }, (_, index) => ({
            ...group.findings.items[0], id: `finding-ac1-${findingOffset + index}`, description: `Recorded AC-1 requirement gap ${findingOffset + index}`,
          })) } : group.findings })), totalCount: selected.length, limit, offset } } });
    }
    if (request.method() === 'POST' && path.endsWith('/runs')) {
      if (options.failure) return route.fulfill({ status: 503, json: { error: 'Readiness refresh unavailable. Previous check remains recorded.' } });
      return route.fulfill({ status: 201, json: {
        ...workspace, run: workspace.latestRun, checks: { items: [
          { id: 'system-design', ruleId: 'system-design', title: 'System design', outcome: 'Blocking', category: 'system-design',
            required: true, applicability: 'Applicable', why: 'Review required', missingSource: 'Review required',
            sources: [], nextSteps: ['Review design'], expectedRole: 'Issm', recordedOwner: null, action: groups[0]!.action },
          { id: 'requirements', ruleId: 'requirements', title: 'Requirements', outcome: 'Blocking', category: 'requirement-coverage',
            required: true, applicability: 'Applicable', why: 'Response required', missingSource: 'Response required',
            sources: [], nextSteps: ['Review requirements'], expectedRole: 'Issm', recordedOwner: groups[1]!.owner, action: { ...groups[1]!.action, path: 'narratives' } },
        ], totalCount: 2, limit: 50, offset: 0 },
      } });
    }
    return route.fulfill({ json: workspace });
  });
  await context.route('**/api/dashboard/systems/system-a/conmon/workspace', route => route.fulfill({ json: {
    canManageRules: false, canReviewImpacts: false, boundaries: [{ id: 'area', name: 'Mission workload' }],
    rules: [{ id: 'rule', name: 'Review baseline change', boundaryDefinitionId: 'area', baselineReference: 'reviewed-baseline-7',
      ownerId: 'person-a', signal: 'Alert', triggerCondition: '{"field":"Type","operator":"Equals","value":"Drift"}',
      cadenceMinutes: 60, severityOverride: 'Medium', isEnabled: false, version: 1, lastEvaluatedAt: null }],
    coverage: [{ assignmentId: 'assignment', boundaryId: 'area', resourceId: null, providerComponentId: null,
      health: 'ScopeUnsupported', lastSuccessAt: null, error: 'Monitoring connectivity is not established.' }],
    changes: [], evaluations: [], impacts: [],
  } }));
  return { writes };
}
