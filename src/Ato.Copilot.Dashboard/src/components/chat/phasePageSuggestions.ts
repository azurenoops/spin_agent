import type { ChatContext, SuggestedAction } from '../../types/chat';

// ─── Types ─────────────────────────────────────────────────────────────────────

interface Suggestion extends SuggestedAction {
  /** Higher priority = shown first (100 = urgent, 50 = normal, 10 = nice-to-have). */
  priority: number;
}

type RmfPhase = 'Prepare' | 'Categorize' | 'Select' | 'Implement' | 'Assess' | 'Authorize' | 'Monitor';

// ─── Phase guidance blurbs ─────────────────────────────────────────────────────

const PHASE_GUIDANCE: Record<RmfPhase, string> = {
  Prepare: 'Set up your system registration, assign RMF roles, and define your authorization boundary.',
  Categorize: 'Complete FIPS 199 categorization by defining information types and security impact levels.',
  Select: 'Select your security control baseline and tailor controls for your system.',
  Implement: 'Write implementation narratives for your controls and map security capabilities.',
  Assess: 'Run compliance assessments, review findings, and generate a Security Assessment Report.',
  Authorize: 'Review residual risk, issue an authorization decision, and create POA&M items for open findings.',
  Monitor: 'Set up continuous monitoring, track compliance trends, and manage ongoing POA&M remediation.',
};

export function getPhaseGuidance(phase: string | null | undefined): string {
  if (!phase) return 'Select a system to get started with your ATO journey.';
  return PHASE_GUIDANCE[phase as RmfPhase] ?? 'Continue working through the RMF lifecycle.';
}

// ─── Next-step label for the phase badge ────────────────────────────────────────

const NEXT_PHASE: Record<RmfPhase, RmfPhase | null> = {
  Prepare: 'Categorize',
  Categorize: 'Select',
  Select: 'Implement',
  Implement: 'Assess',
  Assess: 'Authorize',
  Authorize: 'Monitor',
  Monitor: null,
};

export function getNextPhase(phase: string | null | undefined): string | null {
  if (!phase) return null;
  return NEXT_PHASE[phase as RmfPhase] ?? null;
}

// ─── Intelligent suggestion engine ──────────────────────────────────────────────

/**
 * Inspects the current phase, page, and live metrics to produce
 * prioritised "what to do next" suggestions. The engine knows
 * what the user needs before they ask.
 */
