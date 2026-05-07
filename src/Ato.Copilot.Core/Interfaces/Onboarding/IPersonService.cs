using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Core.Interfaces.Onboarding;

/// <summary>
/// Hybrid Person directory service (research §R1 / FR-022). Supports:
/// <list type="bullet">
/// <item><description>Local-only Person records (free-text create) for fast wizard entry.</description></item>
/// <item><description>Directory search via Microsoft Graph for promote-on-demand.</description></item>
/// <item><description>One-way promotion of a local Person to a directory-linked record
/// while preserving the same primary-key id.</description></item>
/// </list>
/// </summary>
public interface IPersonService
{
    /// <summary>List all non-removed Person rows for a tenant (paged-friendly — caller may take/skip).</summary>
    Task<IReadOnlyList<Person>> ListAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Search local Person rows by name or email (case-insensitive substring).</summary>
    Task<IReadOnlyList<Person>> SearchLocalAsync(
        Guid tenantId, string query, CancellationToken ct = default);

    /// <summary>Create a new local-only Person row (FR-022).</summary>
    Task<Person> CreateLocalAsync(
        Guid tenantId,
        string displayName,
        string email,
        string? phoneNumber,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default);

    /// <summary>
    /// Search the directory (Microsoft Graph) for users matching a query (display name or
    /// email prefix). Returns lightweight DTOs the UI can present alongside local hits.
    /// </summary>
    Task<IReadOnlyList<DirectoryPersonDto>> SearchDirectoryAsync(
        string query, CancellationToken ct = default);

    /// <summary>
    /// One-way promotion (research §R1) — link an existing local Person to a directory
    /// account. The Person id is preserved so all downstream FKs stay intact.
    /// Throws <see cref="InvalidOperationException"/> when the Person is already
    /// directory-linked.
    /// </summary>
    Task<Person> PromoteToDirectoryAsync(
        Guid tenantId,
        Guid personId,
        Guid entraObjectId,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default);
}

/// <summary>Lightweight directory-search result.</summary>
public sealed record DirectoryPersonDto(
    Guid EntraObjectId,
    string DisplayName,
    string Email,
    string? Department);

/// <summary>
/// Adapter abstraction for Microsoft Graph user search. Implemented in
/// <c>Ato.Copilot.Agents.Compliance.Services.Onboarding</c>; stubbed in unit tests.
/// </summary>
public interface IDirectorySearchClient
{
    /// <summary>Search the directory by display name or email prefix.</summary>
    Task<IReadOnlyList<DirectoryPersonDto>> SearchAsync(string query, CancellationToken ct = default);
}
