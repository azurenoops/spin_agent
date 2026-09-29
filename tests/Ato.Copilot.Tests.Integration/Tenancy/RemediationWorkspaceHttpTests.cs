using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed class RemediationWorkspaceHttpTests : IClassFixture<WorkspaceMembershipFactory>
{
    private static readonly Guid Directory = Guid.NewGuid();
    private static readonly Guid Tenant = WorkspaceMembershipFactory.TenantAId;
    private readonly WebApplicationFactory<McpProgram> factory;

    public RemediationWorkspaceHttpTests(WorkspaceMembershipFactory source)
    {
        factory = source.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            foreach (var descriptor in services.Where(s => s.ServiceType == typeof(IHostedService)
                && s.ImplementationType != typeof(TenancySeedHostedService)).ToArray())
                services.Remove(descriptor);
        }));
    }

    [Fact]
    public async Task Workspace_ConnectedFlow_IsScopedAuditedAndUsesCanonicalTransitions()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        var other = await SeedAsync(OrganizationRole.Issm);
        using var client = Client(f.Actor);
        var root = $"/api/dashboard/systems/{f.System}/remediation-workspace";
        var finding = await client.PostAsJsonAsync(root + "/findings", new {
            requestId = "finding", title = "Manual issue", description = "Needs remediation", controlId = "AC-2", severity = "High" });
        finding.StatusCode.Should().Be(HttpStatusCode.OK, await finding.Content.ReadAsStringAsync());
        var findingId = (await finding.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        var taskRequest = new { requestId = "task", title = "Remediate", description = "Remediation work",
            controlId = "AC-2", severity = "High", findingId };
        var created = await client.PostAsJsonAsync(root + "/tasks", taskRequest);
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var taskId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        var retry = await client.PostAsJsonAsync(root + "/tasks", taskRequest);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString().Should().Be(taskId);

        // Act
        var linked = await client.PutAsync(root + $"/poams/{f.Poam}/tasks/{taskId}", null);
        var cross = await client.PutAsync(root + $"/poams/{other.Poam}/tasks/{taskId}", null);
        var snapshot = await client.GetAsync(root);

        // Assert
        linked.StatusCode.Should().Be(HttpStatusCode.OK, await linked.Content.ReadAsStringAsync());
        cross.StatusCode.Should().Be(HttpStatusCode.NotFound);
        snapshot.StatusCode.Should().Be(HttpStatusCode.OK, await snapshot.Content.ReadAsStringAsync());
        snapshot.Headers.CacheControl!.NoStore.Should().BeTrue();
        var data = await snapshot.Content.ReadFromJsonAsync<JsonElement>();
        data.GetProperty("counts").GetProperty("findings").GetInt32().Should().Be(1);
        data.GetProperty("poams")[0].GetProperty("taskIds")[0].GetString().Should().Be(taskId);
        var task = data.GetProperty("tasks")[0];
        var existingFindingLink = await client.PutAsJsonAsync(root + $"/findings/{findingId}/tasks/{taskId}",
            new { rowVersion = task.GetProperty("rowVersion").GetGuid() });
        existingFindingLink.StatusCode.Should().Be(HttpStatusCode.OK, await existingFindingLink.Content.ReadAsStringAsync());
        var linkResponse = await existingFindingLink.Content.ReadFromJsonAsync<JsonElement>();
        linkResponse.GetProperty("findingId").GetString().Should().Be(findingId);
        linkResponse.GetProperty("taskId").GetString().Should().Be(taskId);
        var directDone = await client.PostAsJsonAsync(root + $"/tasks/{taskId}/move",
            new { rowVersion = task.GetProperty("rowVersion").GetGuid(), status = "Done" });
        directDone.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var moved = await client.PutAsJsonAsync($"/api/dashboard/remediation/tasks/{taskId}/move",
            new { rowVersion = task.GetProperty("rowVersion").GetGuid(), status = "InProgress" });
        moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        var stale = await client.PostAsJsonAsync(root + $"/tasks/{taskId}/verify",
            new { rowVersion = task.GetProperty("rowVersion").GetGuid(), status = "Passed", notes = "Reviewed" });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        data = await client.GetFromJsonAsync<JsonElement>(root);
        task = data.GetProperty("tasks")[0];
        var update = await client.PutAsJsonAsync(root + $"/tasks/{taskId}", new {
            rowVersion = task.GetProperty("rowVersion").GetGuid(), title = "Updated remediation",
            description = "Updated work instructions", dueDate = DateTime.UtcNow.AddDays(45),
            assigneeId = f.Actor.ToString(), assigneeName = "Responsible owner" });
        update.StatusCode.Should().Be(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        data = await client.GetFromJsonAsync<JsonElement>(root);
        task = data.GetProperty("tasks")[0];
        task.GetProperty("title").GetString().Should().Be("Updated remediation");
        task.GetProperty("assigneeName").GetString().Should().Be("Responsible owner");
        var verify = await client.PostAsJsonAsync(root + $"/tasks/{taskId}/verify", new {
            rowVersion = task.GetProperty("rowVersion").GetGuid(), status = "Passed", notes = "Explicit human verification" });
        verify.StatusCode.Should().Be(HttpStatusCode.OK, await verify.Content.ReadAsStringAsync());
        foreach (var status in new[] { "InReview", "Done" })
        {
            data = await client.GetFromJsonAsync<JsonElement>(root);
            task = data.GetProperty("tasks")[0];
            var transition = await client.PostAsJsonAsync(root + $"/tasks/{taskId}/move", new {
                rowVersion = task.GetProperty("rowVersion").GetGuid(), status });
            transition.StatusCode.Should().Be(HttpStatusCode.OK, await transition.Content.ReadAsStringAsync());
        }
        data = await client.GetFromJsonAsync<JsonElement>(root);
        data.GetProperty("tasks")[0].GetProperty("status").GetString().Should().Be("Done");
        data.GetProperty("findings")[0].GetProperty("status").GetString().Should().Be("Open");
        data.GetProperty("poams")[0].GetProperty("status").GetString().Should().Be("Ongoing");
    }

    [Fact]
    public async Task ReadOnlyAssessor_CannotCreateFindingOrTask_AndOtherSystemIsHidden()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Assessor);
        var other = await SeedAsync(OrganizationRole.Issm);
        using var client = Client(f.Actor);
        var root = $"/api/dashboard/systems/{f.System}/remediation-workspace";
        // Act
        var read = await client.GetAsync(root);
        var write = await client.PostAsJsonAsync(root + "/findings", new {
            requestId = "finding", title = "Manual issue", description = "Needs remediation", controlId = "AC-2", severity = "High" });
        var hidden = await client.GetAsync($"/api/dashboard/systems/{other.System}/remediation-workspace");
        // Assert
        read.StatusCode.Should().Be(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        var data = await read.Content.ReadFromJsonAsync<JsonElement>();
        data.GetProperty("permissions").GetProperty("canManageRemediation").GetBoolean().Should().BeFalse();
        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AuthorizedOwnerIdentity_MatchesCanonicalOwnTaskTransition()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Isso);
        string taskId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var board = new RemediationBoard { TenantId = Tenant, Name = "Owner board" };
            var task = new RemediationTask { TenantId = Tenant, RegisteredSystemId = f.System, BoardId = board.Id,
                Title = "Owned task", ControlId = "AC-2", AssigneeId = f.Actor.ToString(), AssigneeName = "Remediation actor" };
            taskId = task.Id;
            db.AddRange(board, task);
            await db.SaveChangesAsync();
        }
        using var client = Client(f.Actor);
        var root = $"/api/dashboard/systems/{f.System}/remediation-workspace";
        var data = await client.GetFromJsonAsync<JsonElement>(root);
        data.GetProperty("owners").EnumerateArray().Should().Contain(o => o.GetProperty("id").GetString() == f.Actor.ToString());
        // Act
        var moved = await client.PostAsJsonAsync(root + $"/tasks/{taskId}/move", new {
            rowVersion = data.GetProperty("tasks")[0].GetProperty("rowVersion").GetGuid(), status = "ToDo" });
        // Assert
        moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PairMutations_CheckBothExpectedVersions_AndAdvanceBothAuditedRecords()
    {
        // Arrange
        var f = await SeedAsync(OrganizationRole.Issm);
        using var client = Client(f.Actor);
        var root = $"/api/dashboard/systems/{f.System}/remediation-workspace";
        var created = await client.PostAsJsonAsync(root + "/tasks", new {
            requestId = "pair-task", title = "Linked task", description = "Work", controlId = "AC-2", severity = "High" });
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var taskId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        var pair = root + $"/poams/{f.Poam}/tasks/{taskId}";
        var before = await client.GetFromJsonAsync<JsonElement>(root);
        var poamVersion = before.GetProperty("poams")[0].GetProperty("rowVersion").GetGuid();
        var taskVersion = before.GetProperty("tasks")[0].GetProperty("rowVersion").GetGuid();
        // Act
        var stalePoam = await client.PutAsJsonAsync(pair, new { expectedPoamRevision = Guid.NewGuid(), expectedTaskRevision = taskVersion });
        var staleTask = await client.PutAsJsonAsync(pair, new { expectedPoamRevision = poamVersion, expectedTaskRevision = Guid.NewGuid() });
        // Assert
        stalePoam.StatusCode.Should().Be(HttpStatusCode.Conflict);
        staleTask.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var linked = await client.PutAsJsonAsync(pair, new { expectedPoamRevision = poamVersion, expectedTaskRevision = taskVersion });
        linked.StatusCode.Should().Be(HttpStatusCode.OK, await linked.Content.ReadAsStringAsync());
        var after = await client.GetFromJsonAsync<JsonElement>(root);
        after.GetProperty("poams")[0].GetProperty("rowVersion").GetGuid().Should().NotBe(poamVersion);
        after.GetProperty("tasks")[0].GetProperty("rowVersion").GetGuid().Should().NotBe(taskVersion);
        after.GetProperty("poams")[0].GetProperty("history").GetArrayLength().Should().Be(1);
        after.GetProperty("tasks")[0].GetProperty("history").EnumerateArray()
            .Should().Contain(h => h.GetProperty("eventType").GetString() == "RelationshipLinked");
        var staleDelete = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, pair) {
            Content = JsonContent.Create(new { expectedPoamRevision = poamVersion, expectedTaskRevision = taskVersion }) });
        staleDelete.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var unlinked = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, pair) {
            Content = JsonContent.Create(new { expectedPoamRevision = after.GetProperty("poams")[0].GetProperty("rowVersion").GetGuid(),
                expectedTaskRevision = after.GetProperty("tasks")[0].GetProperty("rowVersion").GetGuid() }) });
        unlinked.StatusCode.Should().Be(HttpStatusCode.OK, await unlinked.Content.ReadAsStringAsync());
        var final = await client.GetFromJsonAsync<JsonElement>(root);
        final.GetProperty("poams")[0].GetProperty("rowVersion").GetGuid()
            .Should().NotBe(after.GetProperty("poams")[0].GetProperty("rowVersion").GetGuid());
        final.GetProperty("tasks")[0].GetProperty("rowVersion").GetGuid()
            .Should().NotBe(after.GetProperty("tasks")[0].GetProperty("rowVersion").GetGuid());
        final.GetProperty("poams")[0].GetProperty("taskIds").GetArrayLength().Should().Be(0);
        final.GetProperty("poams")[0].GetProperty("history").GetArrayLength().Should().Be(2);
        final.GetProperty("tasks")[0].GetProperty("history").EnumerateArray()
            .Should().Contain(h => h.GetProperty("eventType").GetString() == "RelationshipUnlinked");
    }

    private HttpClient Client(Guid actor)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Tid", Directory.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", actor.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", Tenant.ToString());
        return client;
    }

    private async Task<(Guid Actor, string System, string Poam)> SeedAsync(OrganizationRole role)
    {
        using var initialized = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        if (!await db.NistControls.AnyAsync(c => c.Id == "AC-2"))
            db.NistControls.Add(new NistControl { Id = "AC-2", Family = "AC", Title = "Account Management" });
        var actor = Guid.NewGuid();
        var person = new Person { TenantId = Tenant, DisplayName = "Remediation actor", Email = $"{actor:N}@example.invalid" };
        var system = new RegisteredSystem { TenantId = Tenant, Name = "Remediation system", IsActive = true };
        var poam = new PoamItem { TenantId = Tenant, RegisteredSystemId = system.Id, Weakness = "Weakness",
            WeaknessSource = "Manual", SecurityControlNumber = "AC-2", PointOfContact = "Owner", ScheduledCompletionDate = DateTime.UtcNow.AddDays(10) };
        db.AddRange(person, system, poam);
        db.OrganizationMemberships.Add(new() { TenantId = Tenant, DirectoryTenantId = Directory, ObjectId = actor,
            PersonId = person.Id, GrantedBy = "fixture" });
        db.SystemRoleAssignments.Add(new() { TenantId = Tenant, PersonId = person.Id, RegisteredSystemId = system.Id, Role = role });
        await db.SaveChangesAsync();
        return (actor, system.Id, poam.Id);
    }
}
