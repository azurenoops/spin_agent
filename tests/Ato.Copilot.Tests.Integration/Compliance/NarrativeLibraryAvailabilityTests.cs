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
public sealed class NarrativeLibraryAvailabilityTests(LoginAuthTestFactory factory)
{
    [Theory]
    [InlineData("", true)]
    [InlineData("/access", true)]
    [InlineData("/proposals", true)]
    [InlineData("", false)]
    [InlineData("/access", false)]
    [InlineData("/proposals", false)]
    public async Task PageReadRoutes_UseRegisteredHostAndAssignedSystemAccess(string suffix, bool assigned)
    {
        // Arrange
        factory.GetActiveContext().TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId;
        await factory.EnsureActiveCspProfileAsync();
        var systemId = Guid.NewGuid().ToString();
        var actorId = $"narrative-reader-{Guid.NewGuid():N}";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = ServiceProviderServiceExtensions.GetRequiredService<AtoCopilotContext>(scope.ServiceProvider);
            db.RegisteredSystems.Add(new RegisteredSystem
            {
                Id = systemId,
                TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
                Name = "Synthetic narrative system",
                CreatedBy = "integration-test",
            });
            if (assigned)
            {
                db.RmfRoleAssignments.Add(new RmfRoleAssignment
                {
                    TenantId = MultiTenantWebApplicationFactory<McpProgram>.TenantAId,
                    RegisteredSystemId = systemId,
                    UserId = actorId,
                    UserDisplayName = "Synthetic system owner",
                    RmfRole = RmfRole.SystemOwner,
                    AssignedBy = "integration-test",
                    IsActive = true,
                });
            }
            await db.SaveChangesAsync();
        }
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Oid", actorId);
        client.DefaultRequestHeaders.Add("X-Test-Roles", ComplianceRoles.Analyst);

        // Act
        using var response = await client.GetAsync($"/api/systems/{systemId}/narrative-library{suffix}");

        // Assert
        response.StatusCode.Should().Be(assigned ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (!assigned)
        {
            payload.GetProperty("errorCode").GetString().Should().Be("FORBIDDEN");
        }
        else if (suffix == "/access")
        {
            payload.GetProperty("systemName").GetString().Should().Be("Synthetic narrative system");
            payload.GetProperty("canAuthor").GetBoolean().Should().BeTrue();
        }
        else
        {
            payload.ValueKind.Should().Be(JsonValueKind.Array);
            payload.GetArrayLength().Should().Be(0);
        }
    }
}
