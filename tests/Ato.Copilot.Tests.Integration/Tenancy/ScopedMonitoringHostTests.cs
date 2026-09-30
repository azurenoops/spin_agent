using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

[Collection("Tenancy")]
public sealed class ScopedMonitoringHostTests(MultiTenantWebApplicationFactory<McpProgram> factory)
{
    [Fact]
    public async Task Production_host_enforces_assignment_tenant_version_and_read_only_testing()
    {
        // Arrange
        var tenant = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var person = Guid.NewGuid();
        var system = Guid.NewGuid().ToString();
        var otherSystem = Guid.NewGuid().ToString();
        var boundary = Guid.NewGuid().ToString();
        var component = Guid.NewGuid().ToString();
        var resource = $"/subscriptions/{system}/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/assigned";
        var context = factory.GetActiveContext();
        context.TenantId = tenant;
        context.PersonId = person;
        context.IsCspAdmin = false;
        context.ImpersonatedTenantId = null;
        context.IsWorkspaceRequest = true;
        context.Status = TenantStatus.Active;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Persons.Add(new() { Id = person, TenantId = tenant, DisplayName = "Monitoring reviewer", Email = $"{person}@example.invalid" });
            db.OrganizationMemberships.Add(new() { TenantId = tenant, PersonId = person, DirectoryTenantId = Guid.NewGuid(), ObjectId = Guid.NewGuid(), GrantedBy = "test" });
            db.RegisteredSystems.AddRange(new RegisteredSystem { Id = system, TenantId = tenant, Name = "Assigned system" },
                new RegisteredSystem { Id = otherSystem, TenantId = tenant, Name = "Other system" });
            db.AuthorizationBoundaryDefinitions.Add(new() { Id = boundary, TenantId = tenant, RegisteredSystemId = system, Name = "Reviewed scope", CreatedBy = "test" });
            db.SystemComponents.Add(new() { Id = component, TenantId = tenant, RegisteredSystemId = system, Name = "Resource",
                AzureResourceId = resource, CreatedBy = "test" });
            db.BoundaryComponentAssignments.Add(new() { TenantId = tenant, SystemComponentId = component, AuthorizationBoundaryDefinitionId = boundary, CreatedBy = "test" });
            db.ControlImplementations.Add(new() { Id = Guid.NewGuid().ToString(), TenantId = tenant, RegisteredSystemId = system,
                ControlId = "SC-7", Narrative = "Retained approved baseline", TechnicalNarrative = "Retained approved baseline", CurrentVersion = 3 });
            db.ComplianceAlerts.Add(new() { Id = Guid.NewGuid(), TenantId = tenant, Title = "Network drift", Type = AlertType.Drift,
                SubscriptionId = system, ControlId = "SC-7", AffectedResources = new() { resource } });
            db.MonitoringConfigurations.Add(new() { Id = Guid.NewGuid(), TenantId = tenant, SubscriptionId = system, IsEnabled = true,
                LastRunAt = DateTimeOffset.UtcNow, NextRunAt = DateTimeOffset.UtcNow.AddHours(1) });
            db.SystemRoleAssignments.Add(new() { TenantId = tenant, PersonId = person, RegisteredSystemId = system, Role = OrganizationRole.Isso });
            await db.SaveChangesAsync();
        }
        using var client = factory.CreateClient();
        var root = $"/api/dashboard/systems/{system}/conmon";
        var input = new SaveMonitoringRuleRequest("Scoped test", boundary, "Reviewed baseline", person.ToString(),
            "Alert", new("Type", "Equals", "Drift"), 60, "High", true);

