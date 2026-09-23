import { Navigate, useLocation } from 'react-router-dom';
import { buildWorkspaceUrl, canonicalizeSystemRoute, parseWorkspaceUrl } from './workspaceRoutes';

export default function SystemAliasRedirect() {
  const location = useLocation();
  const url = `${location.pathname}${location.search}${location.hash}`;
  const context = parseWorkspaceUrl(url);
  const canonical = canonicalizeSystemRoute(context?.route ?? url);
  const destination = context ? buildWorkspaceUrl(context.workspace, canonical) : canonical;
  return <Navigate to={destination} replace />;
}
