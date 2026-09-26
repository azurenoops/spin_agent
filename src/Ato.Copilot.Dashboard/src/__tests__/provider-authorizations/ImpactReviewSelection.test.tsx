import { useState } from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ImpactReviewSelection, impactSelectionError } from '../../features/provider-authorizations/ImpactReviewSelection';
import * as api from '../../features/provider-authorizations/api';
import { PackageImportError } from '../../features/package-imports/request';
import { acceptedImpact, emptyImpactPage, pendingImpact } from './impactFixtures';
import { offering } from './testData';
import { impactNeedsAction, impactOutcome, impactTitle } from '../../features/provider-authorizations/impactPresentation';

vi.mock('../../features/provider-authorizations/api', async original => ({
  ...await original<typeof api>(), listOfferings: vi.fn(), listImpactReviews: vi.fn(),
}));
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.listOfferings).mockResolvedValue({ ...emptyImpactPage, total: 2,
    items: [offering, { ...offering, offeringId: 'offering-2', name: 'Secondary offering' }] });
  vi.mocked(api.listImpactReviews).mockResolvedValue({ ...emptyImpactPage, total: 1, items: [acceptedImpact] });
});
function Harness({ fixed = false, disabled = false, initial = '' }: { fixed?: boolean; disabled?: boolean; initial?: string }) {
  const [value, setValue] = useState(initial);
  return <><ImpactReviewSelection value={value} onChange={setValue} disabled={disabled}
    offeringId={fixed ? offering.offeringId : undefined} capabilityId="capability-1" /><output aria-label="Selected wire IDs">{value}</output></>;
}
const show = (props: React.ComponentProps<typeof Harness> = {}) => render(<MemoryRouter><Harness {...props} /></MemoryRouter>);
describe('named publication impact selection', () => {
  it('uses names and explicit offering choice, with no manually entered review IDs', async () => {
    // Arrange
    show();
    // Act
    await screen.findByRole('option', { name: 'Synthetic service' });
    // Assert
    expect(api.listImpactReviews).not.toHaveBeenCalled();
    expect(screen.queryByRole('textbox', { name: /review IDs/i })).not.toBeInTheDocument();
    fireEvent.change(screen.getByRole('combobox', { name: 'Offering' }), { target: { value: offering.offeringId } });
    fireEvent.click(await screen.findByRole('checkbox', { name: /^Logging coverage change/ }));
    expect(screen.getByLabelText('Selected wire IDs')).toHaveTextContent('impact-1');
    expect(screen.getByRole('link', { name: 'Review changes' })).toHaveAttribute('href',
      '/workspaces/csp/authorizations/offerings/offering-1/impact?capabilityId=capability-1');
  });
  it('does not offer pending, rejected or changed reviews as publication authority', async () => {
    // Arrange
    vi.mocked(api.listImpactReviews).mockResolvedValue({ ...emptyImpactPage, total: 4, items: [
      acceptedImpact, { ...pendingImpact, reviewId: 'pending', title: 'Awaiting decision' },
      { ...acceptedImpact, reviewId: 'stale', title: 'Changed source', stale: true },
      { ...acceptedImpact, reviewId: 'rejected', title: 'Rejected proposal', disposition: 'Reject' },
    ] });
    show({ fixed: true });
    // Act
    await screen.findByRole('checkbox', { name: /^Logging coverage change/ });
    // Assert
    expect(screen.getByRole('checkbox', { name: /^Logging coverage change/ })).toBeEnabled();
    for (const name of [/^Awaiting decision/, /^Changed source/, /^Rejected proposal/])
      expect(screen.getByRole('checkbox', { name })).toBeDisabled();
    expect(screen.getByText('Changes detected since this review')).toBeInTheDocument();
  });
  it('retains named review selections across offerings instead of dropping required approvals', async () => {
    // Arrange
    vi.mocked(api.listImpactReviews).mockImplementation(async id => ({ ...emptyImpactPage, total: 1,
      items: [id === offering.offeringId ? acceptedImpact : { ...acceptedImpact, reviewId: 'impact-2', title: 'Secondary scope change' }] }));
    show();
    await screen.findByRole('option', { name: 'Synthetic service' });
    // Act
    fireEvent.change(screen.getByRole('combobox', { name: 'Offering' }), { target: { value: offering.offeringId } });
    fireEvent.click(await screen.findByRole('checkbox', { name: /^Logging coverage change/ }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Offering' }), { target: { value: 'offering-2' } });
    fireEvent.click(await screen.findByRole('checkbox', { name: /^Secondary scope change/ }));
    // Assert
    const selected = screen.getByRole('list', { name: 'Selected impact reviews' });
    expect(within(selected).getByText('Logging coverage change')).toBeInTheDocument();
    expect(within(selected).getByText('Secondary scope change')).toBeInTheDocument();
    expect(screen.getByLabelText('Selected wire IDs').textContent).toBe('impact-1\nimpact-2');
  });
  it('pages reviews without clearing earlier selections', async () => {
    // Arrange
    vi.mocked(api.listImpactReviews).mockImplementation(async (_id, page = 1) => ({
      ...emptyImpactPage, page, total: 26, items: [page === 1 ? acceptedImpact : { ...acceptedImpact, reviewId: 'last', title: 'Older review' }],
    }));
    show({ fixed: true });
    // Act
    fireEvent.click(await screen.findByRole('checkbox', { name: /^Logging coverage change/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    fireEvent.click(await screen.findByRole('checkbox', { name: /^Older review/ }));
    // Assert
    expect(api.listImpactReviews).toHaveBeenLastCalledWith('offering-1', 2, expect.any(AbortSignal));
    expect(screen.getByLabelText('Selected wire IDs').textContent).toBe('impact-1\nlast');
  });
  it('reports unavailable reads and retries without success-shaped empty lists', async () => {
    // Arrange
    vi.mocked(api.listImpactReviews).mockRejectedValueOnce(new PackageImportError('Service unavailable.', 404));
    show({ fixed: true });
    // Act
    await screen.findByRole('alert');
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Impact reviews unavailable');
    expect(screen.queryByText(/No impact reviews recorded/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByRole('checkbox', { name: /^Logging coverage change/ })).toBeInTheDocument();
  });
  it('keeps unknown retained identifiers in Details and permits explicit removal', async () => {
    // Arrange
    show({ fixed: true, initial: 'older-review' });
    // Act
    await screen.findByText('Selected review; name unavailable');
    // Assert
    expect(within(screen.getByRole('list', { name: 'Selected impact reviews' })).getByText('older-review').closest('details')).not.toHaveAttribute('open');
    fireEvent.click(screen.getByRole('button', { name: 'Remove selected review 1' }));
    await waitFor(() => expect(screen.getByLabelText('Selected wire IDs')).toBeEmptyDOMElement());
  });
  it('freezes selection and source navigation during a parent operation', async () => {
    // Arrange
    show({ fixed: true, disabled: true });
    // Act
    await screen.findByRole('checkbox', { name: /^Logging coverage change/ });
    // Assert
    expect(screen.getByRole('checkbox', { name: /^Logging coverage change/ })).toBeDisabled();
    expect(screen.queryByRole('link', { name: 'Review changes' })).not.toBeInTheDocument();
  });
  it('preserves bounded distinct wire validation', () => {
    // Arrange
    const repeated = 'impact-1\nIMPACT-1';
    const tooMany = Array.from({ length: 101 }, (_, index) => `impact-${index}`).join('\n');
    // Act / Assert
    expect(impactSelectionError(repeated)).toMatch(/only once/);
    expect(impactSelectionError(tooMany)).toMatch(/at most 100/);
    expect(impactSelectionError('impact-1\nimpact-2')).toBeNull();
    expect(impactSelectionError('')).toBeNull();
  });
  it('explains legacy outcomes and separates active work from previous outcomes', () => {
    // Arrange
    const legacy = { ...acceptedImpact, title: null };
    // Act / Assert
    expect(impactTitle(legacy)).toBe('Offering change review');
    expect(impactOutcome('Unexpected')).toBe('Review outcome unavailable');
    expect(impactOutcome('RequestChanges')).toBe('Changes requested');
    expect(impactNeedsAction(pendingImpact)).toBe(true);
    expect(impactNeedsAction({ ...legacy, disposition: 'RequestChanges' })).toBe(true);
    expect(impactNeedsAction({ ...legacy, disposition: 'Reject' })).toBe(false);
    expect(impactNeedsAction({ ...legacy, stale: true })).toBe(true);
  });
});
