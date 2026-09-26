using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed class ProviderPublicationConflictException(string errorCode, string message)
    : DbUpdateConcurrencyException($"{errorCode}: {message}")
{
    public string ErrorCode { get; } = errorCode;
}
