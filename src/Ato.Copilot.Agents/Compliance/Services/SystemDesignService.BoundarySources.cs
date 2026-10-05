using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed partial class SystemDesignService
{
    private async Task ProjectBoundarySourcesAsync(AtoCopilotContext db, string systemId,
        Action<string, DesignNode> add, List<string> fingerprints, CancellationToken ct)
    {
        var definitions = await db.AuthorizationBoundaryDefinitions.AsNoTracking()
            .Where(b => b.TenantId == TenantId && b.RegisteredSystemId == systemId).OrderBy(b => b.Id).Take(201).ToListAsync(ct);
        if (definitions.Count > 200) throw new ArgumentException("Boundary definitions exceed the design scope budget.");
        foreach (var definition in definitions)
            add("InventoryBoundary", new()
            {
                Id = $"boundary-definition:{definition.Id}", Kind = "BoundaryDefinition", Label = definition.Name,
                Source = Source("BoundaryDefinition", definition.Id, Scalars(definition), Link(systemId, "inventory"), "Recorded"),
                ReviewState = "Recorded", ProjectionStatus = "Canonical",
                Properties = Fields(definition, "Name", "BoundaryType", "Description", "IsPrimary")
            });
        var decisions = await db.AuthorizationDecisions.AsNoTracking()
            .Where(d => d.TenantId == TenantId && d.RegisteredSystemId == systemId).OrderBy(d => d.Id).Take(201).ToListAsync(ct);
        if (decisions.Count > 200) throw new ArgumentException("Authorization decision history exceeds design scope budget.");
        foreach (var decision in decisions)
        {
            var currency = !decision.IsActive || decision.SupersededById is not null ? "Inactive"
                : decision.DecisionDate == default ? "Decision date not recorded"
                : decision.DecisionDate > DateTime.UtcNow ? "Future"
                : decision.DecisionType == AuthorizationDecisionType.Dato ? "Denied"
                : decision.ExpirationDate is null ? "Expiration not recorded"
                : decision.ExpirationDate <= DateTime.UtcNow ? "Expired"
                : string.IsNullOrWhiteSpace(decision.IssuedBy) && string.IsNullOrWhiteSpace(decision.ExternalIssuingAuthority)
                    ? "Issuing authority not recorded" : "Current system decision";
            var properties = Fields(decision, "DecisionType", "DecisionDate", "ExpirationDate", "IssuedByName",
                "ExternalIssuingAuthority", "SourceEvidenceId", "SourceEvidenceHash", "BaselinePackageId", "BaselinePackageHash", "IsActive", "TermsAndConditions");
            properties["currency"] = currency;
            properties["componentCoverage"] = "Not verified";
            properties["scopeCoverageNote"] = "System decision record does not pin individual components or named boundaries; design inclusion is not authorization coverage.";
            add("Mission", new()
            {
                Id = $"authorization-scope:{decision.Id}", Kind = "AuthorizationScope",
                Label = $"Recorded {decision.DecisionType} · {currency}",
                Source = Source("AuthorizationDecision", decision.Id, new { decision = Scalars(decision), currency },
                    $"/systems/{Uri.EscapeDataString(systemId)}/authorization", "Recorded"),
                Properties = properties, ReviewState = "Recorded", ProjectionStatus = "Recorded"
            });
        }
        fingerprints.Add(Json(new { definitions = definitions.Select(Scalars), decisions = decisions.Select(Scalars) }));
    }
}
