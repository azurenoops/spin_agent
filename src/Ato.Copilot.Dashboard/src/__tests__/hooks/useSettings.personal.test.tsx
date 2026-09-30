import { act, render, renderHook, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { DEFAULT_SETTINGS, useSettingsProvider } from '../../hooks/useSettings';
import dashboardCss from '../../index.css?raw';

beforeEach(() => localStorage.clear());
afterEach(() => {
  delete document.documentElement.dataset.tableDensity;
});

describe('Personal preferences', () => {
  it('keeps the previous null-storage fallback when resolving and updating width', () => {
    // Arrange
    localStorage.setItem('ato-dashboard-settings', 'null');
    // Act
    const { result } = renderHook(useSettingsProvider);
    act(() => result.current.updateSettings({ tableDensity: 'compact' }));
    // Assert
    expect(result.current.settings.chatPanelWidth).toBe(420);
    expect(result.current.settings.tableDensity).toBe('compact');
  });

  it('resets only personal display and assistant presentation fields', () => {
    // Arrange
    const retained = {
      displayName: 'Synthetic user', organization: 'Synthetic organization', role: 'ISSO',
      poamOverdueAlerts: false, atoExpirationAlerts: false, complianceDriftAlerts: false, alertDaysBefore: 60,
      defaultLandingPage: '/assessments', defaultRemediationView: 'kanban', autoRefreshInterval: 0,
      showSummaryCards: false, chatVerbosity: 'concise', defaultExportFormat: 'xlsx',
      activeFramework: 'CNSSI 1253', baselineOverride: 'High', poamOverdueThreshold: 20,
      emassEnabled: true, acasEnabled: true, prismaCloudEnabled: true, stigViewerEnabled: true,
      sharePointSiteUrl: 'https://example.invalid', sourceDocuments: 'Retained document',
      sessionTimeout: 120, enableAnalytics: false, debugMode: true,
      futureCompatibilityKey: 'retained',
    };
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({
      ...DEFAULT_SETTINGS, ...retained, theme: 'dark', tableDensity: 'compact',
      dateFormat: 'ISO', timezone: 'UTC', showQuickActions: false, chatPanelWidth: 580,
    }));
    const { result } = renderHook(useSettingsProvider);
    // Act
    act(() => result.current.resetSettings());
    // Assert
    expect(result.current.settings).toEqual({ ...DEFAULT_SETTINGS, ...retained });
  });

  it('changes actual table-cell padding and restores comfortable page styling', () => {
    // Arrange
    const style = document.createElement('style');
    style.textContent = `td, th { padding-top: 12px; padding-bottom: 12px; }\n${dashboardCss.replace(/@tailwind[^;]+;/g, '')}`;
    document.head.append(style);
    const { result, unmount } = renderHook(useSettingsProvider);
    render(<table><tbody><tr><td>Record</td></tr></tbody></table>);
    const cell = screen.getByRole('cell');
    // Act
    act(() => result.current.updateSettings({ tableDensity: 'compact' }));
    // Assert
    expect(document.documentElement).toHaveAttribute('data-table-density', 'compact');
    expect(getComputedStyle(cell).paddingTop).toBe('0.375rem');
    act(() => result.current.updateSettings({ tableDensity: 'comfortable' }));
    expect(getComputedStyle(cell).paddingTop).toBe('12px');
    unmount();
    style.remove();
  });

  it.each([[10, 320], [900, 600], [512, 512], [Number.NaN, 420]])('bounds chat width %s to %s', (input, expected) => {
    // Arrange
    const { result } = renderHook(useSettingsProvider);
    // Act
    act(() => result.current.updateSettings({ chatPanelWidth: input }));
    // Assert
    expect(result.current.settings.chatPanelWidth).toBe(expected);
  });
});
