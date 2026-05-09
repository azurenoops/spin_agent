using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.Tenancy;

/// <summary>
/// Feature 048 T203 — persists the candidate components from a
/// <see cref="ParsedAtoDocument"/> as <see cref="CspInheritedComponent"/>
/// rows owned by the singleton <see cref="CspProfile"/> (FR-007 / FR-100).
/// </summary>
/// <remarks>
/// <para>
/// Dedupes by <c>(Name, ComponentType)</c> per <c>CspProfileId</c> so
/// re-uploads of the same artifact (or partial overlap across multiple
/// uploads) do not create duplicate rows. Existing rows are <strong>not
/// overwritten</strong> — the human reviewer keeps any prior edits.
/// </para>
/// <para>
/// All newly-created rows land in
/// <see cref="CspInheritedComponentStatus.Draft"/>. The wizard's "submit"
/// step (T209) flips Draft rows for the wizard's <c>CspProfileId</c> to
/// <see cref="CspInheritedComponentStatus.Published"/> in the same
/// transaction as setting <c>CspProfile.OnboardingState = Active</c>.
/// </para>
/// </remarks>
public sealed class CspComponentExtractionService : ICspComponentExtractionService
{
    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly ILogger<CspComponentExtractionService> _logger;

    public CspComponentExtractionService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        ILogger<CspComponentExtractionService> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CspInheritedComponent>> ExtractAsync(
        ParsedAtoDocument document,
        Guid cspProfileId,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        if (document.Components.Count == 0)
        {
            return Array.Empty<CspInheritedComponent>();
        }

        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Pre-load existing (Name, ComponentType) pairs for this profile so
        // we can short-circuit duplicates without a per-row round-trip.
        var existingKeys = await db.CspInheritedComponents
            .Where(c => c.CspProfileId == cspProfileId)
            .Select(c => new { c.Name, c.ComponentType })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var seen = existingKeys
            .Select(k => Key(k.Name, k.ComponentType))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var persisted = new List<CspInheritedComponent>(document.Components.Count);
        foreach (var candidate in document.Components)
        {
            ct.ThrowIfCancellationRequested();
            var name = candidate.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            // Truncate at the entity's MaxLength so the bulk insert never
            // fails on a single oversized row.
            if (name!.Length > 256)
            {
                name = name[..256];
            }
            var description = (candidate.Description ?? string.Empty).Trim();
            if (description.Length > 2000)
            {
                description = description[..2000];
            }

            var key = Key(name, candidate.ComponentType);
            if (!seen.Add(key))
            {
                // Either an existing row OR a duplicate inside the SAME
                // ParsedAtoDocument — skip in both cases.
                continue;
            }

            var sourceFileName = document.SourceFileName.Length > 512
                ? document.SourceFileName[..512]
                : document.SourceFileName;
            var sourceArtifactRef = document.SourceArtifactReference;
            if (sourceArtifactRef is { Length: > 2048 })
            {
                sourceArtifactRef = sourceArtifactRef[..2048];
            }

            var row = new CspInheritedComponent
            {
                Id = Guid.NewGuid(),
                CspProfileId = cspProfileId,
                Name = name,
                Description = description,
                ComponentType = candidate.ComponentType,
                SourceFileName = sourceFileName,
                SourceFormat = document.Format,
                SourceArtifactReference = sourceArtifactRef,
                Status = CspInheritedComponentStatus.Draft,
                ImportedAt = DateTimeOffset.UtcNow,
                ImportedBy = actor,
            };
            db.CspInheritedComponents.Add(row);
            persisted.Add(row);
        }

        if (persisted.Count == 0)
        {
            return Array.Empty<CspInheritedComponent>();
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Persisted {Count} CspInheritedComponent rows from {File} ({Format}) for profile {ProfileId}",
            persisted.Count, document.SourceFileName, document.Format, cspProfileId);

        return persisted;
    }

    private static string Key(string name, CspComponentType type)
        => $"{type}|{name}";
}
