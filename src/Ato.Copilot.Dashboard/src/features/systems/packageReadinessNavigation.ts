import type { PackagePurpose } from '../../api/package';

export const packagePurposeLabels: Record<PackagePurpose, string> = {
  Legacy: 'Existing authorization package (legacy validation)',
  InitialSubmission: 'Initial ATO submission',
  AuthorizedBaselineArchive: 'Authorized baseline archive',
  ChangeSubmission: 'Change submission — retained baseline and SSP change',
};

export function packagePurposeFromSearch(search: string): PackagePurpose | null {
  const purpose = new URLSearchParams(search).get('purpose');
  if (purpose === null) return 'Legacy';
  return purpose === 'Legacy' || purpose === 'InitialSubmission' || purpose === 'AuthorizedBaselineArchive'
    || purpose === 'ChangeSubmission' ? purpose : null;
}

const sourceRoots = new Set(['profile', 'boundaries', 'security-capabilities', 'inheritance', 'narratives',
  'evidence', 'legal', 'assessments', 'poam', 'remediation', 'deviations', 'documents', 'emass',
  'authorize', 'roles', 'history', 'baseline', 'categorization', 'settings', 'conmon', 'privacy', 'inventory']);

export function packageSourceHref(systemId: string, path: string, readinessSearch: string): string | null {
  const decoded = (() => { try { return decodeURIComponent(path); } catch { return ''; } })();
  if (!decoded || decoded.startsWith('/') || decoded.includes('\\') || /^[a-z][a-z0-9+.-]*:/i.test(decoded)
    || decoded.split(/[/?#]/).some(segment => segment === '.' || segment === '..')
    || !sourceRoots.has(decoded.split(/[/?#]/)[0]!)) return null;
  const base = `/systems/${encodeURIComponent(systemId)}`;
  const target = new URL(`${base}/${path}`, 'https://readiness.invalid');
  if (target.origin !== 'https://readiness.invalid' || !target.pathname.startsWith(`${base}/`)) return null;
  target.searchParams.set('readinessReturn', `${base}/documents${readinessSearch}`);
  return `${target.pathname}${target.search}${target.hash}`;
}

export function packageReturnHref(systemId: string, search: string): string | null {
  const target = new URLSearchParams(search).get('readinessReturn');
  if (!target || target.includes('\\') || target.includes('#')) return null;
  const expected = `/systems/${encodeURIComponent(systemId)}/documents`;
  const [path, query] = target.split('?', 2);
  if (path !== expected || packagePurposeFromSearch(query ?? '') === null) return null;
  return target;
}
