using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Tests.Integration.Tenancy;

internal static class SyntheticRequirementCatalogFixture
{
    internal static ComplianceFramework CreateFramework(IEnumerable<string> controlIds)
    {
        var ids = controlIds.ToArray();
        var framework = new ComplianceFramework
        {
            Identifier = $"SYNTHETIC-EXPORT-{Guid.NewGuid():N}",
            Name = "Synthetic export acceptance catalog",
            Publisher = "Synthetic test laboratory", Version = "test-1",
            CatalogUrl = "https://example.invalid/synthetic-export-catalog.json",
            ControlCount = ids.Length
        };
        framework.RequirementCatalogJson = JsonSerializer.Serialize(new
        {
            uuid = Guid.NewGuid().ToString(),
            metadata = new { title = framework.Name, version = framework.Version },
            controls = ids.Select((id, index) => new
            {
                id = id.ToLowerInvariant(), title = $"Synthetic {id} documentation requirement",
                props = new[] { new { name = "label", value = id } },
                parts = new[] { new { id = $"synthetic-statement-{index}", name = "statement",
                    prose = $"Document the synthetic {id} laboratory procedure." } }
            })
        });
        return framework;
    }

    internal static BaselineCatalogBinding CreateBinding(ControlBaseline baseline, ComplianceFramework framework)
    {
        var binding = new BaselineCatalogBinding
        {
            TenantId = baseline.TenantId, ControlBaselineId = baseline.Id, FrameworkId = framework.Id,
            FrameworkIdentifier = framework.Identifier, CatalogVersion = framework.Version,
            CatalogJson = framework.RequirementCatalogJson!, ContentHash = RequirementCoverageService.Hash(framework.RequirementCatalogJson!),
            SourceUri = framework.CatalogUrl!, Publisher = framework.Publisher,
            Rationale = "Explicit synthetic fixture source; no real compliance or authorization claim.", BoundBy = "synthetic-fixture"
        };
        baseline.RequirementCatalogBindingId = binding.Id;
        return binding;
    }

    internal static RequirementResponse[] Responses(RequirementCatalog catalog, ControlImplementation implementation,
        EvidenceArtifact evidence) =>
        catalog.Controls.Single(x => x.DisplayId == implementation.ControlId || x.Id == implementation.ControlId)
            .Requirements.Select(x => new RequirementResponse(x.Id, "Technical",
                $"SYNTHETIC reviewed response for {implementation.ControlId}: laboratory procedure and retained test evidence.",
                [new(evidence.Id, evidence.ContentHash)])).ToArray();
}
