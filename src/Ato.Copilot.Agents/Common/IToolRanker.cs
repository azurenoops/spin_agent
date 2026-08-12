using Microsoft.Extensions.AI;

namespace Ato.Copilot.Agents.Common;

/// <summary>
/// Ranks a candidate set of tools by relevance to a user message.
/// Used by <see cref="BaseAgent"/> when the registered tool count exceeds
/// <c>MaxToolsPerRequest</c> (128) and a subset must be selected.
///
/// Implementations MUST be thread-safe and SHOULD be pre-initialized so the
/// hot path (per-message tool selection) does not block on startup work.
/// </summary>
public interface IToolRanker
{
    /// <summary>
    /// Rank <paramref name="tools"/> by relevance to <paramref name="message"/>.
    /// Returns all tools in descending score order.  Tools that must always be
    /// included (core compliance tools) receive a score of 1.0.
    /// </summary>
    Task<IReadOnlyList<RankedTool>> RankAsync(
        string message,
        IReadOnlyList<BaseTool> tools,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A scored tool candidate returned by <see cref="IToolRanker"/>.
/// </summary>
/// <param name="Tool">The tool instance.</param>
/// <param name="Score">Relevance score in [0, 1].</param>
/// <param name="Reason">Human-readable reason for the score (for audit trail).</param>
public sealed record RankedTool(BaseTool Tool, double Score, string Reason);
