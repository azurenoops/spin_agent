import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import { AuthorizationsPage } from '../../features/provider-authorizations/AuthorizationsPage';
import * as api from '../../features/provider-authorizations/api';
import { PackageImportError } from '../../features/package-imports/request';
import { offering, boundary } from './testData';
import { offeringOverview, recordedAuthorization } from './overviewFixtures';
import { page } from '../package-imports/fixtures';
import '../helpers/dialog';
import '../package-imports/crypto';

vi.mock('../../components/layout/PageLayout', () => ({ default: ({ children, leftPanel }: { children: ReactNode; leftPanel: ReactNode }) => <main>{leftPanel}{children}</main> }));
vi.mock('../../components/layout/PageHero', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => ({ target: { kind: 'csp' }, workspace: { permissions: { canAccessCsp: true } } }),
}));
vi.mock('../../features/provider-authorizations/api', async original => ({
  ...await original<typeof api>(), getOffering: vi.fn(), getOfferingOverview: vi.fn(), listDecisions: vi.fn(),
  listBoundaries: vi.fn(), createDecision: vi.fn(), recordDecision: vi.fn(), listDecisionHistory: vi.fn(),
}));

function mount() {
  return render(<MemoryRouter initialEntries={[api.authorizationHref(offering.offeringId)]}>
    <WorkspaceNavigationProvider workspace={{ kind: 'csp' }}><AuthorizationsPage /></WorkspaceNavigationProvider>
  </MemoryRouter>);
}
async function manual() {
  fireEvent.click(await screen.findByRole('button', { name: 'Record authorization manually' }));
  const dialog = await screen.findByRole('dialog', { name: 'Record an existing authorization' });
  await within(dialog).findByRole('option', { name: /Test service boundary/ });
  return dialog;
}
function fill(dialog: HTMLElement) {
  fireEvent.change(within(dialog).getByLabelText('Boundary revision'), { target: { value: boundary.boundaryRevisionId } });
  fireEvent.change(within(dialog).getByLabelText('Reference', { exact: true }), { target: { value: 'Existing source decision' } });
  fireEvent.change(within(dialog).getByLabelText('Scope statement'), { target: { value: 'Provider services only.' } });
}
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.getOffering).mockResolvedValue(offering);
  vi.mocked(api.getOfferingOverview).mockResolvedValue(offeringOverview());
  vi.mocked(api.listDecisions).mockResolvedValue(page([]));
  vi.mocked(api.listBoundaries).mockResolvedValue(page([boundary]));
  vi.mocked(api.listDecisionHistory).mockResolvedValue(page([recordedAuthorization]));
});

