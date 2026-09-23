namespace Ato.Copilot.Mcp.Prompts;

public class McpPrompt
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public PromptArgument[] Arguments { get; init; } = Array.Empty<PromptArgument>();
}

public class PromptArgument
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public bool Required { get; init; }
}

/// <summary>
/// MCP Prompts for compliance domain operations
/// </summary>
public static class CompliancePrompts
{
    public static readonly McpPrompt AssessCompliance = new()
    {
        Name = "assess_compliance",
        Description = "Run a compliance assessment against NIST 800-53 controls for Azure resources",
        Arguments = new[]
        {
            new PromptArgument { Name = "scope", Description = "Subscription ID or resource group to assess", Required = true },
            new PromptArgument { Name = "impact_level", Description = "FIPS 199 impact level: Low, Moderate, or High", Required = false },
            new PromptArgument { Name = "control_families", Description = "Specific control families to assess (e.g., AC, AU, IA)", Required = false }
        }
    };

    public static readonly McpPrompt GenerateSSP = new()
    {
        Name = "generate_ssp",
        Description = "Generate a System Security Plan (SSP) document for FedRAMP/ATO",
        Arguments = new[]
        {
            new PromptArgument { Name = "system_name", Description = "Name of the system", Required = true },
            new PromptArgument { Name = "impact_level", Description = "FIPS 199 impact level", Required = true },
            new PromptArgument { Name = "subscription_id", Description = "Azure subscription ID for automated evidence collection", Required = false }
        }
    };

    public static readonly McpPrompt RemediateFindings = new()
    {
        Name = "remediate_findings",
        Description = "Remediate compliance findings with guided or automated fixes",
        Arguments = new[]
        {
            new PromptArgument { Name = "finding_ids", Description = "Comma-separated list of finding IDs to remediate", Required = true },
            new PromptArgument { Name = "auto_fix", Description = "Whether to apply fixes automatically (true/false)", Required = false }
        }
    };

    public static readonly McpPrompt CollectEvidence = new()
    {
        Name = "collect_evidence",
        Description = "Collect compliance evidence from Azure resources for audit documentation",
        Arguments = new[]
        {
            new PromptArgument { Name = "control_id", Description = "NIST control ID to collect evidence for", Required = true },
            new PromptArgument { Name = "subscription_id", Description = "Azure subscription ID", Required = true }
        }
    };

    public static IEnumerable<McpPrompt> GetAll() => new[] { AssessCompliance, GenerateSSP, RemediateFindings, CollectEvidence };
}

/// <summary>
/// Registry of all MCP prompts - compliance only for Security Posture Intelligence Navigator
/// </summary>
public static class PromptRegistry
{
    public static IEnumerable<McpPrompt> GetAllPrompts() => CompliancePrompts.GetAll();

    public static McpPrompt? FindPrompt(string name) =>
        GetAllPrompts().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
