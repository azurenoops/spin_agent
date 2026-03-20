using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;
using Ato.Copilot.State.Abstractions;

namespace Ato.Copilot.Agents.Compliance.Tools;

/// <summary>
/// Chat tool with conversation memory for natural language compliance interaction.
/// Maintains conversation context via <see cref="IConversationStateManager"/>.
/// </summary>
public class ComplianceChatTool : BaseTool
{
    private readonly IConversationStateManager _conversationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComplianceChatTool"/> class.
    /// </summary>
    public ComplianceChatTool(
        IConversationStateManager conversationManager,
        ILogger<ComplianceChatTool> logger)
        : base(logger)
    {
        _conversationManager = conversationManager;
    }

    /// <inheritdoc />
    public override string Name => "compliance_chat";

    /// <inheritdoc />
    public override string Description => "Natural language conversation about compliance topics with context memory.";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, ToolParameter> Parameters => new Dictionary<string, ToolParameter>
    {
        ["message"] = new() { Name = "message", Description = "User message", Type = "string", Required = true },
        ["conversation_id"] = new() { Name = "conversation_id", Description = "Conversation ID for context continuity", Type = "string" }
    };

    /// <inheritdoc />
    public override async Task<string> ExecuteCoreAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var message = GetArg<string>(arguments, "message") ?? "";
        var conversationId = GetArg<string>(arguments, "conversation_id");

        // Get or create conversation
        ConversationState conversation;
        if (!string.IsNullOrEmpty(conversationId))
        {
            conversation = await _conversationManager.GetConversationAsync(conversationId, cancellationToken)
                           ?? new ConversationState { Id = conversationId };
        }
        else
        {
            var newId = await _conversationManager.CreateConversationAsync(cancellationToken);
            conversation = new ConversationState { Id = newId };
        }

        // Add user message to conversation history
        conversation.Messages.Add(new ConversationMessage
        {
            Role = "user",
            Content = message
        });

        // Generate contextual response based on conversation history
        var response = GenerateResponse(message, conversation);

        // Add assistant response to conversation history
        conversation.Messages.Add(new ConversationMessage
        {
            Role = "assistant",
            Content = response
        });

        // Save updated conversation
        conversation.LastActivityAt = DateTime.UtcNow;
        await _conversationManager.SaveConversationAsync(conversation, cancellationToken);