export function getIntelligentSuggestions(context: ChatContext): SuggestedAction[] {
  const suggestions: Suggestion[] = [];
  const phase = context.rmfPhase as RmfPhase | undefined;
  const page = context.page;
  const d = context.pageData;

  // ─── Phase-specific proactive guidance ──────────────────────────────

  if (phase === 'Prepare') {
    suggestions.push(
      { label: 'Assign RMF roles', prompt: 'Help me assign the required RMF roles for this system', icon: '👤', priority: 80 },
      { label: 'Define boundary', prompt: 'Guide me through defining the authorization boundary', icon: '🔒', priority: 70 },
      { label: 'Privacy threshold', prompt: 'Start the Privacy Threshold Analysis for this system', icon: '📋', priority: 60 },
    );
    if (d && !d.boundaryCount) {
      suggestions.push({
        label: 'No boundary resources yet',
        prompt: 'I need to add resources to my authorization boundary — what should I include?',
        icon: '⚠️',
        priority: 95,
      });
    }
  }

  if (phase === 'Categorize') {
    if (d && !d.hasCategorization) {
      suggestions.push({
        label: 'Set categorization now',
        prompt: 'Help me perform FIPS 199 security categorization for this system',
        icon: '⚠️',
        priority: 100,
      });
    } else {
      suggestions.push({
        label: 'Review categorization',
        prompt: 'Show me the current security categorization and information types',
        icon: '✅',
        priority: 40,
      });
    }
    suggestions.push(
      { label: 'Add information types', prompt: 'What SP 800-60 information types should I add for this system?', icon: '📊', priority: 70 },
      { label: 'Ready for Select?', prompt: 'Check if this system is ready to advance to the Select phase', icon: '→', priority: 50 },
    );
  }

  if (phase === 'Select') {
    if (d && !d.hasBaseline) {
      suggestions.push({
        label: 'Select baseline now',
        prompt: 'Help me select the right security control baseline for this system',
        icon: '⚠️',
        priority: 100,
      });
    } else if (d?.baselineLevel) {
      suggestions.push({
        label: `Review ${d.baselineLevel} baseline`,
        prompt: `Show me the controls in the ${d.baselineLevel} baseline and any tailoring options`,
        icon: '📋',
        priority: 50,
      });
    }
    suggestions.push(
      { label: 'Tailor controls', prompt: 'Are there any controls I should tailor or add beyond the baseline?', icon: '🔧', priority: 60 },
      { label: 'Ready for Implement?', prompt: 'Check if this system is ready to advance to the Implement phase', icon: '→', priority: 40 },
    );
  }

  if (phase === 'Implement') {
    const nc = d?.narrativeCoverage ?? 0;
    if (nc < 80) {
      suggestions.push({
        label: `Narratives at ${Math.round(nc)}% — need ≥80%`,
        prompt: 'Which controls are missing implementation narratives? Help me write them.',
        icon: '⚠️',
        priority: 100,
      });
    } else {
      suggestions.push({
        label: `Narratives at ${Math.round(nc)}% ✓`,
        prompt: 'Show narrative coverage details and any gaps',
        icon: '✅',
        priority: 30,
      });
    }
    suggestions.push(
      { label: 'Generate narratives', prompt: 'Auto-generate implementation narratives for controls that are missing them', icon: '✨', priority: nc < 80 ? 90 : 40 },
      { label: 'Map capabilities', prompt: 'Help me map security capabilities to controls', icon: '🔗', priority: 50 },
    );
    if (nc >= 80) {
      suggestions.push({
        label: 'Ready for Assess?',
        prompt: 'Check if this system is ready to advance to the Assess phase',
        icon: '→',
        priority: 60,
      });
    }
  }

  if (phase === 'Assess') {
    const score = d?.complianceScore ?? 0;
    const totalFindings = d?.totalFindings ?? 0;
    if (totalFindings === 0) {
      suggestions.push({
        label: 'Run assessment now',
        prompt: 'Run a compliance assessment for this system',
        icon: '⚠️',
        priority: 100,
      });
    } else {
      suggestions.push({
        label: `Score: ${Math.round(score)}% — ${totalFindings} findings`,
        prompt: 'Show me assessment results and the highest priority findings to fix',
        icon: score >= 80 ? '✅' : '⚠️',
        priority: 60,
      });
    }
    if ((d?.catIFindings ?? 0) > 0) {
      suggestions.push({
        label: `${d!.catIFindings} CAT I — fix critical`,
        prompt: 'Show me all CAT I critical findings and how to remediate them',
        icon: '🔴',
        priority: 95,
      });
    }
    suggestions.push(
      { label: 'Generate SAR', prompt: 'Generate a Security Assessment Report for the latest assessment', icon: '📄', priority: 50 },
      { label: 'Ready for Authorize?', prompt: 'Check if this system is ready to advance to the Authorize phase', icon: '→', priority: 40 },
    );
  }

  if (phase === 'Authorize') {
    const poams = d?.openPoams ?? 0;
    const overdue = d?.overduePoams ?? 0;
    if (d?.atoStatus === 'None' || !d?.atoStatus) {
      suggestions.push({
        label: 'Issue ATO decision',
        prompt: 'Guide me through issuing an authorization decision for this system',
        icon: '⚠️',
        priority: 100,
      });
    }
    if (poams > 0) {
      suggestions.push({
        label: `${poams} open POA&M items${overdue > 0 ? ` (${overdue} overdue!)` : ''}`,
        prompt: 'Show me all open POA&M items and help me prioritise remediation',
        icon: overdue > 0 ? '🔴' : '📋',
        priority: overdue > 0 ? 95 : 60,
      });
    }
    suggestions.push(
      { label: 'Accept residual risk', prompt: 'What residual risks should I document for the authorization decision?', icon: '⚖️', priority: 50 },
      { label: 'Ready for Monitor?', prompt: 'Check if this system is ready to advance to the Monitor phase', icon: '→', priority: 40 },
    );
  }

  if (phase === 'Monitor') {
    const overdue = d?.overduePoams ?? 0;
    const days = d?.atoDaysRemaining;
    if (days != null && days <= 90) {
      suggestions.push({
        label: `ATO expires in ${days} days`,
        prompt: 'My ATO expires soon — what do I need to do for reauthorization?',
        icon: '🔴',
        priority: 100,
      });
    }
    if (overdue > 0) {
      suggestions.push({
        label: `${overdue} overdue POA&Ms`,
        prompt: 'Show me overdue POA&M items and help me create a remediation plan',
        icon: '🔴',
        priority: 95,
      });
    }
    suggestions.push(
      { label: 'Create ConMon plan', prompt: 'Help me set up a continuous monitoring plan for this system', icon: '📊', priority: 60 },
      { label: 'Generate ConMon report', prompt: 'Generate a continuous monitoring report for the current period', icon: '📄', priority: 50 },
      { label: 'Check compliance drift', prompt: 'Has my compliance score changed recently? Show me trends and drift alerts.', icon: '📈', priority: 55 },
    );
  }

  // ─── Page-specific overlays (add extra suggestions based on which page the user is on) ──

  if (page === 'boundaries') {
    suggestions.push(
      { label: 'Boundary coverage', prompt: 'Show me authorization boundary coverage and any gaps', icon: '🗺️', priority: 55 },
      { label: 'Add boundary resource', prompt: 'What resources should I add to the authorization boundary?', icon: '➕', priority: 45 },
    );
  }

  if (page === 'components') {
    suggestions.push(
      { label: 'Inventory completeness', prompt: 'Check hardware and software inventory completeness for this system', icon: '📦', priority: 55 },
    );
  }

  if (page === 'narratives') {
    const nc = d?.narrativeCoverage ?? 0;
    if (nc < 100) {
      suggestions.push({
        label: 'Auto-fill missing narratives',
        prompt: 'Generate implementation narratives for all controls that are missing them',
        icon: '✨',
        priority: 90,
      });
    }
    suggestions.push(
      { label: 'Narrative quality check', prompt: 'Review my implementation narratives for quality and completeness', icon: '🔍', priority: 60 },
    );
  }

  if (page === 'gap-analysis') {
    suggestions.push(
      { label: 'Prioritise gaps', prompt: 'Which control gaps have the highest risk and should be fixed first?', icon: '🎯', priority: 75 },
      { label: 'Remediation plan', prompt: 'Create a remediation plan for the top failing control families', icon: '📋', priority: 65 },
    );
  }

  if (page === 'documents') {
    suggestions.push(
      { label: 'Generate SSP', prompt: 'Generate a System Security Plan for this system', icon: '📄', priority: 55 },
    );
  }

  if (page === 'assessments') {
    suggestions.push(
      { label: 'Compare assessments', prompt: 'Compare the last two assessments and show me what changed', icon: '📊', priority: 55 },
    );
  }

  if (page === 'remediation') {
    suggestions.push(
      { label: 'POA&M status summary', prompt: 'Show me a summary of all open POA&M items and their status', icon: '📋', priority: 60 },
      { label: 'Overdue items', prompt: 'List all overdue POA&M items that need immediate attention', icon: '⏰', priority: 70 },
      { label: 'Remediation priorities', prompt: 'What are the highest priority remediation tasks I should focus on?', icon: '🎯', priority: 55 },
    );
  }

  if (page === 'roadmap') {
    suggestions.push(
      { label: 'Roadmap progress', prompt: 'Show me overall RMF roadmap progress and what needs attention', icon: '🗺️', priority: 55 },
    );
  }

  if (page === 'deviations') {
    suggestions.push(
      { label: 'Pending reviews', prompt: 'List all pending deviation requests that need my review', icon: '📋', priority: 60 },
      { label: 'Expiring soon', prompt: 'Which deviations are expiring in the next 30 days?', icon: '⏰', priority: 55 },
      { label: 'Request deviation', prompt: 'Help me request a false positive or risk acceptance for a finding', icon: '➕', priority: 50 },
    );
  }

  // ─── Cross-cutting urgency triggers (regardless of phase/page) ──────

  if (d) {
    if ((d.catIFindings ?? 0) > 0 && phase !== 'Assess') {
      suggestions.push({
        label: `${d.catIFindings} CAT I findings`,
        prompt: 'Show me all CAT I critical findings — these need immediate attention',
        icon: '🔴',
        priority: 92,
      });
    }
    if ((d.overduePoams ?? 0) > 0 && phase !== 'Monitor' && phase !== 'Authorize') {
      suggestions.push({
        label: `${d.overduePoams} overdue POA&Ms`,
        prompt: 'Show me overdue POA&M items that need immediate remediation',
        icon: '🔴',
        priority: 90,
      });
    }
    if ((d.complianceScore ?? 100) < 50 && phase !== 'Assess') {
      suggestions.push({
        label: `Compliance at ${Math.round(d.complianceScore!)}%`,
        prompt: 'My compliance score is low — what are the top things to fix?',
        icon: '⚠️',
        priority: 85,
      });
    }

    // ─── Deviation urgency triggers (Feature 035 — T022) ────────────────
    if ((d.pendingDeviations ?? 0) > 0) {
      suggestions.push({
        label: `${d.pendingDeviations} pending reviews`,
        prompt: `There are ${d.pendingDeviations} deviation requests pending review — show me the details`,
        icon: '📋',
        priority: 90,
      });
    }
    if ((d.expiringDeviations ?? 0) > 0) {
      suggestions.push({
        label: `${d.expiringDeviations} deviations expiring`,
        prompt: 'Show me deviations expiring within 30 days that need renewal',
        icon: '⏰',
        priority: 85,
      });
    }
    if ((d.deviationsMissingEvidence ?? 0) > 0) {
      suggestions.push({
        label: `${d.deviationsMissingEvidence} missing evidence`,
        prompt: 'Which deviations are missing supporting evidence?',
        icon: '📎',
        priority: 70,
      });
    }
    if ((d.catIDeviations ?? 0) > 0) {
      suggestions.push({
        label: `${d.catIDeviations} CAT I deviations`,
        prompt: 'Show me CAT I deviations requiring AO approval',
        icon: '🔴',
        priority: 88,
      });
    }

    // ─── Outstanding-info urgency triggers (Feature 035 — T023) ─────────
    if ((d.catIWithoutDeviationOrRemediation ?? 0) > 0) {
      suggestions.push({
        label: `${d.catIWithoutDeviationOrRemediation} unaddressed CAT I`,
        prompt: 'Show CAT I findings without remediation plans or deviation requests',
        icon: '🔴',
        priority: 95,
      });
    }
    if ((d.draftSspSections ?? 0) > 0) {
      suggestions.push({
        label: `${d.draftSspSections} SSP sections need work`,
        prompt: 'Which SSP sections are still in Draft or NeedsRevision status?',
        icon: '📝',
        priority: 80,
      });
    }
    if ((d.missingDocDueDates ?? 0) > 0) {
      suggestions.push({
        label: `${d.missingDocDueDates} docs missing due dates`,
        prompt: 'Show documents that are missing due dates',
        icon: '📅',
        priority: 75,
      });
    }
    if ((d.poamMissingCompletionDates ?? 0) > 0) {
      suggestions.push({
        label: `${d.poamMissingCompletionDates} POA&Ms need dates`,
        prompt: 'Which POA&M items are missing scheduled completion dates?',
        icon: '📅',
        priority: 78,
      });
    }
    if ((d.authDecisionMissingExpiry ?? 0) > 0) {
      suggestions.push({
        label: 'Authorization missing expiry',
        prompt: 'The authorization decision is missing an expiration date — help me set one',
        icon: '⚠️',
        priority: 82,
      });
    }
  }

  // ─── Portfolio page (no system selected) ─────────────────────────────

  if (page === 'portfolio' && !context.systemId) {
    return [
      { label: 'Portfolio overview', prompt: 'Show me the portfolio compliance overview and systems that need attention', icon: '📊' },
      { label: 'At-risk systems', prompt: 'Which systems have the lowest compliance scores or expiring ATOs?', icon: '⚠️' },
      { label: 'Register new system', prompt: 'Help me register a new system for ATO', icon: '➕' },
      { label: 'RMF guidance', prompt: 'Explain the RMF lifecycle phases and what I need to do', icon: '📖' },
    ];
  }

  // Sort by priority descending and take top 4
  suggestions.sort((a, b) => b.priority - a.priority);
  return suggestions.slice(0, 4).map(({ priority: _, ...rest }) => rest);
}

