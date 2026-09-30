import type { ReactNode } from 'react';
import { beforeEach, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import AuditLogPage from '../../pages/AuditLogPage';
import apiClient from '../../api/client';

vi.mock('../../components/layout/PageLayout', () => ({ default: ({ children }: { children: ReactNode }) => <main>{children}</main> }));
vi.mock('../../components/layout/PageHero', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/client', () => ({ default: { get: vi.fn() } }));
vi.mock('../../hooks/usePolling', async () => {
  const { useEffect } = await import('react');
  return { usePolling: (callback: () => Promise<unknown>) => {
    useEffect(() => { void callback(); }, [callback]);
    return { refresh: callback };
  } };
});
beforeEach(() => vi.clearAllMocks());

it('uses the registered audit endpoint and unwraps its real response envelope', async () => {
  // Arrange
  vi.mocked(apiClient.get).mockResolvedValue({ data: { status: 'success', data: {
    items: [{ id: 'event-a', action: 'Export', timestamp: '2026-09-27T12:00:00Z', actorDisplayName: 'Provider operator',
      actorUserId: 'actor-a', entityType: 'Package', entityId: 'package-a', detail: 'Retained export created',
      tenantId: null, impersonatedTenantName: null, ipAddress: null, surface: null, correlationId: 'correlation-a' }],
    page: 1, pageSize: 50, totalCount: 1,
  } } });
  // Act
  render(<AuditLogPage />);
  // Assert
  expect(await screen.findByText('Provider operator')).toBeVisible();
  expect(screen.getByText('Retained export created')).toBeVisible();
  expect(apiClient.get).toHaveBeenCalledWith('/audit?page=1&pageSize=50', { baseURL: '/api' });
});

it('reports an invalid audit envelope rather than presenting empty history', async () => {
  // Arrange
  vi.mocked(apiClient.get).mockResolvedValue({ data: { status: 'success', data: null } });
  // Act
  render(<AuditLogPage />);
  // Assert
  await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('audit response'));
  expect(screen.queryByText('No events match your filters.')).not.toBeInTheDocument();
});
