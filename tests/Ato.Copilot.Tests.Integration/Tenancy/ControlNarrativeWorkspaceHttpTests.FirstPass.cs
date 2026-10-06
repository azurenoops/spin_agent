using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Onboarding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tenancy;

public sealed partial class ControlNarrativeWorkspaceHttpTests
{
    [Fact]
    public async Task FirstPass_HttpIsScopedNonPersistentAndRetainsProtectedProvenanceOnExplicitSave()
    {
        // Arrange
        var fixture = await SeedAsync(OrganizationRole.Issm);
        var generator = new Mock<IControlNarrativeService>();
        generator.Setup(g => g.GenerateRequirementFirstPassAsync(It.IsAny<RequirementFirstPassContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RequirementFirstPassContext context, CancellationToken _) => new(
                [new(context.Requirements[0].Id, "Synthetic source-backed AI requirement draft", [context.Sources[0].Id], "Authorized source context")],
                [], ["Record the actual authority before review."], []));
        using var firstPassHost = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IControlNarrativeService)).ToArray()) services.Remove(descriptor);
            services.AddSingleton(generator.Object);
        }));
        await using (var scope = firstPassHost.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var baseline = await db.ControlBaselines.SingleAsync(b => b.RegisteredSystemId == fixture.SystemId);
            var raw = JsonSerializer.Serialize(new { uuid = Guid.NewGuid().ToString(), metadata = new { version = "synthetic-firstpass" },
                controls = new[] { new { id = "synthetic-source", title = "Synthetic requirement control",
                    props = new[] { new { name = "label", value = fixture.AttentionControlId } },
                    parts = new[] { new { id = "firstpass-statement", name = "statement", prose = "Document the recorded system facts." } } } } });
            var binding = new BaselineCatalogBinding { TenantId = Tenant, ControlBaselineId = baseline.Id, FrameworkIdentifier = "SYNTHETIC",
                CatalogVersion = "synthetic-firstpass", CatalogJson = raw, ContentHash = RequirementCoverageService.Hash(raw), SourceUri = "https://example.invalid/catalog" };
            db.BaselineCatalogBindings.Add(binding);
            baseline.RequirementCatalogBindingId = binding.Id;
            await db.SaveChangesAsync();
        }
        using var client = firstPassHost.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Tid", DirectoryId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Oid", fixture.Actor.ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Kind", "organization");
        client.DefaultRequestHeaders.Add("X-Workspace-Mode", "ordinary");
        client.DefaultRequestHeaders.Add("X-Workspace-Tenant-Id", Tenant.ToString());
        var path = $"/api/systems/{fixture.SystemId}/requirement-coverage/{fixture.AttentionControlId}";
        var before = await client.GetFromJsonAsync<RequirementCoverageDetail>(path);
        // Act
        var generated = await client.PostAsJsonAsync(path + "/first-pass", new RequirementFirstPassInput(before!.NarrativeVersion!.Value, before.BaselineRevision, "Policy"));
        // Assert
        generated.StatusCode.Should().Be(HttpStatusCode.OK, await generated.Content.ReadAsStringAsync());
        var firstPass = await generated.Content.ReadFromJsonAsync<RequirementFirstPassResponse>();
        (await client.GetFromJsonAsync<RequirementCoverageDetail>(path))!.Requirements[0].Responses.Should().BeEmpty();
        // Act
        var saved = await client.PutAsJsonAsync(path + "/responses", new RequirementMappingInput(before.NarrativeVersion.Value,
            [new("firstpass-statement", "Policy", firstPass!.Responses[0].Response, [])], new Dictionary<string, string>(), firstPass.Token));
        // Assert
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var result = await saved.Content.ReadFromJsonAsync<RequirementCoverageDetail>();
        result!.FirstPass!.ContextHash.Should().Be(firstPass.ContextHash);
        result.Requirements[0].Reviewed.Should().BeFalse();
        // Act
        var foreign = await client.PostAsJsonAsync($"/api/systems/{fixture.ForeignTenantSystemId}/requirement-coverage/{fixture.AttentionControlId}/first-pass",
            new RequirementFirstPassInput(1, 0, "Policy"));
        // Assert
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
