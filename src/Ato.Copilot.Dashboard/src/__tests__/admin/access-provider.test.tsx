import { type ReactNode } from 'react';
import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { getMock, postMock } = vi.hoisted(() => ({
  getMock: vi.fn(),
  postMock: vi.fn(),
}));

vi.mock('axios', () => ({
  default: {
    get: getMock,
    post: postMock,
  },
}));

vi.mock('@azure/msal-react', () => ({
  useIsAuthenticated: () => false,
}));

import {
  EffectiveAccessProvider,
  useEffectiveAccess,
  type EffectiveAccess,
} from '../../features/admin/access';

const access = (tenantId: string, name: string): EffectiveAccess => ({
  version: '1',
  generatedAt: new Date().toISOString(),
  subject: {
    objectId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    displayName: 'Test User',
    tenantId,
    isCspAdmin: false,
  },
  defaultDestinationId: `administration:organization:${tenantId}`,
  destinations: [{
    id: `administration:organization:${tenantId}`,
    workspace: 'Administration',
    scopeKind: 'Organization',
    scopeId: tenantId,
    displayName: name,
    actions: ['organization.overview.view'],
    badges: [{ label: 'Administrator', source: 'OrganizationRoleAssignment' }],
    availability: 'Available',
  }],
});

const envelope = (data: EffectiveAccess) => ({
  data: { status: 'success', data },
});

const wrapper = ({ children }: { children: ReactNode }) => (
  <EffectiveAccessProvider>{children}</EffectiveAccessProvider>
);

beforeEach(() => {
  getMock.mockReset();
  postMock.mockReset();
  window.localStorage.clear();
});

describe('EffectiveAccessProvider', () => {
  it('loads server access for simulation sessions and ignores a delayed previous tenant response', async () => {
    // Arrange
    let resolveOld!: (value: ReturnType<typeof envelope>) => void;
    let resolveNew!: (value: ReturnType<typeof envelope>) => void;
    getMock
      .mockReturnValueOnce(new Promise((resolve) => { resolveOld = resolve; }))
      .mockReturnValueOnce(new Promise((resolve) => { resolveNew = resolve; }));
    const { result } = renderHook(() => useEffectiveAccess(), { wrapper });
    await waitFor(() => expect(getMock).toHaveBeenCalledTimes(1));

    // Act
    act(() => {
      window.dispatchEvent(new CustomEvent('ato:tenant-changed', {
        detail: { tenantId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb' },
      }));
    });
    await waitFor(() => expect(getMock).toHaveBeenCalledTimes(2));
    await act(async () => {
      resolveNew(envelope(access(
        'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
        'New organization',
      )));
    });
    await waitFor(() => expect(result.current.access?.subject.tenantId)
      .toBe('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'));
    await act(async () => {
      resolveOld(envelope(access(
        'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
        'Old organization',
      )));
    });

    // Assert
    expect(result.current.access?.subject.tenantId)
      .toBe('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb');
    expect(result.current.selectedDestination?.displayName).toBe('New organization');
  });
});
