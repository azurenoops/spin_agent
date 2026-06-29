using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.Agents.KnowledgeBase.Configuration;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.KnowledgeBase.Tools;

/// <summary>
/// Tool that explains the RMF process, individual steps, service-specific guidance,
/// and deliverable requirements.
/// </summary>
public class ExplainRmfTool : BaseTool
{
    private readonly IRmfKnowledgeService _rmfService;
    private readonly IDoDInstructionService _dodInstructionService;
    private readonly IDoDWorkflowService _dodWorkflowService;
    private readonly IMemoryCache _cache;
    private readonly KnowledgeBaseAgentOptions _options;

    public ExplainRmfTool(
        IRmfKnowledgeService rmfService,
        IDoDInstructionService dodInstructionService,
        IDoDWorkflowService dodWorkflowService,
        IMemoryCache cache,
        IOptions<KnowledgeBaseAgentOptions> options,
        ILogger<ExplainRmfTool> logger) : base(logger)
    {
        _rmfService = rmfService;
        _dodInstructionService = dodInstructionService;
        _dodWorkflowService = dodWorkflowService;
        _cache = cache;
        _options = options.Value;
    }

    /// <inheritdoc />
    public override string Name => "kb_explain_rmf";

    /// <inheritdoc />
    public override string Description =>
        "Explain the Risk Management Framework (RMF) process, individual steps, " +
        "service-specific guidance (Navy, Army, Air Force), and deliverable requirements.";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters { get; } =
        new Dictionary<string, ToolParameter>
        {
            ["topic"] = new()
            {
                Name = "topic",
                Description = "RMF topic to explain: 'overview', 'step' (with step_number), " +
                              "'service' (with organization), 'deliverables', 'instruction' (with instruction_id), " +
                              "'workflow' (with organization)",
                Type = "string",
                Required = false
            },
            ["step_number"] = new()
            {
                Name = "step_number",
                Description = "RMF step number (1-6) when topic is 'step'",
                Type = "integer",
                Required = false
            },
            ["organization"] = new()
            {
                Name = "organization",
                Description = "Military branch/organization (e.g., 'Navy', 'Army', 'Air Force')",
                Type = "string",
                Required = false
            },
            ["instruction_id"] = new()
            {
                Name = "instruction_id",
                Description = "DoD instruction identifier (e.g., 'DoDI 8510.01', '8510.01')",
                Type = "string",
                Required = false
            }
        };

