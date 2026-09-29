using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

[Collection("Tenancy")]
public sealed class InterconnectionEditingHttpTests(MultiTenantWebApplicationFactory<McpProgram> factory)
{
    [Fact]
    public async Task FullDetail_UpdateAndCreate_RereadCanonicalFieldsWithoutChangingApprovalsOrHistory()
    {
        // Arrange
        var (system, connection, _) = await SeedAsync(OrganizationRole.Issm);
        using var client = factory.CreateClient();
        var root = $"/api/dashboard/systems/{system}/interconnections";
        var body = Editable();

        // Act
        var before = await client.GetAsync($"{root}/{connection}");
        var update = await client.PutAsJsonAsync($"{root}/{connection}", body);
        var after = await client.GetAsync($"{root}/{connection}");
        var created = await client.PostAsJsonAsync(root, body);
        var list = await client.GetAsync($"{root}?page=1&pageSize=1");

        // Assert
        before.StatusCode.Should().Be(HttpStatusCode.OK);
        update.StatusCode.Should().Be(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        after.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await after.Content.ReadFromJsonAsync<JsonElement>();
        var original = await before.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var field in body)
            detail.GetProperty(field.Key).ToString().Should().Be(JsonSerializer.SerializeToElement(field.Value).ToString());
        detail.GetProperty("status").GetString().Should().Be("Suspended");
        detail.GetProperty("statusReason").GetString().Should().Be("Retained reason");
        detail.GetProperty("authorizationToConnect").GetBoolean().Should().BeTrue();
        detail.GetProperty("agreements")[0].GetProperty("status").GetString().Should().Be("Signed");
        detail.GetProperty("canManageInterconnections").GetBoolean().Should().BeTrue();
        detail.GetProperty("createdAt").GetString().Should().Be(original.GetProperty("createdAt").GetString());
        detail.GetProperty("agreements").ToString().Should().Be(original.GetProperty("agreements").ToString());
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var receipt = await created.Content.ReadFromJsonAsync<JsonElement>();
        var newDetail = await client.GetFromJsonAsync<JsonElement>($"{root}/{receipt.GetProperty("interconnectionId").GetString()}");
        newDetail.GetProperty("targetSystemOwner").GetString().Should().Be("Remote owner");
        newDetail.GetProperty("status").GetString().Should().Be("Proposed");
        newDetail.GetProperty("authorizationToConnect").GetBoolean().Should().BeFalse();
        newDetail.GetProperty("agreements").GetArrayLength().Should().Be(0);
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await list.Content.ReadFromJsonAsync<JsonElement>();
        page.GetProperty("total").GetInt32().Should().Be(2);
        page.GetProperty("items").GetArrayLength().Should().Be(1);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        (await db.RegisteredSystems.SingleAsync(x => x.Id == system)).HasNoExternalInterconnections.Should().BeFalse();
        (await db.DashboardActivities.CountAsync(x => x.RegisteredSystemId == system)).Should().Be(3);
    }

