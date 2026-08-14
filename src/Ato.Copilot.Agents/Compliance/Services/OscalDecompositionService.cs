using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// AI-assisted OSCAL control statement decomposition service (Feature 076 — T012).
/// Uses <see cref="IChatClient"/> for a single-shot structured JSON response via
/// Azure AI Foundry; does NOT use persistent Foundry threads (decomposition is one-shot).
/// </summary>
public sealed class OscalDecompositionService : IOscalDecompositionService
{
    private readonly IChatClient _chatClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OscalDecompositionService> _logger;

    // ── Prompt ───────────────────────────────────────────────────────────────

    private const string DecompositionSystemPrompt =
        "You are an OSCAL compliance expert. Decompose the following NIST SP 800-53 control " +
        "implementation narrative into OSCAL 1.1.2 statement-level fragments.\n\n" +
        "Control ID: {controlId}\n" +
        "Narrative: {narrative}\n\n" +
        "Return a JSON object with this exact schema:\n" +
        "{{\n" +
        "  \"fragments\": [\n" +
        "    {{\n" +
        "      \"statement_id\": \"ac-1_smt.a\",\n" +
        "      \"component_uuid\": null,\n" +
        "      \"description\": \"narrative text for this statement\",\n" +
        "      \"suggested_params\": [\n" +
        "        {{ \"param_id\": \"ac-1_prm_1\", \"value\": \"annually\" }}\n" +
        "      ],\n" +
        "      \"confidence_score\": 0.92\n" +
        "    }}\n" +
        "  ]\n" +
        "}}\n\n" +
        "Rules:\n" +
        "1. statement_id must follow OSCAL format: {controlId}_smt.a, {controlId}_smt.b, etc.\n" +
        "2. confidence_score 0.0-1.0 (how confident the fragment mapping is correct)\n" +
        "3. component_uuid: null unless you can infer from context\n" +
        "4. Return ONLY the JSON object - no markdown, no explanation.\n" +
        "5. confidence_score is model self-reported and does not constitute an ATO determination. It always requires human review and validation before use in any compliance decision.";

    // ── JSON parsing ─────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Internal records for AI response parsing ─────────────────────────────

    private record DecompositionOutput(
        [property: JsonPropertyName("fragments")] List<FragmentOutput> Fragments);

