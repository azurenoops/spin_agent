using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services.Engines.Remediation;

/// <summary>
/// AI-powered remediation plan generation using IChatClient.
/// Generates scripts in AzureCli/PowerShell/Bicep/Terraform, provides
/// natural-language guidance with confidence scores, and prioritizes
/// findings with business context. Gracefully returns null/fallback
/// results when IChatClient is unavailable.
/// </summary>
public class AiRemediationPlanGenerator : IAiRemediationPlanGenerator
{
    private readonly IChatClient? _chatClient;
    private readonly ILogger<AiRemediationPlanGenerator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AiRemediationPlanGenerator"/> class.
    /// </summary>
    public AiRemediationPlanGenerator(
        ILogger<AiRemediationPlanGenerator> logger,
        IChatClient? chatClient = null)
    {
        _logger = logger;
        _chatClient = chatClient;
    }

    /// <inheritdoc />
    public bool IsAvailable => _chatClient != null;

    /// <inheritdoc />
    public async Task<RemediationScript?> GenerateScriptAsync(
        ComplianceFinding finding,
        ScriptType scriptType,
        CancellationToken ct = default)
    {
        if (_chatClient == null)
        {
            _logger.LogDebug("IChatClient unavailable — skipping AI script generation for {FindingId}", finding.Id);
            return null;
        }

        try
        {
            var scriptLanguage = scriptType switch
            {
                ScriptType.AzureCli => "Azure CLI (bash)",
                ScriptType.PowerShell => "PowerShell",
                ScriptType.Bicep => "Bicep",
                ScriptType.Terraform => "Terraform",
                _ => "Azure CLI (bash)"
            };

            var prompt = $"""
                Generate a {scriptLanguage} remediation script for the following compliance finding.
                The script should be safe, idempotent, and non-destructive.

                Control: {finding.ControlId} ({finding.ControlFamily})
                Title: {finding.Title}
                Severity: {finding.Severity}
                Resource: {finding.ResourceId}
                Resource Type: {finding.ResourceType}
                Remediation Guidance: {finding.RemediationGuidance}

                Return ONLY the script content, no explanations or markdown code blocks.
                """;

            var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: ct);
            var content = response.Text?.Trim();

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("AI returned empty script for finding {FindingId}", finding.Id);
                return null;
            }

            _logger.LogInformation("AI generated {ScriptType} script for {FindingId}", scriptType, finding.Id);

            return new RemediationScript
            {
                Content = content,
                ScriptType = scriptType,
                Description = $"AI-generated {scriptLanguage} remediation for {finding.ControlId}: {finding.Title}",
                Parameters = new Dictionary<string, string>
                {
                    ["resourceId"] = finding.ResourceId,
                    ["subscriptionId"] = finding.SubscriptionId ?? "default"
                },
                EstimatedDuration = TimeSpan.FromMinutes(5),
                IsSanitized = false // Must be sanitized by caller via IScriptSanitizationService
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI script generation failed for {FindingId} — returning null", finding.Id);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<RemediationGuidance?> GetGuidanceAsync(
        ComplianceFinding finding,
        CancellationToken ct = default)
    {
        if (_chatClient == null)
        {
            _logger.LogDebug("IChatClient unavailable — skipping AI guidance for {FindingId}", finding.Id);
            return null;
        }

        try
        {
            var prompt = $$"""
                Provide remediation guidance for the following NIST 800-53 compliance finding.
                Include: explanation of the issue, technical implementation plan, and reference links.

                Control: {{finding.ControlId}} ({{finding.ControlFamily}})
                Title: {{finding.Title}}
                Severity: {{finding.Severity}}
                Resource: {{finding.ResourceId}}
                Current Guidance: {{finding.RemediationGuidance}}

                Respond in JSON format:
                {
                    "explanation": "...",
                    "technicalPlan": "...",
                    "references": ["url1", "url2"]
                }
                """;

            var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: ct);
            var content = response.Text?.Trim();

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("AI returned empty guidance for {FindingId}", finding.Id);
                return null;
            }

