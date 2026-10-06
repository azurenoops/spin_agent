using System.Text.Json;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services;

public partial class NarrativeTemplateService
{
    public async Task<RequirementFirstPassSuggestion> GenerateRequirementFirstPassAsync(
        RequirementFirstPassContext context, CancellationToken cancellationToken = default)
    {
        if (!IsAiEnabled) throw new InvalidOperationException("AI_NOT_AVAILABLE: First-pass drafting is not configured. You can still enter a manual draft.");
        var content = JsonSerializer.Serialize(context);
        if (context.Kind is not ("Policy" or "Technical") || content.Length > 100_000)
            throw new ArgumentException("Requirement generation context is invalid or exceeds the supported size.");
        using var stream = typeof(NarrativeTemplateService).Assembly.GetManifestResourceStream("Ato.Copilot.Core.Prompts.RequirementFirstPass.prompt.txt")
            ?? throw new InvalidOperationException("Requirement first-pass prompt is unavailable.");
        using var reader = new StreamReader(stream);
        try
        {
            var response = await _chatClient!.GetResponseAsync(
                [new(ChatRole.System, await reader.ReadToEndAsync(cancellationToken)), new(ChatRole.User, content)],
                new ChatOptions { Temperature = 0, ResponseFormat = ChatResponseFormat.Json }, cancellationToken);
            var result = JsonSerializer.Deserialize<RequirementFirstPassSuggestion>(response.Text, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("AI_FIRST_PASS_FAILED: Model returned an empty first pass.");
            ValidateRequirementSuggestion(context, result);
            return result;
        }
        catch (Exception error) when (error is JsonException or HttpRequestException or Azure.RequestFailedException or System.ClientModel.ClientResultException)
        {
            _logger?.LogWarning("Requirement generation failed for {ControlId}: {FailureType}", context.ControlId, error.GetType().Name);
            throw new InvalidOperationException("AI_FIRST_PASS_FAILED: First-pass generation failed. Your draft was not changed.", error);
        }
    }

    public static void ValidateRequirementSuggestion(RequirementFirstPassContext context, RequirementFirstPassSuggestion result)
    {
        var sourceIds = context.Sources.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        bool ValidSources(IReadOnlyList<string>? ids) => ids is { Count: > 0 and <= 20 } && ids.All(sourceIds.Contains);
        bool ValidText(string? text, int max) => !string.IsNullOrWhiteSpace(text) && text.Length <= max
            && !text.Contains("{{ insert:", StringComparison.OrdinalIgnoreCase);
        if (result.Responses is null || result.Parameters is null || result.Questions is null || result.Conflicts is null
            || result.Responses.Count > context.Requirements.Count || result.Parameters.Count > context.Parameters.Count
            || result.Questions.Count > 30 || result.Conflicts.Count > 30
            || result.Responses.Count == 0 && result.Parameters.Count == 0 && result.Questions.Count == 0 && result.Conflicts.Count == 0
            || result.Questions.Concat(result.Conflicts).Any(s => !ValidText(s, 2000))
            || result.Responses.GroupBy(r => r?.StatementId).Any(g => g.Count() != 1)
            || result.Parameters.GroupBy(p => p?.ParameterId).Any(g => g.Count() != 1)
            || result.Responses.Any(r => r is null || !context.Requirements.Any(q => q.Id == r.StatementId)
                || !ValidText(r.Response, 8000) || !ValidText(r.Explanation, 2000) || !ValidSources(r.SourceIds))
            || result.Parameters.Any(p => p is null || !ValidText(p.Value, 2000) || !ValidText(p.Explanation, 2000) || !ValidSources(p.SourceIds)
                || !context.Parameters.Any(q => q.Id == p.ParameterId) || !GroundedParameter(context, p)))
            throw new InvalidOperationException("AI_FIRST_PASS_FAILED: AI returned unsupported fields, source references or parameter values. No suggestion was applied.");
    }

    private static bool GroundedParameter(RequirementFirstPassContext context, RequirementParameterDraft draft)
    {
        var parameter = context.Parameters.Single(p => p.Id == draft.ParameterId);
        using var definition = JsonDocument.Parse(parameter.Definition);
        var label = definition.RootElement.TryGetProperty("label", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        var authority = label.Contains("authority", StringComparison.OrdinalIgnoreCase) || label.Contains("legal", StringComparison.OrdinalIgnoreCase);
        return context.Sources.Where(s => draft.SourceIds.Contains(s.Id))
            .Any(s => (!authority || s.Kind is "RetainedPolicy" or "RecordedParameter")
                && s.RecordedValues.Any(v => string.Equals(v.Trim(), draft.Value.Trim(), StringComparison.Ordinal)));
    }
}
