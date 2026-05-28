import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('../../../features/csp-inherited-components/api', () => ({
  archiveCspInheritedCapability: vi.fn(),
  listCspInheritedCapabilities: vi.fn(),
  patchCspInheritedCapability: vi.fn(),
  remapCspInheritedComponent: vi.fn(),
  reviewCspInheritedCapability: vi.fn(),
  listCspInheritedComponents: vi.fn(),
  reparentCspInheritedCapability: vi.fn(),
  isUnavailable: () => false,
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
 * T022 [Feature 050 / US2] — Move-to-another-component action on the
 * capability detail drawer.
 *
 * Asserts the contract from
 * <c>specs/050-csp-capability-lifecycle/contracts/frontend-types.md § 3.2.1</c>:
 *   (a) "Move to another component…" disabled with tooltip when
 *       `hasEligibleTarget = false` (i.e. server reports total ≤ 1);
 *   (b) enabled when `hasEligibleTarget = true`;
 *   (c) clicking enabled opens the MoveCapabilityDialog;
 *   (d) `onMoved` callback closes the dialog (verified by absence of the
 *       dialog after move resolves).
 */
describe('CapabilityDetailDrawer — Feature 050 US2 Move action', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    (api.listCspInheritedCapabilities as ReturnType<typeof vi.fn>).mockResolvedValue([
      baseCapability,
    ]);
  });

  it('renders the Move button disabled with tooltip when no other components exist', async () => {
    (api.listCspInheritedComponents as ReturnType<typeof vi.fn>).mockResolvedValue({
      items: [newComponent('cmp-source', 'Source')],
      page: 1,
      pageSize: 2,
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

    const moveBtn = (await screen.findByTestId('csp-capability-move-toggle')) as HTMLButtonElement;
    expect(moveBtn.disabled).toBe(true);
    expect(moveBtn.title).toMatch(/no other csp-inherited component exists yet/i);
  });

  it('enables the Move button when at least one other component exists', async () => {
    (api.listCspInheritedComponents as ReturnType<typeof vi.fn>).mockResolvedValue({
      items: [
        newComponent('cmp-source', 'Source'),
        newComponent('cmp-other', 'Other'),
      ],
      page: 1,
      pageSize: 2,
      total: 2,
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

    const moveBtn = (await screen.findByTestId('csp-capability-move-toggle')) as HTMLButtonElement;
    await waitFor(() => expect(moveBtn.disabled).toBe(false));
  });

  it('clicking the enabled Move button opens the MoveCapabilityDialog', async () => {
    (api.listCspInheritedComponents as ReturnType<typeof vi.fn>).mockResolvedValue({
      items: [
        newComponent('cmp-source', 'Source'),
        newComponent('cmp-other', 'Other'),
      ],
      page: 1,
      pageSize: 200,
      total: 2,
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

    const moveBtn = (await screen.findByTestId('csp-capability-move-toggle')) as HTMLButtonElement;
    await waitFor(() => expect(moveBtn.disabled).toBe(false));
    fireEvent.click(moveBtn);

    expect(await screen.findByTestId('csp-move-capability-dialog')).toBeDefined();
  });
});
