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
  firstPass?: { contextHash: string; kind: string; generatedAt: string; sources: FirstPassSource[] } | null;
  firstPasses?: { contextHash: string; kind: string; generatedAt: string; sources: FirstPassSource[] }[] | null;
}
export interface RequirementCatalogChoice { id: string; identifier: string; name: string; version: string; sourceAvailable: boolean }
export interface RequirementMappingInput {
  expectedVersion: number; responses: RequirementResponse[]; parameters: Record<string, string>;
  firstPassToken?: string;
  firstPassTokens?: string[];
}
export interface FirstPassSource { id: string; kind: string; title: string; version: string; contentHash: string; reviewState: string }
export interface FirstPassResponseDraft { statementId: string; response: string; sourceIds: string[]; explanation: string }
export interface FirstPassParameterDraft { parameterId: string; value: string; sourceIds: string[]; explanation: string }
export interface RequirementFirstPass {
  systemId: string; controlId: string; kind: 'Policy' | 'Technical'; expectedVersion: number; contextHash: string; token: string;
  generatedAt: string; sources: FirstPassSource[]; responses: FirstPassResponseDraft[]; parameters: FirstPassParameterDraft[];
  questions: string[]; conflicts: string[];
}

const root = (systemId: string) => `/systems/${encodeURIComponent(systemId)}/requirement-coverage`;
const config = () => ({ baseURL: (apiClient.defaults.baseURL ?? '/api/dashboard').replace(/\/dashboard\/?$/, '') });
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === 'string';
const nullableText = (value: unknown) => value === null || text(value);
const number = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value) && value >= 0;
const strings = (value: unknown): value is string[] => Array.isArray(value) && value.every(text);
function firstPassSource(value: unknown): value is FirstPassSource {
  return object(value) && ['id', 'kind', 'title', 'version', 'contentHash', 'reviewState'].every(key => text(value[key]));
}
function firstPassMetadata(value: unknown): boolean {
  return object(value) && text(value.contextHash) && (value.kind === 'Policy' || value.kind === 'Technical')
    && text(value.generatedAt) && Array.isArray(value.sources) && value.sources.every(firstPassSource);
}
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
    && (value.firstPass === undefined || value.firstPass === null || firstPassMetadata(value.firstPass))
    && (value.firstPasses === undefined || value.firstPasses === null || Array.isArray(value.firstPasses) && value.firstPasses.every(firstPassMetadata))
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
export async function generateRequirementFirstPass(systemId: string, controlId: string,
  input: { expectedVersion: number; expectedBaselineRevision: number; kind: 'Policy' | 'Technical' }, signal?: AbortSignal): Promise<RequirementFirstPass> {
  const { data } = await apiClient.post<unknown>(`${root(systemId)}/${encodeURIComponent(controlId)}/first-pass`, input, { ...config(), signal });
  if (!object(data) || data.systemId !== systemId || data.controlId !== controlId || data.kind !== input.kind
    || data.expectedVersion !== input.expectedVersion || !text(data.contextHash) || !text(data.token) || !data.token.trim() || !text(data.generatedAt)
    || !Array.isArray(data.sources) || !data.sources.every(firstPassSource)
    || !Array.isArray(data.responses) || !data.responses.every((r): r is FirstPassResponseDraft => object(r)
      && text(r.statementId) && text(r.response) && strings(r.sourceIds) && text(r.explanation))
    || !Array.isArray(data.parameters) || !data.parameters.every((p): p is FirstPassParameterDraft => object(p)
      && text(p.parameterId) && text(p.value) && strings(p.sourceIds) && text(p.explanation))
    || !strings(data.questions) || !strings(data.conflicts))
    throw new Error('Unexpected or mismatched requirement first-pass response. No suggestions were applied.');
  const sources = data.sources;
  if ([...data.responses, ...data.parameters].some(r => !r.sourceIds.length || r.sourceIds.some(id => !sources.some(s => s.id === id))))
    throw new Error('First-pass source references could not be verified. No suggestions were applied.');
  return { systemId, controlId, kind: input.kind, expectedVersion: input.expectedVersion, contextHash: data.contextHash,
    token: data.token, generatedAt: data.generatedAt, sources, responses: data.responses, parameters: data.parameters,
    questions: data.questions, conflicts: data.conflicts };
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
