import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('../../../features/csp-inherited-components/api', () => ({
  listCspInheritedComponents: vi.fn(),
  reparentCspInheritedCapability: vi.fn(),
  isUnavailable: () => false,
}));

import MoveCapabilityDialog from '../../../features/csp-inherited-components/MoveCapabilityDialog';
import * as api from '../../../features/csp-inherited-components/api';

const sourceComponentId = 'cmp-source';
const otherComponentId = 'cmp-other';
const otherComponentName = 'Other Component';

const capability = {
  id: 'cap-1',
  componentId: sourceComponentId,
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

function newComponent(id: string, name: string) {
  return {
    id,
    cspProfileId: 'prof-1',
    name,
    description: 'd',
    componentType: 'Service' as const,
    status: 'Published' as const,
    sourceFormat: 'Manual' as const,
    sourceFileName: null,
    sourceArtifactReference: null,
    importedAt: '2026-04-01T00:00:00Z',
    importedBy: 'seed',
    updatedAt: null,
    updatedBy: null,
    capabilityMappedCount: 0,
    capabilityNeedsReviewCount: 0,
    capabilities: [],
    rowVersion: null,
  };
}

/**
 * T021 [Feature 050 / US2] — frontend coverage for the new
 * <c>MoveCapabilityDialog</c> component.
 *
 * Asserts the contract from
 * <c>specs/050-csp-capability-lifecycle/contracts/frontend-types.md § 3.1</c>:
 *   (a) single eager fetch (pageSize=200, status=Published);
 *   (b) current parent excluded from candidates;
 *   (c) filter-as-you-type (case-insensitive substring);
 *   (d) Confirm disabled until target selected;
 *   (e) Confirm sends If-Match + targetComponentId body;
 *   (f) 412 → inline error + Reload link;
 *   (g) success calls onMoved(updatedCapability);
 *   (h) total > 200 renders the "showing first 200" notice.
 */
describe('MoveCapabilityDialog — Feature 050 US2', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    (api.listCspInheritedComponents as ReturnType<typeof vi.fn>).mockResolvedValue({
      items: [
        newComponent(sourceComponentId, 'Source Component'),
        newComponent(otherComponentId, otherComponentName),
        newComponent('cmp-extra', 'Storage Stack'),
      ],
      page: 1,
      pageSize: 200,
      total: 3,
    });
    (api.reparentCspInheritedCapability as ReturnType<typeof vi.fn>).mockResolvedValue({
      ...capability,
      componentId: otherComponentId,
      status: 'NeedsReview',
      rowVersion: 'NEWROWVERSION',
    });
  });

  it('fires exactly one ListCspInheritedComponents call with pageSize=200 + status=Published', async () => {
    render(
      <MoveCapabilityDialog
        capability={capability}
        sourceComponentId={sourceComponentId}
        onMoved={() => {}}
        onCancel={() => {}}
      />,
    );

    await waitFor(() =>
      expect(api.listCspInheritedComponents).toHaveBeenCalledTimes(1),
    );
    const params = (api.listCspInheritedComponents as ReturnType<typeof vi.fn>).mock.calls[0]![0];
    expect(params).toMatchObject({ page: 1, pageSize: 200, status: 'Published' });
  });

  it('excludes the current parent component from the candidate list', async () => {
    render(
      <MoveCapabilityDialog
        capability={capability}
        sourceComponentId={sourceComponentId}
        onMoved={() => {}}
        onCancel={() => {}}
      />,
    );

    await screen.findByText(otherComponentName);
    expect(screen.queryByText('Source Component')).toBeNull();
  });

  it('filter-as-you-type narrows visible rows (case-insensitive substring)', async () => {
    render(
      <MoveCapabilityDialog
        capability={capability}
        sourceComponentId={sourceComponentId}
        onMoved={() => {}}
        onCancel={() => {}}
      />,
    );

    await screen.findByText(otherComponentName);
    expect(screen.queryByText('Storage Stack')).not.toBeNull();

    const filter = screen.getByTestId('csp-move-capability-filter');
    fireEvent.change(filter, { target: { value: 'storage' } });

    expect(screen.queryByText('Storage Stack')).not.toBeNull();
    expect(screen.queryByText(otherComponentName)).toBeNull();
  });

  it('Confirm button is disabled until a target is selected', async () => {
    render(
      <MoveCapabilityDialog
        capability={capability}
        sourceComponentId={sourceComponentId}
        onMoved={() => {}}
        onCancel={() => {}}
      />,
    );

    const confirm = (await screen.findByTestId('csp-move-capability-confirm')) as HTMLButtonElement;
    expect(confirm.disabled).toBe(true);

    fireEvent.click(await screen.findByTestId(`csp-move-capability-option-${otherComponentId}`));

    expect(confirm.disabled).toBe(false);
  });

  it('Confirm sends If-Match header + targetComponentId body, then calls onMoved', async () => {
    const onMoved = vi.fn();
    render(
      <MoveCapabilityDialog
        capability={capability}
        sourceComponentId={sourceComponentId}
        onMoved={onMoved}
        onCancel={() => {}}
      />,
    );

    fireEvent.click(await screen.findByTestId(`csp-move-capability-option-${otherComponentId}`));
    fireEvent.click(screen.getByTestId('csp-move-capability-confirm'));

    await waitFor(() =>
      expect(api.reparentCspInheritedCapability).toHaveBeenCalledTimes(1),
    );
    const callArgs = (api.reparentCspInheritedCapability as ReturnType<typeof vi.fn>).mock.calls[0]!;
    expect(callArgs[0]).toBe(sourceComponentId); // componentId
    expect(callArgs[1]).toBe(capability.id);     // capabilityId
    expect(callArgs[2]).toMatchObject({ targetComponentId: otherComponentId });
    expect(callArgs[3]).toBe(capability.rowVersion); // If-Match header value

    await waitFor(() => expect(onMoved).toHaveBeenCalledTimes(1));
  });

  it('412 ROW_VERSION_MISMATCH renders an inline error and a "Reload capability" link', async () => {
    (api.reparentCspInheritedCapability as ReturnType<typeof vi.fn>).mockRejectedValueOnce(
      Object.assign(new Error('Capability was modified by another user; reload and retry.'), {
        errorCode: 'ROW_VERSION_MISMATCH',
      }),
    );

    render(
      <MoveCapabilityDialog
        capability={capability}
        sourceComponentId={sourceComponentId}
        onMoved={() => {}}
        onCancel={() => {}}
      />,
    );

    fireEvent.click(await screen.findByTestId(`csp-move-capability-option-${otherComponentId}`));
    fireEvent.click(screen.getByTestId('csp-move-capability-confirm'));

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText(/modified by another user/i)).toBeDefined();
    expect(await screen.findByText(/reload capability/i)).toBeDefined();
  });

  it('renders the "showing first 200" notice when total > 200', async () => {
    (api.listCspInheritedComponents as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      items: [newComponent(otherComponentId, otherComponentName)],
      page: 1,
      pageSize: 200,
      total: 312,
    });

    render(
      <MoveCapabilityDialog
        capability={capability}
        sourceComponentId={sourceComponentId}
        onMoved={() => {}}
        onCancel={() => {}}
      />,
    );

    expect(await screen.findByText(/showing first 200/i)).toBeDefined();
  });
});