    [Theory]
    [InlineData(OrganizationRole.Assessor)]
    [InlineData(OrganizationRole.SystemOwner)]
    [InlineData(OrganizationRole.Isso)]
    public async Task ReadOnlyPermission_CrossSystemAndCrossTenant_DoNotAllowWrites(OrganizationRole role)
    {
        // Arrange
        var (system, connection, person) = await SeedAsync(role);
        using var client = factory.CreateClient();
        var root = $"/api/dashboard/systems/{system}/interconnections";

        // Act
        var list = await client.GetFromJsonAsync<JsonElement>(root);
        var denied = await client.PutAsJsonAsync($"{root}/{connection}", Editable());
        var deniedCreate = await client.PostAsJsonAsync(root, Editable());
        var (otherSystem, _, _) = await SeedAsync(OrganizationRole.Issm, person);
        var wrongSystem = await client.PutAsJsonAsync(
            $"/api/dashboard/systems/{otherSystem}/interconnections/{connection}", Editable());
        var wrongDetail = await client.GetAsync($"/api/dashboard/systems/{otherSystem}/interconnections/{connection}");
        factory.GetActiveContext().TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;
        var crossTenant = await client.GetAsync($"{root}/{connection}");
        var crossWrite = await client.PutAsJsonAsync($"{root}/{connection}", Editable());

        // Assert
        list.GetProperty("canManageInterconnections").GetBoolean().Should().BeFalse();
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        deniedCreate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        wrongSystem.StatusCode.Should().Be(HttpStatusCode.NotFound);
        wrongDetail.StatusCode.Should().Be(HttpStatusCode.NotFound);
        crossTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);
        crossWrite.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("targetSystemName", "")]
    [InlineData("interconnectionType", "999")]
    [InlineData("dataFlowDirection", "999")]
    [InlineData("dataClassification", "")]
    [InlineData("status", "Active")]
    [InlineData("authorizationToConnect", true)]
    [InlineData("certifyNoInterconnections", true)]
    [InlineData("dataClassification", null)]
    [InlineData("targetSystemName", null)]
    [InlineData("protocolsUsed", null)]
    [InlineData("portsUsed", null)]
    [InlineData("securityMeasures", null)]
    public async Task InvalidOrProtectedFields_AreRejected(string field, object? value)
    {
        // Arrange
        var (system, connection, _) = await SeedAsync(OrganizationRole.Issm);
        using var client = factory.CreateClient();
        var body = Editable();
        body[field] = value;
        var root = $"/api/dashboard/systems/{system}/interconnections";

        // Act
        var update = await client.PutAsJsonAsync($"{root}/{connection}", body);
        var create = await client.PostAsJsonAsync(root, body);

        // Assert
        update.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("targetSystemName", 200)]
    [InlineData("targetSystemOwner", 200)]
    [InlineData("targetSystemAcronym", 20)]
    [InlineData("dataClassification", 50)]
    [InlineData("dataDescription", 2000)]
    [InlineData("authenticationMethod", 200)]
    public async Task FieldLengths_AcceptMaximumAndRejectOverflow(string field, int maximum)
    {
        // Arrange
        var (system, connection, _) = await SeedAsync(OrganizationRole.Issm);
        using var client = factory.CreateClient();
        var root = $"/api/dashboard/systems/{system}/interconnections/{connection}";
        var body = Editable();
        body[field] = new string('x', maximum);

        // Act
        var accepted = await client.PutAsJsonAsync(root, body);
        body[field] = new string('x', maximum + 1);
        var rejected = await client.PutAsJsonAsync(root, body);

        // Assert
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("protocolsUsed")]
    [InlineData("portsUsed")]
    [InlineData("securityMeasures")]
    public async Task DetailArrays_RejectNullItemsEmptyItemsAndOverflow(string field)
    {
        // Arrange
        var (system, connection, _) = await SeedAsync(OrganizationRole.Issm);
        using var client = factory.CreateClient();
        var root = $"/api/dashboard/systems/{system}/interconnections/{connection}";
        var body = Editable();

        // Act
        var responses = new List<HttpResponseMessage>();
        foreach (var values in new[] { new string?[] { null }, [""], [new string('x', 201)], Enumerable.Repeat<string?>("x", 201).ToArray() })
        {
            body[field] = values;
            responses.Add(await client.PutAsJsonAsync(root, body));
        }
        body[field] = Enumerable.Repeat(new string('x', 200), 200).ToArray();
        var maximum = await client.PutAsJsonAsync(root, body);

        // Assert
        responses.Should().OnlyContain(x => x.StatusCode == HttpStatusCode.BadRequest);
        maximum.StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var response in responses) response.Dispose();
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=201")]
    [InlineData("?page=2147483647&pageSize=200")]
    public async Task InvalidPagination_IsRejected(string query)
    {
        // Arrange
        var (system, _, _) = await SeedAsync(OrganizationRole.Issm);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/dashboard/systems/{system}/interconnections{query}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnonymousRequests_AreRejected()
    {
        // Arrange
        var (system, connection, _) = await SeedAsync(OrganizationRole.Issm);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        var root = $"/api/dashboard/systems/{system}/interconnections";

        // Act
        var list = await client.GetAsync(root);
        var detail = await client.GetAsync($"{root}/{connection}");
        var create = await client.PostAsJsonAsync(root, Editable());
        var update = await client.PutAsJsonAsync($"{root}/{connection}", Editable());

        // Assert
        new[] { list, detail, create, update }.Should().OnlyContain(x => x.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OptionalDetails_CanBeCleared_AndLegacyCreateRemainsCompatible()
    {
        // Arrange
        var (system, connection, _) = await SeedAsync(OrganizationRole.Issm);
        using var client = factory.CreateClient();
        var root = $"/api/dashboard/systems/{system}/interconnections";
        var body = Editable();
        foreach (var field in new[] { "targetSystemOwner", "targetSystemAcronym", "dataDescription", "authenticationMethod" })
            body[field] = "";
        foreach (var field in new[] { "protocolsUsed", "portsUsed", "securityMeasures" })
            body[field] = Array.Empty<string>();

        // Act
        var update = await client.PutAsJsonAsync($"{root}/{connection}", body);
        var after = await client.GetAsync($"{root}/{connection}");
        var legacy = await client.PostAsJsonAsync(root, new { remoteSystem = "Legacy", direction = "Outbound", protocol = "TLS", port = "443" });

        // Assert
        update.StatusCode.Should().Be(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        var detail = await after.Content.ReadFromJsonAsync<JsonElement>();
        detail.GetProperty("targetSystemOwner").GetString().Should().BeEmpty();
        detail.GetProperty("portsUsed").GetArrayLength().Should().Be(0);
        legacy.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static Dictionary<string, object?> Editable() => new()
    {
        ["targetSystemName"] = "Remote updated", ["targetSystemOwner"] = "Remote owner",
        ["targetSystemAcronym"] = "REM", ["interconnectionType"] = "Api",
        ["dataFlowDirection"] = "Outbound", ["dataClassification"] = "CUI",
        ["dataDescription"] = "Synthetic data", ["protocolsUsed"] = new[] { "TLS 1.3", "HTTPS" },
        ["portsUsed"] = new[] { "443", "8443" }, ["securityMeasures"] = new[] { "MFA" },
        ["authenticationMethod"] = "Mutual TLS"
    };

    private async Task<(string System, string Connection, Guid Person)> SeedAsync(OrganizationRole role, Guid? existingPerson = null)
    {
        var tenant = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var person = existingPerson ?? Guid.NewGuid();
        factory.ResetLegacyTenantContext(tenant);
        var active = factory.GetActiveContext();
        active.PersonId = person;
        active.IsWorkspaceRequest = true;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        if (existingPerson is null)
        {
            db.Persons.Add(new() { Id = person, TenantId = tenant, DisplayName = "Synthetic user", Email = $"{person}@example.invalid" });
            db.OrganizationMemberships.Add(new()
            {
                TenantId = tenant, PersonId = person, DirectoryTenantId = Guid.NewGuid(),
                ObjectId = Guid.NewGuid(), GrantedBy = "test"
            });
        }
        var system = new RegisteredSystem { TenantId = tenant, Name = "Synthetic system", CreatedBy = "test", HasNoExternalInterconnections = true };
        db.RegisteredSystems.Add(system);
        db.SystemRoleAssignments.Add(new() { TenantId = tenant, PersonId = person, RegisteredSystemId = system.Id, Role = role });
        var connection = new SystemInterconnection
        {
            TenantId = tenant, RegisteredSystemId = system.Id, TargetSystemName = "Original",
            DataClassification = "CUI", CreatedBy = "test", Status = InterconnectionStatus.Suspended,
            StatusReason = "Retained reason", AuthorizationToConnect = true,
            Agreements = [new() { TenantId = tenant, Title = "Retained ISA", Status = AgreementStatus.Signed, CreatedBy = "test" }]
        };
        db.SystemInterconnections.Add(connection);
        db.DashboardActivities.Add(new()
        {
            RegisteredSystemId = system.Id, EventType = "ExistingHistory", Actor = "test", Summary = "Retain this event"
        });
        await db.SaveChangesAsync();
        return (system.Id, connection.Id, person);
    }
}
