import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import SystemTaskNavigation from '../../features/systems/SystemTaskNavigation';
import { isSystemScreenActive, SYSTEM_SCREEN_GROUPS } from '../../features/systems/systemScreenRoutes';

function RouteView() {
  const location = useLocation();
  return <><SystemTaskNavigation /><output>{location.pathname}{location.search}</output></>;
}

describe('Systems task navigation', () => {
  it('exposes all System definition tasks as the mock horizontal group tabs', () => {
    // Arrange / Act
    render(<MemoryRouter initialEntries={['/systems/a/profile/MissionAndPurpose']}>
      <Routes><Route path="/systems/:id/*" element={<RouteView />} /></Routes>
    </MemoryRouter>);
    // Assert
    expect(screen.getByRole('link', { name: 'Mission' })).toHaveAttribute('aria-current', 'page');
    for (const label of ['Users', 'Environment & hosting', 'Data', 'Inventory & boundary', 'Ports & interconnections']) {
      expect(screen.getByRole('link', { name: label })).toBeVisible();
    }
  });

  it('connects hosting, applied capabilities, responsibilities and documents without leaving the selected system', () => {
    // Arrange
    render(<MemoryRouter initialEntries={['/systems/a/profile/EnvironmentAndDeployment/hosting']}>
      <Routes><Route path="/systems/:id/*" element={<RouteView />} /></Routes>
    </MemoryRouter>);
    // Act
    fireEvent.click(screen.getByRole('link', { name: 'Applied capabilities' }));
    // Assert
    expect(screen.getByRole('status')).toHaveTextContent('/systems/a/security-capabilities');
    expect(screen.getByRole('link', { name: 'Applied capabilities' })).toHaveAttribute('aria-current', 'page');
    // Act
    fireEvent.click(screen.getByRole('link', { name: 'Responsibilities' }));
    // Assert
    expect(screen.getByRole('status')).toHaveTextContent('/systems/a/inheritance/subscriptions');
    expect(screen.getByRole('link', { name: 'Control inheritance summary' })).toHaveAttribute('href', '/systems/a/inheritance');
    // Act
    fireEvent.click(screen.getByRole('link', { name: 'Documents' }));
    // Assert
    expect(screen.getByRole('status')).toHaveTextContent('/systems/a/documents');
  });

  it('marks only the chosen document or assessment task active', () => {
    // Arrange
    const base = '/systems/a';
    // Act / Assert
    expect(isSystemScreenActive('documents?tab=exports', `${base}/documents`, '?tab=exports', base)).toBe(true);
    expect(isSystemScreenActive('documents', `${base}/documents`, '?tab=exports', base)).toBe(false);
    expect(isSystemScreenActive('assessments?tab=plan', `${base}/assessments`, '?tab=plan', base)).toBe(true);
    expect(isSystemScreenActive('assessments', `${base}/assessments`, '?tab=plan', base)).toBe(false);
    expect(isSystemScreenActive('conmon', `${base}/conmon/reports`, '', base)).toBe(false);
    expect(isSystemScreenActive('boundaries', `${base}/security-capabilities/inventory`, '', base, true)).toBe(true);
    expect(isSystemScreenActive('security-capabilities', `${base}/security-capabilities/inventory`, '', base)).toBe(false);
    expect(isSystemScreenActive('inheritance/subscriptions', `${base}/inheritance`, '', base, true)).toBe(true);
    expect(isSystemScreenActive('documents', `${base}/documents`, '?tab=unknown', base)).toBe(true);
  });

  it('has a real destination for every mock-defined system task', () => {
    // Arrange
    const items = SYSTEM_SCREEN_GROUPS.flatMap(group => group.items);
    // Act
    const unavailable = items.filter(item => item.unavailable).map(item => item.path);
    // Assert
    expect(unavailable).toEqual([]);
  });
});
