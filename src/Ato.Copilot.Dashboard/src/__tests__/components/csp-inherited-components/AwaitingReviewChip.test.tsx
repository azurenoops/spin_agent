import { render, screen } from '@testing-library/react';
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

function capability(
  id: string,
  status: 'Mapped' | 'NeedsReview' | 'Archived',
  name: string,
) {
  return {
    id,
    componentId: 'cmp-1',
    name,
    description: 'desc',
    mappedNistControlIds: ['AC-2'],
    mappingConfidence: 0.9,
    status,
    mappingFailureReason: null,
    mappedBy: 'AI' as const,
    createdAt: '2026-04-10T08:11:02.000Z',
    createdBy: 'system',
    reviewedAt: null,
    reviewedBy: null,
    reviewerNote: null,
    rowVersion: 'AAA=',
  };
}

/**
 * T046 [Feature 050 / US5] — frontend coverage for the rolled-up
 * "(N awaiting review)" chip on the Linked Capabilities section header
 * inside the component detail drawer.
 *
 * Asserts the contract from
 * `specs/050-csp-capability-lifecycle/contracts/frontend-types.md § 3.5`
 * and spec.md § US5 acceptance:
 *   (a) N > 0 → renders "(N awaiting review)" amber text with
 *       `aria-label="N capabilities awaiting review"`;
 *   (b) N = 0 → indicator suppressed entirely (NOT "(0 awaiting review)");
 *   (c) per-row amber pill on NeedsReview rows remains rendered
 *       (regression guard against accidental removal during the chip work).
 */
describe('ComponentDetailDrawer — Feature 050 US5 awaiting-review chip', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    (api.getCspInheritedComponent as ReturnType<typeof vi.fn>).mockResolvedValue(baseComponent);
  });

  it('renders the "(N awaiting review)" chip in amber with aria-label when NeedsReview rows exist', async () => {
    (api.listCspInheritedCapabilities as ReturnType<typeof vi.fn>).mockResolvedValue([
      capability('cap-1', 'Mapped', 'Auth'),
      capability('cap-2', 'NeedsReview', 'RBAC'),
      capability('cap-3', 'NeedsReview', 'Audit'),
    ]);

    render(
      <ComponentDetailDrawer
        componentId="cmp-1"
        canManage={true}
        onClose={() => {}}
        onMutated={() => {}}
      />,
    );

    // Section header total count should still render the "(3)" suffix.
    expect(await screen.findByText('(3)')).toBeDefined();

    // Chip carries the required aria-label and amber styling.
    const chip = await screen.findByTestId('csp-linked-capabilities-needs-review-chip');
    expect(chip.getAttribute('aria-label')).toBe('2 capabilities awaiting review');
    expect(chip.textContent ?? '').toMatch(/2.*awaiting review/i);
    // Amber palette class (matches the per-row pill) — Tailwind amber-700.
    expect(chip.className).toMatch(/amber/);
  });

  it('suppresses the chip entirely when N = 0', async () => {
    (api.listCspInheritedCapabilities as ReturnType<typeof vi.fn>).mockResolvedValue([
      capability('cap-1', 'Mapped', 'Auth'),
      capability('cap-2', 'Mapped', 'RBAC'),
    ]);

    render(
      <ComponentDetailDrawer
        componentId="cmp-1"
        canManage={true}
        onClose={() => {}}
        onMutated={() => {}}
      />,
    );

    // Header total should still render.
    expect(await screen.findByText('(2)')).toBeDefined();

    // Chip must NOT be rendered (NOT just zero — entirely absent).
    expect(screen.queryByTestId('csp-linked-capabilities-needs-review-chip')).toBeNull();
    expect(screen.queryByText(/0 awaiting review/i)).toBeNull();
  });

  it('keeps the per-row amber "Needs review" pill rendered (regression guard)', async () => {
    (api.listCspInheritedCapabilities as ReturnType<typeof vi.fn>).mockResolvedValue([
      capability('cap-1', 'NeedsReview', 'RBAC'),
    ]);

    render(
      <ComponentDetailDrawer
        componentId="cmp-1"
        canManage={true}
        onClose={() => {}}
        onMutated={() => {}}
      />,
    );

    // Search the linked-capabilities list for the per-row indicator. The
    // exact element shape is implementation-defined, but the visible text
    // "Needs review" MUST remain after the chip work lands.
    const list = await screen.findByTestId('csp-linked-capabilities');
    expect(list.textContent ?? '').toMatch(/needs review/i);
  });
});
