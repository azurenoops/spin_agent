using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp;
using Ato.Copilot.Tests.Integration.Auth;
using Ato.Copilot.Tests.Integration.Tenancy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Compliance;

[Collection("Auth")]
public sealed class ControlValidationPolicyHostTests
{
    private readonly LoginAuthTestFactory _factory;

    public ControlValidationPolicyHostTests(LoginAuthTestFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/api/systems/{0}/controls/{1}/validation")]
    [InlineData("/api/dashboard/systems/{0}/controls/{1}/validation")]
    public async Task ValidationGet_Viewer_ReturnsEmptyResult(string routeTemplate)
    {
        // Arrange
        var fixture = await SeedControlImplementationsAsync();
        using var client = CreateClient(ComplianceRoles.Viewer);
        var route = string.Format(routeTemplate, fixture.TenantASystemId, fixture.ControlId);

        // Act
        using var response = await client.GetAsync(route);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("systemId").GetString().Should().Be(fixture.TenantASystemId);
        body.GetProperty("controlId").GetString().Should().Be(fixture.ControlId);
        body.GetProperty("total").GetInt32().Should().Be(0);
        body.GetProperty("links").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ValidationGet_NonComplianceRole_ReturnsForbidden()
    {
        // Arrange
        var fixture = await SeedControlImplementationsAsync();
        using var client = CreateClient("Unrelated.Role");

        // Act
        using var response = await client.GetAsync(
            $"/api/dashboard/systems/{fixture.TenantASystemId}/controls/{fixture.ControlId}/validation");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ValidationGet_ForeignTenantControl_ReturnsNotFound()
    {
        // Arrange
        var fixture = await SeedControlImplementationsAsync();
        using var client = CreateClient(ComplianceRoles.Viewer);

        // Act
        using var response = await client.GetAsync(
            $"/api/dashboard/systems/{fixture.TenantBSystemId}/controls/{fixture.ControlId}/validation");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private HttpClient CreateClient(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Oid", $"validation-user-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add("X-Test-Roles", role);
        return client;
    }

    private async Task<ValidationFixture> SeedControlImplementationsAsync()
    {
        _factory.GetActiveContext().TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        await _factory.EnsureActiveCspProfileAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var controlId = $"ZZ-{suffix[..8]}";
        var tenantASystem = CreateSystem(
            $"validation-a-{suffix}",
            MultiTenantWebApplicationFactory<McpProgram>.TenantAId);
        var tenantBSystem = CreateSystem(
            $"validation-b-{suffix}",
            MultiTenantWebApplicationFactory<McpProgram>.TenantBId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = ServiceProviderServiceExtensions
            .GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
        db.RegisteredSystems.AddRange(tenantASystem, tenantBSystem);
        db.ControlImplementations.AddRange(
            CreateImplementation(tenantASystem, controlId),
            CreateImplementation(tenantBSystem, controlId));
        await db.SaveChangesAsync();

        return new ValidationFixture(tenantASystem.Id, tenantBSystem.Id, controlId);
    }

    private static RegisteredSystem CreateSystem(string id, Guid tenantId) => new()
    {
        TenantId = tenantId,
        Id = id,
        Name = id,
        SystemType = SystemType.MajorApplication,
        MissionCriticality = MissionCriticality.MissionSupport,
        HostingEnvironment = "Azure Government",
        CreatedBy = "integration-test",
    };

    private static ControlImplementation CreateImplementation(
        RegisteredSystem system,
        string controlId) => new()
    {
        TenantId = system.TenantId,
        Id = $"implementation-{system.Id}",
        RegisteredSystemId = system.Id,
        ControlId = controlId,
        ImplementationStatus = ImplementationStatus.Implemented,
        AuthoredBy = "integration-test",
    };

    private sealed record ValidationFixture(
        string TenantASystemId,
        string TenantBSystemId,
        string ControlId);
}