    /// <inheritdoc />
    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> args,
        CancellationToken cancellationToken = default)
    {
        var topic = GetArg<string>(args, "topic") ?? "overview";

        var cacheKey = BuildCacheKey(topic, args);
        if (_cache.TryGetValue(cacheKey, out string? cached) && cached != null)
            return cached;

        var result = topic.ToLowerInvariant() switch
        {
            "step" => await ExplainStepAsync(args, cancellationToken),
            "service" => await ExplainServiceGuidanceAsync(args, cancellationToken),
            "deliverables" => await ExplainDeliverablesAsync(cancellationToken),
            "instruction" => await ExplainInstructionAsync(args, cancellationToken),
            "workflow" => await ExplainWorkflowAsync(args, cancellationToken),
            _ => await ExplainOverviewAsync(cancellationToken)
        };

        _cache.Set(cacheKey, result, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.CacheDurationMinutes), Size = 1 });
        return result;
    }

    private async Task<string> ExplainOverviewAsync(CancellationToken cancellationToken)
    {
        var process = await _rmfService.GetRmfProcessAsync(cancellationToken);
        if (process == null)
            return "RMF process data is not currently available. The Risk Management Framework (RMF) is a 6-step process defined in NIST SP 800-37 and implemented by DoD via DoDI 8510.01.";

        var sb = new StringBuilder();
        sb.AppendLine("# Risk Management Framework (RMF) Overview");
        sb.AppendLine();
        sb.AppendLine("The RMF provides a structured process for managing security and privacy risk. " +
                      "It is defined in NIST SP 800-37 and implemented by DoD via DoDI 8510.01.");
        sb.AppendLine();
        sb.AppendLine("## The 6-Step RMF Process");
        sb.AppendLine();

        foreach (var step in process.Steps.OrderBy(s => s.Step))
        {
            sb.AppendLine($"### Step {step.Step}: {step.Title}");
            sb.AppendLine(step.Description);
            sb.AppendLine();
            sb.AppendLine("**Key Activities:**");
            foreach (var activity in step.Activities)
                sb.AppendLine($"- {activity}");
            sb.AppendLine();
            sb.AppendLine($"**Outputs:** {string.Join(", ", step.Outputs)}");
            sb.AppendLine($"**Roles:** {string.Join(", ", step.Roles)}");
            sb.AppendLine($"**DoD Reference:** {step.DodInstruction}");
            sb.AppendLine();
        }

        if (process.ServiceGuidance.Count > 0)
        {
            sb.AppendLine("## Service-Specific Guidance Available");
            foreach (var kvp in process.ServiceGuidance)
                sb.AppendLine($"- **{kvp.Value.Organization}**: {kvp.Value.Description[..Math.Min(100, kvp.Value.Description.Length)]}...");
        }

        return sb.ToString();
    }

    private async Task<string> ExplainStepAsync(
        Dictionary<string, object?> args,
        CancellationToken cancellationToken)
    {
        var stepNumberRaw = GetArg<object>(args, "step_number");
        if (stepNumberRaw == null)
            return "Please specify a step_number (1-6) to get details about a specific RMF step.";

        var stepNumber = Convert.ToInt32(stepNumberRaw);
        if (stepNumber < 1 || stepNumber > 6)
            return "Invalid step number. RMF has 6 steps (1-6): Categorize, Select, Implement, Assess, Authorize, Monitor.";

        var step = await _rmfService.GetRmfStepAsync(stepNumber, cancellationToken);
        if (step == null)
            return $"Details for RMF Step {stepNumber} are not currently available.";

        var sb = new StringBuilder();
        sb.AppendLine($"# RMF Step {step.Step}: {step.Title}");
        sb.AppendLine();
        sb.AppendLine(step.Description);
        sb.AppendLine();

        sb.AppendLine("## Key Activities");
        for (var i = 0; i < step.Activities.Count; i++)
            sb.AppendLine($"{i + 1}. {step.Activities[i]}");
        sb.AppendLine();

        sb.AppendLine("## Expected Outputs");
        foreach (var output in step.Outputs)
            sb.AppendLine($"- {output}");
        sb.AppendLine();

        sb.AppendLine("## Responsible Roles");
        foreach (var role in step.Roles)
            sb.AppendLine($"- {role}");
        sb.AppendLine();

        sb.AppendLine($"## DoD Reference");
        sb.AppendLine(step.DodInstruction);

        return sb.ToString();
    }

    private async Task<string> ExplainServiceGuidanceAsync(
        Dictionary<string, object?> args,
        CancellationToken cancellationToken)
    {
        var organization = GetArg<string>(args, "organization");
        if (string.IsNullOrWhiteSpace(organization))
            return "Please specify an organization (e.g., 'Navy', 'Army', 'Air Force') to get service-specific guidance.";

        var guidance = await _rmfService.GetServiceGuidanceAsync(organization, cancellationToken);
        if (guidance == null)
            return $"No specific RMF guidance found for '{organization}'. Available branches: Navy, Army, Air Force.";

        var sb = new StringBuilder();
        sb.AppendLine($"# {guidance.Organization} RMF Guidance");
        sb.AppendLine();
        sb.AppendLine(guidance.Description);
        sb.AppendLine();

        sb.AppendLine("## Key Contacts");
        foreach (var contact in guidance.Contacts)
            sb.AppendLine($"- {contact}");
        sb.AppendLine();

        sb.AppendLine("## Service-Specific Requirements");
        foreach (var req in guidance.Requirements)
            sb.AppendLine($"- {req}");
        sb.AppendLine();

        sb.AppendLine("## Timeline");
        sb.AppendLine(guidance.Timeline);
        sb.AppendLine();

        sb.AppendLine("## Tools");
        sb.AppendLine(string.Join(", ", guidance.Tools));

        // Include workflows for this organization
        var workflows = await _dodWorkflowService.GetWorkflowsByOrganizationAsync(organization, cancellationToken);
        if (workflows.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"## Available {guidance.Organization} Authorization Workflows");
            foreach (var wf in workflows)
                sb.AppendLine($"- **{wf.Name}** ({wf.ImpactLevel} Impact): {wf.Steps.Count} steps, {wf.RequiredDocuments.Count} required documents");
        }

        return sb.ToString();
    }

    private async Task<string> ExplainDeliverablesAsync(CancellationToken cancellationToken)
    {
        var process = await _rmfService.GetRmfProcessAsync(cancellationToken);
        if (process?.DeliverablesOverview == null || process.DeliverablesOverview.Count == 0)
            return "Deliverables overview is not currently available.";

        var sb = new StringBuilder();
        sb.AppendLine("# RMF Deliverables by Step");
        sb.AppendLine();
        sb.AppendLine("The following deliverables are produced at each step of the RMF process:");
        sb.AppendLine();

        foreach (var d in process.DeliverablesOverview.OrderBy(x => x.Step))
        {
            sb.AppendLine($"## Step {d.Step}: {d.StepTitle}");
            foreach (var deliverable in d.Deliverables)
                sb.AppendLine($"- {deliverable}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private async Task<string> ExplainInstructionAsync(
        Dictionary<string, object?> args,
        CancellationToken cancellationToken)
    {
        var instructionId = GetArg<string>(args, "instruction_id");
        if (string.IsNullOrWhiteSpace(instructionId))
            return "Please specify an instruction_id (e.g., 'DoDI 8510.01') to get instruction details.";

        var instruction = await _dodInstructionService.ExplainInstructionAsync(instructionId, cancellationToken);
        if (instruction == null)
            return $"No DoD instruction found matching '{instructionId}'. Try: DoDI 8510.01, DoDI 8500.01, DoDI 8551.01, DoDI 8520.02, DoDI 8530.01, CNSSI 1253.";

        var sb = new StringBuilder();
        sb.AppendLine($"# {instruction.InstructionId}: {instruction.Title}");
        sb.AppendLine();
        sb.AppendLine(instruction.Description);
        sb.AppendLine();
        sb.AppendLine($"**Publication Date:** {instruction.PublicationDate}");
        sb.AppendLine($"**Applicability:** {instruction.Applicability}");
        sb.AppendLine($"**URL:** {instruction.Url}");
        sb.AppendLine();

        if (instruction.RelatedNistControls.Count > 0)
        {
            sb.AppendLine("## Related NIST Controls");
            sb.AppendLine(string.Join(", ", instruction.RelatedNistControls));
            sb.AppendLine();
        }

        if (instruction.ControlMappings.Count > 0)
        {
            sb.AppendLine("## Control Mappings");
            foreach (var m in instruction.ControlMappings)
            {
                sb.AppendLine($"### {m.ControlId}: {m.Requirement}");
                sb.AppendLine(m.Guidance);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private async Task<string> ExplainWorkflowAsync(
        Dictionary<string, object?> args,
        CancellationToken cancellationToken)
    {
        var organization = GetArg<string>(args, "organization");
        if (string.IsNullOrWhiteSpace(organization))
            return "Please specify an organization (e.g., 'Navy', 'Army', 'Air Force') to see available workflows.";

        var workflows = await _dodWorkflowService.GetWorkflowsByOrganizationAsync(organization, cancellationToken);
        if (workflows.Count == 0)
            return $"No authorization workflows found for '{organization}'. Available: Navy, Army, Air Force.";

        var sb = new StringBuilder();
        sb.AppendLine($"# {organization} Authorization Workflows");
        sb.AppendLine();

        foreach (var wf in workflows)
        {
            sb.AppendLine($"## {wf.Name}");
            sb.AppendLine(wf.Description);
            sb.AppendLine();

            sb.AppendLine("### Steps");
            foreach (var step in wf.Steps.OrderBy(s => s.Order))
                sb.AppendLine($"{step.Order}. **{step.Title}** ({step.Duration}) — {step.Description}");
            sb.AppendLine();

            sb.AppendLine("### Required Documents");
            foreach (var doc in wf.RequiredDocuments)
                sb.AppendLine($"- {doc}");
            sb.AppendLine();

            sb.AppendLine("### Approval Authorities");
            sb.AppendLine(string.Join(", ", wf.ApprovalAuthorities));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildCacheKey(string topic, Dictionary<string, object?> args)
    {
        var key = $"kb:rmf:explain:{topic}";

        if (args.TryGetValue("step_number", out var step))
            key += $":{step}";
        if (args.TryGetValue("organization", out var org))
            key += $":{org}";
        if (args.TryGetValue("instruction_id", out var instrId))
            key += $":{instrId}";

        return key.ToLowerInvariant();
    }
}
