/**
 * Card Router (FR-011, T046)
 *
 * Selects the appropriate Adaptive Card builder based on intentType and data.type.
 * Priority-ordered rules per contracts/adaptive-cards.json CardRouting.
 */

import type { McpResponse } from "../services/atoApiClient";
import { buildComplianceCard } from "./complianceCard";
import { buildGenericCard } from "./genericCard";
import { buildErrorCard } from "./errorCard";
import { buildFollowUpCard } from "./followUpCard";
import { buildKnowledgeBaseCard } from "./knowledgeBaseCard";
import { buildConfigurationCard } from "./configurationCard";
import { buildFindingDetailCard } from "./findingDetailCard";
import { buildRemediationPlanCard } from "./remediationPlanCard";
import { buildAlertLifecycleCard } from "./alertLifecycleCard";
import { buildComplianceTrendCard } from "./complianceTrendCard";
import { buildEvidenceCollectionCard } from "./evidenceCollectionCard";
import { buildNistControlCard } from "./nistControlCard";
import { buildClarificationCard } from "./clarificationCard";
import { buildConfirmationCard } from "./confirmationCard";
import { buildKanbanBoardCard } from "./kanbanBoardCard";
import { buildSystemSummaryCard } from "./systemSummaryCard";
import { buildCategorizationCard } from "./categorizationCard";
import { buildAuthorizationCard } from "./authorizationCard";
import { buildDashboardCard } from "./dashboardCard";
import { buildRoadmapCard } from "./roadmapCard";
import { buildRoadmapPhaseDetailCard } from "./roadmapPhaseDetailCard";

/**
 * Select and build the appropriate Adaptive Card for an MCP response.
 * Follows priority-ordered routing rules from the card contracts.
 */
