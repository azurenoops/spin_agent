/**
 * Tenant Impersonation Badge — Adaptive Card element rendered when the
 * upstream tenant-context SignalR hub broadcasts that the user is currently
 * impersonating another tenant. Implements feature 048 spec FR-024 / T142.
 *
 * Render this badge near the top of any card so the operator always knows
 * the elevated scope they're acting under.
 */

export interface TenantImpersonationContext {
  /** Optional tenant display name (e.g. "Contoso, Inc."). */
  impersonatedTenantName?: string;
  /** Required Guid string of the impersonated tenant. */
  impersonatedTenantId: string;
}

/**
 * Builds an Adaptive Card TextBlock that renders an "Impersonating: <tenant>"
 * banner with warning color. Returns `null` when no impersonation is active.
 */
export function buildTenantImpersonationBadge(
  context?: { impersonatedTenantId?: string; impersonatedTenantName?: string }
): Record<string, unknown> | null {
  if (!context?.impersonatedTenantId) return null;

  const display =
    context.impersonatedTenantName ?? shortGuid(context.impersonatedTenantId);

  return {
    type: "Container",
    style: "warning",
    bleed: true,
    items: [
      {
        type: "TextBlock",
        text: `\u26A0\uFE0F Impersonating: **${display}**`,
        wrap: true,
        weight: "Bolder",
        color: "Warning",
        size: "Small",
      },
    ],
    spacing: "None",
  };
}

function shortGuid(id: string): string {
  return id.length > 8 ? `${id.slice(0, 8)}…` : id;
}
