import { useMemo } from 'react';
import { useLocation } from 'react-router-dom';
import { useSystemContext } from './useSystemContext';
import type { ChatContext } from '../types/chat';

const PAGE_MAP: Record<string, string> = {
  '/': 'portfolio',
  '/portfolio': 'portfolio',
  '/capabilities': 'capabilities',
  '/assessments': 'assessments',
  '/remediation': 'remediation',
  '/audit': 'audit',
  '/admin/migration': 'admin-migration',
  // fix(#496): /systems list page was falling through to 'unknown'
  '/systems': 'systems',
};

function resolvePageName(pathname: string): string {
  if (PAGE_MAP[pathname]) return PAGE_MAP[pathname];
  // Wave 6 GAP-006
  if (pathname.includes('/gap-analysis')) return 'gap-analysis';
  if (pathname.includes('/boundaries')) return 'boundaries';
  if (pathname.includes('/components')) return 'components';
  if (pathname.includes('/gaps')) return 'gap-analysis';
  if (pathname.includes('/roadmap') || pathname.includes('/implementation-roadmap')) return 'roadmap';
  if (pathname.includes('/documents')) return 'documents';
  if (pathname.includes('/narratives')) return 'narratives';
  if (pathname.includes('/legal') || pathname.includes('/legal-regulatory')) return 'legal';
  if (pathname.includes('/conmon')) return 'conmon';
  if (pathname.includes('/deviations')) return 'deviations';
  if (pathname.includes('/assessments')) return 'assessments';
  if (pathname.includes('/remediation')) return 'remediation';
  if (pathname.includes('/evidence')) return 'evidence';
  if (pathname.includes('/poam')) return 'poam';
  if (pathname.includes('/inheritance') || pathname.includes('/control-inheritance')) return 'inheritance';
  if (pathname.includes('/baseline') || pathname.includes('/categorization')) return 'baseline';
  if (pathname.includes('/capability-coverage') || pathname.includes('/capabilities')) return 'capabilities';
  // fix(#526): alias sub-pages that live under /systems/:id before the
  // SystemRedirect fires — the chat context is evaluated while the browser
  // still shows the alias URL, causing 'Viewing: unknown'.
  if (
    pathname.includes('/mission-purpose') ||
    pathname.includes('/users-access') ||
    pathname.includes('/environment') ||
    pathname.includes('/data-types') ||
    pathname.includes('/ports-protocols') ||
    pathname.includes('/leveraged-auth')
  ) return 'system-profile';
  if (pathname.includes('/profile/')) return 'system-profile';
  // fix(#526,#523): Wave 9 profile slug aliases — must resolve to 'system-profile'
  // even before React Router performs the SystemRedirect to /systems/:id/profile/…
  if (
    pathname.includes('/mission-purpose') ||
    pathname.includes('/users-access') ||
    pathname.includes('/environment') ||
    pathname.includes('/data-types') ||
    pathname.includes('/ports-protocols') ||
    pathname.includes('/leveraged-auth')
  ) return 'system-profile';
  if (pathname.includes('/authorize')) return 'authorize';
  if (pathname.includes('/roles')) return 'roles';
  if (pathname.match(/^\/systems\/[^/]+$/)) return 'system-detail';
  return 'unknown';
}

export function useChatContext(): ChatContext {
  const location = useLocation();
  // fix(#722): ChatPanel renders outside the <Routes> tree, so useParams()
  // always returns {} there. Extract the system id directly from the URL
  // pathname with a regex — works at any render position in the tree.
  const systemId = location.pathname.match(/^\/systems\/([^/]+)/)?.[1] ?? null;
  const systemCtx = useSystemContext();

  return useMemo<ChatContext>(() => {
    const page = resolvePageName(location.pathname);
    return {
      page,
      systemId,
      boundaryId: null,
      entityType: null,
      entityId: null,
      rmfPhase: systemCtx?.currentRmfPhase ?? null,
      systemName: systemCtx?.name ?? null,
      pageData: systemCtx ? {
        complianceScore: systemCtx.keyMetrics?.complianceScore,
        narrativeCoverage: systemCtx.keyMetrics?.narrativeCoverage,
        catIFindings: systemCtx.keyMetrics?.catIFindings,
        catIIFindings: systemCtx.keyMetrics?.catIIFindings,
        catIIIFindings: systemCtx.keyMetrics?.catIIIFindings,
        totalFindings: systemCtx.keyMetrics?.totalFindings,
        openPoams: systemCtx.keyMetrics?.totalOpenPoams,
        overduePoams: systemCtx.keyMetrics?.overduePoams,
        atoStatus: systemCtx.keyMetrics?.atoStatus,
        atoDaysRemaining: systemCtx.keyMetrics?.atoDaysRemaining,
        baselineLevel: systemCtx.baselineLevel,
        hasCategorization: systemCtx.categorization != null,
        hasBaseline: !!systemCtx.baselineLevel && systemCtx.baselineLevel !== 'None',
        phaseCompletionPercent: systemCtx.rmfPhaseProgress?.find(
          (p) => p.status === 'current'
        )?.completionPercent,
      } : null,
    };
  }, [location.pathname, systemId, systemCtx]);
}
