using System.Text.Json;
using Ato.Copilot.Core.Configuration.Tenancy;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tenancy.Csp;

/// <summary>
/// T032 [Feature 050 / US3] — assert
/// <see cref="CspInheritedComponentService.RemapAsync"/> writes
/// per-changed-capability history rows per R11 / contracts/internal-services.md § 2.4:
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>One <c>remapRunId</c> GUID generated at run start, shared by all
///         rows in the run.</item>
///   <item>AI created new → one <c>Created</c> row with metadata
///         <c>{ remapRunId, source: "Remap" }</c>.</item>
///   <item>AI changed existing AI row → one <c>Edited</c> row with
///         metadata <c>{ remapRunId, source: "Remap" }</c>.</item>
///   <item>AI removed existing AI row → one <c>Archived</c> row (status
///         flipped to Archived rather than hard-deleted) with metadata
///         <c>{ remapRunId, source: "Remap" }</c>.</item>
///   <item>Preserved <c>MappedBy = User</c> row → ZERO history rows.</item>
///   <item>AI row whose output is identical to prior state → ZERO history
///         rows.</item>
///   <item><c>actorOid</c> on every row equals the human caller's OID.</item>
/// </list>
/// </remarks>
public sealed class RemapAsyncAuditTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly Guid _tenantId = Guid.NewGuid();

    public RemapAsyncAuditTests()
    {
        var options = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"RemapAudit_{Guid.NewGuid()}")
            .Options;
        _factory = new TestDbContextFactory(options);
    }

    public void Dispose() => GC.SuppressFinalize(this);

    [Fact]
    public async Task Remap_ClassifiesByName_WritesOnePerChangedCapability_WithSharedRemapRunId()
    {
        // Arrange — seed 4 existing capabilities under one component:
        //   • "auth"        (AI, will be unchanged by remap)
        //   • "rbac"        (AI, AI will change its description)
        //   • "audit logs"  (AI, AI will NOT include it → archive)
        //   • "manual cap"  (User, preserved entirely → no history)
        var componentId = await SeedComponentAsync();
        await SeedCapabilityAsync(componentId, "auth",       MappedBy.AI,   description: "Authentication seed.");
        await SeedCapabilityAsync(componentId, "rbac",       MappedBy.AI,   description: "OLD desc.");
        await SeedCapabilityAsync(componentId, "audit logs", MappedBy.AI,   description: "Audit seed.");
        await SeedCapabilityAsync(componentId, "manual cap", MappedBy.User, description: "Human-authored.");

        var aiMapping = new Mock<ICspCapabilityMappingService>();
        aiMapping
            .Setup(s => s.MapAsync(It.IsAny<CspInheritedComponent>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CapabilityMappingResult(
                Mapped: new[]
                {
                    NewMapping(componentId, "auth", "Authentication seed."), // unchanged
                    NewMapping(componentId, "rbac", "NEW desc."),             // changed
                    NewMapping(componentId, "encryption", "Fresh new cap."), // created
                },
                NeedsReview: Array.Empty<CspInheritedCapability>(),
                AiMappingAvailable: true,
                AiMappingFailureReason: null));

        var sut = BuildSut(aiMapping.Object);

        // Act
        await sut.RemapAsync(componentId, preserveHumanMappings: true, actor: "user-oid");

        // Assert — group rows by capability and classify.
        await using var db = _factory.CreateDbContext();
        var rows = await db.CapabilityHistoryEvents
            .Where(h => h.TenantId == _tenantId)
            .ToListAsync();

        // remapRunId should be the same for every row.
        var runIds = rows
            .Select(r => ExtractMetadataField(r.MetadataJson, "remapRunId"))
            .Where(v => v is not null)
            .Distinct()
            .ToList();
        runIds.Should().HaveCount(1, "all rows must share the same remapRunId GUID.");

        // All rows must carry source = "Remap" and actorOid = caller.
        rows.Should().AllSatisfy(r =>
        {
            r.ActorOid.Should().Be("user-oid");
            ExtractMetadataField(r.MetadataJson, "source").Should().Be("Remap");
        });

        // Classify by event type — must match the seeded scenario.
        var byType = rows.GroupBy(r => r.EventType)
            .ToDictionary(g => g.Key, g => g.Count());
        byType.GetValueOrDefault(CapabilityHistoryEventType.Created).Should().Be(1,
            "exactly one new AI capability (encryption) was added.");
        byType.GetValueOrDefault(CapabilityHistoryEventType.Edited).Should().Be(1,
            "exactly one AI capability (rbac) had its description change.");
        byType.GetValueOrDefault(CapabilityHistoryEventType.Archived).Should().Be(1,
            "exactly one AI capability (audit logs) was no longer produced.");

        // Total rows = 3 (auth + manual cap contribute zero).
        rows.Should().HaveCount(3);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static string? ExtractMetadataField(string? json, string field)
    {
        if (json is null) return null;
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(field, out var prop)) return null;
        return prop.GetString();
    }

    private CspInheritedComponentService BuildSut(ICspCapabilityMappingService aiMapping)
    {
        var historySvc = new CapabilityHistoryService(
            _factory, NullLogger<CapabilityHistoryService>.Instance);
        var options = Options.Create(new CspInheritedOptions { MappingConfidenceThreshold = 0.6d });

        var tenantCtx = new Mock<ITenantContext>();
        tenantCtx.SetupGet(t => t.TenantId).Returns(_tenantId);
        tenantCtx.SetupGet(t => t.EffectiveTenantId).Returns(_tenantId);
        tenantCtx.SetupGet(t => t.IsCspAdmin).Returns(true);
        tenantCtx.SetupGet(t => t.Status).Returns(TenantStatus.Active);

        return new CspInheritedComponentService(
            _factory,
            aiMapping,
            options,
            NullLogger<CspInheritedComponentService>.Instance,
            historySvc,
            tenantCtx.Object);
    }

    private async Task<Guid> SeedComponentAsync()
    {
        await using var db = _factory.CreateDbContext();
        var id = Guid.NewGuid();
        db.CspInheritedComponents.Add(new CspInheritedComponent
        {
            Id = id,
            CspProfileId = Guid.NewGuid(),
            Name = "Component",
            Description = "seed",
            ComponentType = CspComponentType.Service,
            SourceFormat = SourceFormat.Manual,
            Status = CspInheritedComponentStatus.Published,
            ImportedAt = DateTimeOffset.UtcNow,
            ImportedBy = "seed",
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SeedCapabilityAsync(
        Guid componentId, string name, MappedBy mappedBy, string description)
    {
        await using var db = _factory.CreateDbContext();
        db.CspInheritedCapabilities.Add(new CspInheritedCapability
        {
            Id = Guid.NewGuid(),
            CspInheritedComponentId = componentId,
            Name = name,
            Description = description,
            MappedNistControlIds = new List<string> { "AC-2" },
            // Match the confidence the mock pipeline emits below so the
            // "unchanged AI row" branch in RowsAreEquivalent succeeds.
            // User-mapped rows have null confidence per real-world contract.
            MappingConfidence = mappedBy == MappedBy.AI ? 0.91 : (double?)null,
            MappedBy = mappedBy,
            Status = CspInheritedCapabilityStatus.Mapped,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CreatedBy = mappedBy == MappedBy.User ? "human" : "system",
        });
        await db.SaveChangesAsync();
    }

    private static CspInheritedCapability NewMapping(Guid componentId, string name, string description)
        => new()
        {
            Id = Guid.NewGuid(),
            CspInheritedComponentId = componentId,
            Name = name,
            Description = description,
            MappedNistControlIds = new List<string> { "AC-2" },
            MappingConfidence = 0.91,
            MappedBy = MappedBy.AI,
            Status = CspInheritedCapabilityStatus.Mapped,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "system",
        };

    private sealed class TestDbContextFactory : IDbContextFactory<AtoCopilotContext>
    {
        private readonly DbContextOptions<AtoCopilotContext> _options;
        public TestDbContextFactory(DbContextOptions<AtoCopilotContext> options) => _options = options;
        public AtoCopilotContext CreateDbContext() => new(_options);
    }
}
