import { useMemo } from 'react';
import { useLocation, useParams } from 'react-router-dom';
import { useSystemContext } from './useSystemContext';
import type { ChatContext } from '../types/chat';

const PAGE_MAP: Record<string, string> = {
  '/': 'portfolio',
  '/capabilities': 'capabilities',
  '/assessments': 'assessments',
  '/remediation': 'remediation',
};

function resolvePageName(pathname: string): string {
  if (PAGE_MAP[pathname]) return PAGE_MAP[pathname];
  if (pathname.includes('/boundaries')) return 'boundaries';
  if (pathname.includes('/components')) return 'components';
  if (pathname.includes('/gaps')) return 'gap-analysis';
  if (pathname.includes('/roadmap')) return 'roadmap';
  if (pathname.includes('/documents')) return 'documents';
  if (pathname.includes('/narratives')) return 'narratives';
  if (pathname.match(/^\/systems\/[^/]+$/)) return 'system-detail';
  return 'unknown';
}

export function useChatContext(): ChatContext {
  const location = useLocation();
  const params = useParams<{ id?: string }>();
  const systemCtx = useSystemContext();

  return useMemo<ChatContext>(() => {
    const page = resolvePageName(location.pathname);
    return {
      page,
      systemId: params.id ?? null,
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
  }, [location.pathname, params.id, systemCtx]);
}
