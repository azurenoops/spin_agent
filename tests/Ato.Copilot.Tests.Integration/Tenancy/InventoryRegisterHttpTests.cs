using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

[Collection("Tenancy")]
public sealed class InventoryRegisterHttpTests(MultiTenantWebApplicationFactory<McpProgram> factory)
{
    private static object Software(string name = "Synthetic managed service", string version = "2026.09") => new {
        itemName = name, type = 1, softwareFunction = 2, vendor = "Synthetic provider", version,
        location = "Provider-managed service; logical mission boundary"
    };
    [Fact]
    public async Task CanonicalRegister_CreateUpdateExportAndDecommission_PreservesCloudSoftwareWithoutPhysicalFields()
    {
        // Arrange
        var system = await SeedAsync(OrganizationRole.Issm);
        using var client = factory.CreateClient();
        var root = $"/api/dashboard/systems/{system}/inventory-items";
        // Act
        var created = await client.PostAsJsonAsync(root, Software());
        // Assert
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var item = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = item.GetProperty("id").GetString();
        item.GetProperty("type").GetString().Should().Be("Software");
        item.GetProperty("parentHardwareId").ValueKind.Should().Be(JsonValueKind.Null);
        // Act
        var updated = await client.PutAsJsonAsync($"{root}/{id}", Software(version: "2026.10"));
        var page = await client.GetFromJsonAsync<JsonElement>(root);
        var workbookBytes = await client.GetByteArrayAsync(root + "/export");
        // Assert
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        page.GetProperty("items")[0].GetProperty("version").GetString().Should().Be("2026.10");
        page.GetProperty("totalCount").GetInt32().Should().Be(1);
        using var workbook = new XLWorkbook(new MemoryStream(workbookBytes));
        workbook.Worksheets.SelectMany(sheet => sheet.CellsUsed()).Should().Contain(cell => cell.GetString() == "2026.10");
        // Act
        var removed = await client.PostAsJsonAsync($"{root}/{id}/decommission", new { rationale = "Synthetic acceptance retirement" });
        var remaining = await client.GetFromJsonAsync<JsonElement>(root);
        // Assert
        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        remaining.GetProperty("totalCount").GetInt32().Should().Be(0);
    }
    [Fact]
    public async Task ReaderCannotMutate_AndForeignSystemCannotAccessTheRegister()
    {
        // Arrange
        var system = await SeedAsync(OrganizationRole.SystemOwner);
        using var client = factory.CreateClient();
        var root = $"/api/dashboard/systems/{system}/inventory-items";
        // Act
        var page = await client.GetFromJsonAsync<JsonElement>(root);
        var denied = await client.PostAsJsonAsync(root, Software());
        factory.GetActiveContext().TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantBId;
        var foreign = await client.GetAsync(root);
        // Assert
        page.GetProperty("canManage").GetBoolean().Should().BeFalse();
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=201")]
    public async Task InvalidPaging_IsRejected(string query)
    {
        // Arrange
        var system = await SeedAsync(OrganizationRole.Issm);
        using var client = factory.CreateClient();
        // Act
        var response = await client.GetAsync($"/api/dashboard/systems/{system}/inventory-items{query}");
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
    [Fact]
    public async Task InvalidEnumsMissingFieldsAndCrossSystemItems_AreRejected()
    {
        // Arrange
        var system = await SeedAsync(OrganizationRole.Issm);
        using var client = factory.CreateClient();
        var root = $"/api/dashboard/systems/{system}/inventory-items";
        var created = await client.PostAsJsonAsync(root, Software());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        // Act
        var invalidType = await client.PostAsJsonAsync(root, new { itemName = "Invalid", type = 99 });
        var missingHardware = await client.PostAsJsonAsync(root, new { itemName = "Server", type = 0, hardwareFunction = 0, manufacturer = "Vendor" });
        var emptyVersion = await client.PutAsJsonAsync($"{root}/{id}", new { version = "" });
        var parent = await client.PutAsJsonAsync($"{root}/{id}", new { parentHardwareId = "unavailable-parent" });
        var wrongItem = await client.PutAsJsonAsync($"{root}/missing-item", Software());
        // Assert
        invalidType.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        missingHardware.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        emptyVersion.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        parent.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        wrongItem.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var reread = await client.GetFromJsonAsync<JsonElement>($"{root}/{id}");
        reread.GetProperty("version").GetString().Should().Be("2026.09");
    }
    private async Task<string> SeedAsync(OrganizationRole role)
    {
        var tenant = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        var person = Guid.NewGuid();
        factory.ResetLegacyTenantContext(tenant);
        factory.GetActiveContext().PersonId = person;
        factory.GetActiveContext().IsWorkspaceRequest = true;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        db.Persons.Add(new() { Id = person, TenantId = tenant, DisplayName = "Inventory acceptance", Email = $"{person}@example.invalid" });
        db.OrganizationMemberships.Add(new() { TenantId = tenant, PersonId = person, DirectoryTenantId = Guid.NewGuid(), ObjectId = Guid.NewGuid(), GrantedBy = "test" });
        var system = new RegisteredSystem { TenantId = tenant, Name = "Synthetic inventory", CreatedBy = "test" };
        db.RegisteredSystems.Add(system);
        db.SystemRoleAssignments.Add(new() { TenantId = tenant, PersonId = person, RegisteredSystemId = system.Id, Role = role });
        await db.SaveChangesAsync();
        return system.Id;
    }
}
