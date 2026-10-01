using System.Text.Json;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services;

public sealed class ResponsibilityDraftGenerator(ILogger<ResponsibilityDraftGenerator> logger,
    IChatClient? client = null, AzureAiOptions? options = null) : IResponsibilityDraftGenerator
{
    public static readonly string[] FieldNames =
        ["allocation", "provider", "providerDuties", "customer", "scope", "exclusions", "source", "basis", "information"];
    public static bool IsAllocation(string value) => value is "Inherited" or "Shared" or "Customer" or "NeedsConfirmation";

    public async Task<ResponsibilityDraftSuggestion> GenerateAsync(string controlId,
        IReadOnlyList<ResponsibilityDraftSource> sources, CancellationToken ct = default)
    {
        if (client is null || options is { Enabled: false })
            throw new ResponsibilityGenerationException("AI is not configured. Source-backed fields remain available for manual drafting.");
        var content = JsonSerializer.Serialize(new { controlId, sources });
        if (content.Length > 100_000) throw new ResponsibilityGenerationException("The authorized source context exceeds the generation limit. Narrow the source scope.");
        using var stream = typeof(ResponsibilityDraftGenerator).Assembly.GetManifestResourceStream(
            "Ato.Copilot.Core.Prompts.ResponsibilityFirstPass.prompt.txt")
            ?? throw new InvalidOperationException("Responsibility generation prompt is unavailable.");
        using var reader = new StreamReader(stream);
        try
        {
            var response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.System, await reader.ReadToEndAsync(ct)), new ChatMessage(ChatRole.User, content)],
                new ChatOptions { Temperature = 0, ResponseFormat = ChatResponseFormat.Json }, ct);
            var result = JsonSerializer.Deserialize<ResponsibilityDraftSuggestion>(response.Text,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var ids = sources.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            if (result?.Values is null || result.Questions is null || result.Conflicts is null
                || result.Questions.Count > 20 || result.Conflicts.Count > 20
                || result.Questions.Concat(result.Conflicts).Any(x => x is null || x.Length > 1000)
                || result.Values.Any(x => !FieldNames.Contains(x.Key) || x.Key == "provider" || x.Value is null
                    || x.Value.Value is null || x.Value.Value.Length > 2000 || x.Value.Explanation is null
                    || x.Value.Explanation.Length > 2000 || x.Value.SourceIds is null
                    || x.Value.SourceIds.Any(id => !ids.Contains(id))
                    || x.Value.Value.Length > 0 && x.Value.SourceIds.Count == 0
                    || x.Key == "allocation" && !IsAllocation(x.Value.Value)))
                throw new ResponsibilityGenerationException("AI returned unsupported fields or source references. No suggestion was applied.");
            return result with { Values = result.Values.ToDictionary(x => x.Key,
                x => x.Value with { Origin = "AI proposed", UserEdited = false }) };
        }
        catch (Exception failure) when (failure is JsonException or HttpRequestException or Azure.RequestFailedException
            or System.ClientModel.ClientResultException)
        {
            logger.LogWarning("Responsibility generation failed for control {ControlId}: {FailureType}", controlId, failure.GetType().Name);
            throw new ResponsibilityGenerationException("Responsibility generation failed. Your saved draft was not replaced.", failure);
        }
    }
}
