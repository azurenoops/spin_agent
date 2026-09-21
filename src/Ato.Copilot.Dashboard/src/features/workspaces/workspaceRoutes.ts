export type WorkspaceTarget =
  | { kind: 'csp' }
  | { kind: 'organization'; tenantId: string };

export interface WorkspaceLocation {
  workspace: WorkspaceTarget;
  route: string;
}

const WORKSPACE_ROOT = '/workspaces';
const INVALID_URL = 'Invalid workspace URL';
const INVALID_SEGMENT = /[/\\%?#\u0000-\u0020\u007f]/;

const SYSTEM_ALIASES: Readonly<Record<string, string>> = {
  'control-inheritance': 'inheritance',
  categorization: 'baseline',
  capabilities: 'capability-coverage',
  'mission-purpose': 'profile/MissionAndPurpose',
  'users-access': 'profile/UsersAndAccess',
  environment: 'profile/EnvironmentAndDeployment',
  'data-types': 'profile/DataTypes',
  'ports-protocols': 'profile/PortsProtocolsAndServices',
  'leveraged-auth': 'profile/LeveragedAuthorizations',
  'legal-regulatory': 'legal',
};

function validateSegment(segment: string): void {
  if (!segment || segment === '.' || segment === '..' || INVALID_SEGMENT.test(segment)) {
    throw new Error(INVALID_URL);
  }
}

function decodeSegment(segment: string): string {
  let decoded: string;
  try {
    decoded = decodeURIComponent(segment);
  } catch (error) {
    if (error instanceof URIError) throw new Error(INVALID_URL, { cause: error });
    throw error;
  }
  validateSegment(decoded);
  return decoded;
}

function splitLocalUrl(url: string): { pathname: string; suffix: string } {
  if (!url.startsWith('/') || url.startsWith('//') || /[\\\u0000-\u0020\u007f]/.test(url)) {
    throw new Error(INVALID_URL);
  }
  const suffixIndex = url.search(/[?#]/);
  const pathname = suffixIndex === -1 ? url : url.slice(0, suffixIndex);
  const suffix = suffixIndex === -1 ? '' : url.slice(suffixIndex);
  const segments = pathname.slice(1).split('/');
  if (segments.at(-1) === '') segments.pop();
  segments.forEach(decodeSegment);
  return { pathname, suffix };
}

function isWorkspacePath(pathname: string): boolean {
  return pathname === WORKSPACE_ROOT || pathname.startsWith(`${WORKSPACE_ROOT}/`);
}

export function parseWorkspaceUrl(url: string): WorkspaceLocation | null {
  const { pathname, suffix } = splitLocalUrl(url);
  if (!isWorkspacePath(pathname)) return null;
  const segments = pathname.split('/');
  let workspace: WorkspaceTarget;
  let routeStart: number;
  if (segments[2] === 'csp') {
    workspace = { kind: 'csp' };
    routeStart = 3;
  } else if (segments[2] === 'organizations' && segments[3]) {
    workspace = { kind: 'organization', tenantId: decodeSegment(segments[3]) };
    routeStart = 4;
  } else {
    throw new Error(INVALID_URL);
  }
  const route = `/${segments.slice(routeStart).join('/')}${suffix}`;
  return { workspace, route };
}

export function buildWorkspaceUrl(workspace: WorkspaceTarget, route = '/'): string {
  const { pathname, suffix } = splitLocalUrl(route);
  if (isWorkspacePath(pathname)) throw new Error(INVALID_URL);
  let prefix: string;
  if (workspace.kind === 'organization') {
    validateSegment(workspace.tenantId);
    prefix = `${WORKSPACE_ROOT}/organizations/${encodeURIComponent(workspace.tenantId)}`;
  } else {
    prefix = `${WORKSPACE_ROOT}/csp`;
  }
  return `${prefix}${pathname === '/' ? '' : pathname}${suffix}`;
}

export function canonicalizeSystemRoute(route: string): string {
  const { pathname, suffix } = splitLocalUrl(route);
  const match = /^\/systems\/([^/]+)\/([^/]+)\/?$/i.exec(pathname);
  if (!match || decodeSegment(match[1]!).toLowerCase() === 'new') return route;
  const alias = decodeSegment(match[2]!).toLowerCase();
  const target = Object.hasOwn(SYSTEM_ALIASES, alias) ? SYSTEM_ALIASES[alias] : undefined;
  return target ? `/systems/${match[1]}/${target}${suffix}` : route;
}
