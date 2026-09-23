import { act, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeProvider } from '../../features/auth/MeProvider';
import { useMe } from '../../features/auth/useMe';
import type { MeResponse } from '../../features/auth/types';

const http = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock('axios', () => ({ default: http }));

function identity(tenantId: string): MeResponse {
  const tenant = { id: tenantId, displayName: tenantId, status: 'Active' as const };
  return {
    oid: 'synthetic-user', displayName: 'Synthetic user', persona: 'MissionOwner',
    homeTenant: null, effectiveTenant: tenant, isImpersonating: false, impersonation: null,
    pimRoles: [], isCspAdmin: false, isSocAnalyst: false, tenantMemberships: [tenant],
  };
}

function envelope(tenantId: string) {
  return { data: { status: 'success', data: identity(tenantId) } };
}

function Probe({ name, contextKey, observations }: {
  name: string;
  contextKey?: string;
  observations?: string[];
}) {
  const result = useMe();
  observations?.push(`${contextKey}:${result.data?.effectiveTenant?.id ?? 'none'}`);
  return <output aria-label={name}>{result.isLoading ? 'loading' : result.error?.message ?? result.data?.effectiveTenant?.id ?? 'none'}</output>;
}

beforeEach(() => { http.get.mockReset(); });

describe('shared authenticated context', () => {
  it('shares one server response among consumers and refetches once on a tenant event', async () => {
    // Arrange
    http.get.mockResolvedValue(envelope('org-alpha'));
    render(
      <MeProvider contextKey="org-alpha">
        <Probe name="first" /><Probe name="second" />
      </MeProvider>,
    );
    await waitFor(() => expect(screen.getByLabelText('first')).toHaveTextContent('org-alpha'));

    // Act
    await act(async () => { window.dispatchEvent(new CustomEvent('ato:tenant-changed', { detail: { tenantId: 'org-alpha' } })); });

    // Assert
    expect(http.get).toHaveBeenCalledTimes(2);
    expect(screen.getByLabelText('second')).toHaveTextContent('org-alpha');
  });

  it('never returns previous-context identity while the new context loads', async () => {
    // Arrange
    const observations: string[] = [];
    http.get.mockResolvedValueOnce(envelope('org-alpha')).mockReturnValueOnce(new Promise(() => {}));
    const page = render(
      <MeProvider contextKey="org-alpha"><Probe name="identity" contextKey="alpha" observations={observations} /></MeProvider>,
    );
    await waitFor(() => expect(screen.getByLabelText('identity')).toHaveTextContent('org-alpha'));

    // Act
    page.rerender(<MeProvider contextKey="org-beta"><Probe name="identity" contextKey="beta" observations={observations} /></MeProvider>);

    // Assert
    expect(screen.getByLabelText('identity')).toHaveTextContent('loading');
    expect(observations).not.toContain('beta:org-alpha');
  });

  it('ignores a late previous-context response', async () => {
    // Arrange
    let completeOld!: (value: ReturnType<typeof envelope>) => void;
    http.get.mockReturnValueOnce(new Promise(resolve => { completeOld = resolve; }))
      .mockResolvedValueOnce(envelope('org-beta'));
    const page = render(<MeProvider contextKey="org-alpha"><Probe name="identity" /></MeProvider>);
    page.rerender(<MeProvider contextKey="org-beta"><Probe name="identity" /></MeProvider>);
    await waitFor(() => expect(screen.getByLabelText('identity')).toHaveTextContent('org-beta'));

    // Act
    await act(async () => completeOld(envelope('org-alpha')));

    // Assert
    expect(screen.getByLabelText('identity')).toHaveTextContent('org-beta');
  });

  it('clears the old identity when a refreshed permission request fails', async () => {
    // Arrange
    http.get.mockResolvedValueOnce(envelope('org-alpha')).mockRejectedValueOnce(new Error('Access revoked'));
    render(<MeProvider contextKey="org-alpha"><Probe name="identity" /></MeProvider>);
    await waitFor(() => expect(screen.getByLabelText('identity')).toHaveTextContent('org-alpha'));

    // Act
    await act(async () => { window.dispatchEvent(new CustomEvent('ato:tenant-changed', { detail: { tenantId: 'org-alpha' } })); });

    // Assert
    expect(screen.getByLabelText('identity')).toHaveTextContent('Access revoked');
    expect(screen.queryByText('org-alpha')).not.toBeInTheDocument();
  });

  it('does not request identity on public routes when disabled', () => {
    // Arrange
    const contextKey = 'public';

    // Act
    render(<MeProvider contextKey={contextKey} enabled={false}><Probe name="identity" /></MeProvider>);

    // Assert
    expect(http.get).not.toHaveBeenCalled();
    expect(screen.getByLabelText('identity')).toHaveTextContent('none');
  });
});
