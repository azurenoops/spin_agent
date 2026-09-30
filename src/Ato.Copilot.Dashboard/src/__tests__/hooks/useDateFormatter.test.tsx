import { act, renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SettingsContext, useSettingsProvider } from '../../hooks/useSettings';
import { useDateFormatter } from '../../hooks/useDateFormatter';

function Wrapper({ children }: { children: ReactNode }) {
  const value = useSettingsProvider();
  return <SettingsContext.Provider value={value}>{children}</SettingsContext.Provider>;
}
beforeEach(() => localStorage.clear());

describe('Personal record-date formatting', () => {
  it.each([
    ['US', '09/29/2026'], ['EU', '29/09/2026'], ['ISO', '2026-09-29'],
  ])('renders %s dates in the preferred zone without mutating the instant', (format, expected) => {
    // Arrange
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({ dateFormat: format, timezone: 'America/Los_Angeles' }));
    const timestamp = '2026-09-30T01:15:00Z';
    const original = new Date(timestamp);
    const { result } = renderHook(useDateFormatter, { wrapper: Wrapper });
    // Act
    const displayed = result.current.formatDate(original);
    // Assert
    expect(displayed).toBe(expected);
    expect(original.toISOString()).toBe('2026-09-30T01:15:00.000Z');
    expect(result.current.formatDateTime(timestamp)).toContain(expected);
    expect(result.current.formatDateTime(timestamp)).toContain('18:15');
  });

  it('keeps calendar deadlines on their recorded day regardless of preferred zone', () => {
    // Arrange
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({ dateFormat: 'ISO', timezone: 'America/Los_Angeles' }));
    const { result } = renderHook(useDateFormatter, { wrapper: Wrapper });
    // Act / Assert
    expect(result.current.formatCalendarDate('2026-10-05T00:00:00Z')).toBe('2026-10-05');
    expect(result.current.formatDate('2026-10-05')).toBe('2026-10-05');
  });

  it('updates rendered formatting immediately when preferences change', () => {
    // Arrange
    const { result } = renderHook(() => {
      const context = useSettingsProvider();
      return context;
    });
    const { result: formatted, rerender } = renderHook(useDateFormatter, {
      wrapper: ({ children }) => <SettingsContext.Provider value={result.current}>{children}</SettingsContext.Provider>,
    });
    // Act
    act(() => result.current.updateSettings({ dateFormat: 'ISO', timezone: 'UTC' }));
    rerender();
    // Assert
    expect(formatted.current.formatDateTime('2026-09-30T01:15:00Z')).toBe('2026-09-30 01:15:00 UTC');
  });

  it('handles missing and invalid dates and reports invalid stored time zones', () => {
    // Arrange
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({ timezone: 'Invalid/Zone' }));
    const warning = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const { result } = renderHook(useDateFormatter, { wrapper: Wrapper });
    // Act / Assert
    expect(result.current.formatDate(null)).toBe('—');
    expect(result.current.formatDate('not a date')).toBe('—');
    expect(result.current.formatDateTime('2026-09-30T01:15:00Z')).toContain('UTC');
    expect(warning).toHaveBeenCalledWith('Invalid display time zone; using UTC.');
    warning.mockRestore();
  });
});
