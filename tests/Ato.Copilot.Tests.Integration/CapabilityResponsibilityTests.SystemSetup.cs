using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Compliance;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Integration;

public sealed partial class CapabilityResponsibilityTests
{
    [Fact]
    public async Task ReviewEvidence_SchemaUpgradeRetainsLegacyConfirmationsWithoutInventingReviewChecks()
    {
        // Arrange
        await AddBaselineAsync();
        (await _client.PostAsJsonAsync(Route, new { capabilityId = _capability })).EnsureSuccessStatusCode();
        (await ConfirmAsync(_capability, "AU-6")).EnsureSuccessStatusCode();
        await using var db = new AtoCopilotContext(_options);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE CapabilityResponsibilityConfirmations DROP COLUMN ProviderCoverageVerified;
            ALTER TABLE CapabilityResponsibilityConfirmations DROP COLUMN CustomerDutiesReviewed;
            ALTER TABLE CapabilityResponsibilityConfirmations DROP COLUMN ReviewNotes;
            """);

        // Act
        await CapabilityResponsibilitySchemaAdditions.ApplyAsync(db, NullLogger.Instance);
        await CapabilityResponsibilitySchemaAdditions.ApplyAsync(db, NullLogger.Instance);

        // Assert
        var record = await db.Set<CapabilityResponsibilityConfirmation>().SingleAsync();
        record.ControlId.Should().Be("AU-6");
        record.ProviderCoverageVerified.Should().BeNull();
        record.CustomerDutiesReviewed.Should().BeNull();
        record.ReviewNotes.Should().BeNull();
    }

    [Theory]
    [InlineData(false, true, "Reviewed")]
    [InlineData(true, false, "Reviewed")]
    [InlineData(true, true, null)]
    [InlineData(true, true, "   ")]
    [InlineData(true, true, "oversized")]
    public async Task ReviewEvidence_RejectsIncompleteChecksOrNotes(bool coverage, bool duties, string? notes)
    {
        // Arrange
        await AddBaselineAsync();
        (await _client.PostAsJsonAsync(Route, new { capabilityId = _capability })).EnsureSuccessStatusCode();
        var preview = await _client.GetFromJsonAsync<JsonElement>($"{Route}/responsibilities");
        var body = JsonSerializer.SerializeToNode(ConfirmationBody(preview, _capability, "AU-6", "Shared"))!.AsObject();
        body["providerCoverageVerified"] = coverage;
        body["customerDutiesReviewed"] = duties;
        body["reviewNotes"] = notes == "oversized" ? new string('x', 2001) : notes;

        // Act
        var response = await _client.PutAsJsonAsync($"{Route}/{_capability}/responsibilities", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var db = new AtoCopilotContext(_options);
        (await db.Set<CapabilityResponsibilityConfirmation>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ReviewEvidence_PersistsPerControlAndRetainsPriorReviewHistory()
    {
        // Arrange
        await AddBaselineAsync();
        (await _client.PostAsJsonAsync(Route, new { capabilityId = _capability })).EnsureSuccessStatusCode();
        var preview = await _client.GetFromJsonAsync<JsonElement>($"{Route}/responsibilities");
        var body = JsonSerializer.SerializeToNode(ConfirmationBody(preview, _capability, "AU-6", "Shared"))!.AsObject();
        body["providerCoverageVerified"] = true;
        body["customerDutiesReviewed"] = true;
        body["reviewNotes"] = "  Verified provider coverage and customer alert review.  ";

        // Act
        (await _client.PutAsJsonAsync($"{Route}/{_capability}/responsibilities", body)).EnsureSuccessStatusCode();
        var persisted = await _client.GetFromJsonAsync<JsonElement>($"{Route}/responsibilities");
        var reviewed = persisted.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("controlId").GetString() == "AU-6");
        (await ConfirmAsync(_capability, "AU-6")).EnsureSuccessStatusCode();

        // Assert
        reviewed.GetProperty("providerCoverageVerified").GetBoolean().Should().BeTrue();
        reviewed.GetProperty("customerDutiesReviewed").GetBoolean().Should().BeTrue();
        reviewed.GetProperty("reviewNotes").GetString().Should().Be("Verified provider coverage and customer alert review.");
        await using var db = new AtoCopilotContext(_options);
        var historical = await db.Set<CapabilityResponsibilityConfirmation>().SingleAsync(x => !x.IsCurrent);
        historical.ReviewNotes.Should().Be("Verified provider coverage and customer alert review.");
        historical.ProviderCoverageVerified.Should().BeTrue();
        historical.CustomerDutiesReviewed.Should().BeTrue();
        historical.SourceRevision.Should().Be(body["sourceRevision"]!.GetValue<string>());
        historical.ReviewedBaselineId.Should().Be(body["baselineId"]!.GetValue<string>());
    }

    [Fact]
    public async Task SetupReconciliation_UsesCurrentManagementPermission_AndCallerTransaction()
    {
        // Arrange
        await AddBaselineAsync();
        await using var scope = _app.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ICapabilityResponsibilityService>();
        await using var db = new AtoCopilotContext(_options);
        var denied = () => service.ReconcileSetupAsync(db, _system, "setup-actor");
        await denied.Should().ThrowAsync<UnauthorizedAccessException>();
        var role = await db.SystemRoleAssignments.SingleAsync();
        role.Role = OrganizationRole.Issm;
        await db.SaveChangesAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.CapabilitySubscriptions.Add(new()
        {
            RegisteredSystemId = _system, CspInheritedCapabilityId = _capability.ToString(),
            RoutingCapabilityId = _capability.ToString(), RoutingTenantId = _tenant
        });
        await db.SaveChangesAsync();

        // Act
        var result = await service.ReconcileSetupAsync(db, _system, "setup-actor");
        await transaction.RollbackAsync();

        // Assert
        result.Items.Should().Contain(x => x.State == "MissingAllocation");
        result.CanConfirm.Should().BeTrue();
        await using var after = new AtoCopilotContext(_options);
        (await after.CapabilitySubscriptions.CountAsync()).Should().Be(0);
        (await after.ControlInheritances.CountAsync()).Should().Be(0);
    }
}
