namespace Ato.Copilot.Agents.Common;

/// <summary>
/// Thrown by <see cref="BaseAgent"/> when the estimated prompt token count exceeds
/// the configured <c>AzureAiOptions.MaxInputTokens</c> hard cap and
/// <c>AzureAiOptions.BudgetMode</c> is <see cref="Ato.Copilot.Core.Configuration.TokenBudgetMode.Reject"/>.
///
/// This is a proactive, pre-flight guard — the LLM request is never sent, so no
/// API cost is incurred and no server-side 400 is possible (BUG-5 / #693).
/// </summary>
/// <remarks>
/// Catch this exception at the agent-dispatch boundary to surface a clear, user-readable
/// error without leaking internal token counts or prompt content.
/// </remarks>
public sealed class TokenBudgetExceededException : InvalidOperationException
{
    /// <summary>Estimated prompt token count that triggered the guard.</summary>
    public int EstimatedTokens { get; }

    /// <summary>Configured maximum input-token cap.</summary>
    public int MaxInputTokens { get; }

    /// <summary>Name of the agent that raised the guard.</summary>
    public string AgentName { get; }

    /// <inheritdoc cref="TokenBudgetExceededException"/>
    public TokenBudgetExceededException(int estimatedTokens, int maxInputTokens, string agentName)
        : base(
            $"Prompt token budget exceeded in agent '{agentName}': " +
            $"estimated {estimatedTokens:N0} tokens exceeds the configured maximum of " +
            $"{maxInputTokens:N0} tokens. Reduce the conversation context or increase " +
            $"AzureAiOptions.MaxInputTokens.")
    {
        EstimatedTokens = estimatedTokens;
        MaxInputTokens = maxInputTokens;
        AgentName = agentName;
        IsTokenBudgetError = true;
    }

    /// <summary>
    /// Marker property — always <c>true</c> — allowing callers to distinguish this
    /// from generic <see cref="InvalidOperationException"/> without a type-check.
    /// </summary>
    public bool IsTokenBudgetError { get; }
}