// ─── Phase badge config ─────────────────────────────────────────────────────────

const PHASE_COLORS: Record<RmfPhase, { bg: string; text: string; ring: string }> = {
  Prepare: { bg: 'bg-slate-100', text: 'text-slate-700', ring: 'ring-slate-400' },
  Categorize: { bg: 'bg-purple-100', text: 'text-purple-700', ring: 'ring-purple-400' },
  Select: { bg: 'bg-indigo-100', text: 'text-indigo-700', ring: 'ring-indigo-400' },
  Implement: { bg: 'bg-cyan-100', text: 'text-cyan-700', ring: 'ring-cyan-400' },
  Assess: { bg: 'bg-amber-100', text: 'text-amber-700', ring: 'ring-amber-400' },
  Authorize: { bg: 'bg-green-100', text: 'text-green-700', ring: 'ring-green-400' },
  Monitor: { bg: 'bg-emerald-100', text: 'text-emerald-700', ring: 'ring-emerald-400' },
};

export function getPhaseColors(phase: string | null | undefined) {
  if (!phase) return { bg: 'bg-gray-100', text: 'text-gray-600', ring: 'ring-gray-400' };
  return PHASE_COLORS[phase as RmfPhase] ?? { bg: 'bg-gray-100', text: 'text-gray-600', ring: 'ring-gray-400' };
}
