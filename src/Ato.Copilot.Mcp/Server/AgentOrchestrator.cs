using Microsoft.Extensions.Logging;
using Ato.Copilot.Agents.Common;

namespace Ato.Copilot.Mcp.Server;

/// <summary>
/// Multi-agent orchestrator that routes user messages to the best-matching agent
/// using confidence-scored CanHandle evaluation. Replaces the hard-coded
/// ClassifyAndRouteAgent approach with a pluggable, self-describing agent routing model.
/// </summary>
public class AgentOrchestrator
{
    private readonly IEnumerable<BaseAgent> _agents;
    private readonly double _minimumThreshold;
    private readonly ILogger<AgentOrchestrator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentOrchestrator"/> class.
    /// </summary>
    /// <param name="agents">All registered agents discovered via DI (IEnumerable&lt;BaseAgent&gt;).</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="minimumThreshold">Minimum confidence score to consider an agent (default: 0.3).</param>
    public AgentOrchestrator(
        IEnumerable<BaseAgent> agents,
        ILogger<AgentOrchestrator> logger,
        double minimumThreshold = 0.3)
    {
        _agents = agents;
        _logger = logger;
        _minimumThreshold = minimumThreshold;
    }

    /// <summary>
    /// Selects the best agent to handle the given message based on confidence scoring.
    /// Each agent evaluates its own CanHandle score; the agent with the highest score
    /// above the minimum threshold is selected.
    /// </summary>
    /// <param name="message">User message to route.</param>
    /// <param name="context">Optional context dictionary with page, systemId, rmfPhase, etc.</param>
    /// <returns>The best-matching agent, or null if no agent scores above the threshold.</returns>
    public virtual BaseAgent? SelectAgent(string message, IDictionary<string, object?>? context = null)
    {
        var scored = _agents
            .Select(a => (agent: a, score: a.CanHandle(message)))
            .ToList();

        // Context-aware boost: when the user is on a system page (systemId present),
        // the Compliance agent gets a boost because system-scoped actions are more
        // likely than generic knowledge queries.
        if (context != null &&
            context.TryGetValue("systemId", out var sysId) &&
            sysId is string systemId &&
            !string.IsNullOrEmpty(systemId))
        {
            scored = scored.Select(x =>
            {
                if (x.agent.AgentName.Contains("Compliance", StringComparison.OrdinalIgnoreCase))
                {
                    var boosted = Math.Min(1.0, x.score + 0.15);
                    _logger.LogDebug("Context boost: {AgentId} {OrigScore:F2} → {BoostedScore:F2} (systemId present)",
                        x.agent.AgentId, x.score, boosted);
                    return (x.agent, score: boosted);
                }
                return x;
            }).ToList();
        }

        // Log all scores for observability
        foreach (var (agent, score) in scored)
        {
            _logger.LogDebug("Agent {AgentId} scored {Score:F2} for message: {Message}",
                agent.AgentId, score, TruncateMessage(message));
        }

        var best = scored
            .Where(x => x.score >= _minimumThreshold)
            .OrderByDescending(x => x.score)
            .FirstOrDefault();

        if (best.agent != null)
        {
            _logger.LogInformation(
                "Orchestrator selected {AgentId} (score: {Score:F2}) for message: {Message}",
                best.agent.AgentId, best.score, TruncateMessage(message));
            return best.agent;
        }

        _logger.LogWarning(
            "No agent scored above threshold {Threshold:F2} for message: {Message}",
            _minimumThreshold, TruncateMessage(message));
        return null;
    }

    /// <summary>
    /// Truncates a message for logging purposes.
    /// </summary>
    private static string TruncateMessage(string message, int maxLength = 80) =>
        message.Length <= maxLength ? message : string.Concat(message.AsSpan(0, maxLength), "...");
}
