using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tenancy;

/// <summary>
/// T134 [Phase 10 / FR-081 / FR-082]: unit coverage of
/// <see cref="GlobalBaselineService"/>. Validates publish/list/get/unpublish
/// semantics + audit emission. Uses the EF Core in-memory provider via
/// <see cref="TestDbContextFactory"/>.
/// </summary>
public class GlobalBaselineServiceTests
{
    private static readonly Guid TenantA = new("11111111-1111-1111-1111-111111111111");

    private static (TestDbContextFactory factory, GlobalBaselineService service) Build()
    {
        var opts = new DbContextOptionsBuilder<AtoCopilotContext>()
            .UseInMemoryDatabase($"gb-{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(opts);

        var ctx = new Mock<ITenantContext>();
        ctx.SetupGet(c => c.EffectiveTenantId).Returns(TenantA);
        ctx.SetupGet(c => c.TenantId).Returns(TenantA);
        ctx.SetupGet(c => c.IsCspAdmin).Returns(true);

        var svc = new GlobalBaselineService(factory, ctx.Object,
            NullLogger<GlobalBaselineService>.Instance);
        return (factory, svc);
    }

    [Fact]
    public async Task PublishAsync_RequiresAllowedKind()
    {
        var (_, svc) = Build();

        Func<Task> act = () => svc.PublishAsync(
            kind: "BogusKind",
            sourceId: Guid.NewGuid(),
            title: null,
            notes: null,
            actor: "tester",
            cancellationToken: CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PublishAsync_RequiresNonEmptySourceId()
    {
        var (_, svc) = Build();

        Func<Task> act = () => svc.PublishAsync(
            kind: "ControlNarrative",
            sourceId: Guid.Empty,
            title: null,
            notes: null,
            actor: "tester",
            cancellationToken: CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PublishAsync_PersistsRow_AndEmitsAudit()
    {
        var (factory, svc) = Build();
        var sourceId = Guid.NewGuid();

        var baseline = await svc.PublishAsync(
            kind: "ControlNarrative",
            sourceId: sourceId,
            title: "AC-2 narrative",
            notes: "Reusable",
            actor: "csp.admin@org",
            cancellationToken: CancellationToken.None);

        baseline.Id.Should().NotBeEmpty();
        baseline.Kind.Should().Be("ControlNarrative");
        baseline.SourceTenantId.Should().Be(TenantA);
        baseline.PublishedBy.Should().Be("csp.admin@org");

        var stored = await factory.Context.GlobalBaselines
            .AsNoTracking()
            .FirstAsync(b => b.Id == baseline.Id);
        stored.SourceId.Should().Be(sourceId);

        var audit = await factory.Context.AuditLogs
            .Where(a => a.Action == "GlobalBaseline.Publish")
            .ToListAsync();
        audit.Should().NotBeEmpty();
        audit.Last().Outcome.Should().Be(AuditOutcome.Success);
    }

    [Fact]
    public async Task UnpublishAsync_LogicallyDeletes_AndIsIdempotent()
    {
        var (factory, svc) = Build();

        var baseline = await svc.PublishAsync(
            kind: "EvidenceArtifact",
            sourceId: Guid.NewGuid(),
            title: null,
            notes: null,
            actor: "csp.admin@org",
            cancellationToken: CancellationToken.None);

        var first = await svc.UnpublishAsync(baseline.Id, "csp.admin@org", CancellationToken.None);
        first.Should().BeTrue();

        var stored = await factory.Context.GlobalBaselines
            .AsNoTracking()
            .FirstAsync(b => b.Id == baseline.Id);
        stored.UnpublishedAt.Should().NotBeNull();
        stored.UnpublishedBy.Should().Be("csp.admin@org");

        var second = await svc.UnpublishAsync(baseline.Id, "csp.admin@org", CancellationToken.None);
        second.Should().BeFalse(
            "the row was already unpublished — second call is a no-op");
    }

    [Fact]
    public async Task ListAsync_ExcludesUnpublishedRows()
    {
        var (_, svc) = Build();

        var live = await svc.PublishAsync("ControlNarrative", Guid.NewGuid(), null, null, "actor", CancellationToken.None);
        var dead = await svc.PublishAsync("EvidenceArtifact", Guid.NewGuid(), null, null, "actor", CancellationToken.None);
        await svc.UnpublishAsync(dead.Id, "actor", CancellationToken.None);

        var list = await svc.ListAsync(kind: null, page: 1, pageSize: 50, CancellationToken.None);

        list.Should().Contain(b => b.Id == live.Id);
        list.Should().NotContain(b => b.Id == dead.Id);
    }
}
