import { createContext, forwardRef, useCallback, useContext, type ReactNode } from 'react';
import {
  Link as RouterLink,
  NavLink as RouterNavLink,
  Navigate as RouterNavigate,
  useLocation as useRouterLocation,
  useNavigate as useRouterNavigate,
  type LinkProps,
  type NavLinkProps,
  type NavigateProps,
  type NavigateFunction,
  type NavigateOptions,
  type To,
} from 'react-router-dom';
import { buildWorkspaceUrl, parseWorkspaceUrl, type WorkspaceTarget } from './workspaceRoutes';

export * from 'react-router-dom';

const WorkspaceNavigationContext = createContext<WorkspaceTarget | null>(null);

export function WorkspaceNavigationProvider({ workspace, children }: {
  workspace: WorkspaceTarget;
  children: ReactNode;
}) {
  return <WorkspaceNavigationContext.Provider value={workspace}>{children}</WorkspaceNavigationContext.Provider>;
}

function scopedPath(workspace: WorkspaceTarget | null, path: string): string {
  if (!workspace || !path.startsWith('/') || path.startsWith('//')
    || /^\/(?:workspaces|login)(?:[/?#]|$)/.test(path)) return path;
  return buildWorkspaceUrl(workspace, path);
}

function scopedDestination(workspace: WorkspaceTarget | null, to: To): To {
  if (typeof to === 'string') return scopedPath(workspace, to);
  return to.pathname === undefined ? to : { ...to, pathname: scopedPath(workspace, to.pathname) };
}

export function useWorkspaceHref() {
  const workspace = useContext(WorkspaceNavigationContext);
  return useCallback((path: string) => scopedPath(workspace, path), [workspace]);
}

export const Link = forwardRef<HTMLAnchorElement, LinkProps>(function WorkspaceLink(props, ref) {
  const workspace = useContext(WorkspaceNavigationContext);
  return <RouterLink {...props} ref={ref} to={scopedDestination(workspace, props.to)} />;
});

export const NavLink = forwardRef<HTMLAnchorElement, NavLinkProps>(function WorkspaceNavLink(props, ref) {
  const workspace = useContext(WorkspaceNavigationContext);
  return <RouterNavLink {...props} ref={ref} to={scopedDestination(workspace, props.to)} />;
});

export function Navigate(props: NavigateProps) {
  const workspace = useContext(WorkspaceNavigationContext);
  return <RouterNavigate {...props} to={scopedDestination(workspace, props.to)} />;
}

export function useNavigate(): NavigateFunction {
  const navigate = useRouterNavigate();
  const workspace = useContext(WorkspaceNavigationContext);
  return useCallback((to: To | number, options?: NavigateOptions) => {
    if (typeof to === 'number') return navigate(to);
    const destination = scopedDestination(workspace, to);
    return options === undefined ? navigate(destination) : navigate(destination, options);
  }, [navigate, workspace]);
}

export function useLocation() {
  const location = useRouterLocation();
  const workspace = useContext(WorkspaceNavigationContext);
  if (!workspace) return location;
  const parsed = parseWorkspaceUrl(location.pathname);
  if (!parsed || buildWorkspaceUrl(parsed.workspace) !== buildWorkspaceUrl(workspace)) {
    throw new Error('Workspace navigation context does not match the current URL.');
  }
  return { ...location, pathname: parsed.route };
}
