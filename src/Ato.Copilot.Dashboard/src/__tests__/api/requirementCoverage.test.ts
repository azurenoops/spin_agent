import { beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import {
  acceptEnhancement, bindRequirementCatalog, getRequirementCatalogs, getRequirementCoverage,
  proposeEnhancement, returnEnhancement, reviewRequirementResponses, saveRequirementResponses,
  type RequirementCoverageDetail,
} from '../../api/requirementCoverage';

vi.mock('../../api/client', () => ({ default: { defaults: { baseURL: '/api/dashboard' }, get: vi.fn(), post: vi.fn(), put: vi.fn() } }));

const fixture: RequirementCoverageDetail = {
  systemId: 'system-a', controlId: 'AC-11', framework: 'Synthetic', catalogVersion: '1',
  sourceUri: 'https://example.invalid/catalog', baselineRevision: 2, narrativeVersion: 3,
  parent: { controlId: 'parent', title: 'Synthetic parent', selected: true, hasNarrative: false },
  enhancements: [{ controlId: 'child', title: 'Synthetic child', selected: false, hasNarrative: true }],
  requirements: [{ id: 'source-a', label: 'a.', text: 'Source requirement', responseState: 'Draft', reviewed: false, evidenceGap: false,
    responses: [{ statementId: 'source-a', kind: 'Policy', response: 'Draft response', evidence: [{ artifactId: 'evidence-a', contentHash: 'synthetic-hash' }] }] }],
  parameters: [{ id: 'parameter-a', definition: '{}' }], parameterValues: { 'parameter-a': 'value' }, gaps: [],
  proposals: [{ id: 'proposal-a', controlId: 'child', rationale: 'Scope', policyDraft: null, technicalDraft: 'Draft',
    status: 'Pending', revision: 1, createdBy: 'author', createdAt: '2026-01-01', reviewedBy: null, reviewedAt: null, canAccept: true }],
  canAuthor: true, canReview: false, canBind: false,
};

beforeEach(() => vi.clearAllMocks());

describe('Requirement coverage API', () => {
  it('rejects malformed and mismatched source responses rather than showing empty coverage', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: { systemId: 'another-system', requirements: [] } });

    // Act / Assert
    await expect(getRequirementCoverage('system-a', 'AC-11')).rejects.toThrow('mismatched');
  });

  it('uses authenticated client and encoded system/control scope', async () => {
    // Arrange
    vi.mocked(apiClient.post).mockResolvedValue({ data: { id: 'proposal' } });
    const input = { parentControlId: 'AC-11', controlId: 'AC-11(1)', expectedBaselineRevision: 4,
      rationale: 'Synthetic scope', policyDraft: null, technicalDraft: 'Synthetic draft' };

    // Act
    await proposeEnhancement('system a', input);

    // Assert
    expect(apiClient.post).toHaveBeenCalledWith('/systems/system%20a/requirement-coverage/enhancement-proposals', input, { baseURL: '/api' });
  });

  it('validates complete source mappings and mutation responses', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: fixture });
    vi.mocked(apiClient.put).mockResolvedValue({ data: fixture });
    vi.mocked(apiClient.post).mockResolvedValue({ data: fixture });

    // Act / Assert
    expect(await getRequirementCoverage('system-a', 'AC-11')).toEqual(fixture);
    expect(await saveRequirementResponses('system-a', 'AC-11', {
      expectedVersion: 3, responses: fixture.requirements[0]!.responses, parameters: fixture.parameterValues,
    })).toEqual(fixture);
    expect(await reviewRequirementResponses('system-a', 'AC-11', 3)).toEqual(fixture);
    await acceptEnhancement('system-a', 'proposal-a', 1);
    await returnEnhancement('system-a', 'proposal-a', 1, 'Revise sources');
    await bindRequirementCatalog('system-a', 'catalog-a', 2, 'Verified source');
    expect(apiClient.post).toHaveBeenLastCalledWith('/systems/system-a/requirement-coverage/catalog-binding',
      { frameworkId: 'catalog-a', expectedRevision: 2, rationale: 'Verified source' }, { baseURL: '/api' });
  });

  it.each([
    { ...fixture, controlId: 'other' },
    { ...fixture, parameters: [{ id: 'p', definition: 4 }] },
    { ...fixture, parameterValues: { p: 4 } },
    { ...fixture, requirements: [{ ...fixture.requirements[0], responses: [{ statementId: 'a', kind: 'Combined', response: 'text', evidence: [] }] }] },
    { ...fixture, proposals: [{ ...fixture.proposals[0], revision: '1' }] },
    { ...fixture, parent: {} },
    { ...fixture, baselineRevision: -1 },
  ])('rejects corrupt nested mappings or identities %#', async invalid => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: invalid });

    // Act / Assert
    await expect(getRequirementCoverage('system-a', 'AC-11')).rejects.toThrow('mismatched');
  });

  it('validates catalog choices and refuses malformed availability', async () => {
    // Arrange
    const choices = [{ id: 'catalog-a', identifier: 'SYNTHETIC', name: 'Synthetic catalog', version: '1', sourceAvailable: true }];
    vi.mocked(apiClient.get).mockResolvedValueOnce({ data: choices }).mockResolvedValueOnce({ data: [{ ...choices[0], sourceAvailable: 'yes' }] });

    // Act / Assert
    expect(await getRequirementCatalogs('system-a')).toEqual(choices);
    await expect(getRequirementCatalogs('system-a')).rejects.toThrow('Unexpected catalog choices');
  });
});
