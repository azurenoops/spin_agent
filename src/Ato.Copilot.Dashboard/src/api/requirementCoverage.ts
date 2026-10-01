import apiClient from './client';

export interface RequirementEvidencePin { artifactId: string; contentHash: string }
export interface RequirementResponse {
  statementId: string; kind: 'Policy' | 'Technical'; response: string; evidence: RequirementEvidencePin[];
}
export interface RequirementLink { controlId: string; title: string; selected: boolean; hasNarrative: boolean }
export interface RequirementItem {
  id: string; label: string | null; text: string; responses: RequirementResponse[];
  responseState: string; reviewed: boolean; evidenceGap: boolean;
}
export interface EnhancementProposal {
  id: string; controlId: string; rationale: string; policyDraft: string | null; technicalDraft: string | null;
  status: string; revision: number; createdBy: string; createdAt: string; reviewedBy: string | null;
  reviewedAt: string | null; canAccept: boolean;
  reviewNote?: string | null;
}
export interface RequirementCoverageDetail {
  systemId: string; controlId: string; framework: string | null; catalogVersion: string | null; sourceUri: string | null;
  baselineRevision: number; narrativeVersion: number | null; parent: RequirementLink | null;
  enhancements: RequirementLink[]; requirements: RequirementItem[];
  parameters: { id: string; definition: string }[]; parameterValues: Record<string, string>; gaps: string[];
  proposals: EnhancementProposal[]; canAuthor: boolean; canReview: boolean; canBind: boolean;
}
export interface RequirementCatalogChoice { id: string; identifier: string; name: string; version: string; sourceAvailable: boolean }
export interface RequirementMappingInput {
  expectedVersion: number; responses: RequirementResponse[]; parameters: Record<string, string>;
}

const root = (systemId: string) => `/systems/${encodeURIComponent(systemId)}/requirement-coverage`;
const config = () => ({ baseURL: (apiClient.defaults.baseURL ?? '/api/dashboard').replace(/\/dashboard\/?$/, '') });
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === 'string';
const nullableText = (value: unknown) => value === null || text(value);
const number = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value) && value >= 0;
const strings = (value: unknown): value is string[] => Array.isArray(value) && value.every(text);
function link(value: unknown): value is RequirementLink {
  return object(value) && text(value.controlId) && text(value.title) && typeof value.selected === 'boolean' && typeof value.hasNarrative === 'boolean';
}
function response(value: unknown): value is RequirementResponse {
  return object(value) && text(value.statementId) && (value.kind === 'Policy' || value.kind === 'Technical')
    && text(value.response) && Array.isArray(value.evidence)
    && value.evidence.every(pin => object(pin) && text(pin.artifactId) && text(pin.contentHash));
}
function item(value: unknown): value is RequirementItem {
  return object(value) && text(value.id) && nullableText(value.label) && text(value.text)
    && strings([value.responseState]) && typeof value.reviewed === 'boolean' && typeof value.evidenceGap === 'boolean'
    && Array.isArray(value.responses) && value.responses.every(response);
}
function proposal(value: unknown): value is EnhancementProposal {
  return object(value) && ['id', 'controlId', 'rationale', 'status', 'createdBy', 'createdAt'].every(key => text(value[key]))
    && ['policyDraft', 'technicalDraft', 'reviewedBy', 'reviewedAt'].every(key => nullableText(value[key]))
    && (value.reviewNote === undefined || nullableText(value.reviewNote))
    && number(value.revision) && typeof value.canAccept === 'boolean';
}
function coverage(value: unknown): value is RequirementCoverageDetail {
  return object(value) && text(value.systemId) && text(value.controlId)
    && ['framework', 'catalogVersion', 'sourceUri'].every(key => nullableText(value[key]))
    && number(value.baselineRevision) && (value.narrativeVersion === null || number(value.narrativeVersion))
    && (value.parent === null || link(value.parent)) && Array.isArray(value.enhancements) && value.enhancements.every(link)
    && Array.isArray(value.requirements) && value.requirements.every(item)
    && Array.isArray(value.parameters) && value.parameters.every(p => object(p) && text(p.id) && text(p.definition))
    && object(value.parameterValues) && Object.values(value.parameterValues).every(text) && strings(value.gaps)
    && Array.isArray(value.proposals) && value.proposals.every(proposal)
    && ['canAuthor', 'canReview', 'canBind'].every(key => typeof value[key] === 'boolean');
}
function checked(value: unknown, systemId: string, controlId: string): RequirementCoverageDetail {
  if (!coverage(value) || value.systemId !== systemId || value.controlId !== controlId)
    throw new Error('Unexpected or mismatched requirement coverage response.');
  return value;
}

export async function getRequirementCoverage(systemId: string, controlId: string, signal?: AbortSignal) {
  const { data } = await apiClient.get<unknown>(`${root(systemId)}/${encodeURIComponent(controlId)}`, { ...config(), signal });
  return checked(data, systemId, controlId);
}
export async function saveRequirementResponses(systemId: string, controlId: string, input: RequirementMappingInput) {
  const { data } = await apiClient.put<unknown>(`${root(systemId)}/${encodeURIComponent(controlId)}/responses`, input, config());
  return checked(data, systemId, controlId);
}
export async function reviewRequirementResponses(systemId: string, controlId: string, expectedVersion: number) {
  const { data } = await apiClient.post<unknown>(`${root(systemId)}/${encodeURIComponent(controlId)}/review`, { expectedVersion }, config());
  return checked(data, systemId, controlId);
}
export async function proposeEnhancement(systemId: string, input: {
  parentControlId: string; controlId: string; expectedBaselineRevision: number;
  rationale: string; policyDraft: string | null; technicalDraft: string | null;
}) {
  await apiClient.post(`${root(systemId)}/enhancement-proposals`, input, config());
}
export async function acceptEnhancement(systemId: string, proposalId: string, expectedRevision: number) {
  await apiClient.post(`${root(systemId)}/enhancement-proposals/${encodeURIComponent(proposalId)}/accept`, { expectedRevision }, config());
}
export async function returnEnhancement(systemId: string, proposalId: string, expectedRevision: number, note: string) {
  await apiClient.post(`${root(systemId)}/enhancement-proposals/${encodeURIComponent(proposalId)}/return`, { expectedRevision, note }, config());
}
export async function getRequirementCatalogs(systemId: string): Promise<RequirementCatalogChoice[]> {
  const { data } = await apiClient.get<unknown>(`${root(systemId)}/catalogs`, config());
  if (!Array.isArray(data) || !data.every((c): c is RequirementCatalogChoice => object(c)
    && ['id', 'identifier', 'name', 'version'].every(key => text(c[key])) && typeof c.sourceAvailable === 'boolean'))
    throw new Error('Unexpected catalog choices response.');
  return data;
}
export async function bindRequirementCatalog(systemId: string, frameworkId: string, expectedRevision: number, rationale: string) {
  await apiClient.post(`${root(systemId)}/catalog-binding`, { frameworkId, expectedRevision, rationale }, config());
}
