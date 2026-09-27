using System.Globalization;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed partial class EmassConflictRationaleTests
{
    [Theory]
    [InlineData("SystemInfo.SystemName", "Original", "Returned")]
    [InlineData("SystemInfo.Acronym", "OS", "RS")]
    [InlineData("SystemInfo.DitprId", "DITPR", "RETURNED-ID")]
    [InlineData("ControlImplementation.ImplementationStatus", "Implemented", "Planned")]
    [InlineData("ControlImplementation.Narrative", "Original narrative", "Returned narrative")]
    [InlineData("PoamItem.Weakness", "Weakness", "Returned weakness")]
    [InlineData("PoamItem.WeaknessSource", "Manual", "Returned source")]
    [InlineData("PoamItem.PointOfContact", "Owner", "Reviewed owner")]
    [InlineData("PoamItem.PocEmail", "old@example.invalid", "new@example.invalid")]
    [InlineData("PoamItem.SecurityControlNumber", "AC-1", "AC-2")]
    [InlineData("PoamItem.ScheduledCompletionDate", "01/01/2026", "02/01/2026")]
    [InlineData("PoamItem.ResourcesRequired", "Old resources", "Reviewed resources")]
    [InlineData("PoamItem.CostEstimate", "12.34", "56.78")]
    [InlineData("PoamItem.Status", "Ongoing", "Completed")]
    [InlineData("PoamItem.ActualCompletionDate", "01/01/2026", "02/01/2026")]
    [InlineData("PoamItem.Comments", "Original comments", "Reviewed comments")]
    public async Task Every_supported_source_field_is_compared_before_acceptance_and_keeps_original_diff(
        string field, string original, string returned)
    {
        // Arrange
        using var scope = accessor.Push(new TenantContext(tenant));
        await SeedSourceAsync(field, original, returned);
        // Act
        var result = await service.ResolveConflictAsync("system", "conflict",
            new ResolveConflictRequest(ConflictStatus.AcceptEmass, Rationale: "Verified this specific source field."), "reviewer");
        // Assert
        result.SpinValue.Should().Be(original);
        result.EmassValue.Should().Be(returned);
        await using var verify = new AtoCopilotContext(options);
        var target = field.StartsWith("SystemInfo.", StringComparison.Ordinal)
            ? (object)await verify.RegisteredSystems.SingleAsync()
            : field.StartsWith("ControlImplementation.", StringComparison.Ordinal)
                ? await verify.ControlImplementations.SingleAsync()
                : await verify.PoamItems.SingleAsync();
        var property = field[(field.IndexOf('.') + 1)..];
        if (property == "SystemName") property = "Name";
        var value = target.GetType().GetProperty(property)!.GetValue(target);
        var formatted = value switch
        {
            DateTime date => date.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
            decimal amount => amount.ToString("F2", CultureInfo.InvariantCulture),
            _ => value?.ToString(),
        };
        formatted.Should().Be(returned);
        verify.AuditLogs.Should().ContainSingle();
        verify.Set<EmassExchangeRecord>().Should().BeEmpty();
    }

    [Theory]
    [InlineData("ControlImplementation.Narrative", "Original narrative")]
    [InlineData("PoamItem.Weakness", "Weakness")]
    public async Task Local_edit_to_control_or_poam_is_not_silently_overwritten(string field, string original)
    {
        // Arrange
        using var scope = accessor.Push(new TenantContext(tenant));
        await SeedSourceAsync(field, original, "Returned");
        await using (var local = new AtoCopilotContext(options))
        {
            if (field.StartsWith("ControlImplementation.", StringComparison.Ordinal))
                (await local.ControlImplementations.SingleAsync()).SetCombinedNarrative("Changed after comparison");
            else (await local.PoamItems.SingleAsync()).Weakness = "Changed after comparison";
            await local.SaveChangesAsync();
        }
        // Act
        var overwrite = () => service.ResolveConflictAsync("system", "conflict",
            new ResolveConflictRequest(ConflictStatus.AcceptEmass, Rationale: "Stale review"), "reviewer");
        // Assert
        await overwrite.Should().ThrowAsync<EmassConflictChangedException>();
        await using var verify = new AtoCopilotContext(options);
        verify.AuditLogs.Should().BeEmpty();
        (await verify.EmassConflicts.SingleAsync()).ConflictStatus.Should().Be(ConflictStatus.Unresolved);
    }

    [Theory]
    [InlineData("SystemInfo.UnknownField")]
    [InlineData("ControlImplementation.UnknownField")]
    [InlineData("PoamItem.UnknownField")]
    public async Task Unsupported_field_cannot_be_accepted_with_a_rationale(string field)
    {
        // Arrange
        using var scope = accessor.Push(new TenantContext(tenant));
        await SeedSourceAsync(field, "Original", "Returned");
        // Act
        var overwrite = () => service.ResolveConflictAsync("system", "conflict",
            new ResolveConflictRequest(ConflictStatus.AcceptEmass, Rationale: "Unsupported field"), "reviewer");
        // Assert
        await overwrite.Should().ThrowAsync<InvalidOperationException>().WithMessage("Unsupported conflict field*");
        await using var verify = new AtoCopilotContext(options);
        verify.AuditLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task Matching_legacy_notes_and_rationale_are_normalized_without_ambiguity()
    {
        // Arrange
        using var scope = accessor.Push(new TenantContext(tenant));
        // Act
        var result = await service.ResolveConflictAsync("system", "conflict",
            new ResolveConflictRequest(ConflictStatus.KeepSpin, " Reviewed local source. ", "Reviewed local source."), "reviewer");
        // Assert
        result.Rationale.Should().Be("Reviewed local source.");
    }

    [Theory]
    [InlineData("ControlImplementation.Narrative", "Original narrative", false)]
    [InlineData("ControlImplementation.Narrative", "Original narrative", true)]
    [InlineData("PoamItem.Weakness", "Weakness", false)]
    [InlineData("PoamItem.Weakness", "Weakness", true)]
    public async Task Target_entity_from_another_system_or_tenant_cannot_be_changed(
        string field, string original, bool otherTenant)
    {
        // Arrange
        using var scope = accessor.Push(new TenantContext(tenant));
        await SeedSourceAsync(field, original, "Returned");
        await using (var change = new AtoCopilotContext(options))
        {
            if (field.StartsWith("ControlImplementation.", StringComparison.Ordinal))
            {
                var target = await change.ControlImplementations.SingleAsync();
                if (otherTenant) target.TenantId = Guid.NewGuid();
                else target.RegisteredSystemId = "another-system";
            }
            else
            {
                var target = await change.PoamItems.SingleAsync();
                if (otherTenant) target.TenantId = Guid.NewGuid();
                else target.RegisteredSystemId = "another-system";
            }
            await change.SaveChangesAsync();
        }
        // Act
        var overwrite = () => service.ResolveConflictAsync("system", "conflict",
            new ResolveConflictRequest(ConflictStatus.AcceptEmass, Rationale: "Wrong target"), "reviewer");
        // Assert
        await overwrite.Should().ThrowAsync<InvalidOperationException>();
        await using var verify = new AtoCopilotContext(options);
        verify.AuditLogs.Should().BeEmpty();
        (await verify.EmassConflicts.SingleAsync()).Notes.Should().BeNull();
        (field.StartsWith("ControlImplementation.", StringComparison.Ordinal)
            ? (await verify.ControlImplementations.SingleAsync()).Narrative
            : (await verify.PoamItems.SingleAsync()).Weakness).Should().Be(original);
    }

    [Fact]
    public async Task Terminal_decision_rationale_and_actor_cannot_be_rewritten()
    {
        // Arrange
        using var scope = accessor.Push(new TenantContext(tenant));
        await service.ResolveConflictAsync("system", "conflict",
            new ResolveConflictRequest(ConflictStatus.KeepSpin, Rationale: "Completed reviewed decision."), "first-reviewer");
        // Act
        var rewrite = () => service.ResolveConflictAsync("system", "conflict",
            new ResolveConflictRequest(ConflictStatus.Deferred, Rationale: "Replace previous reason."), "second-reviewer");
        // Assert
        await rewrite.Should().ThrowAsync<EmassConflictAlreadyResolvedException>();
        await using var verify = new AtoCopilotContext(options);
        var conflict = await verify.EmassConflicts.SingleAsync();
        conflict.Notes.Should().Be("Completed reviewed decision.");
        conflict.ResolvedBy.Should().Be("first-reviewer");
        verify.AuditLogs.Should().ContainSingle().Which.UserId.Should().Be("first-reviewer");
    }

    private async Task SeedSourceAsync(string field, string original, string returned)
    {
        await using var db = new AtoCopilotContext(options);
        var system = await db.RegisteredSystems.SingleAsync();
        system.Acronym = "OS";
        system.DitprId = "DITPR";
        db.ControlImplementations.Add(new()
        {
            Id = "control", TenantId = tenant, RegisteredSystemId = "system", ControlId = "AC-1",
            ImplementationStatus = ImplementationStatus.Implemented, Narrative = "Original narrative", AuthoredBy = "fixture",
        });
        db.PoamItems.Add(new()
        {
            Id = "poam", TenantId = tenant, RegisteredSystemId = "system", Weakness = "Weakness", WeaknessSource = "Manual",
            PointOfContact = "Owner", PocEmail = "old@example.invalid", SecurityControlNumber = "AC-1",
            ScheduledCompletionDate = new DateTime(2026, 1, 1), ResourcesRequired = "Old resources", CostEstimate = 12.34m,
            Status = PoamStatus.Ongoing, ActualCompletionDate = new DateTime(2026, 1, 1), Comments = "Original comments",
        });
        var conflict = await db.EmassConflicts.SingleAsync();
        conflict.EntityType = field[..field.IndexOf('.')];
        conflict.EntityId = conflict.EntityType == "SystemInfo" ? null : conflict.EntityType == "PoamItem" ? "poam" : "control";
        conflict.FieldName = field;
        conflict.SpinValue = original;
        conflict.EmassValue = returned;
        await db.SaveChangesAsync();
    }
}
