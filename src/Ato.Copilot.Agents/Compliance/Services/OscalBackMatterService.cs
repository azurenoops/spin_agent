using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Builds OSCAL back-matter from evidence artifacts.
/// EvidenceArtifact.ContentHash already stores SHA-256 — no recomputation.
/// Feature 076 — T008.
/// </summary>
public class OscalBackMatterService : IOscalBackMatterService
{
    private const string FedRampNs = "https://fedramp.gov/ns/oscal";
    private const int MaxArtifacts = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OscalBackMatterService> _logger;

    public OscalBackMatterService(IServiceScopeFactory scopeFactory, ILogger<OscalBackMatterService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<OscalBackMatterSection?> GetBackMatterForSystemAsync(
        string systemId,
        string documentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var artifacts = await db.EvidenceArtifacts
            .AsNoTracking()
            .Where(e => e.RegisteredSystemId == systemId && !e.IsDeleted)
            .OrderByDescending(e => e.UploadedAt)
            .Take(MaxArtifacts)
            .ToListAsync(cancellationToken);

        if (artifacts.Count == 0)
            return null;

        var resources = artifacts.Select(a => new OscalBackMatterResource
        {
            Uuid  = Guid.NewGuid().ToString(),
            Title = a.FileName,
            Props = new List<OscalProp>
            {
                new() { Name = "type", Ns = FedRampNs, Value = a.ArtifactCategory.ToString().ToLower() }
            },
            Rlinks = new List<OscalResourceLink>
            {
                new()
                {
                    Href      = a.StoragePath,
                    MediaType = a.ContentType,
                    Hashes    = string.IsNullOrWhiteSpace(a.ContentHash) ? null : new List<OscalHash>
                    {
                        new() { Algorithm = "SHA-256", Value = a.ContentHash }
                    }
                }
            }
        }).ToList();

        _logger.LogDebug("OscalBackMatter: built {Count} resources for system {SystemId} ({DocType})",
            resources.Count, systemId, documentType);

        return new OscalBackMatterSection { Resources = resources };
    }
}
