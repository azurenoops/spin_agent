import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('../../../features/csp-inherited-components/api', () => ({
  archiveCspInheritedCapability: vi.fn(),
  isUnavailable: () => false,
  listCspInheritedCapabilities: vi.fn(),
  listCspInheritedComponents: vi.fn(),
  listCapabilityHistory: vi.fn(),
  patchCspInheritedCapability: vi.fn(),
  remapCspInheritedComponent: vi.fn(),
  reviewCspInheritedCapability: vi.fn(),
}));

import CapabilityDetailDrawer from '../../../features/csp-inherited-components/CapabilityDetailDrawer';
import * as api from '../../../features/csp-inherited-components/api';

const baseCapability = {
  id: 'cap-1',
  componentId: 'cmp-source',
  name: 'Tenant RBAC',
  description: 'Azure RBAC.',
  mappedNistControlIds: ['AC-2'],
  mappingConfidence: 0.87,
  status: 'Mapped' as const,
  mappingFailureReason: null,
  mappedBy: 'AI' as const,
  createdAt: '2026-04-10T08:11:02.000Z',
  createdBy: 'system',
  reviewedAt: '2026-04-11T10:00:00.000Z',
  reviewedBy: 'reviewer',
  reviewerNote: null,
  rowVersion: 'AAAAAAAAB+E=',
};

/**
 * T034 [Feature 050 / US3] — frontend coverage for the new History tab
 * in `CapabilityDetailDrawer`.
 *
 * Asserts the contract from
 * `specs/050-csp-capability-lifecycle/contracts/frontend-types.md § 3.2.2`:
 *   (a) fetches on tab activation (NOT on initial drawer mount);
 *   (b) renders rows in reverse-chronological order;
 *   (c) empty state renders "No history yet." (NOT an error);
 *   (d) `Moved` rows render fromComponentId / toComponentId;
 *   (e) `Reviewed` rows render reviewerNote when present;
 *   (f) `Created` rows with `metadata.markedMappedImmediately === true`
 *       render the "Auto-mapped on create" pill;
 *   (g) `Created` rows with `metadata.source === "Remap"` render the
 *       "Remap" pill.
 */
describe('CapabilityDetailDrawer — Feature 050 US3 History tab', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    (api.listCspInheritedCapabilities as ReturnType<typeof vi.fn>).mockResolvedValue([
      baseCapability,
    ]);
    (api.listCspInheritedComponents as ReturnType<typeof vi.fn>).mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 2,
      total: 1,
    });
  });

  it('does NOT call listCapabilityHistory until the History tab is opened', async () => {
    (api.listCapabilityHistory as ReturnType<typeof vi.fn>).mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 50,
      total: 0,
    });

    render(
      <CapabilityDetailDrawer
        componentId="cmp-source"
        capabilityId={baseCapability.id}
        componentName="Source"
        componentType="Service"
        canManage={true}
        onClose={() => {}}
        onMutated={() => {}}
      />,
    );

    await screen.findByText(/Tenant RBAC/);
    // Initial drawer load should NOT have triggered the history fetch.
    expect(api.listCapabilityHistory).not.toHaveBeenCalled();
  });

  it('fetches on History tab activation and renders rows in reverse chronological order', async () => {
    (api.listCapabilityHistory as ReturnType<typeof vi.fn>).mockResolvedValue({
      items: [
        {
          id: 'evt-3',
          eventType: 'Reviewed',
          actorOid: 'reviewer-oid',
          occurredAt: '2026-05-22T14:50:11Z',
          summary: 'Reviewed and approved.',
          metadata: { reviewerNote: 'Approved.' },
        },
        {
          id: 'evt-2',
          eventType: 'Moved',
          actorOid: 'mover-oid',
          occurredAt: '2026-05-22T14:40:11Z',
          summary: "Moved from 'A' to 'B'.",
          metadata: { fromComponentId: 'aaa', toComponentId: 'bbb' },
        },
        {
          id: 'evt-1',
          eventType: 'Created',
          actorOid: 'creator-oid',
          occurredAt: '2026-05-22T14:32:11Z',
          summary: 'Capability manually created.',
          metadata: { markedMappedImmediately: true },
        },
      ],
      page: 1,
      pageSize: 50,
      total: 3,
    });

    render(
      <CapabilityDetailDrawer
        componentId="cmp-source"
        capabilityId={baseCapability.id}
        componentName="Source"
        componentType="Service"
        canManage={true}
        onClose={() => {}}
        onMutated={() => {}}
      />,
    );

    fireEvent.click(await screen.findByTestId('csp-capability-history-tab'));

    await waitFor(() =>
      expect(api.listCapabilityHistory).toHaveBeenCalledWith(
        'cmp-source',
        baseCapability.id,
        expect.objectContaining({ page: 1, pageSize: 50 }),
      ),
    );

    // Each rendered row carries an `event-type` testid attribute. The order
    // in the DOM must equal the response order (already DESC).
    const rows = await screen.findAllByTestId(/^csp-capability-history-row-/);
    expect(rows.length).toBe(3);
    expect(rows[0]!.getAttribute('data-event-type')).toBe('Reviewed');
    expect(rows[1]!.getAttribute('data-event-type')).toBe('Moved');
    expect(rows[2]!.getAttribute('data-event-type')).toBe('Created');

    // Per-row metadata previews.
    expect(screen.getByText('Approved.')).toBeDefined();
    // The Moved preview splits "from", `<code>aaa</code>`, "to",
    // `<code>bbb</code>` across nodes — search the row text directly.
    const movedRow = screen.getByTestId('csp-capability-history-row-evt-2');
    expect(movedRow.textContent ?? '').toMatch(/from.*aaa/i);
    expect(movedRow.textContent ?? '').toMatch(/to.*bbb/i);
    expect(screen.getByText(/auto-mapped on create/i)).toBeDefined();
  });

  it('renders "No history yet." on empty result instead of an error', async () => {
    (api.listCapabilityHistory as ReturnType<typeof vi.fn>).mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 50,
      total: 0,
    });

    render(
      <CapabilityDetailDrawer
        componentId="cmp-source"
        capabilityId={baseCapability.id}
        componentName="Source"
        componentType="Service"
        canManage={true}
        onClose={() => {}}
        onMutated={() => {}}
      />,
    );

    fireEvent.click(await screen.findByTestId('csp-capability-history-tab'));

    expect(await screen.findByText(/no history yet/i)).toBeDefined();
    // Must NOT render an error alert.
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('renders the "Remap" pill on Created/Edited/Archived rows when metadata.source === "Remap"', async () => {
    (api.listCapabilityHistory as ReturnType<typeof vi.fn>).mockResolvedValue({
      items: [
        {
          id: 'evt-1',
          eventType: 'Edited',
          actorOid: 'admin-oid',
          occurredAt: '2026-05-22T14:32:11Z',
          summary: 'Capability edited.',
          metadata: {
            remapRunId: '11111111-1111-1111-1111-111111111111',
            source: 'Remap',
          },
        },
      ],
      page: 1,
      pageSize: 50,
      total: 1,
    });

    render(
      <CapabilityDetailDrawer
        componentId="cmp-source"
        capabilityId={baseCapability.id}
        componentName="Source"
        componentType="Service"
        canManage={true}
        onClose={() => {}}
        onMutated={() => {}}
      />,
    );

    fireEvent.click(await screen.findByTestId('csp-capability-history-tab'));

    expect(await screen.findByText(/^Remap$/)).toBeDefined();
  });
});