describe('Offering overview', () => {
  it('keeps an uploaded package visible when no authorization is recorded, with complete summary counts', async () => {
    // Arrange
    mount();
    // Act
    const authorization = await screen.findByRole('region', { name: 'Authorization' });
    // Assert
    expect(screen.getByRole('heading', { name: 'Offering overview' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Offering overview' })).toBeInTheDocument();
    expect(within(authorization).getByText('Not recorded', { exact: true })).toBeInTheDocument();
    expect(within(authorization).getByText(/does not mean this offering has no ATO/)).toBeInTheDocument();
    expect(screen.getByText('Uploaded authorization documents.zip', { exact: true })).toBeInTheDocument();
    expect(screen.getByText('415 of 428 source segments analyzed')).toBeInTheDocument();
    expect(screen.getByText('86', { selector: '[data-metric="Proposed"]' })).toBeInTheDocument();
    expect(screen.getByText('6', { selector: '[data-metric="Awaiting approval"]' })).toBeInTheDocument();
    expect(screen.getByText('4', { selector: '[data-metric="Published"]' })).toBeInTheDocument();
    expect(screen.getByText('2 associated mission systems')).toBeInTheDocument();
    expect(screen.getByText('5 hosting allocations')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Create draft' })).not.toBeInTheDocument();
    expect(api.createDecision).not.toHaveBeenCalled();
  });

  it.each(['AuthorizationDecisionClaim', 'AuthorizationReference'] as const)('prefers retained %s details with exact offering/package context', async type => {
    // Arrange
    const data = offeringOverview();
    data.packages.preferredAuthorizationReview = { packageId: 'older package', packageName: 'Older retained package', type };
    vi.mocked(api.getOfferingOverview).mockResolvedValue(data);
    mount();
    // Act
    const next = await screen.findByRole('region', { name: 'Next action' });
    // Assert
    expect(within(next).getAllByRole('link')).toHaveLength(1);
    expect(within(next).getByRole('link', { name: 'Review extracted authorization details' })).toHaveAttribute('href',
      `${api.authorizationHref(offering.offeringId, 'packages/older%20package')}?type=${type}`);
    expect(within(next).getByText(/does not record a decision or issue an ATO/)).toBeInTheDocument();
  });

  it.each([
    ['package', 'Review package analysis'],
    ['draft', 'Review authorization records'],
    ['capabilities', 'Review capabilities'],
    ['hosting', 'Configure hosting'],
    ['upload', 'Add an authorization package'],
    ['source records', 'Review package analysis'],
    ['associations', 'View associations'],
  ])('offers one useful next action for %s', async (scenario, label) => {
    // Arrange
    const data = offeringOverview();
    data.packages.preferredAuthorizationReview = null;
    data.packages.needsAttention = scenario === 'package' ? 1 : 0;
    data.packages.awaitingReview = scenario === 'source records' ? 1 : 0;
    data.capabilities.awaitingReview = scenario === 'capabilities' ? 3 : 0;
    data.capabilities.awaitingApproval = 0;
    data.authorizations.unconfirmed = scenario === 'draft' ? 1 : 0;
    data.hosting.configured = scenario !== 'hosting';
    if (scenario === 'upload') data.packages = { ...data.packages, ...page([]) };
    vi.mocked(api.getOfferingOverview).mockResolvedValue(data);
    mount();
    // Act
    const next = await screen.findByRole('region', { name: 'Next action' });
    // Assert
    expect([...within(next).queryAllByRole('link'), ...within(next).queryAllByRole('button')]).toHaveLength(1);
    expect(within(next).getByText(label, { exact: true })).toBeInTheDocument();
  });

  it('shows source-stated decision details and evidence without presenting them as independently verified authority', async () => {
    // Arrange
    const data = offeringOverview();
    data.authorizations = { ...page([recordedAuthorization]), recorded: 1, unconfirmed: 0, rejected: 0 };
    vi.mocked(api.getOfferingOverview).mockResolvedValue(data);
    mount();
    // Act
    const authorization = await screen.findByRole('region', { name: 'Authorization' });
    // Assert
    expect(within(authorization).getByText('Example authorizing official')).toBeInTheDocument();
    expect(within(authorization).getByText('2027-01-01')).toBeInTheDocument();
    expect(within(authorization).getByText('Named provider services only.', { selector: 'dd' })).toBeInTheDocument();
    expect(within(authorization).getByText('Annual reassessment required.')).toBeInTheDocument();
    expect(within(authorization).getByText('authorization-letter.pdf')).toBeInTheDocument();
    expect(within(authorization).getByText('Current as recorded, not independently verified')).toBeInTheDocument();
    expect(within(authorization).getByText(/technical-decision-hash/)).not.toBeVisible();
    expect(within(screen.getByRole('region', { name: 'Next action' }))
      .getByRole('link', { name: 'Review extracted authorization details' })).toBeInTheDocument();
  });

  it('does not turn unavailable data into zero counts or an absent authorization', async () => {
    // Arrange
    vi.mocked(api.getOfferingOverview).mockRejectedValue(new PackageImportError('Overview API unavailable', 404));
    mount();
    // Act
    const alert = await screen.findByRole('alert');
    // Assert
    expect(alert).toHaveTextContent('Offering overview unavailable');
    expect(screen.queryByText('Not recorded', { exact: true })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Next action' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Record authorization manually' })).not.toBeInTheDocument();
    // Act
    vi.mocked(api.getOfferingOverview).mockResolvedValue(offeringOverview());
    fireEvent.click(within(alert).getByRole('button', { name: 'Retry overview' }));
    // Assert
    expect(await screen.findByRole('region', { name: 'Next action' })).toBeInTheDocument();
  });

  it.each(['Unconfirmed', 'Rejected'] as const)('distinguishes %s details with missing dates and evidence from a recorded decision', async metadataReviewState => {
    // Arrange
    const data = offeringOverview();
    data.authorizations = { ...page([{ ...recordedAuthorization, metadataReviewState, issuingAuthority: null,
      decisionAsStated: null, issuedOn: null, effectiveOn: null, expiresOn: null,
      expiryBasis: 'NoExpiryStated', conditions: [], citations: [], recordedBy: null, recordedAt: null,
      currentStanding: 'Undetermined' }]), recorded: 0,
      unconfirmed: metadataReviewState === 'Unconfirmed' ? 1 : 0, rejected: metadataReviewState === 'Rejected' ? 1 : 0 };
    vi.mocked(api.getOfferingOverview).mockResolvedValue(data);
    mount();
    // Act
    const authorization = await screen.findByRole('region', { name: 'Authorization' });
    // Assert
    expect(within(authorization).getByText(metadataReviewState === 'Unconfirmed'
      ? 'Draft details - awaiting human review' : 'Rejected metadata')).toBeInTheDocument();
    expect(within(authorization).getByText('No expiry stated in source')).toBeInTheDocument();
    expect(within(authorization).getByText('No supporting citations recorded.')).toBeInTheDocument();
    expect(within(authorization).queryByText('Recorded decision', { exact: true })).not.toBeInTheDocument();
  });

  it('preserves a manual draft through conflict refresh and explicitly adopts the new offering revision', async () => {
    // Arrange
    vi.mocked(api.createDecision).mockRejectedValueOnce(new PackageImportError('Offering revision changed', 409))
      .mockResolvedValue({ ...recordedAuthorization, metadataReviewState: 'Unconfirmed' });
    mount();
    const dialog = await manual();
    fill(dialog);
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save draft' }));
    await within(dialog).findByText(/Stale revision. Inputs retained/);
    vi.mocked(api.getOffering).mockResolvedValue({ ...offering, revision: 8 });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Refresh current records' }));
    const adopt = await within(dialog).findByRole('button', { name: 'Use refreshed revision with retained inputs' });
    await waitFor(() => expect(adopt).toBeEnabled());
    fireEvent.click(adopt);
    // Assert
    expect(within(dialog).getByLabelText('Reference', { exact: true })).toHaveValue('Existing source decision');
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save draft' }));
    // Assert
    await waitFor(() => expect(api.createDecision).toHaveBeenLastCalledWith(offering.offeringId,
      expect.objectContaining({ expectedOfferingRevision: 8, reference: 'Existing source decision' }), expect.any(String)));
  });

  it('pages authorization and package lists independently without changing global totals', async () => {
    // Arrange
    const data = offeringOverview();
    data.packages = { ...data.packages, pageSize: 1, total: 30 };
    data.authorizations = { ...data.authorizations, items: [recordedAuthorization], pageSize: 1, total: 8, recorded: 8 };
    vi.mocked(api.getOfferingOverview).mockImplementation(async (_id, authorizationPage, packagePage) => ({
      ...data, authorizations: { ...data.authorizations, page: authorizationPage ?? 1 },
      packages: { ...data.packages, page: packagePage ?? 1 },
    }));
    mount();
    // Act
    fireEvent.click(within(await screen.findByRole('region', { name: 'Package analysis' })).getByRole('button', { name: 'Next' }));
    // Assert
    await waitFor(() => expect(api.getOfferingOverview).toHaveBeenLastCalledWith(offering.offeringId, 1, 2, expect.any(AbortSignal)));
    // Act
    fireEvent.click(within(await screen.findByRole('region', { name: 'Authorization' })).getByRole('button', { name: 'Next' }));
    // Assert
    await waitFor(() => expect(api.getOfferingOverview).toHaveBeenLastCalledWith(offering.offeringId, 2, 2, expect.any(AbortSignal)));
    expect(await screen.findByText('86', { selector: '[data-metric="Proposed"]' })).toBeInTheDocument();
  });

  it('opens and dismisses manual recording without creating an ATO or writing data', async () => {
    // Arrange
    mount();
    // Act
    const dialog = await manual();
    // Assert
    expect(within(dialog).getByText(/does not issue a new ATO/)).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Save draft' })).toBeInTheDocument();
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: 'Close dialog' }));
    // Assert
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(api.createDecision).not.toHaveBeenCalled();
    expect(api.recordDecision).not.toHaveBeenCalled();
  });

  it('retains authorization history and review actions in the record-management dialog', async () => {
    // Arrange
    const data = offeringOverview();
    data.authorizations = { ...page([recordedAuthorization]), recorded: 1, unconfirmed: 0, rejected: 0 };
    vi.mocked(api.getOfferingOverview).mockResolvedValue(data);
    vi.mocked(api.listDecisions).mockResolvedValue(page([recordedAuthorization]));
    mount();
    // Act
    fireEvent.click(within(await screen.findByRole('region', { name: 'Authorization' }))
      .getByRole('button', { name: 'Review authorization records' }));
    const dialog = await screen.findByRole('dialog', { name: 'Review authorization records' });
    fireEvent.click(await within(dialog).findByRole('button', { name: `Open ${recordedAuthorization.reference}` }));
    fireEvent.click(within(dialog).getByRole('button', { name: 'View history' }));
    // Assert
    expect(await within(dialog).findByText('Historical revision 2')).toBeInTheDocument();
    expect(api.listDecisions).toHaveBeenCalledWith(offering.offeringId, 1, expect.any(AbortSignal), 'ProviderDecision');
    expect(api.createDecision).not.toHaveBeenCalled();
    expect(api.recordDecision).not.toHaveBeenCalled();
  });

  it('refreshes active processing with reads only, then stops polling once processing stops', async () => {
    // Arrange
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    const processing = offeringOverview();
    processing.packages.processing = 1;
    vi.mocked(api.getOfferingOverview).mockResolvedValueOnce(processing).mockResolvedValue(offeringOverview());
    const view = mount();
    try {
      await act(async () => {});
      // Act
      await act(async () => { vi.advanceTimersByTime(5000); });
      // Assert
      expect(api.getOfferingOverview).toHaveBeenCalledTimes(2);
      // Act
      await act(async () => { vi.advanceTimersByTime(5000); });
      // Assert
      expect(api.getOfferingOverview).toHaveBeenCalledTimes(2);
      expect(api.createDecision).not.toHaveBeenCalled();
    } finally { view.unmount(); vi.useRealTimers(); }
  });

  it('retains an uncertain manual save in the dialog, then releases dismissal after the same operation succeeds', async () => {
    // Arrange
    let reject!: (reason: unknown) => void;
    vi.mocked(api.createDecision).mockImplementationOnce(() => new Promise((_resolve, fail) => { reject = fail; }));
    mount();
    const dialog = await manual();
    fill(dialog);
    // Act
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save draft' }));
    // Assert
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Close dialog' })).toBeDisabled());
    // Act
    await act(async () => reject(new Error('Connection lost; outcome unknown.')));
    fireEvent(dialog, new Event('cancel', { cancelable: true }));
    // Assert
    expect(dialog).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Reference', { exact: true })).toHaveValue('Existing source decision');
    expect(within(dialog).getByRole('button', { name: 'Close dialog' })).toBeDisabled();
    const key = vi.mocked(api.createDecision).mock.calls[0]?.[2];
    expect(key).toBeTruthy();
    // Act
    vi.mocked(api.createDecision).mockResolvedValue({ ...recordedAuthorization, metadataReviewState: 'Unconfirmed' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Retry same operation' }));
    // Assert
    await waitFor(() => expect(api.createDecision).toHaveBeenLastCalledWith(offering.offeringId,
      expect.objectContaining({ expectedOfferingRevision: offering.revision, recordKind: 'ProviderDecision' }), key));
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Close dialog' })).toBeEnabled());
    expect(api.recordDecision).not.toHaveBeenCalled();
  });
});
