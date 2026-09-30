using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed partial class EmassExchangeServiceTests : IDisposable
{
    private readonly AtoCopilotContext db;
    private readonly TenantContext tenant = new(Guid.NewGuid()) { PersonId = Guid.NewGuid(), IsWorkspaceRequest = true };
    private readonly Mock<ISystemWorkspaceAccessService> access = new();
    private readonly EmassExchangeService service;
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private readonly DateTimeOffset generated = DateTimeOffset.UtcNow.AddDays(-2);

    public EmassExchangeServiceTests()
    {
        db = new(new DbContextOptionsBuilder<AtoCopilotContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.RegisteredSystems.Add(new() { Id = "system", TenantId = tenant.TenantId, Name = "System", CreatedBy = "seed" });
        db.AuthorizationPackages.Add(new() { Id = "package", TenantId = tenant.TenantId, RegisteredSystemId = "system",
            Status = PackageStatus.Completed, ContentHash = Hash, GeneratedAt = generated, CompletedAt = generated });
        db.SaveChanges();
        SetAccess(true, "Issm");
        service = new(db, tenant, access.Object);
    }

    private void SetAccess(bool canRead, params string[] roles) =>
        access.Setup(x => x.GetAccessAsync(tenant.TenantId, tenant.PersonId, It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid? _, string id, bool _, CancellationToken _) =>
                new SystemWorkspaceAccessResponse(id, roles, new(canRead, false, false, false, false, false, false, false, false)));

    private RecordEmassExchangeRequest Request(string outcome = "ReceiptRecorded", long version = 0, string key = "request-1") =>
        new("package", Hash, generated, outcome, "eMASS receiving workflow", "receipt-123",
            DateTimeOffset.UtcNow.AddHours(-1), "Operator verified the returned receipt.", key, version);

    [Fact]
    public async Task Export_is_not_receipt_and_record_retains_identity_and_actor()
    {
        // Arrange
        var before = await service.GetHistoryAsync("system");
        // Act
        var saved = await service.RecordAsync("system", Request(), "authenticated-actor");
        // Assert
        before.Items.Should().BeEmpty();
        saved.PackageId.Should().Be("package");
        saved.PackageHash.Should().Be(Hash);
        saved.ExportGeneratedAt.Should().Be(generated);
        saved.RecordedBy.Should().Be("authenticated-actor");
        saved.Outcome.Should().Be("ReceiptRecorded");
        (await service.GetHistoryAsync("system")).Version.Should().Be(1);
        db.AuthorizationPackages.Single().Status.Should().Be(PackageStatus.Completed);
        db.AuthorizationDecisions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("TransferRecorded")]
    [InlineData("ReceiptRecorded")]
    [InlineData("ImportAccepted")]
    [InlineData("ImportRejected")]
    [InlineData("PartialImport")]
    public async Task Every_manual_outcome_is_explicit_and_replay_is_idempotent(string outcome)
    {
        // Arrange
        var request = Request(outcome);
        // Act
        var saved = await service.RecordAsync("system", request, "actor");
        var replay = await service.RecordAsync("system", request, "actor");
        // Assert
        replay.Id.Should().Be(saved.Id);
        (await service.GetHistoryAsync("system")).Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Stale_history_and_changed_idempotency_payload_are_conflicts()
    {
        // Arrange
        var request = Request();
        await service.RecordAsync("system", request, "actor");
        // Act
        var stale = () => service.RecordAsync("system", Request(key: "new-key"), "actor");
        var changed = () => service.RecordAsync("system", request with { Outcome = "ImportRejected" }, "actor");
        // Assert
        await stale.Should().ThrowAsync<EmassExchangeConflictException>();
        await changed.Should().ThrowAsync<EmassExchangeConflictException>();
    }

    [Fact]
    public async Task Correction_appends_without_rewriting_receipt()
    {
        // Arrange
        var original = await service.RecordAsync("system", Request(), "first");
        // Act
        var corrected = await service.RecordAsync("system", Request("ImportRejected", 1, "correction") with
            { SupersedesId = original.Id, Notes = "Corrected after receiving the rejection notice." }, "second");
        // Assert
        corrected.SupersedesId.Should().Be(original.Id);
        var history = await service.GetHistoryAsync("system");
        history.Items.Should().HaveCount(2);
        history.Items.Single(x => x.Id == original.Id).Outcome.Should().Be("ReceiptRecorded");
        history.Items.Single(x => x.Id == original.Id).RecordedBy.Should().Be("first");
    }

    [Theory]
    [InlineData("MissionOwner")]
    [InlineData("SystemOwner")]
    [InlineData("AuthorizingOfficial")]
    [InlineData("Sca")]
    [InlineData("CSP.Admin")]
    public async Task Reader_or_oversight_cannot_record_exchange(string role)
    {
        // Arrange
        SetAccess(true, role);
        // Act
        var write = () => service.RecordAsync("system", Request(), "actor");
        // Assert
        await write.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData("Isso")]
    [InlineData("Issm")]
    [InlineData("Administrator")]
    public async Task Workflow_writers_can_append_observations(string role)
    {
        // Arrange
        SetAccess(true, role);
        // Act
        var result = await service.RecordAsync("system", Request(), "actor");
        // Assert
        result.Version.Should().Be(1);
    }

    [Fact]
    public async Task Corrections_require_reason_same_export_and_latest_predecessor()
    {
        // Arrange
        var original = await service.RecordAsync("system", Request(), "actor");
        var correction = Request(version: 1, key: "correction") with { SupersedesId = original.Id };
        // Act
        var missingReason = () => service.RecordAsync("system", correction with { Notes = " " }, "actor");
        var unknownPredecessor = () => service.RecordAsync("system", correction with { SupersedesId = "other-system-observation" }, "actor");
        // Assert
        await missingReason.Should().ThrowAsync<ArgumentException>();
        await unknownPredecessor.Should().ThrowAsync<ArgumentException>();
        // Arrange
        await service.RecordAsync("system", correction, "actor");
        // Act
        var duplicateCorrection = () => service.RecordAsync("system", correction with { ExpectedVersion = 2, IdempotencyKey = "other" }, "actor");
        // Assert
        await duplicateCorrection.Should().ThrowAsync<EmassExchangeConflictException>();
    }

    [Fact]
    public async Task Unassigned_system_and_foreign_tenant_are_not_found()
    {
        // Arrange
        SetAccess(false);
        // Act
        var denied = () => service.GetHistoryAsync("system");
        // Assert
        await denied.Should().ThrowAsync<KeyNotFoundException>();
        // Arrange
        SetAccess(true, "Issm");
        db.RegisteredSystems.Single().TenantId = Guid.NewGuid();
        await db.SaveChangesAsync();
        // Act
        var foreign = () => service.RecordAsync("system", Request(), "actor");
        // Assert
        await foreign.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData("system")]
    [InlineData("tenant")]
    [InlineData("hash")]
    [InlineData("version")]
    [InlineData("incomplete")]
    public async Task Export_identity_must_match_completed_package_in_same_system_and_tenant(string mismatch)
    {
        // Arrange
        var package = db.AuthorizationPackages.Single();
        if (mismatch == "system") package.RegisteredSystemId = "other";
        if (mismatch == "tenant") package.TenantId = Guid.NewGuid();
        if (mismatch == "hash") package.ContentHash = "different";
        if (mismatch == "version") package.GeneratedAt = generated.AddMinutes(1);
        if (mismatch == "incomplete") package.Status = PackageStatus.Pending;
        await db.SaveChangesAsync();
        // Act
        var record = () => service.RecordAsync("system", Request(), "actor");
        // Assert
        await record.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("workflow")]
    [InlineData("reference")]
    [InlineData("actor")]
    [InlineData("future")]
    [InlineData("outcome")]
    public async Task Required_provenance_cannot_be_omitted_or_fabricated_by_undefined_outcome(string invalid)
    {
        // Arrange
        var request = Request();
        if (invalid == "workflow") request = request with { ReceivingWorkflow = "" };
        if (invalid == "reference") request = request with { ExternalReference = "" };
        if (invalid == "future") request = request with { OccurredAt = DateTimeOffset.UtcNow.AddDays(1) };
        if (invalid == "outcome") request = request with { Outcome = "Approved" };
        // Act
        var record = () => service.RecordAsync("system", request, invalid == "actor" ? "" : "actor");
        // Assert
        await record.Should().ThrowAsync<ArgumentException>();
    }

    public void Dispose() => db.Dispose();
}
