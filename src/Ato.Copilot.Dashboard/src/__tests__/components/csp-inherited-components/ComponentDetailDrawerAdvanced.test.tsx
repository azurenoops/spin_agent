import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('../../../features/csp-inherited-components/api', () => ({
  addCspInheritedCapability: vi.fn(),
  archiveCspInheritedComponent: vi.fn(),
  getCspInheritedComponent: vi.fn(),
  listCspInheritedCapabilities: vi.fn(),
  patchCspInheritedComponent: vi.fn(),
  publishCspInheritedComponent: vi.fn(),
  remapCspInheritedComponent: vi.fn(),
}));

import ComponentDetailDrawer from '../../../features/csp-inherited-components/ComponentDetailDrawer';
import * as api from '../../../features/csp-inherited-components/api';

const baseComponent = {
  id: 'cmp-1',
  cspProfileId: 'prof-1',
  name: 'Identity Provider',
  description: 'Seed component for tests.',
  componentType: 'Service' as const,
  status: 'Published' as const,
  sourceFormat: 'Manual' as const,
  sourceFileName: null,
  sourceArtifactReference: null,
  importedAt: '2026-05-22T00:00:00Z',
  importedBy: 'seed@example.com',
  updatedAt: null,
  updatedBy: null,
  capabilityMappedCount: 0,
  capabilityNeedsReviewCount: 0,
  capabilities: [],
  rowVersion: 'AAA=',
};

/**
 * T042 [Feature 050 / US4] — frontend coverage for the Advanced disclosure
 * that gates Remap.
 *
 * Asserts the contract from
 * `specs/050-csp-capability-lifecycle/contracts/frontend-types.md § 3.4`
 * and spec.md § US4 acceptance:
 *   (a) primary toolbar contains Edit, Archive, + Add Capability but NOT Remap;
 *   (b) Advanced disclosure is collapsed by default;
 *   (c) expanding the disclosure reveals the FR-007 explanatory paragraph
 *       verbatim and the Remap button below it;
 *   (d) clicking Remap opens a modal with the FR-008 confirm copy +
 *       acknowledgement checkbox;
 *   (e) Cancel is the default-focused button (per spec.md US4 acceptance);
 *   (f) Continue is disabled until the acknowledgement checkbox is checked;
 *   (g) Continue fires the existing POST .../remap endpoint;
 *   (h) the optional reviewer-note textarea is rendered and accepts input.
 */
