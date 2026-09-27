using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed partial class EmassExchangeServiceTests
{
    [Theory]
    [InlineData(PackagePurpose.Legacy)]
    [InlineData(PackagePurpose.InitialSubmission)]
    public async Task Export_choices_retain_actual_purpose_and_order_and_exclude_unusable_or_foreign_packages(PackagePurpose purpose)
    {
        // Arrange
        db.AuthorizationPackages.Single().Purpose = purpose;
        db.AuthorizationPackages.Add(new()
        {
            Id = "newer", TenantId = tenant.TenantId, RegisteredSystemId = "system",
            ContentHash = "newer-retained-hash", Status = PackageStatus.Completed,
            GeneratedAt = generated.AddHours(1), CompletedAt = generated.AddHours(2), Purpose = purpose,
        });
        foreach (var invalid in new[] { "other-system", "other-tenant", "pending", "no-completion", "null-hash", "blank-hash" })
        {
            db.AuthorizationPackages.Add(new()
            {
                Id = invalid, TenantId = invalid == "other-tenant" ? Guid.NewGuid() : tenant.TenantId,
                RegisteredSystemId = invalid == "other-system" ? "other-system" : "system",
                ContentHash = invalid == "null-hash" ? null : invalid == "blank-hash" ? "  " : Hash,
                Status = invalid == "pending" ? PackageStatus.Pending : PackageStatus.Completed,
                GeneratedAt = generated.AddHours(3), CompletedAt = invalid == "no-completion" ? null : generated.AddHours(4),
                Purpose = purpose,
            });
        }
        await db.SaveChangesAsync();
        SetAccess(true, "MissionOwner");

        // Act
        var choices = await service.GetExportsAsync("system");
        var history = await service.GetHistoryAsync("system");

        // Assert
        choices.Should().Equal(
            new EmassExchangeExport("newer", "newer-retained-hash", generated.AddHours(1), purpose.ToString()),
            new EmassExchangeExport("package", Hash, generated, purpose.ToString()));
        history.Items.Should().BeEmpty("listing retained exports is not evidence of transfer or receipt");
        history.CanRecord.Should().BeFalse();
        db.AuthorizationDecisions.Should().BeEmpty();
    }

    [Fact]
    public async Task Export_listing_denies_unassigned_actor_and_returns_empty_for_no_eligible_exports()
    {
        // Arrange
        SetAccess(false);
        // Act
        var denied = () => service.GetExportsAsync("system");
        // Assert
        await denied.Should().ThrowAsync<KeyNotFoundException>();
        // Arrange
        SetAccess(true, "Issm");
        db.AuthorizationPackages.Single().Status = PackageStatus.Failed;
        await db.SaveChangesAsync();
        // Act
        var empty = await service.GetExportsAsync("system");
        // Assert
        empty.Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing-tenant")]
    [InlineData("missing-person")]
    [InlineData("oversight")]
    public async Task Incomplete_identity_or_provider_oversight_cannot_create_exchange(string identity)
    {
        // Arrange
        if (identity == "missing-tenant") tenant.TenantId = Guid.Empty;
        if (identity == "missing-person") { tenant.PersonId = null; SetAccess(false); }
        if (identity == "oversight")
        {
            tenant.IsCspAdmin = true;
            access.Setup(x => x.GetAccessAsync(tenant.TenantId, tenant.PersonId, "system", true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SystemWorkspaceAccessResponse("system", ["Administrator"],
                    new(true, false, false, false, false, false, false, false, false)));
        }
        // Act
        var write = () => service.RecordAsync("system", Request(), "actor");
        // Assert
        if (identity == "oversight") await write.Should().ThrowAsync<UnauthorizedAccessException>();
        else await write.Should().ThrowAsync<KeyNotFoundException>();
        db.Set<EmassExchangeRecord>().Should().BeEmpty();
    }

    [Fact]
    public async Task History_versions_and_rows_are_filtered_to_the_requested_tenant_and_system()
    {
        // Arrange
        var first = await service.RecordAsync("system", Request(), "first-actor");
        var second = await service.RecordAsync("system", Request("PartialImport", 1, "second"), "second-actor");
        db.Set<EmassExchangeRecord>().AddRange(
            new() { TenantId = Guid.NewGuid(), RegisteredSystemId = "system", Version = 99, IdempotencyKey = "foreign-tenant" },
            new() { TenantId = tenant.TenantId, RegisteredSystemId = "other-system", Version = 100, IdempotencyKey = "foreign-system" });
        await db.SaveChangesAsync();
        // Act
        var history = await service.GetHistoryAsync("system");
        // Assert
        history.Version.Should().Be(2);
        history.Items.Should().Equal(second, first);
        history.CanRecord.Should().BeTrue();
    }

    [Theory]
    [InlineData("notes-null")]
    [InlineData("notes-long")]
    [InlineData("outcome-null")]
    [InlineData("negative-version")]
    [InlineData("missing-time")]
    [InlineData("predates-package")]
    [InlineData("missing-package-id")]
    [InlineData("unknown-package-id")]
    [InlineData("missing-hash")]
    [InlineData("missing-idempotency")]
    [InlineData("long-idempotency")]
    [InlineData("long-workflow")]
    [InlineData("long-reference")]
    [InlineData("long-actor")]
    [InlineData("empty-correction")]
    [InlineData("long-correction")]
    public async Task Invalid_observation_provenance_is_rejected_without_history_mutation(string invalid)
    {
        // Arrange
        var request = invalid switch
        {
            "notes-null" => Request() with { Notes = null! },
            "notes-long" => Request() with { Notes = new string('n', 4001) },
            "outcome-null" => Request() with { Outcome = null! },
            "negative-version" => Request() with { ExpectedVersion = -1 },
            "missing-time" => Request() with { OccurredAt = default },
            "predates-package" => Request() with { OccurredAt = generated.AddMinutes(-1) },
            "missing-package-id" => Request() with { PackageId = null! },
            "unknown-package-id" => Request() with { PackageId = "unknown" },
            "missing-hash" => Request() with { PackageHash = "" },
            "missing-idempotency" => Request() with { IdempotencyKey = "" },
            "long-idempotency" => Request() with { IdempotencyKey = new string('k', 101) },
            "long-workflow" => Request() with { ReceivingWorkflow = new string('w', 201) },
            "long-reference" => Request() with { ExternalReference = new string('r', 501) },
            "empty-correction" => Request() with { SupersedesId = "" },
            "long-correction" => Request() with { SupersedesId = new string('c', 37) },
            _ => Request(),
        };
        // Act
        var record = () => service.RecordAsync("system", request, invalid == "long-actor" ? new string('a', 201) : "actor");
        // Assert
        await record.Should().ThrowAsync<ArgumentException>();
        db.Set<EmassExchangeRecord>().Should().BeEmpty();
        db.AuthorizationDecisions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("blank-hash")]
    [InlineData("null-hash")]
    [InlineData("missing-completion")]
    public async Task Retained_package_requires_hash_and_completed_timestamp(string invalid)
    {
        // Arrange
        var package = db.AuthorizationPackages.Single();
        if (invalid == "blank-hash") package.ContentHash = " ";
        if (invalid == "null-hash") package.ContentHash = null;
        if (invalid == "missing-completion") package.CompletedAt = null;
        await db.SaveChangesAsync();
        // Act
        var record = () => service.RecordAsync("system", Request(), "actor");
        // Assert
        await record.Should().ThrowAsync<ArgumentException>();
        db.Set<EmassExchangeRecord>().Should().BeEmpty();
    }

    [Fact]
    public async Task Correction_cannot_rebind_a_receipt_to_a_different_package_purpose_even_with_same_hash_and_time()
    {
        // Arrange
        db.AuthorizationPackages.Single().Purpose = PackagePurpose.InitialSubmission;
        var request = Request();
        db.AuthorizationPackages.Add(new()
        {
            Id = "legacy-package", TenantId = tenant.TenantId, RegisteredSystemId = "system",
            Status = PackageStatus.Completed, ContentHash = Hash, GeneratedAt = generated,
            CompletedAt = generated, Purpose = PackagePurpose.Legacy,
        });
        await db.SaveChangesAsync();
        var original = await service.RecordAsync("system", request, "actor");
        var correction = Request(version: 1, key: "correction") with
            { PackageId = "legacy-package", SupersedesId = original.Id, Notes = "Attempted reassignment." };
        // Act
        var invalidCorrection = () => service.RecordAsync("system", correction, "actor");
        var replay = await service.RecordAsync("system", request, "actor");
        // Assert
        await invalidCorrection.Should().ThrowAsync<ArgumentException>();
        replay.Should().Be(original);
        (await service.GetHistoryAsync("system")).Items.Should().ContainSingle().Which.PackageId.Should().Be("package");
        db.AuthorizationPackages.Single(x => x.Id == original.PackageId).Purpose.Should().Be(PackagePurpose.InitialSubmission);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("version")]
    public async Task Correction_rejects_changed_retained_identity_but_exact_replay_preserves_original(string changed)
    {
        // Arrange
        var request = Request();
        var original = await service.RecordAsync("system", request, "actor");
        var package = db.AuthorizationPackages.Single();
        if (changed == "hash") package.ContentHash = "new-hash";
        else package.GeneratedAt = generated.AddMinutes(1);
        await db.SaveChangesAsync();
        var correction = Request(version: 1, key: "correction") with
        {
            SupersedesId = original.Id, PackageHash = package.ContentHash!,
            ExportGeneratedAt = package.GeneratedAt, Notes = "Cannot replace the receipt's original export identity.",
        };
        // Act
        var invalidCorrection = () => service.RecordAsync("system", correction, "actor");
        var replay = await service.RecordAsync("system", request, "actor");
        // Assert
        await invalidCorrection.Should().ThrowAsync<ArgumentException>();
        replay.Should().Be(original);
        replay.PackageHash.Should().Be(Hash);
        replay.ExportGeneratedAt.Should().Be(generated);
    }

    [Fact]
    public async Task Replay_requires_same_authenticated_actor_and_current_write_access()
    {
        // Arrange
        var request = Request();
        await service.RecordAsync("system", request, "original-actor");
        // Act
        var differentActor = () => service.RecordAsync("system", request, "different-actor");
        // Assert
        await differentActor.Should().ThrowAsync<EmassExchangeConflictException>();
        // Arrange
        SetAccess(true, "AuthorizingOfficial");
        // Act
        var noLongerWriter = () => service.RecordAsync("system", request, "original-actor");
        // Assert
        await noLongerWriter.Should().ThrowAsync<UnauthorizedAccessException>();
        db.Set<EmassExchangeRecord>().Should().ContainSingle();
    }
}