        return $"**Conversation**: {conversation.Id}\n\n{response}";
    }

    /// <summary>
    /// Generates a contextual response based on the message and conversation history.
    /// </summary>
    private static string GenerateResponse(string message, ConversationState conversation)
    {
        var lower = message.ToLowerInvariant();
        var hasContext = conversation.Messages.Count > 1;

        if (ContainsAny(lower, "how to start", "getting started", "first steps"))
        {
            return "## Getting Started with Compliance\n\n" +
                   "1. **Configure**: Set your subscription and framework\n" +
                   "2. **Assess**: Run `compliance_assess` for a compliance scan\n" +
                   "3. **Review**: Check findings with `compliance_status`\n" +
                   "4. **Remediate**: Fix issues with `compliance_remediate`\n" +
                   "5. **Evidence**: Collect artifacts with `compliance_collect_evidence`\n" +
                   "6. **Document**: Generate SSP/SAR/POA&M with `compliance_generate_document`";
        }

        if (ContainsAny(lower, "help", "what can you do", "commands", "tools"))
        {
            return "## Available Compliance Tools\n\n" +
                   "- **compliance_assess** — Run compliance assessment\n" +
                   "- **compliance_status** — View current compliance posture\n" +
                   "- **compliance_remediate** — Fix compliance findings\n" +
                   "- **compliance_collect_evidence** — Gather audit evidence\n" +
                   "- **compliance_generate_document** — Generate SSP/SAR/POA&M\n" +
                   "- **compliance_generate_package** — Generate authorization package (ZIP)\n" +
                   "- **compliance_package_status** — Check package generation status\n" +
                   "- **compliance_validate_package** — Run readiness check before packaging\n" +
                   "- **compliance_list_packages** — View package history\n" +
                   "- **compliance_validate_oscal_schema** — Validate OSCAL artifact schemas\n" +
                   "- **compliance_generate_sar** — Generate Security Assessment Report\n" +
                   "- **compliance_edit_sar_section** — Edit SAR section\n" +
                   "- **compliance_review_sar** — Submit/approve SAR\n" +
                   "- **compliance_monitoring** — Continuous monitoring status\n" +
                   "- **compliance_history** — View compliance trends\n" +
                   "- **compliance_audit_log** — View audit trail";
        }

        // Package-related intents (T049)
        if (ContainsAny(lower, "generate authorization package", "generate package", "create package", "build package", "export package"))
        {
            return "## Generate Authorization Package\n\n" +
                   "Use **compliance_generate_package** with parameters:\n" +
                   "- `system_id` — System identifier (required)\n" +
                   "- `evidence_mode` — `embedded` (default) or `manifest_only`\n\n" +
                   "This runs a readiness check, then generates a ZIP containing:\n" +
                   "OSCAL SSP, POA&M, Assessment Results, Assessment Plan, SAR, and evidence.\n\n" +
                   "Use **compliance_package_status** with `package_id` to track progress.";
        }

        if (ContainsAny(lower, "package status", "package progress", "check package"))
        {
            return "## Check Package Status\n\n" +
                   "Use **compliance_package_status** with the `package_id` returned from generation.\n\n" +
                   "Returns: status, artifacts generated, validation results, and download link when complete.";
        }

        if (ContainsAny(lower, "validate package", "readiness check", "package readiness", "pre-submission"))
        {
            return "## Package Readiness Validation\n\n" +
                   "Use **compliance_validate_package** with `system_id` to run pre-submission checks:\n\n" +
                   "- Authorization boundary defined\n" +
                   "- All SSP sections approved\n" +
                   "- SAR exists and approved\n" +
                   "- SAP exists and finalized\n" +
                   "- POA&M items present\n" +
                   "- Cross-artifact control consistency\n" +
                   "- OSCAL schema compliance\n" +
                   "- Evidence coverage";
        }

        if (ContainsAny(lower, "export oscal", "oscal export", "oscal poam", "oscal ssp", "oscal assessment"))
        {
            return "## OSCAL Exports\n\n" +
                   "Use **compliance_export_oscal** with `system_id` and `model`:\n" +
                   "- `ssp` — System Security Plan\n" +
                   "- `poam` — Plan of Action & Milestones\n" +
                   "- `assessment-results` — Assessment Results\n" +
                   "- `assessment-plan` — Security Assessment Plan\n\n" +
                   "Use **compliance_validate_oscal_schema** to verify schema conformance.";
        }

        if (ContainsAny(lower, "package history", "list packages", "previous packages", "past packages"))
        {
            return "## Package History\n\n" +
                   "Use **compliance_list_packages** with `system_id` to view generated packages.\n" +
                   "Optional: `limit` (default 10), `include_failed` (default false).\n\n" +
                   "Packages are retained for 90 days. Expired packages show metadata but cannot be downloaded.";
        }

        // SAR-related intents (T050)
        if (ContainsAny(lower, "generate sar", "create sar", "security assessment report"))
        {
            return "## Security Assessment Report (SAR)\n\n" +
                   "Use **compliance_generate_sar** with `system_id` and `title` to create a new SAR.\n\n" +
                   "SAR lifecycle:\n" +
                   "1. Draft → auto-populated from assessment findings\n" +
                   "2. Edit sections with **compliance_edit_sar_section**\n" +
                   "3. Submit for review with **compliance_review_sar** (action: `submit`)\n" +
                   "4. Approve with **compliance_review_sar** (action: `approve`)";
        }

        if (ContainsAny(lower, "sar status", "sar progress", "sar review"))
        {
            return "## SAR Management\n\n" +
                   "- **compliance_review_sar** — Submit or approve a SAR\n" +
                   "  - Actions: `submit`, `approve`, `reject`\n" +
                   "- **compliance_edit_sar_section** — Edit SAR sections\n" +
                   "  - Sections: ExecutiveSummary, Methodology, Findings, Recommendations, ConclusionRiskAssessment";
        }

        if (hasContext)
        {
            return $"I understand your question about \"{message}\". In the context of our conversation, " +
                   "I can help with compliance assessments, remediation, evidence collection, and documentation. " +
                   "Please use one of the specific compliance tools for detailed operations, or ask me " +
                   "about NIST 800-53, FedRAMP, or ATO processes.";
        }

        return $"I can help with Azure Government compliance for NIST 800-53 / FedRAMP. " +
               $"Your question: \"{message}\"\n\n" +
               "Try asking about:\n" +
               "- \"What is NIST 800-53?\"\n" +
               "- \"How to get started with compliance?\"\n" +
               "- \"What tools are available?\"\n\n" +
               "Or use a specific tool like `compliance_assess` or `compliance_status`.";
    }

    /// <summary>Returns true if the text contains any of the specified keywords (case-insensitive).</summary>
    private static bool ContainsAny(string text, params string[] keywords) =>
        keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
}
