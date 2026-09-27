using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed partial class EmassConflictRationaleTests : IAsyncLifetime
{
    private readonly Guid tenant = Guid.NewGuid();
    private readonly TenantContextAccessor accessor = new();
    private readonly DbContextOptions<AtoCopilotContext> options =
        new DbContextOptionsBuilder<AtoCopilotContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private EmassRoundTripSyncService service = null!;

    public async Task InitializeAsync()
    {
        await using var db = new AtoCopilotContext(options);
        db.RegisteredSystems.Add(new() { Id = "system", TenantId = tenant, Name = "Original", CreatedBy = "fixture" });
        db.EmassConflicts.Add(new()
        {
            Id = "conflict", TenantId = tenant, RegisteredSystemId = "system", SyncBatchId = "batch",
            EntityType = "SystemInfo", FieldName = "SystemInfo.SystemName", SpinValue = "Original", EmassValue = "Returned",
        });
        await db.SaveChangesAsync();
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(options, accessor));
        service = new(factory.Object);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(ConflictStatus.AcceptEmass)]
    [InlineData(ConflictStatus.KeepSpin)]
    [InlineData(ConflictStatus.Deferred)]
    public async Task Resolution_retains_rationale_actor_and_original_diff_in_existing_audit(ConflictStatus resolution)
    {
        // Arrange
        using var scope = accessor.Push(new TenantContext(tenant));
        var request = new ResolveConflictRequest(resolution, Rationale: "Reviewed authoritative returned source.");
        // Act
        var result = await service.ResolveConflictAsync("system", "conflict", request, "authenticated-reviewer");
        // Assert
        result.Rationale.Should().Be(request.Rationale);
        result.ResolvedBy.Should().Be("authenticated-reviewer");
        result.SpinValue.Should().Be("Original");
        result.EmassValue.Should().Be("Returned");
        await using var verify = new AtoCopilotContext(options);
        var conflict = await verify.EmassConflicts.SingleAsync();
        conflict.Notes.Should().Be(request.Rationale);
        var audit = await verify.AuditLogs.SingleAsync();
        audit.UserId.Should().Be("authenticated-reviewer");
        audit.TenantId.Should().Be(tenant);
        var details = JsonSerializer.Deserialize<JsonElement>(audit.Details);
        details.GetProperty("Rationale").GetString().Should().Be(request.Rationale);
        details.GetProperty("Resolution").GetString().Should().Be(resolution.ToString());
        details.GetProperty("SpinValue").GetString().Should().Be("Original");
        details.GetProperty("EmassValue").GetString().Should().Be("Returned");
        (await verify.RegisteredSystems.SingleAsync()).Name.Should().Be(resolution == ConflictStatus.AcceptEmass ? "Returned" : "Original");
        verify.Set<EmassExchangeRecord>().Should().BeEmpty();
        verify.AuthorizationDecisions.Should().BeEmpty();
        (await service.GetConflictsAsync("system", status: null)).Single().Rationale.Should().Be(request.Rationale);
    }

    [Fact]
    public async Task Deferred_then_resolved_retains_both_reviewer_rationales_and_actors()
    {
        // Arrange
        using var scope = accessor.Push(new TenantContext(tenant));
        await service.ResolveConflictAsync("system", "conflict",
            new ResolveConflictRequest(ConflictStatus.Deferred, Rationale: "Waiting for owner confirmation."), "first-reviewer");
        // Act
        await service.ResolveConflictAsync("system", "conflict",
            new ResolveConflictRequest(ConflictStatus.KeepSpin, Rationale: "Owner confirmed local value."), "second-reviewer");
        // Assert
        await using var verify = new AtoCopilotContext(options);
        var audits = await verify.AuditLogs.ToListAsync();
        audits.Should().HaveCount(2);
        JsonSerializer.Deserialize<JsonElement>(audits.Single(x => x.UserId == "first-reviewer").Details)
            .GetProperty("Rationale").GetString().Should().Be("Waiting for owner confirmation.");
        var latest = JsonSerializer.Deserialize<JsonElement>(audits.Single(x => x.UserId == "second-reviewer").Details);
        latest.GetProperty("PreviousRationale").GetString().Should().Be("Waiting for owner confirmation.");
        latest.GetProperty("PreviousResolvedBy").GetString().Should().Be("first-reviewer");
        latest.GetProperty("PreviousResolution").GetString().Should().Be("Deferred");
        var conflict = await verify.EmassConflicts.SingleAsync();
        conflict.SpinValue.Should().Be("Original");
        conflict.EmassValue.Should().Be("Returned");
        conflict.Notes.Should().Be("Owner confirmed local value.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Legacy reviewer notes.")]
    public async Task Legacy_notes_and_absent_rationale_remain_supported(string? notes)
    {
        // Arrange
        using var scope = accessor.Push(new TenantContext(tenant));
        // Act
        var result = await service.ResolveConflictAsync("system", "conflict",
            new ResolveConflictRequest(ConflictStatus.AcceptEmass, Notes: notes), "legacy-reviewer");
        // Assert
        result.Rationale.Should().Be(notes);
        await using var verify = new AtoCopilotContext(options);
        (await verify.EmassConflicts.SingleAsync()).Notes.Should().Be(notes);
    }

    [Theory]
    [InlineData("wrong-system")]
    [InlineData("wrong-tenant")]
    public async Task Inaccessible_conflict_cannot_change_data_rationale_or_audit(string denied)
    {
        // Arrange
        using var scope = accessor.Push(new TenantContext(denied == "wrong-tenant" ? Guid.NewGuid() : tenant));
        // Act
        var write = () => service.ResolveConflictAsync(denied == "wrong-system" ? "other-system" : "system", "conflict",
            new ResolveConflictRequest(ConflictStatus.AcceptEmass, Rationale: "Not authorized for this resource."), "other-reviewer");
        // Assert
        await write.Should().ThrowAsync<InvalidOperationException>();
        await using var verify = new AtoCopilotContext(options);
        (await verify.RegisteredSystems.SingleAsync()).Name.Should().Be("Original");
        (await verify.EmassConflicts.SingleAsync()).Notes.Should().BeNull();
        verify.AuditLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task Current_field_changed_after_diff_rejects_overwrite_without_claiming_a_resolution()
    {
        // Arrange
        using var scope = accessor.Push(new TenantContext(tenant));
        await using (var change = new AtoCopilotContext(options))
        {
            (await change.RegisteredSystems.SingleAsync()).Name = "Locally edited after comparison";
            await change.SaveChangesAsync();
        }
        // Act
        var overwrite = () => service.ResolveConflictAsync("system", "conflict",
            new ResolveConflictRequest(ConflictStatus.AcceptEmass, Rationale: "Review based on stale comparison."), "reviewer");
        // Assert
        await overwrite.Should().ThrowAsync<EmassConflictChangedException>();
        await using var verify = new AtoCopilotContext(options);
        (await verify.RegisteredSystems.SingleAsync()).Name.Should().Be("Locally edited after comparison");
        (await verify.EmassConflicts.SingleAsync()).ConflictStatus.Should().Be(ConflictStatus.Unresolved);
        verify.AuditLogs.Should().BeEmpty();
    }

    [Theory]
    [InlineData("too-long")]
    [InlineData("ambiguous")]
    [InlineData("unknown-resolution")]
    [InlineData("empty-actor")]
    public async Task Invalid_resolution_metadata_does_not_overwrite_the_conflict(string invalid)
    {
        // Arrange
        using var scope = accessor.Push(new TenantContext(tenant));
        var request = invalid switch
        {
            "too-long" => new ResolveConflictRequest(ConflictStatus.AcceptEmass, Rationale: new string('r', 1001)),
            "ambiguous" => new ResolveConflictRequest(ConflictStatus.AcceptEmass, "Legacy rationale", "Different rationale"),
            "unknown-resolution" => new ResolveConflictRequest((ConflictStatus)99, Rationale: "Invalid decision"),
            _ => new ResolveConflictRequest(ConflictStatus.AcceptEmass, Rationale: "Reviewed"),
        };
        // Act
        var write = () => service.ResolveConflictAsync("system", "conflict", request, invalid == "empty-actor" ? "" : "reviewer");
        // Assert
        await write.Should().ThrowAsync<ArgumentException>();
        await using var verify = new AtoCopilotContext(options);
        verify.AuditLogs.Should().BeEmpty();
        (await verify.EmassConflicts.SingleAsync()).ConflictStatus.Should().Be(ConflictStatus.Unresolved);
    }

    [Fact]
    public async Task Concurrent_persistence_failure_is_reported_without_a_successful_resolution_audit()
    {
        // Arrange
        var concurrentOptions = new DbContextOptionsBuilder<AtoCopilotContext>(options)
            .AddInterceptors(new ConcurrentResolution()).Options;
        var factory = new Mock<IDbContextFactory<AtoCopilotContext>>();
        factory.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AtoCopilotContext(concurrentOptions));
        var concurrentService = new EmassRoundTripSyncService(factory.Object);
        // Act
        var write = () => concurrentService.ResolveConflictAsync("system", "conflict",
            new ResolveConflictRequest(ConflictStatus.AcceptEmass, Rationale: "Concurrent attempt"), "reviewer");
        // Assert
        await write.Should().ThrowAsync<EmassConflictChangedException>();
        await using var verify = new AtoCopilotContext(options);
        (await verify.RegisteredSystems.SingleAsync()).Name.Should().Be("Original");
        (await verify.EmassConflicts.SingleAsync()).ConflictStatus.Should().Be(ConflictStatus.Unresolved);
        verify.AuditLogs.Should().BeEmpty();
    }

    private sealed class ConcurrentResolution : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateConcurrencyException("Another reviewer committed the resolution first.");
    }
}
