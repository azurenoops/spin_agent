using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class PolicyLegacyMutationHttpTests : IClassFixture<MultiTenantWebApplicationFactory<McpProgram>>
{
    private readonly WebApplicationFactory<McpProgram> _factory;
    private static readonly Guid Tenant = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;

    public PolicyLegacyMutationHttpTests(MultiTenantWebApplicationFactory<McpProgram> factory)
    {
        Environment.GetEnvironmentVariable("ATO_TEST_SQLSERVER_CONNSTRING").Should().BeNullOrEmpty();
        factory.ResetLegacyTenantContext(Tenant);
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            foreach (var descriptor in services.Where(s => s.ServiceType == typeof(IHostedService)
                && s.ImplementationType != typeof(TenancySeedHostedService)).ToArray())
                services.Remove(descriptor);
        }));
    }

    [Fact]
    public async Task LegacyPolicyMutations_CannotEraseRetainedReference_OrBypassScopedWorkflow()
    {
        // Arrange
        using var client = _factory.CreateClient();
        var (system, other, policy) = await SeedAsync(ComponentType.Policy);
        var root = $"/api/dashboard/systems/{system}/policy-workspace";
        var source = await client.GetFromJsonAsync<JsonElement>(root + "/sources/" + policy);
        var created = await client.PostAsJsonAsync(root + "/references",
            new { policyId = policy, expectedSourceRevision = source.GetProperty("revision").GetString(), rationale = "Retain this source." });
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var reference = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = reference.GetProperty("id").GetString();
        var revision = reference.GetProperty("revision").GetInt32();

        // Act
        var unlink = await client.DeleteAsync($"/api/dashboard/components/{policy}/assignments/{id}");
        var delete = await client.DeleteAsync($"/api/dashboard/components/{policy}");
        var reclassify = await client.PutAsJsonAsync($"/api/dashboard/components/{policy}",
            new { name = "Disguised source", componentType = "Thing", status = "Active" });
        var assign = await client.PostAsJsonAsync($"/api/dashboard/components/{policy}/assignments",
            new { registeredSystemId = other });

        // Assert
        foreach (var response in new[] { unlink, delete, reclassify, assign })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
            (await response.Content.ReadAsStringAsync()).Should().Contain("policy");
        }
        var detail = await client.GetFromJsonAsync<JsonElement>(root + "/references/" + id);
        detail.GetProperty("retainedSource").GetProperty("name").GetString().Should().Be("Retained policy");
        detail.GetProperty("reference").GetProperty("revision").GetInt32().Should().Be(revision);
        detail.GetProperty("history").GetArrayLength().Should().Be(1);
        var edit = await client.PutAsJsonAsync($"/api/dashboard/components/{policy}",
            new { name = "New library name", componentType = "Policy", status = "Active", description = "Explicit source edit" });
        edit.StatusCode.Should().Be(HttpStatusCode.OK, await edit.Content.ReadAsStringAsync());
        detail = await client.GetFromJsonAsync<JsonElement>(root + "/references/" + id);
        detail.GetProperty("reference").GetProperty("sourceChanged").GetBoolean().Should().BeTrue();
        detail.GetProperty("retainedSource").GetProperty("name").GetString().Should().Be("Retained policy");
        detail.GetProperty("currentSource").GetProperty("name").GetString().Should().Be("New library name");
        var stale = await client.PostAsJsonAsync($"/api/dashboard/systems/{other}/policy-workspace/references",
            new { policyId = policy, expectedSourceRevision = source.GetProperty("revision").GetString(), rationale = "Stale source" });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.DeleteAsync(root + $"/references/{id}?expectedRevision={revision}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/dashboard/components/{policy}")).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.AuditLogs.CountAsync(a => a.Action.StartsWith("PolicyReference.") && a.Details.Contains(id!))).Should().Be(2);
    }

    [Fact]
    public async Task NonPolicyComponents_KeepExistingLibraryAssignmentAndDeletionBehavior()
    {
        // Arrange
        using var client = _factory.CreateClient();
        var (system, _, component) = await SeedAsync(ComponentType.Thing);

        // Act
        var created = await client.PostAsJsonAsync($"/api/dashboard/components/{component}/assignments",
            new { registeredSystemId = system });

        // Assert
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var assignment = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = assignment.GetProperty("id").GetString();
        (await client.DeleteAsync($"/api/dashboard/components/{component}/assignments/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PutAsJsonAsync($"/api/dashboard/components/{component}",
            new { name = "Updated thing", componentType = "Thing", status = "Active" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync($"/api/dashboard/components/{component}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConcurrentLibraryEditAndCapture_RetainsExactlyTheCheckedSourceOrConflicts()
    {
        // Arrange
        using var client = _factory.CreateClient();
        var (system, _, policy) = await SeedAsync(ComponentType.Policy);
        var root = $"/api/dashboard/systems/{system}/policy-workspace";
        var source = await client.GetFromJsonAsync<JsonElement>(root + "/sources/" + policy);

        // Act
        var captureTask = client.PostAsJsonAsync(root + "/references",
            new { policyId = policy, expectedSourceRevision = source.GetProperty("revision").GetString(), rationale = "Concurrent capture" });
        var updateTask = client.PutAsJsonAsync($"/api/dashboard/components/{policy}",
            new { name = "Concurrently edited source", description = "Edited", componentType = "Policy", status = "Active" });
        await Task.WhenAll(captureTask, updateTask);

        // Assert
        updateTask.Result.StatusCode.Should().Be(HttpStatusCode.OK, await updateTask.Result.Content.ReadAsStringAsync());
        captureTask.Result.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Conflict);
        if (captureTask.Result.IsSuccessStatusCode)
        {
            var reference = await captureTask.Result.Content.ReadFromJsonAsync<JsonElement>();
            var detail = await client.GetFromJsonAsync<JsonElement>(root + "/references/" + reference.GetProperty("id").GetString());
            detail.GetProperty("retainedSource").GetProperty("revision").GetString().Should().Be(source.GetProperty("revision").GetString());
            detail.GetProperty("retainedSource").GetProperty("name").GetString().Should().Be("Retained policy");
            detail.GetProperty("reference").GetProperty("sourceChanged").GetBoolean().Should().BeTrue();
        }
        (await client.GetFromJsonAsync<JsonElement>(root + "/sources/" + policy))
            .GetProperty("name").GetString().Should().Be("Concurrently edited source");
    }

    private async Task<(string System, string OtherSystem, string Component)> SeedAsync(ComponentType type)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var system = new RegisteredSystem { TenantId = Tenant, Name = "Legacy policy guard " + Guid.NewGuid(), IsActive = true };
        var other = new RegisteredSystem { TenantId = Tenant, Name = "Other policy guard " + Guid.NewGuid(), IsActive = true };
        var component = new SystemComponent { TenantId = Tenant, Name = "Retained policy", ComponentType = type, CreatedBy = "fixture" };
        db.AddRange(system, other, component);
        await db.SaveChangesAsync();
        return (system.Id, other.Id, component.Id);
    }
}