        // Act
        var read = await client.GetAsync(root + "/workspace");
        var overview = await client.GetFromJsonAsync<JsonElement>(root);
        var deniedWrite = await client.PostAsJsonAsync(root + "/rules", input);
        var unassigned = await client.GetAsync($"/api/dashboard/systems/{otherSystem}/conmon/workspace");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.SystemRoleAssignments.SingleAsync(x => x.PersonId == person && x.RegisteredSystemId == system)).Role = OrganizationRole.Issm;
            await db.SaveChangesAsync();
        }
        var blockedEnable = await client.PostAsJsonAsync(root + "/rules", input);
        input = input with { IsEnabled = false };
        var created = await client.PostAsJsonAsync(root + "/rules", input);

        // Assert
        read.StatusCode.Should().Be(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        overview.GetProperty("status").GetProperty("monitoringEnabled").GetBoolean().Should().BeFalse();
        overview.GetProperty("status").GetProperty("lastMonitoringCheck").ValueKind.Should().Be(JsonValueKind.Null);
        deniedWrite.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        unassigned.StatusCode.Should().Be(HttpStatusCode.NotFound);
        blockedEnable.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await blockedEnable.Content.ReadAsStringAsync()).Should().Contain("reviewed exact resource scope");
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var rule = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = rule.GetProperty("id").GetGuid();
        var version = rule.GetProperty("version").GetInt64();

        // Arrange — retained pre-reconciliation work stays reviewable; it does not authorize fresh collection.
        Guid impactId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.AlertRules.SingleAsync(x => x.Id == id)).IsEnabled = true;
            var historical = new MonitoringRuleEvaluation
            {
                TenantId = tenant, RegisteredSystemId = system, RuleId = id, RuleVersion = version,
                Outcome = "Matched", InputFingerprint = "retained-before-reconciliation", RuleSnapshotJson = "{}", InputSnapshotJson = "{}"
            };
            var impact = new MonitoringImpactReview
                { TenantId = tenant, RegisteredSystemId = system, EvaluationId = historical.Id, ControlId = "SC-7", OwnerId = person.ToString() };
            impactId = impact.Id;
            db.Set<MonitoringRuleEvaluation>().Add(historical);
            db.Set<MonitoringImpactReview>().Add(impact);
            var alert = await db.ComplianceAlerts.SingleAsync(x => x.SubscriptionId == system);
            db.Set<ScopedMonitoringObservation>().Add(new()
            {
                TenantId = tenant, RegisteredSystemId = system, SourceId = alert.Id.ToString(), Fingerprint = "retained",
                SnapshotJson = JsonSerializer.Serialize(new MonitoringObservedChange(alert.Id.ToString(), "Alert", alert.Title,
                    alert.ControlId, null, "Boundary", alert.CreatedAt, alert), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            });
            await db.SaveChangesAsync();
        }

        // Act
        var tested = await client.PostAsync(root + $"/rules/{id}/test", null);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            using var pushed = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().Push(new TenantContext(tenant));
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            (await db.Set<MonitoringRuleEvaluation>().CountAsync(x => x.RuleId == id && x.Outcome != "Configured")).Should().Be(1);
            context.PersonId = null;
            context.IsWorkspaceRequest = false;
            try { await scope.ServiceProvider.GetRequiredService<ScopedMonitoringService>().EvaluateDueAsync(default); }
            finally { context.PersonId = person; context.IsWorkspaceRequest = true; }
            var evaluations = await db.Set<MonitoringRuleEvaluation>().Where(x => x.RuleId == id).ToListAsync();
            var impacts = await db.Set<MonitoringImpactReview>().Where(x => x.RegisteredSystemId == system).ToListAsync();
            impacts.Should().ContainSingle(string.Join("; ", evaluations.Select(x => x.Outcome + ": " + x.InputSnapshotJson)));
            impacts[0].Id.Should().Be(impactId);
            evaluations.Should().Contain(x => x.Outcome == "CollectionUnavailable");
        }
        var disposition = await client.PostAsJsonAsync(root + $"/impacts/{impactId}/disposition",
            new DispositionMonitoringImpactRequest(1, "StageNarrativeReview", "Review changed network configuration."));
        var projected = await client.GetFromJsonAsync<JsonElement>(root + "/workspace");
        var stale = await client.PutAsJsonAsync(root + $"/rules/{id}", input with { ExpectedVersion = version - 1 });
        var disabled = await client.PutAsJsonAsync(root + $"/rules/{id}", input with { ExpectedVersion = version, IsEnabled = false });
        var foreignParent = await client.PutAsJsonAsync($"/api/dashboard/systems/{otherSystem}/conmon/rules/{id}", input with { ExpectedVersion = version });
        context.TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;
        var foreignTenant = await client.GetAsync(root + "/workspace");
        context.TenantId = tenant;
        using var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        var unauthenticated = await anonymous.GetAsync(root + "/workspace");

        // Assert
        tested.StatusCode.Should().Be(HttpStatusCode.OK, await tested.Content.ReadAsStringAsync());
        disposition.StatusCode.Should().Be(HttpStatusCode.OK, await disposition.Content.ReadAsStringAsync());
        projected.GetProperty("changes").GetArrayLength().Should().Be(1);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        disabled.StatusCode.Should().Be(HttpStatusCode.OK, await disabled.Content.ReadAsStringAsync());
        foreignParent.StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreignTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unauthenticated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await using var verification = factory.Services.CreateAsyncScope();
        var verify = verification.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await verify.Set<MonitoringRuleEvaluation>().CountAsync(x => x.RuleId == id && x.Outcome != "Configured")).Should().Be(2);
        (await verify.Set<MonitoringImpactReview>().SingleAsync(x => x.RegisteredSystemId == system)).Disposition.Should().Be("StageNarrativeReview");
        (await verify.ControlImplementations.SingleAsync(x => x.RegisteredSystemId == system)).TechnicalNarrative.Should().Be("Retained approved baseline");
        (await verify.NarrativeProposals.CountAsync(x => x.RegisteredSystemId == system && x.ChangeSourceKind == "Monitoring")).Should().Be(1);
        (await verify.AuthorizationDecisions.CountAsync(x => x.RegisteredSystemId == system)).Should().Be(0);
    }
}
