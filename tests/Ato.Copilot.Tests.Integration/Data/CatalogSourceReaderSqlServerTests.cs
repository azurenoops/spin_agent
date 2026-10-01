using System.Diagnostics;
using System.Text.Json;
using Ato.Copilot.Core.Data;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Data;

public sealed class CatalogSourceReaderSqlServerTests(BoundarySchemaSqlServerFixture fixture)
    : IClassFixture<BoundarySchemaSqlServerFixture>
{
    [SkippableFact]
    public async Task LargeSources_StreamExactlyWithinFiveSeconds_AndPreserveScope()
    {
        // Arrange
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        await using var db = await fixture.CreateDatabaseAsync();
        await db.Database.EnsureCreatedAsync();
        var tenant = Guid.NewGuid();
        var system = new RegisteredSystem { TenantId = tenant, Name = "Synthetic large-source system", CreatedBy = "fixture" };
        var baseline = new ControlBaseline { TenantId = tenant, RegisteredSystemId = system.Id,
            BaselineLevel = "Synthetic", CreatedBy = "fixture" };
        var source = JsonSerializer.Serialize(new
        {
            uuid = "synthetic", metadata = new { version = "1" },
            controls = new[] { new { id = "source-control", title = "Synthetic control",
                parts = new[] { new { id = "source-statement", name = "statement", prose = "Retain source text." } } } },
            padding = new string('x', 10_500_000)
        });
        var binding = new BaselineCatalogBinding { TenantId = tenant, ControlBaselineId = baseline.Id,
            CatalogJson = source, ContentHash = "fixture", FrameworkIdentifier = "SYNTHETIC", CatalogVersion = "1" };
        var framework = new ComplianceFramework { Identifier = "LARGE-SYNTHETIC", Name = "Synthetic source",
            RequirementCatalogJson = source, RequirementCatalogVersion = "1", RequirementCatalogCapturedAt = DateTime.UtcNow };
        db.AddRange(system, baseline, binding, framework);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var watch = Stopwatch.StartNew();

        // Act
        var loaded = await CatalogSourceReader.ReadBindingAsync(db,
            x => x.Id == binding.Id && x.TenantId == tenant && x.ControlBaselineId == baseline.Id, timeout.Token);
        var reference = await CatalogSourceReader.ReadFrameworkAsync(db, x => x.Id == framework.Id, timeout.Token);
        var inaccessible = await CatalogSourceReader.ReadBindingAsync(db,
            x => x.Id == binding.Id && x.TenantId == Guid.Empty, timeout.Token);

        // Assert
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        loaded!.CatalogJson.Should().Be(source);
        reference!.RequirementCatalogJson.Should().Be(source);
        inaccessible.Should().BeNull();
        RequirementCatalog.Parse(loaded.CatalogJson).Controls.Single().Requirements.Single().Id.Should().Be("source-statement");
        db.ChangeTracker.Entries<BaselineCatalogBinding>().Should().BeEmpty();
    }
}
