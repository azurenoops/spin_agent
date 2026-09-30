import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import '../../helpers/dialog';
import PoamDetailDrawer from '../../../components/poam/PoamDetailDrawer';
import { DEFAULT_SETTINGS, SettingsContext } from '../../../hooks/useSettings';

const fixture = vi.hoisted(() => ({
  id: 'poam-a', systemId: 'system-a', systemName: 'Synthetic system', controlId: 'AC-2',
  weakness: 'Review privileged accounts', weaknessSource: 'Manual', status: 'Ongoing',
  poc: 'Security team', scheduledCompletionDate: '2026-12-01', catSeverity: 'II',
  milestones: [{ id: 'milestone-a', description: 'Review access', targetDate: '2026-11-01', completedDate: null, sequence: 1, isOverdue: false }],
  history: [{ id: 'history-a', eventType: 'Created', timestamp: '2026-09-01', actingUserName: 'Reviewer', details: 'Initial deadline retained' }],
  components: [], rowVersion: 'version-a', createdAt: '2026-09-01', findingId: null, deviationId: null,
  ticketSync: { externalTicketId: 'SYNTHETIC-1', syncStatus: 'Synced', lastSyncAt: '2026-09-30T01:15:00Z' },
}));
vi.mock('../../../hooks/usePoam', () => ({ usePoamDetail: () => ({ data: fixture, loading: false, error: null, refresh: vi.fn() }) }));
vi.mock('../../../components/permissions/useSystemMutationPermission', () => ({ useSystemMutationPermission: () => false }));
vi.mock('../../../components/poam/PoamLifecycleActions', () => ({ default: () => <div>Lifecycle controls</div> }));
vi.mock('../../../components/poam/ComponentPicker', () => ({ default: () => <div>Component picker</div> }));

describe('Commitment detail drawer', () => {
  it('uses personal date display for deadlines and ticket sync without rewriting history', () => {
    // Arrange
    const settings = { ...DEFAULT_SETTINGS, dateFormat: 'ISO' as const, timezone: 'America/Los_Angeles' };
    // Act
    render(<SettingsContext.Provider value={{ settings, updateSettings: vi.fn(), resetSettings: vi.fn() }}>
      <MemoryRouter><PoamDetailDrawer poamId="poam-a" onClose={vi.fn()} /></MemoryRouter>
    </SettingsContext.Provider>);
    // Assert
    expect(screen.getByText('2026-12-01')).toBeInTheDocument();
    expect(screen.getByText(/Target: 2026-11-01/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Evidence & history' }));
    expect(screen.getByText(/Last sync 2026-09-29 18:15:00/)).toBeInTheDocument();
    expect(fixture.ticketSync.lastSyncAt).toBe('2026-09-30T01:15:00Z');
    expect(fixture.history[0]!.timestamp).toBe('2026-09-01');
    expect(screen.getByText(`${new Date(fixture.history[0]!.timestamp).toLocaleString()} · Reviewer`)).toBeInTheDocument();
  });

  it('uses an accessible native drawer and progressively discloses linked work/history', () => {
    // Arrange
    render(<MemoryRouter><PoamDetailDrawer poamId="poam-a" onClose={vi.fn()} /></MemoryRouter>);
    // Act
    const drawer = screen.getByRole('dialog', { name: 'Review privileged accounts' });
    // Assert
    expect(drawer.tagName).toBe('DIALOG');
    expect(screen.getByRole('button', { name: 'Overview' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.queryByText('Initial deadline retained')).toBeNull();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Evidence & history' }));
    // Assert
    expect(screen.getByText('Initial deadline retained')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Review supporting evidence' })).toHaveAttribute('href', '/systems/system-a/evidence');
  });
});
