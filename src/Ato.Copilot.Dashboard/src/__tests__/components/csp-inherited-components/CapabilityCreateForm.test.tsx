import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';

// Mock the api module BEFORE importing the component — vi.mock is hoisted.
vi.mock('../../../features/csp-inherited-components/api', () => ({
  addCspInheritedCapability: vi.fn(),
  getCspInheritedComponent: vi.fn(),
  listCspInheritedCapabilities: vi.fn(),
  patchCspInheritedComponent: vi.fn(),
  publishCspInheritedComponent: vi.fn(),
  archiveCspInheritedComponent: vi.fn(),
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
 * T013 [Feature 050 / US1] — frontend coverage for the "+ Add Capability"
 * form's new <c>markMappedImmediately</c> opt-in checkbox (FR-001).
 *
 * Asserts:
 *   (a) the checkbox defaults to unchecked,
 *   (b) the submit payload always includes the field,
 *   (c) the label matches the FR-001 acceptance copy verbatim,
 *   (d) a help/tooltip note is rendered alongside the checkbox.
 */
describe('CapabilityCreateForm — Feature 050 US1 (markMappedImmediately)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    (api.getCspInheritedComponent as ReturnType<typeof vi.fn>).mockResolvedValue(baseComponent);
    (api.listCspInheritedCapabilities as ReturnType<typeof vi.fn>).mockResolvedValue([]);
    (api.addCspInheritedCapability as ReturnType<typeof vi.fn>).mockResolvedValue({
      id: 'cap-1',
      componentId: 'cmp-1',
      name: 'Cap',
      description: 'd',
      mappedNistControlIds: ['AC-2'],
      mappingConfidence: null,
      status: 'NeedsReview',
      mappingFailureReason: null,
      mappedBy: 'User',
      createdAt: '2026-05-22T00:00:00Z',
      createdBy: 'me',
      reviewedAt: null,
      reviewedBy: null,
      reviewerNote: null,
      rowVersion: 'AAA=',
    });
  });

  async function openAddCapabilityForm() {
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

    // Open the inline "Add capability" form.
    fireEvent.click(await screen.findByRole('button', { name: /\+ add capability/i }));
  }

  it('renders the markMappedImmediately checkbox unchecked by default', async () => {
    await openAddCapabilityForm();

    const checkbox = await screen.findByRole('checkbox', {
      name: /skip review and mark this capability mapped now/i,
    });
    expect(checkbox).toBeDefined();
    expect((checkbox as HTMLInputElement).checked).toBe(false);
  });

  it('renders the FR-001 acceptance label text verbatim', async () => {
    await openAddCapabilityForm();

    // Label must read exactly: "Skip review and mark this capability Mapped now."
    expect(
      await screen.findByText('Skip review and mark this capability Mapped now.'),
    ).toBeDefined();
  });

  it('renders the explanatory tooltip / helper text', async () => {
    await openAddCapabilityForm();

    expect(
      await screen.findByText(
        /use this when you are mapping the capability as you create it/i,
      ),
    ).toBeDefined();
  });

  it('submits payload with markMappedImmediately: false when checkbox is left unchecked', async () => {
    await openAddCapabilityForm();

    fireEvent.change(await screen.findByTestId('csp-add-capability-name'), {
      target: { value: 'Tenant RBAC' },
    });
    fireEvent.change(screen.getByTestId('csp-add-capability-description'), {
      target: { value: 'Azure RBAC.' },
    });
    fireEvent.change(screen.getByTestId('csp-add-capability-controls'), {
      target: { value: 'AC-2' },
    });
    fireEvent.click(screen.getByTestId('csp-add-capability-submit'));

    await waitFor(() =>
      expect(api.addCspInheritedCapability).toHaveBeenCalledTimes(1),
    );
    const calls = (api.addCspInheritedCapability as ReturnType<typeof vi.fn>).mock.calls;
    expect(calls.length).toBeGreaterThan(0);
    const payload = calls[0]![1];
    expect(payload).toMatchObject({
      name: 'Tenant RBAC',
      description: 'Azure RBAC.',
      mappedNistControlIds: ['AC-2'],
      markMappedImmediately: false,
    });
  });

  it('submits payload with markMappedImmediately: true when checkbox is checked', async () => {
    await openAddCapabilityForm();

    const checkbox = await screen.findByRole('checkbox', {
      name: /skip review and mark this capability mapped now/i,
    });
    fireEvent.click(checkbox);

    fireEvent.change(await screen.findByTestId('csp-add-capability-name'), {
      target: { value: 'Tenant RBAC' },
    });
    fireEvent.change(screen.getByTestId('csp-add-capability-description'), {
      target: { value: 'Azure RBAC.' },
    });
    fireEvent.change(screen.getByTestId('csp-add-capability-controls'), {
      target: { value: 'AC-2' },
    });
    fireEvent.click(screen.getByTestId('csp-add-capability-submit'));

    await waitFor(() =>
      expect(api.addCspInheritedCapability).toHaveBeenCalledTimes(1),
    );
    const calls = (api.addCspInheritedCapability as ReturnType<typeof vi.fn>).mock.calls;
    expect(calls.length).toBeGreaterThan(0);
    const payload = calls[0]![1];
    expect(payload.markMappedImmediately).toBe(true);
  });
});
