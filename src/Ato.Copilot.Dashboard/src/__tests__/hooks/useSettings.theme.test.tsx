import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DEFAULT_SETTINGS, useSettingsProvider } from '../../hooks/useSettings';
import tailwindConfiguration from '../../../tailwind.config.js?raw';

let prefersDark = false;
let notify: (() => void) | undefined;
const removeListener = vi.fn();

beforeEach(() => {
  localStorage.clear();
  prefersDark = false;
  notify = undefined;
  removeListener.mockClear();
  vi.stubGlobal('matchMedia', vi.fn(() => ({
    get matches() { return prefersDark; },
    addEventListener: (_event: string, listener: () => void) => { notify = listener; },
    removeEventListener: removeListener,
  })));
});

afterEach(() => {
  vi.unstubAllGlobals();
  document.documentElement.classList.remove('dark');
  document.documentElement.style.removeProperty('color-scheme');
});

describe('Dashboard theme preference', () => {
  it('uses explicit root-class dark variants rather than independent OS media queries', () => {
    // Arrange
    const configuration = tailwindConfiguration;
    // Act
    const classDriven = /darkMode:\s*['"]class['"]/.test(configuration);
    // Assert
    expect(classDriven).toBe(true);
  });

  it('defaults legacy preferences to Light even when the OS prefers dark', () => {
    // Arrange
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({ displayName: 'Existing user' }));
    prefersDark = true;
    // Act
    const { result } = renderHook(() => useSettingsProvider());
    // Assert
    expect(result.current.settings.theme).toBe('light');
    expect(result.current.settings.displayName).toBe('Existing user');
    expect(document.documentElement).not.toHaveClass('dark');
    expect(document.documentElement.style.colorScheme).toBe('light');
  });

  it('follows OS changes only after explicitly selecting System', () => {
    // Arrange
    const { result, unmount } = renderHook(() => useSettingsProvider());
    act(() => result.current.updateSettings({ theme: 'system' }));
    // Act
    act(() => { prefersDark = true; notify?.(); });
    // Assert
    expect(document.documentElement).toHaveClass('dark');
    expect(document.documentElement.style.colorScheme).toBe('dark');
    act(() => { prefersDark = false; notify?.(); });
    expect(document.documentElement).not.toHaveClass('dark');
    expect(document.documentElement.style.colorScheme).toBe('light');
    unmount();
    expect(removeListener).toHaveBeenCalled();
  });

  it('persists explicit dark and light choices across remounts without replacing other settings', async () => {
    // Arrange
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({ ...DEFAULT_SETTINGS, displayName: 'Existing user' }));
    const first = renderHook(() => useSettingsProvider());
    // Act
    act(() => first.result.current.updateSettings({ theme: 'dark' }));
    // Assert
    expect(document.documentElement).toHaveClass('dark');
    await waitFor(() => expect(JSON.parse(localStorage.getItem('ato-dashboard-settings')!).theme).toBe('dark'));
    first.unmount();
    const second = renderHook(() => useSettingsProvider());
    expect(second.result.current.settings.theme).toBe('dark');
    expect(second.result.current.settings.displayName).toBe('Existing user');
    act(() => {
      prefersDark = true;
      second.result.current.updateSettings({ theme: 'light' });
    });
    expect(document.documentElement).not.toHaveClass('dark');
    expect(document.documentElement.style.colorScheme).toBe('light');
    await waitFor(() => expect(JSON.parse(localStorage.getItem('ato-dashboard-settings')!).theme).toBe('light'));
    second.unmount();
    const third = renderHook(() => useSettingsProvider());
    expect(third.result.current.settings.theme).toBe('light');
    expect(document.documentElement).not.toHaveClass('dark');
  });

  it('reset restores Light even on a dark OS', () => {
    // Arrange
    localStorage.setItem('ato-dashboard-settings', JSON.stringify({ ...DEFAULT_SETTINGS, theme: 'dark' }));
    prefersDark = true;
    const { result } = renderHook(() => useSettingsProvider());
    // Act
    act(() => result.current.resetSettings());
    // Assert
    expect(result.current.settings.theme).toBe('light');
    expect(document.documentElement).not.toHaveClass('dark');
  });
});
