using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

public interface IResponsibilityDraftGenerator
{
    Task<ResponsibilityDraftSuggestion> GenerateAsync(string controlId,
        IReadOnlyList<ResponsibilityDraftSource> sources, CancellationToken ct = default);
}

public sealed class ResponsibilityGenerationException(string message, Exception? inner = null)
    : Exception(message, inner);
