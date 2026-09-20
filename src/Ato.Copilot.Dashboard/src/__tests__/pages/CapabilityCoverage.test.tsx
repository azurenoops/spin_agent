import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useParams: () => ({ id: 'system-1' }) };
});

vi.mock('../../api/capabilities', () => ({
  getCapabilityCoverage: vi.fn(),
  bulkRegenerateNarratives: vi.fn(),
}));

vi.mock('../../hooks/usePolling', () => ({ usePolling: vi.fn() }));
vi.mock('../../hooks/useSettings', () => ({ useSettings: () => ({ settings: null }) }));
vi.mock('../../components/AddCapabilityDialog', () => ({ default: () => null }));
vi.mock('../../components/EvidenceUploadDialog', () => ({ default: () => null }));

import * as capabilityApi from '../../api/capabilities';
import { usePolling } from '../../hooks/usePolling';
import CapabilityCoverage from '../../pages/CapabilityCoverage';

const mockBulkRegenerate = capabilityApi.bulkRegenerateNarratives as ReturnType<typeof vi.fn>;
const mockUsePolling = usePolling as ReturnType<typeof vi.fn>;
const refresh = vi.fn();

const coverage = {
  systemId: 'system-1',
  systemName: 'Mission System',
  capabilities: [{
    capabilityId: 'csp-capability-1',
    capabilityName: 'CSP Identity Governance',
    source: 'CSP' as const,
    provider: 'Cloud Provider',
    category: 'Identity',
    implementationStatus: 'Implemented',
    owner: null,
    role: 'Shared',
    mappedControlCount: 4,
    narrativeStatus: { populated: 1, custom: 1, empty: 2, aiGenerated: 0 },
    components: [],
  }],
  summary: {
    totalCapabilities: 1,
    totalMappedControls: 4,
    totalNarrativesPopulated: 1,
    totalNarrativesCustom: 1,
    totalNarrativesEmpty: 2,
    coveragePercent: 50,
  },
};

beforeEach(() => {
  vi.clearAllMocks();
  mockUsePolling.mockReturnValue({ data: coverage, loading: false, error: null, refresh });
});

describe('CapabilityCoverage narrative regeneration', () => {
  it('shows regenerated, skipped, and failed counts for a partial result', async () => {
    // Arrange
    mockBulkRegenerate.mockResolvedValue({
      totalControls: 4,
      regenerated: 2,
      skippedCustom: 1,
      failed: 1,
      regeneratedControlIds: ['AC-2', 'AC-6'],
    });
    render(<CapabilityCoverage />);
    fireEvent.click(screen.getByText('CSP Identity Governance'));

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Regenerate Narratives' }));

    // Assert
    await waitFor(() => expect(screen.getByText('2 regenerated')).toBeInTheDocument());
    expect(screen.getByText('1 custom skipped')).toBeInTheDocument();
    expect(screen.getByText('1 failed')).toBeInTheDocument();
  });

  it('shows safe backend error guidance when regeneration is rejected', async () => {
    // Arrange
    mockBulkRegenerate.mockRejectedValue({
      error: 'Capability subscription is not active',
      suggestion: 'Subscribe the system to this CSP capability and try again',
    });
    render(<CapabilityCoverage />);
    fireEvent.click(screen.getByText('CSP Identity Governance'));

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Regenerate Narratives' }));

    // Assert
    await waitFor(() => {
      expect(screen.getByText(/Capability subscription is not active/)).toBeInTheDocument();
    });
    expect(screen.getByText(/Subscribe the system to this CSP capability and try again/))
      .toBeInTheDocument();
  });
});