export function selectCard(response: McpResponse): Record<string, unknown> {
  const { intentType, data, suggestions, agentUsed, conversationId } = response;
  const dataType = (data?.type as string) ?? "";

  // Priority 0: Error
  if (response.success === false && response.errors && response.errors.length > 0) {
    const err = response.errors[0];
    return buildErrorCard({
      errorCode: err.errorCode,
      errorMessage: err.message,
      suggestion: err.suggestion,
      agentUsed,
    });
  }

  // Priority 1: Follow-up
  if (response.requiresFollowUp && response.followUpPrompt) {
    return buildFollowUpCard({
      followUpPrompt: response.followUpPrompt,
      missingFields: response.missingFields ?? [],
      agentUsed,
      suggestions,
      conversationId,
    });
  }

  // Priority 2: Clarification (data.type === 'clarification')
  if (dataType === "clarification" && response.followUpPrompt) {
    return buildClarificationCard({
      followUpPrompt: response.followUpPrompt,
      missingFields: response.missingFields ?? [],
      agentUsed,
      conversationId,
    });
  }

  // Priority 3–8: Compliance sub-types
  if (intentType === "compliance") {
    switch (dataType) {
      case "systemSummary":
        return buildSystemSummaryCard({
          systemName: (data?.systemName as string) ?? "Unknown System",
          acronym: data?.acronym as string,
          systemType: data?.systemType as string,
          hostingEnvironment: data?.hostingEnvironment as string,
          currentRmfStep: data?.currentRmfStep as string,
          rmfStepNumber: data?.rmfStepNumber as number,
          missionCriticality: data?.missionCriticality as string,
          impactLevel: data?.impactLevel as string,
          complianceScore: data?.complianceScore as number,
          activeAlerts: data?.activeAlerts as number,
          isActive: data?.isActive as boolean,
          authorizedDate: data?.authorizedDate as string,
          atoExpiration: data?.atoExpiration as string,
          agentUsed,
          suggestions,
          conversationId,
        });

      case "categorization":
        return buildCategorizationCard({
          systemName: (data?.systemName as string) ?? "Unknown System",
          fipsCategory: data?.fipsCategory as string,
          impactLevel: data?.impactLevel as string,
          confidentialityImpact: data?.confidentialityImpact as string,
          integrityImpact: data?.integrityImpact as string,
          availabilityImpact: data?.availabilityImpact as string,
          overallImpact: data?.overallImpact as string,
          informationTypes: data?.informationTypes as Array<{
            name: string;
            confidentiality: string;
            integrity: string;
            availability: string;
          }>,
          justification: data?.justification as string,
          categorizedDate: data?.categorizedDate as string,
          agentUsed,
          suggestions,
          conversationId,
        });

      case "authorization":
        return buildAuthorizationCard({
          systemName: (data?.systemName as string) ?? "Unknown System",
          decisionType: data?.decisionType as string,
          status: data?.status as string,
          riskLevel: data?.riskLevel as string,
          authorizedDate: data?.authorizedDate as string,
          expirationDate: data?.expirationDate as string,
          daysUntilExpiration: data?.daysUntilExpiration as number,
          authorizingOfficialName: data?.authorizingOfficialName as string,
          conditions: data?.conditions as Array<{ description: string; status?: string }>,
          riskAcceptances: data?.riskAcceptances as number,
          openFindings: data?.openFindings as number,
          agentUsed,
          suggestions,
          conversationId,
        });

      case "dashboard":
        return buildDashboardCard({
          title: data?.title as string,
          systems: (data?.systems as Array<{
            systemName: string;
            acronym?: string;
            currentRmfStep?: string;
            complianceScore?: number;
            impactLevel?: string;
            atoStatus?: string;
            activeAlerts?: number;
          }>) ?? [],
          totalSystems: data?.totalSystems as number,
          averageComplianceScore: data?.averageComplianceScore as number,
          rmfDistribution: data?.rmfDistribution as Array<{ step: string; count: number }>,
          criticalAlerts: data?.criticalAlerts as number,
          expiringAtos: data?.expiringAtos as number,
          agentUsed,
          suggestions,
          conversationId,
        });

      case "finding":
        return buildFindingDetailCard({
          title: (data?.title as string) ?? "Compliance Finding",
          severity: (data?.severity as string) ?? "Medium",
          findingId: data?.findingId as string,
          controlId: data?.controlId as string,
          controlFamily: data?.controlFamily as string,
          description: data?.description as string,
          resourceId: data?.resourceId as string,
          resourceType: data?.resourceType as string,
          remediationGuidance: data?.remediationGuidance as string,
          autoRemediable: data?.autoRemediable as boolean,
          riskLevel: data?.riskLevel as string,
          agentUsed,
          suggestions,
          conversationId,
        });

      case "remediationPlan":
        return buildRemediationPlanCard({
          planId: data?.planId as string,
          riskReduction: data?.riskReduction as number,
          findings: data?.findings as Array<{ title: string; severity: string; controlId?: string }>,
          phases: data?.phases as Array<{ name: string; duration?: string; findings?: number }>,
          steps: data?.steps as string[],
          agentUsed,
          suggestions,
          conversationId,
        });

      case "alert":
        return buildAlertLifecycleCard({
          alertId: (data?.alertId as string) ?? "",
          severity: (data?.severity as string) ?? "Medium",
          title: data?.title as string,
          description: data?.description as string,
          affectedResources: data?.affectedResources as string[],
          slaDeadline: data?.slaDeadline as string,
          status: data?.status as string,
          agentUsed,
          suggestions,
          conversationId,
        });

      case "trend":
        return buildComplianceTrendCard({
          dataPoints: (data?.dataPoints as Array<{ date: string; score: number }>) ?? [],
          direction: (data?.direction as "improving" | "declining" | "stable") ?? "stable",
          significantEvents: data?.significantEvents as Array<{ date: string; event: string }>,
          agentUsed,
          suggestions,
          conversationId,
        });

      case "evidence":
        return buildEvidenceCollectionCard({
          completeness: (data?.completeness as number) ?? 0,
          items: (data?.items as Array<{ name: string; hash?: string; status?: string }>) ?? [],
          framework: data?.framework as string,
          agentUsed,
          suggestions,
          conversationId,
        });

      case "kanban":
        return buildKanbanBoardCard({
          boardTitle: data?.boardTitle as string,
          tasks: data?.tasks as Array<{
            taskId: string;
            title: string;
            severity?: string;
            assignedTo?: string;
            status: string;
          }>,
          board: data?.board as Record<string, unknown>,
          agentUsed,
          suggestions,
          conversationId,
        });

      case "confirmation":
        return buildConfirmationCard({
          findingId: (data?.findingId as string) ?? "",
          scriptPreview: data?.scriptPreview as string,
          resourceId: data?.resourceId as string,
          resourceType: data?.resourceType as string,
          riskLevel: data?.riskLevel as string,
          controlId: data?.controlId as string,
          severity: data?.severity as string,
          agentUsed,
          conversationId,
        });

      case "roadmap":
        return buildRoadmapCard({
          roadmapId: data?.roadmapId as string,
          systemId: data?.systemId as string,
          systemName: data?.systemName as string,
          totalGaps: data?.totalGaps as number,
          phaseCount: data?.phaseCount as number,
          totalEffortDays: data?.totalEffortDays as number,
          riskReduction: data?.riskReduction as number,
          phases: data?.phases as Array<{
            name: string;
            timeline: string;
            controlCount: number;
            effortDays: number;
            riskReductionPercent: number;
          }>,
          agentUsed,
          suggestions,
          conversationId,
        });

      case "roadmapPhaseDetail":
        return buildRoadmapPhaseDetailCard({
          phaseName: data?.phaseName as string,
          systemId: data?.systemId as string,
          phaseOrder: data?.phaseOrder as number,
          items: data?.items as Array<{
            controlId: string;
            effortDays: number;
            assignedRole: string;
            gapType: string;
            dependsOn: string[];
            status: string;
          }>,
          agentUsed,
          suggestions,
          conversationId,
        });

      default:
        // Priority 9: Generic compliance card (assessment data)
        return buildComplianceCard({
          complianceScore: (data?.complianceScore as number) ?? 0,
          passedControls: (data?.passedControls as number) ?? 0,
          warningControls: (data?.warningControls as number) ?? 0,
          failedControls: (data?.failedControls as number) ?? 0,
          response: response.response,
          agentUsed,
          suggestions,
          conversationId,
        });
    }
  }

  // Priority 10: NIST control (knowledgebase sub-type)
  if (intentType === "knowledgebase" && dataType === "control") {
    return buildNistControlCard({
      controlId: (data?.controlId as string) ?? "",
      statement: (data?.statement as string) ?? "",
      title: data?.title as string,
      implementationGuidance: data?.implementationGuidance as string,
      stigs: data?.stigs as string[],
      fedRampBaseline: data?.fedRampBaseline as string,
      controlFamily: data?.controlFamily as string,
      agentUsed,
      suggestions,
      conversationId,
    });
  }

  // Priority 11: Knowledge base
  if (intentType === "knowledgebase") {
    return buildKnowledgeBaseCard({
      answer: response.response,
      sources: data?.sources as Array<{ title: string; url: string }>,
      controlId: data?.controlId as string,
      controlFamily: data?.controlFamily as string,
      agentUsed,
      suggestions,
      conversationId,
    });
  }

  // Priority 12: Configuration
  if (intentType === "configuration") {
    return buildConfigurationCard({
      framework: data?.framework as string,
      baseline: data?.baseline as string,
      subscriptionId: data?.subscriptionId as string,
      cloudEnvironment: data?.cloudEnvironment as string,
      agentUsed,
      suggestions,
      conversationId,
    });
  }

  // Priority 99: Generic fallback
  return buildGenericCard({
    response: response.response,
    agentUsed,
    suggestions,
    conversationId,
  });
}
