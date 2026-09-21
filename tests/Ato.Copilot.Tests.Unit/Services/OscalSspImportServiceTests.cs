using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class OscalSspImportServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;

    public OscalSspImportServiceTests()
    {
        var services = new ServiceCollection();
      var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = $"OscalSspImport_{Guid.NewGuid():N}";
        services.AddDbContext<AtoCopilotContext>(options =>
          options.UseInMemoryDatabase(databaseName, databaseRoot));
        _provider = services.BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    [Theory]
    [InlineData("technical", ImportMode.Full)]
    [InlineData("policy", ImportMode.Full)]
    [InlineData("technical", ImportMode.Preview)]
    [InlineData("technical", ImportMode.Full, true)]
    public async Task Import_PreservesProvenanceAndAuditsRollback(string half, ImportMode mode, bool underReview = false)
    {
      // Arrange
      using var scope = _provider.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
      db.RegisteredSystems.Add(new RegisteredSystem { Id = "system-1", Name = "Synthetic import system" });
      db.ControlImplementations.Add(new ControlImplementation
      {
        RegisteredSystemId = "system-1", ControlId = "AC-2", PolicyNarrative = "Original policy",
        TechnicalNarrative = "Original model text", Narrative = "Original model text",
        AiSuggested = true, IsAutoPopulated = true, CurrentVersion = 1,
        ApprovalStatus = underReview ? SspSectionStatus.UnderReview : SspSectionStatus.Draft,
      });
      await db.SaveChangesAsync();
      var service = new OscalSspImportService(_provider.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<OscalSspImportService>.Instance);
      var json = JsonSerializer.Serialize(new Dictionary<string, object>
      {
        ["system-security-plan"] = new Dictionary<string, object>
        {
          ["control-implementation"] = new Dictionary<string, object>
          {
            ["implemented-requirements"] = new[] { new Dictionary<string, object>
            {
              ["control-id"] = "ac-2", ["statements"] = new[] { new Dictionary<string, object>
              {
                ["statement-id"] = $"ac-2_smt.{half}", ["description"] = "Imported text"
              } }
            } }
          }
        }
      });

      // Act
      var result = await service.ImportAsync("system-1", json, mode);

      // Assert
      db.ChangeTracker.Clear();
      var saved = await db.ControlImplementations.SingleAsync();
      if (underReview)
      {
        result.ControlsFailed.Should().Be(1);
        result.ValidationErrors.Should().ContainSingle().Which.Should().Contain("UNDER_REVIEW");
        saved.TechnicalNarrative.Should().Be("Original model text");
        saved.PolicyNarrative.Should().Be("Original policy");
        saved.AiSuggested.Should().BeTrue();
        saved.IsAutoPopulated.Should().BeTrue();
        saved.ApprovalStatus.Should().Be(SspSectionStatus.UnderReview);
        saved.CurrentVersion.Should().Be(1);
        (await db.NarrativeVersions.CountAsync()).Should().Be(0);
        return;
      }
      saved.AiSuggested.Should().Be(half == "policy" || mode == ImportMode.Preview);
      saved.IsAutoPopulated.Should().Be(half == "policy" || mode == ImportMode.Preview);
      var versions = await db.NarrativeVersions.OrderBy(version => version.VersionNumber).ToListAsync();
      if (mode == ImportMode.Preview)
      {
        versions.Should().BeEmpty();
        saved.TechnicalNarrative.Should().Be("Original model text");
        return;
      }
      versions.Should().HaveCount(2);
      versions.Last().ChangeReason.Should().Contain(result.RunId);
      var repeated = await service.ImportAsync("system-1", json, mode);
      repeated.ControlsSkipped.Should().Be(1);
      (await db.NarrativeVersions.CountAsync()).Should().Be(2);
      var governance = new NarrativeGovernanceService(_provider.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<NarrativeGovernanceService>.Instance);
      await governance.RollbackNarrativeAsync("system-1", "AC-2", 1);
      db.ChangeTracker.Clear();
      var restored = await db.ControlImplementations.SingleAsync();
      restored.TechnicalNarrative.Should().Be("Original model text");
      restored.PolicyNarrative.Should().Be("Original policy");
      restored.AiSuggested.Should().BeTrue();
      restored.IsAutoPopulated.Should().BeTrue();
    }

    [Fact]
    public async Task Import_RepeatedControl_KeepsUniqueOrderedSnapshots()
    {
      // Arrange
      using var scope = _provider.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
      db.ControlImplementations.Add(new ControlImplementation
      {
        RegisteredSystemId = "system-1", ControlId = "AC-2",
        TechnicalNarrative = "Original", Narrative = "Original", AiSuggested = true,
      });
      await db.SaveChangesAsync();
      const string json = """
        {"system-security-plan":{"control-implementation":{"implemented-requirements":[
          {"control-id":"ac-2","statements":[{"statement-id":"ac-2_smt.technical","description":"First import"}]},
          {"control-id":"ac-2","statements":[{"statement-id":"ac-2_smt.technical","description":"Second import"}]}
        ]}}}
        """;
      var service = new OscalSspImportService(_provider.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<OscalSspImportService>.Instance);

      // Act
      var result = await service.ImportAsync("system-1", json, ImportMode.Full);

      // Assert
      result.ControlsUpdated.Should().Be(2);
      db.ChangeTracker.Clear();
      var versions = await db.NarrativeVersions.OrderBy(version => version.VersionNumber).ToListAsync();
      versions.Select(version => version.VersionNumber).Should().Equal(1, 2, 3);
      versions.Select(version => version.Content).Should().Equal("Original", "First import", "Second import");
      var implementation = await db.ControlImplementations.SingleAsync();
      implementation.TechnicalNarrative.Should().Be("Second import");
      implementation.AiSuggested.Should().BeFalse();
    }

    [Fact]
    public async Task ImportAsync_DualStatements_PersistsCanonicalHalves()
    {
        // Arrange
        const string oscalJson = """
            {
              "system-security-plan": {
                "control-implementation": {
                  "implemented-requirements": [{
                    "control-id": "ac-2",
                    "statements": [
                      {
                        "statement-id": "AC-2_smt.policy",
                        "description": "Accounts are governed by the access control policy."
                      },
                      {
                        "statement-id": "AC-2_smt.technical",
                        "description": "Microsoft Entra ID enforces account lifecycle controls."
                      }
                    ]
                  }]
                }
              }
            }
            """;
        var service = new OscalSspImportService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OscalSspImportService>.Instance);

        // Act
        var result = await service.ImportAsync("system-1", oscalJson, ImportMode.Full);

        // Assert
        result.ControlsCreated.Should().Be(1);
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var implementation = await db.ControlImplementations.IgnoreQueryFilters().SingleAsync();
        implementation.PolicyNarrative.Should().Be("Accounts are governed by the access control policy.");
        implementation.TechnicalNarrative.Should().Be("Microsoft Entra ID enforces account lifecycle controls.");
        implementation.Narrative.Should().Be(implementation.TechnicalNarrative);
    }
}