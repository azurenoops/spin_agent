using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Common;

/// <summary>
/// Composite ranker: tries the primary ranker first; falls back to the secondary
/// if the primary returns an empty list (which embedding rankers do when the
/// network is unavailable).
///
/// Default wiring (registered in DI):
///   primary   = <see cref="EmbeddingToolRanker"/> (Azure OpenAI, requires network)
///   secondary = <see cref="TfIdfToolRanker"/>     (offline-capable)
/// </summary>
public sealed class FallbackToolRanker : IToolRanker
{
    private readonly IToolRanker _primary;
    private readonly IToolRanker _secondary;
    private readonly ILogger<FallbackToolRanker> _logger;

    public FallbackToolRanker(
        IToolRanker primary,
        IToolRanker secondary,
        ILogger<FallbackToolRanker> logger)
    {
        _primary = primary;
        _secondary = secondary;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RankedTool>> RankAsync(
        string message,
        IReadOnlyList<BaseTool> tools,
        CancellationToken cancellationToken = default)
    {
        var result = await _primary.RankAsync(message, tools, cancellationToken);
        if (result.Count > 0)
            return result;

        _logger.LogWarning(
            "FallbackToolRanker: primary ranker returned empty — falling back to secondary");
        return await _secondary.RankAsync(message, tools, cancellationToken);
    }
}
