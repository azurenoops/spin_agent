import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import DeviationTable from '../../components/DeviationTable';
import { DEFAULT_SETTINGS, SettingsContext } from '../../hooks/useSettings';
import type { DeviationListItem } from '../../types/dashboard';

describe('Deviation requested date display', () => {
  it('uses preferred date order and timezone without changing the record timestamp', () => {
    // Arrange
    const item: DeviationListItem = {
      id: 'deviation', deviationType: 'Waiver', controlId: 'AC-2', catSeverity: 1, status: 'Pending',
      justification: 'Synthetic waiver', expirationDate: '2026-12-01', daysUntilExpiration: 60,
      requestedBy: 'Synthetic reviewer', requestedAt: '2026-09-30T01:15:00Z',
      reviewedBy: null, reviewedAt: null, evidenceCount: 0, findingId: null, poamEntryId: null, boundaryDefinitionId: null,
    };
    const settings = { ...DEFAULT_SETTINGS, dateFormat: 'EU' as const, timezone: 'America/Los_Angeles' };
    // Act
    render(<SettingsContext.Provider value={{ settings, updateSettings: vi.fn(), resetSettings: vi.fn() }}>
      <DeviationTable items={[item]} totalCount={1} page={1} pageSize={25} typeFilter="" statusFilter="" severityFilter="" search=""
        onTypeChange={vi.fn()} onStatusChange={vi.fn()} onSeverityChange={vi.fn()} onSearchChange={vi.fn()} onPageChange={vi.fn()} onRowClick={vi.fn()} />
    </SettingsContext.Provider>);
    // Assert
    expect(screen.getByText(/29\/09\/2026/)).toBeInTheDocument();
    expect(item.requestedAt).toBe('2026-09-30T01:15:00Z');
  });
});