    private record FragmentOutput(
        [property: JsonPropertyName("statement_id")] string StatementId,
        [property: JsonPropertyName("component_uuid")] string? ComponentUuid,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("suggested_params")] List<ParamOutput> SuggestedParams,
        [property: JsonPropertyName("confidence_score")] double? ConfidenceScore,
        string DerivationBasis = "ModelSelfReported",
        bool RequiresHumanValidation = true);

    private record ParamOutput(
        [property: JsonPropertyName("param_id")] string ParamId,
        [property: JsonPropertyName("value")] string Value);

    // ── Constructor ───────────────────────────────────────────────────────────

    public OscalDecompositionService(
        IChatClient chatClient,
        IServiceScopeFactory scopeFactory,
        ILogger<OscalDecompositionService> logger)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── IOscalDecompositionService ────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<DecompositionDraftDto> DecomposeAsync(
        string tenantId,
        string systemId,
        string controlId,
        string narrative,
        string requestedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId, nameof(tenantId));
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(controlId, nameof(controlId));
        ArgumentException.ThrowIfNullOrWhiteSpace(narrative, nameof(narrative));
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy, nameof(requestedBy));

        _logger.LogInformation(
            "Starting OSCAL decomposition for system={SystemId}, control={ControlId}, requestedBy={RequestedBy}",
            systemId, controlId, requestedBy);

        // 1. Build the prompt with the control ID and narrative interpolated
        var prompt = DecompositionSystemPrompt
            .Replace("{controlId}", controlId)
            .Replace("{narrative}", narrative);

        // 2. Call IChatClient for a single-shot structured JSON response
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, prompt)
        };

        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.Json
        };

        List<FragmentOutput> parsedFragments;

        try
        {
            var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken);

            var rawJson = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<TextContent>()
                .Select(t => t.Text)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .FirstOrDefault() ?? string.Empty;

            _logger.LogDebug(
                "OSCAL decomposition AI response for control={ControlId}: {RawJson}",
                controlId, rawJson.Length > 500 ? rawJson[..500] + "..." : rawJson);

            // 3. Parse the JSON response
            var output = JsonSerializer.Deserialize<DecompositionOutput>(rawJson, JsonOpts);
            parsedFragments = output?.Fragments ?? [];
        }
        catch (JsonException ex)
        {
            // 4. Fallback: return a single fragment with the full narrative
            _logger.LogWarning(ex,
                "OSCAL decomposition JSON parse failed for control={ControlId}; using full-narrative fallback",
                controlId);
            parsedFragments =
            [
                new FragmentOutput(
                    StatementId: $"{controlId}_smt.a",
                    ComponentUuid: null,
                    Description: narrative,
                    SuggestedParams: [],
                    ConfidenceScore: null,
                    DerivationBasis: "Fallback",
                    RequiresHumanValidation: true)
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "OSCAL decomposition AI call failed for control={ControlId}; using full-narrative fallback",
                controlId);
            parsedFragments =
            [
                new FragmentOutput(
                    StatementId: $"{controlId}_smt.a",
                    ComponentUuid: null,
                    Description: narrative,
                    SuggestedParams: [],
                    ConfidenceScore: null,
                    DerivationBasis: "Fallback",
                    RequiresHumanValidation: true)
            ];
        }

        // 5. Persist OscalDecompositionDraft + OscalDecompositionFragment rows
        var draftId = Guid.NewGuid().ToString();
        var generatedAt = DateTimeOffset.UtcNow;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var draft = new OscalDecompositionDraft
        {
            Id = draftId,
            TenantId = Guid.Parse(tenantId),
            RegisteredSystemId = systemId,
            ControlId = controlId,
            Status = DecompositionDraftStatus.Pending,
            GeneratedAt = generatedAt.UtcDateTime,
            GeneratedBy = requestedBy,
        };

        var fragmentEntities = parsedFragments.Select(f => new OscalDecompositionFragment
        {
            Id = Guid.NewGuid().ToString(),
            DraftId = draftId,
            StatementId = f.StatementId,
            ComponentUuid = f.ComponentUuid,
            Description = f.Description,
            SuggestedParamsJson = JsonSerializer.Serialize(f.SuggestedParams, JsonOpts),
            ConfidenceScore = f.ConfidenceScore,
        }).ToList();

        draft.Fragments = fragmentEntities;

        db.OscalDecompositionDrafts.Add(draft);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Persisted OSCAL decomposition draft={DraftId} with {FragmentCount} fragment(s) for control={ControlId}",
            draftId, fragmentEntities.Count, controlId);

        // 6. Return DTO
        return MapDraftToDto(draft, fragmentEntities);
    }

    /// <inheritdoc />
    public async Task<DecompositionDraftDto?> GetDraftAsync(
        string tenantId,
        string systemId,
        string controlId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var draft = await db.OscalDecompositionDrafts
            .AsNoTracking()
            .Include(d => d.Fragments)
            .Where(d => d.TenantId == Guid.Parse(tenantId)
                     && d.RegisteredSystemId == systemId
                     && d.ControlId == controlId
                     && d.Status == DecompositionDraftStatus.Pending)
            .OrderByDescending(d => d.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (draft is null)
            return null;

        return MapDraftToDto(draft, draft.Fragments);
    }

    /// <inheritdoc />
    public async Task<DecompositionApprovalResult> ApproveAsync(
        string tenantId,
        string systemId,
        string controlId,
        string approvedBy,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var draft = await db.OscalDecompositionDrafts
            .Include(d => d.Fragments)
            .Where(d => d.TenantId == Guid.Parse(tenantId)
                     && d.RegisteredSystemId == systemId
                     && d.ControlId == controlId
                     && d.Status == DecompositionDraftStatus.Pending)
            .OrderByDescending(d => d.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                $"No pending decomposition draft found for system={systemId}, control={controlId}.");

        // Mark draft Approved
        draft.Status = DecompositionDraftStatus.Approved;
        draft.ApprovedBy = approvedBy;
        draft.ApprovedAt = DateTime.UtcNow;

        var approvedAt = DateTimeOffset.UtcNow;
        var fragmentCount = draft.Fragments.Count;

        // Update ControlImplementation narrative (concatenate all fragment descriptions)
        var controlImpl = await db.ControlImplementations
            .FirstOrDefaultAsync(
                ci => ci.RegisteredSystemId == systemId && ci.ControlId == controlId,
                cancellationToken);

        if (controlImpl is null)
        {
            _logger.LogWarning(
                "No ControlImplementation found for system={SystemId}, control={ControlId}; " +
                "skipping narrative update (draft={DraftId} still marked Approved).",
                systemId, controlId, draft.Id);
        }
        else
        {
            var compositeNarrative = string.Join("\n\n", draft.Fragments
                .OrderBy(f => f.StatementId)
                .Select(f => $"[{f.StatementId}] {f.Description}"));

            controlImpl.Narrative = compositeNarrative;
            controlImpl.AiSuggested = true;
            controlImpl.ModifiedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Approved OSCAL decomposition draft={DraftId} for system={SystemId}, control={ControlId}; " +
            "{FragmentCount} fragment(s) applied by {ApprovedBy}",
            draft.Id, systemId, controlId, fragmentCount, approvedBy);

        return new DecompositionApprovalResult(
            DraftId: draft.Id,
            ControlId: controlId,
            FragmentsApplied: fragmentCount,
            ApprovedAt: approvedAt);
    }

    /// <inheritdoc />
    public async Task DiscardAsync(
        string tenantId,
        string systemId,
        string controlId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var draft = await db.OscalDecompositionDrafts
            .Where(d => d.TenantId == Guid.Parse(tenantId)
                     && d.RegisteredSystemId == systemId
                     && d.ControlId == controlId
                     && d.Status == DecompositionDraftStatus.Pending)
            .OrderByDescending(d => d.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                $"No pending decomposition draft found for system={systemId}, control={controlId}.");

        draft.Status = DecompositionDraftStatus.Discarded;
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Discarded OSCAL decomposition draft={DraftId} for system={SystemId}, control={ControlId}",
            draft.Id, systemId, controlId);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static DecompositionDraftDto MapDraftToDto(
        OscalDecompositionDraft draft,
        IEnumerable<OscalDecompositionFragment> fragments)
    {
        var fragmentDtos = fragments
            .OrderBy(f => f.StatementId)
            .Select(f =>
            {
                List<SuggestedParamDto> suggestedParams;
                try
                {
                    var raw = JsonSerializer.Deserialize<List<ParamOutput>>(
                        f.SuggestedParamsJson ?? "[]", JsonOpts);
                    suggestedParams = raw?
                        .Select(p => new SuggestedParamDto(p.ParamId, p.Value))
                        .ToList() ?? [];
                }
                catch
                {
                    suggestedParams = [];
                }

                return new DecompositionFragmentDto(
                    FragmentId: f.Id,
                    StatementId: f.StatementId,
                    ComponentUuid: f.ComponentUuid,
                    Description: f.Description,
                    SuggestedParams: suggestedParams,
                    ConfidenceScore: f.ConfidenceScore,
                    DerivationBasis: f.ConfidenceScore.HasValue ? "ModelSelfReported" : "Fallback",
                    RequiresHumanValidation: true);
            })
            .ToList();

        return new DecompositionDraftDto(
            DraftId: draft.Id,
            ControlId: draft.ControlId,
            Status: draft.Status.ToString(),
            GeneratedAt: new DateTimeOffset(draft.GeneratedAt, TimeSpan.Zero),
            GeneratedBy: draft.GeneratedBy,
            Fragments: fragmentDtos);
    }
}