describe('ComponentDetailDrawer — Feature 050 US4 Advanced disclosure', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    (api.getCspInheritedComponent as ReturnType<typeof vi.fn>).mockResolvedValue(baseComponent);
    (api.listCspInheritedCapabilities as ReturnType<typeof vi.fn>).mockResolvedValue([]);
    (api.remapCspInheritedComponent as ReturnType<typeof vi.fn>).mockResolvedValue({
      componentId: 'cmp-1',
      capabilitiesMapped: 3,
      capabilitiesNeedsReview: 1,
      capabilitiesAdded: 1,
      capabilitiesUpdated: 2,
    });
  });

  async function renderAsCspAdmin() {
    render(
      <ComponentDetailDrawer
        componentId="cmp-1"
        canManage={true}
        onClose={() => {}}
        onMutated={() => {}}
      />,
    );
    await waitFor(() =>
      expect(api.getCspInheritedComponent).toHaveBeenCalledWith('cmp-1'),
    );
  }

  it('renders Edit, Archive and + Add capability in the primary toolbar but NOT Remap', async () => {
    await renderAsCspAdmin();

    // Edit, Archive, + Add capability are present.
    expect(screen.getByRole('button', { name: 'Edit' })).toBeDefined();
    expect(screen.getByRole('button', { name: 'Archive' })).toBeDefined();
    expect(screen.getByRole('button', { name: /\+ add capability/i })).toBeDefined();

    // The primary toolbar must NOT contain "Remap capabilities" — only the
    // Advanced disclosure carries it, and it stays collapsed by default.
    expect(screen.queryByRole('button', { name: /^remap capabilities$/i })).toBeNull();
  });

  it('keeps the Advanced disclosure collapsed by default', async () => {
    await renderAsCspAdmin();

    const advancedToggle = screen.getByTestId('csp-component-advanced-toggle');
    expect(advancedToggle.getAttribute('aria-expanded')).toBe('false');

    // The explanatory paragraph and Remap CTA are NOT in the DOM until expanded.
    expect(screen.queryByText(/this re-runs ai capability mapping/i)).toBeNull();
    expect(screen.queryByTestId('csp-component-advanced-remap')).toBeNull();
  });

  it('reveals the FR-007 explanatory paragraph and the Remap button when expanded', async () => {
    await renderAsCspAdmin();

    fireEvent.click(screen.getByTestId('csp-component-advanced-toggle'));

    // FR-007 verbatim from spec.md US4 acceptance.
    expect(
      await screen.findByText(
        /This re-runs AI capability mapping for this component\. Capabilities you have approved \(mappedBy = User\) are preserved\. AI-mapped capabilities \(mappedBy = AI\) may be replaced\. Continue\?/,
      ),
    ).toBeDefined();
    expect(screen.getByTestId('csp-component-advanced-remap')).toBeDefined();
  });

  it('opens the FR-008 confirm dialog with Cancel focused by default and Continue disabled', async () => {
    await renderAsCspAdmin();
    fireEvent.click(screen.getByTestId('csp-component-advanced-toggle'));
    fireEvent.click(await screen.findByTestId('csp-component-advanced-remap'));

    const dialog = await screen.findByRole('dialog');
    // FR-008 verbatim confirm copy.
    expect(
      within(dialog).getByText(
        /Re-running AI mapping will overwrite AI-produced capabilities and reset their NeedsReview status\. User-mapped capabilities are preserved\. Continue\?/,
      ),
    ).toBeDefined();

    const cancel = within(dialog).getByTestId('csp-remap-confirm-cancel');
    const cont = within(dialog).getByTestId('csp-remap-confirm-continue') as HTMLButtonElement;

    // Cancel is the default-focused button.
    expect(document.activeElement).toBe(cancel);

    // Continue is disabled until the acknowledge checkbox is checked.
    expect(cont.disabled).toBe(true);

    // Tick the acknowledgement.
    const ack = within(dialog).getByTestId('csp-remap-confirm-acknowledge');
    fireEvent.click(ack);
    expect(cont.disabled).toBe(false);
  });

  it('does NOT call remapCspInheritedComponent until Continue is clicked', async () => {
    await renderAsCspAdmin();
    fireEvent.click(screen.getByTestId('csp-component-advanced-toggle'));
    fireEvent.click(await screen.findByTestId('csp-component-advanced-remap'));

    // Confirm dialog is open but Continue has not been clicked yet.
    await screen.findByRole('dialog');
    expect(api.remapCspInheritedComponent).not.toHaveBeenCalled();

    // Cancel dismisses without calling the API.
    fireEvent.click(screen.getByTestId('csp-remap-confirm-cancel'));
    expect(api.remapCspInheritedComponent).not.toHaveBeenCalled();
  });

  it('fires remapCspInheritedComponent when Continue is clicked after acknowledgement', async () => {
    await renderAsCspAdmin();
    fireEvent.click(screen.getByTestId('csp-component-advanced-toggle'));
    fireEvent.click(await screen.findByTestId('csp-component-advanced-remap'));

    fireEvent.click(screen.getByTestId('csp-remap-confirm-acknowledge'));
    fireEvent.click(screen.getByTestId('csp-remap-confirm-continue'));

    await waitFor(() =>
      expect(api.remapCspInheritedComponent).toHaveBeenCalledTimes(1),
    );
    expect(api.remapCspInheritedComponent).toHaveBeenCalledWith('cmp-1');
  });

  it('renders the optional reviewer-note textarea and accepts input', async () => {
    await renderAsCspAdmin();
    fireEvent.click(screen.getByTestId('csp-component-advanced-toggle'));
    fireEvent.click(await screen.findByTestId('csp-component-advanced-remap'));

    const note = await screen.findByTestId('csp-remap-confirm-note');
    fireEvent.change(note, { target: { value: 'Refreshing after CSP doc update.' } });
    expect((note as HTMLTextAreaElement).value).toBe('Refreshing after CSP doc update.');
  });
});