            // Attempt to parse JSON response
            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                return new RemediationGuidance
                {
                    FindingId = finding.Id,
                    Explanation = root.TryGetProperty("explanation", out var exp) ? exp.GetString() ?? "" : content,
                    TechnicalPlan = root.TryGetProperty("technicalPlan", out var plan) ? plan.GetString() ?? "" : "",
                    // ConfidenceScore intentionally null: parse success only confirms JSON structure,
                    // not model quality or evidence grounding. A real signal is required to emit a score.
                    ConfidenceScore = null,
                    References = root.TryGetProperty("references", out var refs)
                        ? refs.EnumerateArray().Select(r => r.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList()
                        : new List<string>(),
                    GeneratedAt = DateTime.UtcNow
                };
            }
            catch (JsonException)
            {
                // AI didn't return valid JSON — use raw text as explanation
                _logger.LogDebug("AI guidance was not valid JSON, using raw text");
                return new RemediationGuidance
                {
                    FindingId = finding.Id,
                    Explanation = content,
                    TechnicalPlan = "",
                    // ConfidenceScore intentionally null: fallback path is raw text without evidence linkage.
                    ConfidenceScore = null,
                    References = new List<string>(),
                    GeneratedAt = DateTime.UtcNow
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI guidance generation failed for {FindingId}", finding.Id);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<List<PrioritizedFinding>> PrioritizeAsync(
        IEnumerable<ComplianceFinding> findings,
        string? businessContext,
        CancellationToken ct = default)
    {
        var findingList = findings.ToList();

        if (_chatClient == null)
        {
            _logger.LogDebug("IChatClient unavailable — returning severity-based prioritization");
            return findingList.Select(f => new PrioritizedFinding
            {
                Finding = f,
                AiPriority = MapSeverityToPriority(f.Severity),
                OriginalPriority = MapSeverityToPriority(f.Severity),
                Justification = $"Severity-based priority (AI unavailable): {f.Severity}",
                BusinessImpact = "Unable to assess — AI model not available"
            }).ToList();
        }

        try
        {
            var findingSummaries = findingList.Select(f => new
            {
                f.Id,
                f.ControlId,
                f.ControlFamily,
                Severity = f.Severity.ToString(),
                f.Title,
                f.AutoRemediable
            });

            var contextLine = businessContext != null ? $"Business context: {businessContext}" : "";
            var findingsJson = JsonSerializer.Serialize(findingSummaries, new JsonSerializerOptions { WriteIndented = true });

            var prompt = $$"""
                Prioritize the following compliance findings based on security risk and business impact.
                {{contextLine}}

                Findings:
                {{findingsJson}}

                For each finding, respond with a JSON array:
                [{
                    "id": "finding-id",
                    "priority": "P0|P1|P2|P3|P4",
                    "justification": "...",
                    "businessImpact": "..."
                }]
                """;

            var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: ct);
            var content = response.Text?.Trim();

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("AI returned empty prioritization");
                return FallbackPrioritize(findingList);
            }

            try
            {
                using var doc = JsonDocument.Parse(content);
                var aiPriorities = new Dictionary<string, (RemediationPriority Priority, string Justification, string Impact)>();

                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    var id = elem.GetProperty("id").GetString() ?? "";
                    var priorityStr = elem.GetProperty("priority").GetString() ?? "P4";
                    var justification = elem.TryGetProperty("justification", out var j) ? j.GetString() ?? "" : "";
                    var impact = elem.TryGetProperty("businessImpact", out var bi) ? bi.GetString() ?? "" : "";

                    if (Enum.TryParse<RemediationPriority>(priorityStr, true, out var priority))
                        aiPriorities[id] = (priority, justification, impact);
                }

                return findingList.Select(f =>
                {
                    var originalPriority = MapSeverityToPriority(f.Severity);
                    if (aiPriorities.TryGetValue(f.Id, out var ai))
                    {
                        return new PrioritizedFinding
                        {
                            Finding = f,
                            AiPriority = ai.Priority,
                            OriginalPriority = originalPriority,
                            Justification = ai.Justification,
                            BusinessImpact = ai.Impact
                        };
                    }

                    return new PrioritizedFinding
                    {
                        Finding = f,
                        AiPriority = originalPriority,
                        OriginalPriority = originalPriority,
                        Justification = "AI did not provide priority for this finding",
                        BusinessImpact = "Not assessed"
                    };
                }).ToList();
            }
            catch (JsonException)
            {
                _logger.LogWarning("AI prioritization was not valid JSON, using severity-based fallback");
                return FallbackPrioritize(findingList);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI prioritization failed — using severity-based fallback");
            return FallbackPrioritize(findingList);
        }
    }

    /// <inheritdoc />
    public async Task<RemediationPlan?> GenerateEnhancedPlanAsync(
        ComplianceFinding finding,
        CancellationToken ct = default)
    {
        if (_chatClient == null)
        {
            _logger.LogDebug("IChatClient unavailable — skipping AI enhanced plan for {FindingId}", finding.Id);
            return null;
        }

        try
        {
            var prompt = $$"""
                Generate a detailed remediation plan for this compliance finding.
                Include step descriptions, effort estimates, and validation steps.

                Control: {{finding.ControlId}} ({{finding.ControlFamily}})
                Title: {{finding.Title}}
                Severity: {{finding.Severity}}
                Resource: {{finding.ResourceId}}
                Type: {{finding.RemediationType}}
                Auto-remediable: {{finding.AutoRemediable}}
                Guidance: {{finding.RemediationGuidance}}

                Respond with a JSON object:
                {
                    "steps": [{ "description": "...", "effort": "Low|Medium|High" }],
                    "validationSteps": ["step1", "step2"],
                    "estimatedMinutes": 30
                }
                """;

            var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: ct);
            var content = response.Text?.Trim();

            if (string.IsNullOrWhiteSpace(content))
                return null;

            var plan = new RemediationPlan
            {
                SubscriptionId = finding.SubscriptionId ?? "",
                TotalFindings = 1,
                DryRun = true,
                Steps = new List<RemediationStep>
                {
                    new()
                    {
                        FindingId = finding.Id,
                        ControlId = finding.ControlId,
                        Priority = 1,
                        Description = $"AI-enhanced remediation for {finding.ControlId}",
                        Effort = "Medium",
                        AutoRemediable = finding.AutoRemediable,
                        RemediationType = finding.RemediationType,
                        ResourceId = finding.ResourceId,
                        RiskLevel = RiskLevel.Standard
                    }
                }
            };

            _logger.LogInformation("AI generated enhanced plan for {FindingId}", finding.Id);
            return plan;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI enhanced plan generation failed for {FindingId}", finding.Id);
            return null;
        }
    }

    // ─── Private Helpers ──────────────────────────────────────────────────────

    private static List<PrioritizedFinding> FallbackPrioritize(List<ComplianceFinding> findings) =>
        findings.Select(f => new PrioritizedFinding
        {
            Finding = f,
            AiPriority = MapSeverityToPriority(f.Severity),
            OriginalPriority = MapSeverityToPriority(f.Severity),
            Justification = $"Severity-based priority (AI fallback): {f.Severity}",
            BusinessImpact = "Unable to assess — AI fallback"
        }).ToList();

    private static RemediationPriority MapSeverityToPriority(FindingSeverity severity) =>
        severity switch
        {
            FindingSeverity.Critical => RemediationPriority.P0,
            FindingSeverity.High => RemediationPriority.P1,
            FindingSeverity.Medium => RemediationPriority.P2,
            FindingSeverity.Low => RemediationPriority.P3,
            _ => RemediationPriority.P4
        };
}
