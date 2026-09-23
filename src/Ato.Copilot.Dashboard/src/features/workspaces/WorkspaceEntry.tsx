import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useMe } from '../auth/useMe';
import type { MeResponse } from '../auth/types';
import { WorkspaceStatus } from './WorkspaceBoundary';
import { workspaceErrorMessage } from './api';
import { buildWorkspaceUrl, parseWorkspaceUrl, type WorkspaceTarget } from './workspaceRoutes';
import type { WorkspaceOption } from './types';

export function hasWorkspaceContract(identity: MeResponse): boolean {
  return ['workspace', 'availableWorkspaces', 'availableWorkspacesTotal', 'permissions']
    .some(key => Object.hasOwn(identity, key));
}

export function optionTarget(option: WorkspaceOption): WorkspaceTarget {
  if (!option || typeof option.displayName !== 'string' || typeof option.status !== 'string'
    || typeof option.onboardingState !== 'string') throw new Error('The workspace API returned incomplete choices.');
  if (option.kind === 'csp' && option.tenantId === null) return { kind: 'csp' };
  if (option.kind === 'organization' && typeof option.tenantId === 'string' && option.tenantId) {
    const target: WorkspaceTarget = { kind: 'organization', tenantId: option.tenantId };
    buildWorkspaceUrl(target);
    return target;
  }
  throw new Error('The workspace API returned incomplete choices.');
}

export function safeDeepLink(value: unknown): string {
  if (typeof value !== 'string') return '/';
  try {
    parseWorkspaceUrl(value);
    return value.startsWith('/login') ? '/' : value;
  } catch {
    return '/';
  }
}

export default function WorkspaceEntry({ children }: { children: ReactNode }) {
  const { data: identity, isLoading, error, refetch } = useMe();
  const location = useLocation();
  if (isLoading) return <WorkspaceStatus loading message="Resolving your authorized workspaces..." />;
  if (error) return <WorkspaceStatus message={workspaceErrorMessage(error)} onRetry={refetch} />;
  if (!identity) return <WorkspaceStatus message="Your authenticated identity could not be resolved." onRetry={refetch} />;
  if (!hasWorkspaceContract(identity)) return <>{children}</>;
  const choices = identity.availableWorkspaces;
  const total = identity.availableWorkspacesTotal;
  if (!Object.hasOwn(identity, 'workspace') || !Array.isArray(choices)
    || !Number.isInteger(total) || total! < choices.length || total! < 0) {
    return <WorkspaceStatus message="The workspace API returned incomplete authorization information." onRetry={refetch} />;
  }
  const deepLink = location.pathname + location.search + location.hash;
  try {
    choices.forEach(optionTarget);
    // A legacy system URL does not identify its owning organization. Only the
    // server's single authorized choice, or an explicit user choice, can select it.
    if (total === 1 && choices.length === 1 && choices[0]!.status !== 'Disabled') {
      return <Navigate replace to={buildWorkspaceUrl(optionTarget(choices[0]!), deepLink)} state={location.state} />;
    }
  } catch (reason) {
    return <WorkspaceStatus message={workspaceErrorMessage(reason)} onRetry={refetch} />;
  }
  return <Navigate replace to="/login/select-tenant" state={{ deepLink }} />;
}
