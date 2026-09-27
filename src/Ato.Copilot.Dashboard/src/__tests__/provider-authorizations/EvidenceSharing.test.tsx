import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { EvidenceSharingControls } from '../../features/provider-authorizations/EvidenceSharingControls';
import { ProviderEvidencePanel } from '../../features/provider-authorizations/ProviderEvidencePanel';
import * as api from '../../features/provider-authorizations/evidenceSharingApi';
import type { FindingEvidence } from '../../features/provider-authorizations/types';

vi.mock('../../features/provider-authorizations/evidenceSharingApi', () => ({
  listShareTargets: vi.fn(), listEvidenceShares: vi.fn(), approveEvidenceShare: vi.fn(),
  revokeEvidenceShare: vi.fn(), listMissionEvidence: vi.fn(), summaryUrl: (system: string, id: string) => `/summary/${system}/${id}`,
}));
vi.mock('../../components/AuthenticatedDownload', () => ({
  default: ({ url, children }: { url: string; children: React.ReactNode }) => <a href={url}>{children}</a>,
}));
const evidence: FindingEvidence = { evidenceId: 'evidence', evidenceRevision: 3, findingId: 'finding', offeringId: 'offering',
  findingRevision: 8, fileName: 'private.pdf', mediaType: 'application/pdf', byteLength: 10, sha256: 'hash', description: '',
  state: 'Reviewed', createdAt: '', latestReview: null };
const target = { assignmentId: 'assignment', assignmentRevision: 2, targetTenantId: 'tenant', systemId: 'system', systemName: 'Mission Alpha' };
const grant: api.EvidenceShare = { shareId: 'grant', providerId: 'provider', offeringId: 'offering', evidenceId: 'evidence',
  assignmentId: 'assignment', targetTenantId: 'tenant', systemId: 'system', version: 1, previousVersionId: null,
  summary: 'Approved summary', sourceSha256: 'source-hash', contentHash: 'summary-hash',
  approvedBy: 'reviewer', approvedAt: '2026-09-26T12:00:00Z', revision: 1, revokedAt: null };
const page = <T,>(items: T[]) => ({ items, page: 1, pageSize: 25, total: items.length });
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.listShareTargets).mockResolvedValue(page([target]));
  vi.mocked(api.listEvidenceShares).mockResolvedValue(page([]));
  vi.mocked(api.listMissionEvidence).mockResolvedValue(page([grant]));
  vi.mocked(api.approveEvidenceShare).mockResolvedValue(grant);
});

it('requires a named eligible system, reviewed summary and explicit approval before writing', async () => {
  // Arrange
  render(<EvidenceSharingControls evidence={evidence} />);
  await screen.findByRole('option', { name: /Mission Alpha/ });
  // Act
  fireEvent.change(screen.getByLabelText('Named mission system'), { target: { value: 'assignment' } });
  fireEvent.change(screen.getByLabelText('Customer-facing summary'), { target: { value: 'Approved summary' } });
  // Assert
  expect(api.approveEvidenceShare).not.toHaveBeenCalled();
  expect(screen.getByRole('button', { name: 'Approve summary access' })).toBeDisabled();
  fireEvent.click(screen.getByLabelText(/I approve only this summary/));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Approve summary access' })).toBeEnabled());
  fireEvent.click(screen.getByRole('button', { name: 'Approve summary access' }));
  await waitFor(() => expect(api.approveEvidenceShare).toHaveBeenCalledWith('offering', 'evidence', {
    assignmentId: 'assignment', expectedAssignmentRevision: 2, expectedEvidenceRevision: 3,
    summary: 'Approved summary', previousVersionId: null, version: 1,
  }, expect.any(String)));
});

it('shows retained reference/hash and summary download without a private attachment link', async () => {
  // Arrange / Act
  render(<ProviderEvidencePanel systemId="system" />);
  // Assert
  expect(await screen.findByText('Approved summary')).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Download approved summary' })).toHaveAttribute('href', '/summary/system/grant');
  expect(screen.getByText(/Private attachments remain restricted/)).toBeInTheDocument();
  expect(screen.getByText('summary-hash')).toBeInTheDocument();
  expect(screen.queryByRole('link', { name: /private/i })).not.toBeInTheDocument();
});

it('presents approved summaries in an accessible source and availability table without sharing controls', async () => {
  // Arrange / Act
  render(<ProviderEvidencePanel systemId="system" />);
  // Assert
  const table = await screen.findByRole('table', { name: 'Provider-approved summaries' });
  expect(within(table).getByRole('columnheader', { name: 'Source' })).toBeInTheDocument();
  expect(within(table).getByRole('columnheader', { name: 'Availability' })).toBeInTheDocument();
  expect(within(table).getByText('Approved summary only')).toBeInTheDocument();
  expect(within(table).getByText('Private attachment restricted')).toBeInTheDocument();
  expect(within(table).getByText('source-hash')).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Approve summary access' })).not.toBeInTheDocument();
  expect(api.approveEvidenceShare).not.toHaveBeenCalled();
});

it('distinguishes unavailable provider access from a successfully empty summary list', async () => {
  // Arrange
  vi.mocked(api.listMissionEvidence).mockRejectedValue(new Error('Provider access unavailable'));
  // Act
  render(<ProviderEvidencePanel systemId="system" />);
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent(/Provider evidence is unavailable.*Provider access unavailable/);
  expect(screen.queryByText(/No approved provider evidence is available/)).not.toBeInTheDocument();
  expect(screen.queryByRole('link', { name: 'Download approved summary' })).not.toBeInTheDocument();
});

it('revokes a persisted approval through its fenced API even when no eligible target remains', async () => {
  // Arrange
  vi.mocked(api.listShareTargets).mockResolvedValue(page([]));
  vi.mocked(api.listEvidenceShares).mockResolvedValue(page([grant]));
  vi.mocked(api.revokeEvidenceShare).mockResolvedValue({ ...grant, revision: 2, revokedAt: '2026-09-26T13:00:00Z' });
  render(<EvidenceSharingControls evidence={evidence} />);
  // Act
  fireEvent.change(await screen.findByLabelText('Revocation rationale'), { target: { value: 'Withdraw access' } });
  fireEvent.click(screen.getByRole('button', { name: 'Revoke summary access' }));
  // Assert
  await waitFor(() => expect(api.revokeEvidenceShare).toHaveBeenCalledWith('offering', 'grant',
    { expectedRevision: 1, rationale: 'Withdraw access' }, expect.any(String)));
  expect(api.approveEvidenceShare).not.toHaveBeenCalled();
});

it('removes a revoked summary from the mission view after access is refreshed', async () => {
  // Arrange
  render(<ProviderEvidencePanel systemId="system" />);
  await screen.findByText('Approved summary');
  vi.mocked(api.listMissionEvidence).mockResolvedValue(page([]));
  // Act
  fireEvent.click(screen.getByRole('button', { name: 'Refresh evidence access' }));
  // Assert
  await screen.findByText(/No approved provider evidence is available/);
  expect(screen.queryByRole('link', { name: 'Download approved summary' })).not.toBeInTheDocument();
});
